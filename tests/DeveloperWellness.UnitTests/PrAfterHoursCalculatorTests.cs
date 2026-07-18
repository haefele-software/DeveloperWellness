using DeveloperWellness.Domain.Model;
using DeveloperWellness.Domain.Options;
using DeveloperWellness.Domain.Signals;

namespace DeveloperWellness.UnitTests;

/// <summary>
/// Unit tests for <see cref="PrAfterHoursCalculator"/> (tasks.md T031): PR-event classification (review
/// plus PR-opened; comments and commits ignored), the organisation-timezone basis that is always used
/// regardless of the event's own UTC offset (spec edge case: PR events carry no author-local offset),
/// the working-hours boundary (start-inclusive, end-exclusive), weekend handling, the four overwork-flag
/// quadrants formed by the threshold and the minimum-PR-events guard (FR-025), the flag reason's counts
/// and percent, and the unmatched-author skip.
/// </summary>
public class PrAfterHoursCalculatorTests
{
    private static readonly DeveloperLogin Alice = new("alice");
    private static readonly DeveloperLogin Bob = new("bob");

    private const string ProjectName = "demo-project";

    // 2024-01-01 is a Monday and 2024-01-06 is a Saturday; every boundary test below anchors on this
    // known week so DayOfWeek assertions do not depend on when the suite happens to run.
    private static readonly DateTimeOffset Monday = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Saturday = new(2024, 1, 6, 0, 0, 0, TimeSpan.Zero);

    private static WellnessOptions CreateOptions() => new()
    {
        OrganisationTimeZone = "UTC",
    };

    private static ReviewEvent ReviewAt(DeveloperLogin author, DateTimeOffset day, int hour, int minute, int prNumber = 1) =>
        new(author, ProjectName, day.AddHours(hour).AddMinutes(minute), prNumber, ReviewState.Approved);

    private static PrOpenedEvent OpenedAt(DeveloperLogin author, DateTimeOffset day, int hour, int minute, int prNumber = 1) =>
        new(author, ProjectName, day.AddHours(hour).AddMinutes(minute), prNumber);

    [Fact]
    public void IsOutOfHours_WithReviewAt10AmMondayOrganisationLocal_ReturnsFalse()
    {
        var review = ReviewAt(Alice, Monday, 10, 0);
        var options = CreateOptions();

        var result = PrAfterHoursCalculator.IsOutOfHours(review, options);

        Assert.False(result);
    }

    [Fact]
    public void IsOutOfHours_WithReviewAt859AmMondayOrganisationLocal_ReturnsTrue()
    {
        var review = ReviewAt(Alice, Monday, 8, 59);
        var options = CreateOptions();

        var result = PrAfterHoursCalculator.IsOutOfHours(review, options);

        Assert.True(result);
    }

    [Fact]
    public void IsOutOfHours_WithPrOpenedAt6PmMondayOrganisationLocal_ReturnsTrueBecauseEndIsExclusive()
    {
        var opened = OpenedAt(Alice, Monday, 18, 0);
        var options = CreateOptions();

        var result = PrAfterHoursCalculator.IsOutOfHours(opened, options);

        Assert.True(result);
    }

    [Fact]
    public void IsOutOfHours_WithReviewAt11AmSaturdayOrganisationLocal_ReturnsTrueBecauseWeekendIsNotAWorkingDay()
    {
        var review = ReviewAt(Alice, Saturday, 11, 0);
        var options = CreateOptions();

        var result = PrAfterHoursCalculator.IsOutOfHours(review, options);

        Assert.True(result);
    }

    [Fact]
    public void IsOutOfHours_WithUtcInstantOutOfHoursButOrganisationLocalInHours_ReturnsFalseBecauseOrganisationTimeZoneIsAlwaysUsed()
    {
        // 21:00 UTC on Monday reads as out-of-hours at face value, but organisation time is
        // America/Los_Angeles, which in January (no DST) is UTC-8, so the correctly-converted org-local
        // time is 13:00 - in hours. This proves classification always converts to organisation time
        // rather than reading the event's own UTC offset as-is, per the spec edge case that PR events
        // carry no usable author-local offset.
        var review = ReviewAt(Alice, Monday, 21, 0);
        var options = CreateOptions();
        options.OrganisationTimeZone = "America/Los_Angeles";

        var result = PrAfterHoursCalculator.IsOutOfHours(review, options);

        Assert.False(result);
    }

    [Fact]
    public void IsOutOfHours_WithUtcInstantInHoursButOrganisationLocalOutOfHours_ReturnsTrueBecauseOrganisationTimeZoneIsAlwaysUsed()
    {
        // 10:00 UTC on Monday reads as in-hours at face value, but organisation time is
        // America/New_York, which in January (no DST) is UTC-5, so the correctly-converted org-local
        // time is 05:00 - out of hours.
        var review = ReviewAt(Alice, Monday, 10, 0);
        var options = CreateOptions();
        options.OrganisationTimeZone = "America/New_York";

        var result = PrAfterHoursCalculator.IsOutOfHours(review, options);

        Assert.True(result);
    }

