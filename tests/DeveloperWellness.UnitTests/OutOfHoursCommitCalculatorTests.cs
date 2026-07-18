using DeveloperWellness.Domain.Model;
using DeveloperWellness.Domain.Options;
using DeveloperWellness.Domain.Signals;

namespace DeveloperWellness.UnitTests;

/// <summary>
/// Unit tests for <see cref="OutOfHoursCommitCalculator"/> (tasks.md T017): the working-hours boundary
/// (start-inclusive, end-exclusive), weekend handling, the organisation-timezone fallback for commits
/// without a usable author-local offset, the exactly-25%-is-not-flagged boundary, the flag reason's
/// counts and percent, SHA dedup, and the unmatched-author skip.
/// </summary>
public class OutOfHoursCommitCalculatorTests
{
    private static readonly DeveloperLogin Alice = new("alice");

    private const string ProjectName = "demo-project";

    // 2024-01-01 is a Monday and 2024-01-06 is a Saturday; every boundary test below anchors on this
    // known week so DayOfWeek assertions do not depend on when the suite happens to run.
    private static readonly DateTimeOffset Monday = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Saturday = new(2024, 1, 6, 0, 0, 0, TimeSpan.Zero);

    private static WellnessOptions CreateOptions() => new()
    {
        OrganisationTimeZone = "UTC",
    };

    private static CommitEvent CommitAt(DateTimeOffset day, int hour, int minute, string sha, bool hasUsableOffset = true) =>
        new(Alice, ProjectName, day.AddHours(hour).AddMinutes(minute), sha, hasUsableOffset);

    [Fact]
    public void IsOutOfHours_WithCommitAt10AmMondayAuthorLocal_ReturnsFalse()
    {
        var commit = CommitAt(Monday, 10, 0, "sha-1");
        var options = CreateOptions();

        var result = OutOfHoursCommitCalculator.IsOutOfHours(commit, options);

        Assert.False(result);
    }

    [Fact]
    public void IsOutOfHours_WithCommitAt859AmMondayAuthorLocal_ReturnsTrue()
    {
        var commit = CommitAt(Monday, 8, 59, "sha-1");
        var options = CreateOptions();

        var result = OutOfHoursCommitCalculator.IsOutOfHours(commit, options);

        Assert.True(result);
    }

    [Fact]
    public void IsOutOfHours_WithCommitAt6PmMondayAuthorLocal_ReturnsTrueBecauseEndIsExclusive()
    {
        var commit = CommitAt(Monday, 18, 0, "sha-1");
        var options = CreateOptions();

        var result = OutOfHoursCommitCalculator.IsOutOfHours(commit, options);

        Assert.True(result);
    }

    [Fact]
    public void IsOutOfHours_WithCommitAt930PmMondayAuthorLocal_ReturnsTrue()
    {
        var commit = CommitAt(Monday, 21, 30, "sha-1");
        var options = CreateOptions();

        var result = OutOfHoursCommitCalculator.IsOutOfHours(commit, options);

        Assert.True(result);
    }

    [Fact]
    public void IsOutOfHours_WithCommitAt11AmSaturdayAuthorLocal_ReturnsTrueBecauseWeekendIsNotAWorkingDay()
    {
        var commit = CommitAt(Saturday, 11, 0, "sha-1");
        var options = CreateOptions();

        var result = OutOfHoursCommitCalculator.IsOutOfHours(commit, options);

        Assert.True(result);
    }

    [Fact]
    public void IsOutOfHours_WithUnusableOffset_FallsBackToOrganisationTimeZoneRatherThanTheFaceValueOffset()
    {
        // 10:00 UTC on Monday reads as in-hours at face value, but January in America/New_York is
        // UTC-5 (no DST), so the correctly-converted local time is 05:00 — out of hours. HasUsableOffset
        // is false, so the calculator must ignore the commit's own (irrelevant) offset and convert the
        // instant, proving the fallback path actually runs rather than reading the offset as-is.
        var commit = CommitAt(Monday, 10, 0, "sha-1", hasUsableOffset: false);
        var options = CreateOptions();
        options.OrganisationTimeZone = "America/New_York";

        var result = OutOfHoursCommitCalculator.IsOutOfHours(commit, options);

        Assert.True(result);
    }