    [Fact]
    public void GetLocalTime_ConvertsToOrganisationTimeZoneRegardlessOfTheEventsOwnOffset()
    {
        var occurredAt = new DateTimeOffset(2024, 1, 1, 10, 0, 0, TimeSpan.FromHours(4)); // 06:00 UTC
        var review = new ReviewEvent(Alice, ProjectName, occurredAt, prNumber: 1, ReviewState.Approved);
        var options = CreateOptions();
        options.OrganisationTimeZone = "America/New_York"; // UTC-5 in January

        var local = PrAfterHoursCalculator.GetLocalTime(review, options);

        Assert.Equal(new DateTimeOffset(2024, 1, 1, 1, 0, 0, TimeSpan.FromHours(-5)), local);
    }

    [Fact]
    public void Calculate_WithReviewsAndPrOpens_CountsBothAsPrEvents()
    {
        var events = new List<ActivityEvent>
        {
            ReviewAt(Alice, Monday, 10, 0),
            OpenedAt(Alice, Monday, 11, 0),
        };
        var options = CreateOptions();

        var results = PrAfterHoursCalculator.Calculate(events, options, periodDays: 14);

        Assert.Equal(2, results[Alice].TotalPrEvents);
    }

    [Fact]
    public void Calculate_WithCommentAndCommitEvents_IgnoresThemBecauseTheyAreNotPrEvents()
    {
        var events = new List<ActivityEvent>
        {
            new CommentEvent(Alice, ProjectName, Monday.AddHours(10), commentId: 1, bodyText: "lgtm"),
            new CommitEvent(Alice, ProjectName, Monday.AddHours(10), "sha-1", hasUsableOffset: true),
        };
        var options = CreateOptions();

        var results = PrAfterHoursCalculator.Calculate(events, options, periodDays: 14);

        Assert.False(results.ContainsKey(Alice));
    }

    [Fact]
    public void Calculate_WithWeekendPrEvent_CountsItAsOutOfHours()
    {
        var events = new List<ActivityEvent>
        {
            ReviewAt(Alice, Saturday, 11, 0),
            ReviewAt(Alice, Monday, 10, 0),
            ReviewAt(Alice, Monday, 11, 0),
        };
        var options = CreateOptions();

        var results = PrAfterHoursCalculator.Calculate(events, options, periodDays: 14);

        Assert.Equal(1, results[Alice].OutOfHoursPrEvents);
    }

    [Fact]
    public void Calculate_WithShareAboveThresholdAndGuardMet_ProducesFlag()
    {
        // 5 of 9 (~55.6%) out of hours: above the 25% threshold, and 9 >= MinPrEvents (3).
        var events = BuildPrEvents(Alice, outOfHoursCount: 5, inHoursCount: 4);
        var options = CreateOptions();

        var results = PrAfterHoursCalculator.Calculate(events, options, periodDays: 14);

        Assert.NotNull(results[Alice].Flag);
        Assert.Equal(FlagKind.OverworkPrActivity, results[Alice].Flag!.Kind);
    }

    [Fact]
    public void Calculate_WithShareAboveThresholdButOnlyTwoEvents_ProducesNoFlagBecauseTheGuardFails()
    {
        // 2 of 2 (100%) out of hours: above the threshold, but only 2 PR events - below MinPrEvents (3).
        var events = BuildPrEvents(Alice, outOfHoursCount: 2, inHoursCount: 0);
        var options = CreateOptions();

        var results = PrAfterHoursCalculator.Calculate(events, options, periodDays: 14);

        Assert.Null(results[Alice].Flag);
    }

    [Fact]
    public void Calculate_WithShareExactlyAtTwentyFivePercentAndGuardMet_ProducesNoFlagBecauseTheRuleIsStrictlyGreaterThan()
    {
        // 1 of 4 (exactly 25%) out of hours; the rule is strictly greater-than, so no flag even though
        // the guard (>= 3 events) is satisfied.
        var events = BuildPrEvents(Alice, outOfHoursCount: 1, inHoursCount: 3);
        var options = CreateOptions();

        var results = PrAfterHoursCalculator.Calculate(events, options, periodDays: 14);

        Assert.Equal(0.25m, results[Alice].Share);
        Assert.Null(results[Alice].Flag);
    }

    [Fact]
    public void Calculate_WithShareBelowThresholdAndGuardMet_ProducesNoFlag()
    {
        // 1 of 5 (20%) out of hours - below the 25% threshold, even though the guard is satisfied.
        var events = BuildPrEvents(Alice, outOfHoursCount: 1, inHoursCount: 4);
        var options = CreateOptions();

        var results = PrAfterHoursCalculator.Calculate(events, options, periodDays: 14);

        Assert.Null(results[Alice].Flag);
    }