    [Fact]
    public void GetLocalTime_WithUsableOffset_ReturnsOccurredAtUnchanged()
    {
        var commit = CommitAt(Monday, 10, 0, "sha-1", hasUsableOffset: true);
        var options = CreateOptions();

        var local = OutOfHoursCommitCalculator.GetLocalTime(commit, options);

        Assert.Equal(commit.OccurredAt, local);
    }

    [Fact]
    public void Calculate_WithShareExactlyAtTwentyFivePercent_ProducesNoFlag()
    {
        // 1 out-of-hours commit out of 4 total is exactly 0.25; the rule is strictly greater-than.
        var events = new List<ActivityEvent>
        {
            CommitAt(Monday, 10, 0, "sha-1"),
            CommitAt(Monday, 11, 0, "sha-2"),
            CommitAt(Monday, 12, 0, "sha-3"),
            CommitAt(Monday, 21, 0, "sha-4"), // out of hours
        };
        var options = CreateOptions();

        var results = OutOfHoursCommitCalculator.Calculate(events, options, periodDays: 14);

        var result = results[Alice];
        Assert.Equal(0.25m, result.Share);
        Assert.Null(result.Flag);
    }

    [Fact]
    public void Calculate_WithShareAboveTwentyFivePercent_ProducesFlagWithReasonContainingCountsAndPercent()
    {
        // 13 of 34 commits out of hours ≈ 38.24%, above the 25% threshold — the design's own example ratio.
        var events = new List<ActivityEvent>();
        for (var i = 0; i < 21; i++)
        {
            events.Add(CommitAt(Monday, 10, 0, $"in-{i}"));
        }
        for (var i = 0; i < 13; i++)
        {
            events.Add(CommitAt(Monday, 21, 0, $"out-{i}"));
        }
        var options = CreateOptions();

        var results = OutOfHoursCommitCalculator.Calculate(events, options, periodDays: 14);

        var result = results[Alice];
        Assert.Equal(34, result.TotalCommits);
        Assert.Equal(13, result.OutOfHoursCommits);
        Assert.NotNull(result.Flag);
        Assert.Equal(FlagKind.OverworkCommits, result.Flag!.Kind);
        Assert.Contains("13 of 34 commits (38%)", result.Flag.Reason, StringComparison.Ordinal);
        Assert.Contains("over the last 14 days", result.Flag.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Calculate_WithNoCommitsForADeveloper_OmitsThemFromTheResult()
    {
        var events = new List<ActivityEvent>
        {
            new ReviewEvent(Alice, ProjectName, Monday.AddHours(10), prNumber: 1, ReviewState.Approved),
        };
        var options = CreateOptions();

        var results = OutOfHoursCommitCalculator.Calculate(events, options, periodDays: 14);

        Assert.False(results.ContainsKey(Alice));
    }

    [Fact]
    public void Calculate_WithDuplicateShaCommits_CountsTheCommitOnce()
    {
        var events = new List<ActivityEvent>
        {
            CommitAt(Monday, 10, 0, "sha-shared"),
            CommitAt(Monday, 10, 5, "sha-shared"),
            CommitAt(Monday, 10, 10, "sha-shared"),
        };
        var options = CreateOptions();

        var results = OutOfHoursCommitCalculator.Calculate(events, options, periodDays: 14);

        Assert.Equal(1, results[Alice].TotalCommits);
    }

    [Fact]
    public void Calculate_WithUnmatchedAuthorCommits_SkipsThem()
    {
        var events = new List<ActivityEvent>
        {
            new CommitEvent(DeveloperLogin.Unmatched, ProjectName, Monday.AddHours(10), "sha-1", hasUsableOffset: true),
        };
        var options = CreateOptions();

        var results = OutOfHoursCommitCalculator.Calculate(events, options, periodDays: 14);

        Assert.Empty(results);
    }
}