    [Fact]
    public void Calculate_WithExactlyThreeEvents_MeetsTheMinimumGuardBoundary()
    {
        // 2 of 3 (~66.7%) out of hours: above the threshold, with exactly MinPrEvents (3) - the guard
        // boundary is inclusive (>=).
        var events = BuildPrEvents(Alice, outOfHoursCount: 2, inHoursCount: 1);
        var options = CreateOptions();

        var results = PrAfterHoursCalculator.Calculate(events, options, periodDays: 14);

        Assert.NotNull(results[Alice].Flag);
    }

    [Fact]
    public void Calculate_ComputesShareAsOutOfHoursEventsOverTotalPrEvents()
    {
        var events = BuildPrEvents(Alice, outOfHoursCount: 3, inHoursCount: 6);
        var options = CreateOptions();

        var results = PrAfterHoursCalculator.Calculate(events, options, periodDays: 14);

        Assert.Equal(9, results[Alice].TotalPrEvents);
        Assert.Equal(3, results[Alice].OutOfHoursPrEvents);
        Assert.Equal(1m / 3m, results[Alice].Share);
    }

    [Fact]
    public void Calculate_WithFlaggedDeveloper_ReasonContainsCountsPercentAndOrganisationTimeBasis()
    {
        // 5 of 9 PR events out of hours: ~55.6%, rounds away from zero to 56%.
        var events = BuildPrEvents(Alice, outOfHoursCount: 5, inHoursCount: 4);
        var options = CreateOptions();

        var results = PrAfterHoursCalculator.Calculate(events, options, periodDays: 14);

        var reason = results[Alice].Flag!.Reason;
        Assert.Contains("5 of 9 PR reviews and opens (56%)", reason, StringComparison.Ordinal);
        Assert.Contains("organisation time", reason, StringComparison.Ordinal);
        Assert.Contains("over the last 14 days", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Calculate_WithUnmatchedAuthorPrEvents_SkipsThem()
    {
        var events = new List<ActivityEvent>
        {
            new ReviewEvent(DeveloperLogin.Unmatched, ProjectName, Monday.AddHours(10), prNumber: 1, ReviewState.Approved),
        };
        var options = CreateOptions();

        var results = PrAfterHoursCalculator.Calculate(events, options, periodDays: 14);

        Assert.Empty(results);
    }

    [Fact]
    public void Calculate_WithNoPrEventsForADeveloper_OmitsThemFromTheResult()
    {
        var events = new List<ActivityEvent>
        {
            new CommitEvent(Alice, ProjectName, Monday.AddHours(10), "sha-1", hasUsableOffset: true),
        };
        var options = CreateOptions();

        var results = PrAfterHoursCalculator.Calculate(events, options, periodDays: 14);

        Assert.False(results.ContainsKey(Alice));
    }

    [Fact]
    public void Calculate_WithMultipleDevelopers_ComputesEachIndependently()
    {
        var events = new List<ActivityEvent>();
        events.AddRange(BuildPrEvents(Alice, outOfHoursCount: 5, inHoursCount: 4)); // flagged: 56%, 9 events
        events.AddRange(BuildPrEvents(Bob, outOfHoursCount: 1, inHoursCount: 4)); // not flagged: 20%
        var options = CreateOptions();

        var results = PrAfterHoursCalculator.Calculate(events, options, periodDays: 14);

        Assert.NotNull(results[Alice].Flag);
        Assert.Null(results[Bob].Flag);
        Assert.Equal(9, results[Alice].TotalPrEvents);
        Assert.Equal(5, results[Bob].TotalPrEvents);
    }

    /// <summary>
    /// Builds a mix of PR events for one author: <paramref name="outOfHoursCount"/> at 21:00 Monday (out
    /// of hours) plus <paramref name="inHoursCount"/> at 10:00 Monday (in hours), alternating reviews and
    /// PR-opened events so both event kinds are exercised.
    /// </summary>
    private static List<ActivityEvent> BuildPrEvents(DeveloperLogin author, int outOfHoursCount, int inHoursCount)
    {
        var events = new List<ActivityEvent>();
        var prNumber = 1;

        for (var i = 0; i < outOfHoursCount; i++)
        {
            ActivityEvent prEvent = i % 2 == 0
                ? ReviewAt(author, Monday, 21, 0, prNumber++)
                : OpenedAt(author, Monday, 21, 0, prNumber++);
            events.Add(prEvent);
        }

        for (var i = 0; i < inHoursCount; i++)
        {
            ActivityEvent prEvent = i % 2 == 0
                ? ReviewAt(author, Monday, 10, 0, prNumber++)
                : OpenedAt(author, Monday, 10, 0, prNumber++);
            events.Add(prEvent);
        }

        return events;
    }
}
