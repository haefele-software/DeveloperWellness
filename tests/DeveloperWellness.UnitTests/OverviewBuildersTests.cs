using DeveloperWellness.Domain.Model;
using DeveloperWellness.Domain.Options;
using DeveloperWellness.Domain.Signals;

namespace DeveloperWellness.UnitTests;

/// <summary>
/// Unit tests for <see cref="RecommendationMapper"/>, <see cref="TrendCalculator"/>, and
/// <see cref="TeamCardBuilder"/> (tasks.md T028): the Overview's domain builders (FR-036, FR-037, FR-038).
/// Every fixture is hand-rolled inline (Domain tests stay Infrastructure-free), matching the style of
/// <c>SpreadThinCalculatorTests</c> and <c>OutOfHoursCommitCalculatorTests</c>.
/// </summary>
public class OverviewBuildersTests
{
    private static readonly WellnessOptions DefaultOptions = new() { OrganisationTimeZone = "UTC" };

    private static Developer Dev(string login, string? displayName = null) => new(new DeveloperLogin(login), displayName, isBot: false);

    private static WellbeingFlag Flag(FlagKind kind, string reason) => new(kind, reason);

    private static ActivitySummary Summary(
        Developer developer,
        int commitCount = 0,
        int reviewCount = 0,
        int commentCount = 0,
        int prsOpenedCount = 0,
        decimal? outOfHoursCommitShare = null,
        decimal? outOfHoursPrShare = null,
        int? distinctProjectCount = null,
        IReadOnlyList<WellbeingFlag>? flags = null,
        bool hasActivity = true) =>
        new(developer, commitCount, reviewCount, commentCount, prsOpenedCount,
            outOfHoursCommitShare, outOfHoursPrShare, distinctProjectCount, flags ?? [], hasActivity);

    private static CommitEvent CommitAt(DeveloperLogin author, string projectName, DateTimeOffset occurredAt, string sha) =>
        new(author, projectName, occurredAt, sha, hasUsableOffset: true);

    // ---- RecommendationMapper -------------------------------------------------------------

    [Fact]
    public void Map_WithMoreThanSixFlaggedDevelopers_CapsAtSix()
    {
        var summaries = Enumerable.Range(0, 8)
            .Select(i => Summary(Dev($"dev{i}"), flags: [Flag(FlagKind.OverworkCommits, "Reason.")]))
            .ToList();

        var result = RecommendationMapper.Map(summaries, []);

        Assert.Equal(6, result.Count);
    }

    [Fact]
    public void Map_OrdersByConcurrentSignalCountDescendingThenAlphabetical()
    {
        var summaries = new List<ActivitySummary>
        {
            Summary(Dev("zed", "Zed"), flags: [Flag(FlagKind.OverworkCommits, "One.")]),
            Summary(Dev("amy", "Amy"), flags: [Flag(FlagKind.OverworkCommits, "One."), Flag(FlagKind.SpreadThin, "Two.")]),
            Summary(Dev("bob", "Bob"), flags: [Flag(FlagKind.OverworkCommits, "One.")]),
        };

        var result = RecommendationMapper.Map(summaries, []);

        Assert.Equal(["Amy", "Bob", "Zed"], result.Select(r => r.Developer.DisplayName));
    }

    [Theory]
    [InlineData(FlagKind.OverworkCommits, "Encourage real time off")]
    [InlineData(FlagKind.OverworkPrActivity, "Nudge reviews back into the day")]
    [InlineData(FlagKind.SpreadThin, "Rebalance project load")]
    [InlineData(FlagKind.NegativeTone, "Check in on review climate")]
    [InlineData(FlagKind.PossibleRushing, "Ease the pace pressure")]
    public void Map_MapsEachLeadingFlagKindToItsExactAction(FlagKind kind, string expectedAction)
    {
        var summaries = new List<ActivitySummary> { Summary(Dev("alice"), flags: [Flag(kind, "Reason.")]) };

        var result = RecommendationMapper.Map(summaries, []);

        Assert.Equal(expectedAction, result[0].Action);
    }

    [Fact]
    public void Map_UsesTheFirstFlagInTheListAsTheLeadingFlag()
    {
        var summaries = new List<ActivitySummary>
        {
            Summary(Dev("alice"), flags: [Flag(FlagKind.SpreadThin, "Spread reason."), Flag(FlagKind.OverworkCommits, "OOH reason.")]),
        };

        var result = RecommendationMapper.Map(summaries, []);

        Assert.Equal("Rebalance project load", result[0].Action);
    }

    [Fact]
    public void Map_JoinsTheFirstSentenceOfEveryFlagReasonWithASpace()
    {
        var summaries = new List<ActivitySummary>
        {
            Summary(
                Dev("alice"),
                flags:
                [
                    Flag(FlagKind.OverworkCommits, "13 of 34 commits landed out of hours. It might be worth a check-in."),
                    Flag(FlagKind.SpreadThin, "Active in 5 projects. That's a lot of context switching."),
                ]),
        };

        var result = RecommendationMapper.Map(summaries, []);

        Assert.Equal(
            "13 of 34 commits landed out of hours. Active in 5 projects.",
            result[0].Reason);
    }

    [Theory]
    [InlineData("Multiple sentences here. Second one follows.", "Multiple sentences here.")]
    [InlineData("No terminating period here", "No terminating period here")]
    [InlineData("  Leading whitespace. Trailing content.", "Leading whitespace.")]
    [InlineData("Only one sentence.", "Only one sentence.")]
    public void ExtractFirstSentence_ExtractsUpToAndIncludingTheFirstPeriod(string reason, string expected)
    {
        var result = RecommendationMapper.ExtractFirstSentence(reason);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void Map_WithNoFlaggedDevelopers_ReturnsEmpty()
    {
        var summaries = new List<ActivitySummary>
        {
            Summary(Dev("alice")),
            Summary(Dev("bob")),
        };

        var result = RecommendationMapper.Map(summaries, []);

        Assert.Empty(result);
    }

    [Fact]
    public void Map_UsesTheContainingTeamName()
    {
        var team = new Team("Platform", [new DeveloperLogin("alice")]);
        var summaries = new List<ActivitySummary> { Summary(Dev("alice"), flags: [Flag(FlagKind.OverworkCommits, "Reason.")]) };

        var result = RecommendationMapper.Map(summaries, [team]);

        Assert.Equal("Platform", result[0].TeamName);
    }

    [Fact]
    public void Map_WhenDeveloperBelongsToNoTeam_FallsBackToNoTeam()
    {
        var team = new Team("Platform", [new DeveloperLogin("bob")]);
        var summaries = new List<ActivitySummary> { Summary(Dev("alice"), flags: [Flag(FlagKind.OverworkCommits, "Reason.")]) };

        var result = RecommendationMapper.Map(summaries, [team]);

        Assert.Equal("No team", result[0].TeamName);
    }

    // ---- RecommendationMapper (CheckInStatus overload) ------------------------------------

    private static CheckInStatus Status(Developer developer, IReadOnlyList<WellbeingFlag>? flags = null) =>
        new(developer, flags ?? []);

    [Fact]
    public void Map_CheckInStatusOverload_ToneOnlyDeveloperSurfacesAsARecommendation()
    {
        // Tone (and rushing) flags never reach ActivitySummary.Flags — they are merged in only when the
        // check-in roster is composed — so this proves the gap the CheckInStatus overload exists to close.
        var roster = new List<CheckInStatus>
        {
            Status(Dev("marlowe", "Marlowe Critique"), [Flag(FlagKind.NegativeTone, "Reason.")]),
        };

        var result = RecommendationMapper.Map(roster, []);

        var recommendation = Assert.Single(result);
        Assert.Equal("Marlowe Critique", recommendation.Developer.DisplayName);
        Assert.Equal("Check in on review climate", recommendation.Action);
    }

    [Fact]
    public void Map_CheckInStatusOverload_RushingOnlyDeveloperSurfacesAsARecommendation()
    {
        var roster = new List<CheckInStatus>
        {
            Status(Dev("river", "River Hurrybrook"), [Flag(FlagKind.PossibleRushing, "Reason.")]),
        };

        var result = RecommendationMapper.Map(roster, []);

        var recommendation = Assert.Single(result);
        Assert.Equal("River Hurrybrook", recommendation.Developer.DisplayName);
        Assert.Equal("Ease the pace pressure", recommendation.Action);
    }

    [Fact]
    public void Map_CheckInStatusOverload_OrdersByConcurrentSignalCountDescendingThenAlphabetical()
    {
        var roster = new List<CheckInStatus>
        {
            Status(Dev("zed", "Zed"), [Flag(FlagKind.OverworkCommits, "One.")]),
            Status(Dev("amy", "Amy"), [Flag(FlagKind.OverworkCommits, "One."), Flag(FlagKind.NegativeTone, "Two.")]),
            Status(Dev("bob", "Bob"), [Flag(FlagKind.OverworkCommits, "One.")]),
        };

        var result = RecommendationMapper.Map(roster, []);

        Assert.Equal(["Amy", "Bob", "Zed"], result.Select(r => r.Developer.DisplayName));
    }

    [Fact]
    public void Map_CheckInStatusOverload_WithMoreThanSixFlaggedDevelopers_CapsAtSix()
    {
        var roster = Enumerable.Range(0, 8)
            .Select(i => Status(Dev($"dev{i}"), [Flag(FlagKind.PossibleRushing, "Reason.")]))
            .ToList();

        var result = RecommendationMapper.Map(roster, []);

        Assert.Equal(6, result.Count);
    }

    [Fact]
    public void Map_CheckInStatusOverload_WithNoFlaggedDevelopers_ReturnsEmpty()
    {
        var roster = new List<CheckInStatus> { Status(Dev("alice")), Status(Dev("bob")) };

        var result = RecommendationMapper.Map(roster, []);

        Assert.Empty(result);
    }

    [Fact]
    public void Map_CheckInStatusOverload_UsesTheContainingTeamName()
    {
        var team = new Team("Platform", [new DeveloperLogin("alice")]);
        var roster = new List<CheckInStatus> { Status(Dev("alice"), [Flag(FlagKind.OverworkCommits, "Reason.")]) };

        var result = RecommendationMapper.Map(roster, [team]);

        Assert.Equal("Platform", result[0].TeamName);
    }

    // ---- TrendCalculator -------------------------------------------------------------------

    [Fact]
    public void Calculate_WithRisingActivity_ProducesUpStatementWithoutCaution()
    {
        // First quarter (3 weeks) averages 10, last quarter averages 12 -> +20%, above flat, at/below steep threshold.
        int[] weeks = [10, 10, 10, 11, 11, 11, 12, 12, 12, 12, 12, 12];
        var options = new WellnessOptions { TrendWeeks = 12 };

        var result = TrendCalculator.Calculate(weeks, options);

        Assert.Contains("up ~20%", result.ChangeStatement, StringComparison.Ordinal);
        Assert.DoesNotContain("ramp this steep", result.ChangeStatement, StringComparison.Ordinal);
        Assert.Contains("Series shows weekly relative commits.", result.ChangeStatement, StringComparison.Ordinal);
    }

    [Fact]
    public void Calculate_WithSteepRiseAboveTwentyFivePercent_AppendsSteepRampCaution()
    {
        // First quarter averages 10, last quarter averages 15 -> +50%, above the steep-ramp threshold.
        int[] weeks = [10, 10, 10, 12, 12, 12, 13, 13, 13, 15, 15, 15];
        var options = new WellnessOptions { TrendWeeks = 12 };

        var result = TrendCalculator.Calculate(weeks, options);

        Assert.Contains("up ~50%", result.ChangeStatement, StringComparison.Ordinal);
        Assert.Contains(
            "A ramp this steep is itself worth watching — sustained sprints often precede burnout.",
            result.ChangeStatement, StringComparison.Ordinal);
    }

    [Fact]
    public void Calculate_WithFallingActivity_ProducesDownStatement()
    {
        // First quarter averages 20, last quarter averages 10 -> -50%.
        int[] weeks = [20, 20, 20, 18, 16, 14, 12, 12, 12, 10, 10, 10];
        var options = new WellnessOptions { TrendWeeks = 12 };

        var result = TrendCalculator.Calculate(weeks, options);

        Assert.Contains("down ~50%", result.ChangeStatement, StringComparison.Ordinal);
        Assert.DoesNotContain("up ~", result.ChangeStatement, StringComparison.Ordinal);
    }

    [Fact]
    public void Calculate_WithChangeAtFivePercentBoundary_ReadsAsSteady()
    {
        // First quarter averages 100, last quarter averages 105 -> +5%, at the flat boundary (<= 5).
        int[] weeks = [100, 100, 100, 100, 100, 100, 100, 100, 105, 105, 105, 105];
        var options = new WellnessOptions { TrendWeeks = 12 };

        var result = TrendCalculator.Calculate(weeks, options);

        Assert.Equal("Commit activity is steady across the window. Series shows weekly relative commits.", result.ChangeStatement);
    }

    [Fact]
    public void Calculate_WithChangeJustAboveFivePercentBoundary_ReadsAsRising()
    {
        // First quarter averages 100, last quarter averages 106 -> +6%, just above the flat boundary.
        int[] weeks = [100, 100, 100, 100, 100, 100, 100, 100, 106, 106, 106, 106];
        var options = new WellnessOptions { TrendWeeks = 12 };

        var result = TrendCalculator.Calculate(weeks, options);

        Assert.Contains("up ~6%", result.ChangeStatement, StringComparison.Ordinal);
    }

    [Fact]
    public void Calculate_WithEmptySeries_ReturnsNotEnoughHistoryAndEmptySeries()
    {
        var options = new WellnessOptions { TrendWeeks = 12 };

        var result = TrendCalculator.Calculate([], options);

        Assert.Equal("Not enough history for a trend yet.", result.ChangeStatement);
        Assert.Empty(result.WeeklyCommits);
    }

    [Fact]
    public void Calculate_WithAllZeroSeries_ReturnsNotEnoughHistoryAndOriginalSeries()
    {
        int[] weeks = [0, 0, 0, 0];
        var options = new WellnessOptions { TrendWeeks = 12 };

        var result = TrendCalculator.Calculate(weeks, options);

        Assert.Equal("Not enough history for a trend yet.", result.ChangeStatement);
        Assert.Equal(weeks, result.WeeklyCommits);
    }

    [Fact]
    public void Calculate_WithMoreThanTrendWeeksEntries_TrimsToTheMostRecentTrendWeeks()
    {
        // 16 weeks of history but TrendWeeks = 12: only the last 12 (5..20 step 1, i.e. values 5..20) should remain.
        int[] weeks = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16];
        var options = new WellnessOptions { TrendWeeks = 12 };

        var result = TrendCalculator.Calculate(weeks, options);

        Assert.Equal(12, result.WeeklyCommits.Count);
        Assert.Equal(Enumerable.Range(5, 12), result.WeeklyCommits);
    }

    // ---- TeamCardBuilder -------------------------------------------------------------------

    private static readonly DateTimeOffset PeriodEnd = new(2024, 1, 15, 0, 0, 0, TimeSpan.Zero);
    private static readonly Period FourteenDayPeriod = new(14, PeriodEnd);

    [Fact]
    public void Build_ProducesOneCardPerTeamOrderedByName()
    {
        var teams = new List<Team>
        {
            new("Zeta", [new DeveloperLogin("alice")]),
            new("Alpha", [new DeveloperLogin("bob")]),
        };
        var summaries = new List<ActivitySummary> { Summary(Dev("alice")), Summary(Dev("bob")) };
        var events = new List<ActivityEvent>();

        var cards = TeamCardBuilder.Build(teams, summaries, events, DefaultOptions, FourteenDayPeriod);

        Assert.Equal(["Alpha", "Zeta"], cards.Select(c => c.Name));
    }

    [Fact]
    public void Build_SizeCountsOnlyRosterMembersPresentAmongSummaries()
    {
        var team = new Team("Platform", [new DeveloperLogin("alice"), new DeveloperLogin("ghost")]);
        var summaries = new List<ActivitySummary> { Summary(Dev("alice")) };
        var events = new List<ActivityEvent>();

        var cards = TeamCardBuilder.Build([team], summaries, events, DefaultOptions, FourteenDayPeriod);

        Assert.Equal(1, cards[0].Size);
    }

    [Fact]
    public void Build_WhenNoDeveloperBelongsToNoTeam_OmitsTheNoTeamCard()
    {
        var team = new Team("Platform", [new DeveloperLogin("alice")]);
        var summaries = new List<ActivitySummary> { Summary(Dev("alice")) };
        var events = new List<ActivityEvent>();

        var cards = TeamCardBuilder.Build([team], summaries, events, DefaultOptions, FourteenDayPeriod);

        Assert.DoesNotContain(cards, c => c.Name == "No team");
    }

    [Fact]
    public void Build_WhenADeveloperBelongsToNoTeam_AppendsANoTeamCardWithThatDeveloper()
    {
        var team = new Team("Platform", [new DeveloperLogin("alice")]);
        var summaries = new List<ActivitySummary> { Summary(Dev("alice")), Summary(Dev("carol")) };
        var events = new List<ActivityEvent>();

        var cards = TeamCardBuilder.Build([team], summaries, events, DefaultOptions, FourteenDayPeriod);

        var noTeamCard = Assert.Single(cards, c => c.Name == "No team");
        Assert.Equal(1, noTeamCard.Size);
    }

    [Fact]
    public void Build_AfterHoursShare_IsTheRatioOfTeamOutOfHoursCommitsToTeamCommits()
    {
        var alice = new DeveloperLogin("alice");
        var team = new Team("Platform", [alice]);
        var summaries = new List<ActivitySummary> { Summary(Dev("alice")) };
        // Monday 10:00 UTC is in hours; Monday 21:00 UTC is out of hours (working hours default 09:00-18:00).
        var monday = new DateTimeOffset(2024, 1, 8, 0, 0, 0, TimeSpan.Zero);
        var events = new List<ActivityEvent>
        {
            CommitAt(alice, "proj", monday.AddHours(10), "sha-1"),
            CommitAt(alice, "proj", monday.AddHours(10), "sha-2"),
            CommitAt(alice, "proj", monday.AddHours(21), "sha-3"),
            CommitAt(alice, "proj", monday.AddHours(21), "sha-4"),
        };

        var cards = TeamCardBuilder.Build([team], summaries, events, DefaultOptions, FourteenDayPeriod);

        Assert.Equal(0.5m, cards[0].AfterHoursShare);
    }

    [Fact]
    public void Build_AfterHoursShare_IsNullWhenTheTeamHasZeroCommits()
    {
        var team = new Team("Platform", [new DeveloperLogin("alice")]);
        var summaries = new List<ActivitySummary> { Summary(Dev("alice")) };
        var events = new List<ActivityEvent>();

        var cards = TeamCardBuilder.Build([team], summaries, events, DefaultOptions, FourteenDayPeriod);

        Assert.Null(cards[0].AfterHoursShare);
    }

    [Fact]
    public void Build_AfterHoursShare_DeduplicatesCommitsBySha()
    {
        var alice = new DeveloperLogin("alice");
        var team = new Team("Platform", [alice]);
        var summaries = new List<ActivitySummary> { Summary(Dev("alice")) };
        var monday = new DateTimeOffset(2024, 1, 8, 10, 0, 0, TimeSpan.Zero);
        var events = new List<ActivityEvent>
        {
            CommitAt(alice, "proj", monday, "shared-sha"),
            CommitAt(alice, "proj", monday, "shared-sha"),
            CommitAt(alice, "proj", monday, "shared-sha"),
        };

        var cards = TeamCardBuilder.Build([team], summaries, events, DefaultOptions, FourteenDayPeriod);

        Assert.Equal(0m, cards[0].AfterHoursShare); // one deduplicated in-hours commit
    }

    [Fact]
    public void Build_AvgProjectsInFlight_AveragesOnlyNonNullMemberCounts()
    {
        var team = new Team("Platform", [new DeveloperLogin("alice"), new DeveloperLogin("bob")]);
        var summaries = new List<ActivitySummary>
        {
            Summary(Dev("alice"), distinctProjectCount: 4),
            Summary(Dev("bob"), distinctProjectCount: null), // project scope: excluded from the average
        };
        var events = new List<ActivityEvent>();

        var cards = TeamCardBuilder.Build([team], summaries, events, DefaultOptions, FourteenDayPeriod);

        Assert.Equal(4m, cards[0].AvgProjectsInFlight);
    }

    [Fact]
    public void Build_AvgProjectsInFlight_IsNullWhenNoMemberHasACount()
    {
        var team = new Team("Platform", [new DeveloperLogin("alice")]);
        var summaries = new List<ActivitySummary> { Summary(Dev("alice"), distinctProjectCount: null) };
        var events = new List<ActivityEvent>();

        var cards = TeamCardBuilder.Build([team], summaries, events, DefaultOptions, FourteenDayPeriod);

        Assert.Null(cards[0].AvgProjectsInFlight);
    }

    [Fact]
    public void Build_AvgReviewsPerDev_AveragesReviewCountAcrossMembers()
    {
        var team = new Team("Platform", [new DeveloperLogin("alice"), new DeveloperLogin("bob")]);
        var summaries = new List<ActivitySummary>
        {
            Summary(Dev("alice"), reviewCount: 10),
            Summary(Dev("bob"), reviewCount: 4),
        };
        var events = new List<ActivityEvent>();

        var cards = TeamCardBuilder.Build([team], summaries, events, DefaultOptions, FourteenDayPeriod);

        Assert.Equal(7m, cards[0].AvgReviewsPerDev);
    }

    [Fact]
    public void Build_AvgReviewsPerDev_IsNullWhenTheTeamHasNoSummarisedMembers()
    {
        var team = new Team("Platform", [new DeveloperLogin("ghost")]);
        var summaries = new List<ActivitySummary>();
        var events = new List<ActivityEvent>();

        var cards = TeamCardBuilder.Build([team], summaries, events, DefaultOptions, FourteenDayPeriod);

        Assert.Null(cards[0].AvgReviewsPerDev);
    }

    [Fact]
    public void Build_TopFlagged_OrdersByFlagCountDescendingThenAlphabeticalCappedAtThree()
    {
        var logins = new List<DeveloperLogin>
        {
            new("amy"), new("bob"), new("cara"), new("dave"),
        };
        var team = new Team("Platform", logins);
        var summaries = new List<ActivitySummary>
        {
            Summary(Dev("amy", "Amy"), flags: [Flag(FlagKind.OverworkCommits, "r.")]),
            Summary(Dev("bob", "Bob"), flags: [Flag(FlagKind.OverworkCommits, "r."), Flag(FlagKind.SpreadThin, "r.")]),
            Summary(Dev("cara", "Cara"), flags: [Flag(FlagKind.OverworkCommits, "r.")]),
            Summary(Dev("dave", "Dave")), // no flags: excluded
        };
        var events = new List<ActivityEvent>();

        var cards = TeamCardBuilder.Build([team], summaries, events, DefaultOptions, FourteenDayPeriod);

        Assert.Equal(3, cards[0].TopFlagged.Count);
        Assert.Equal(["Bob", "Amy", "Cara"], cards[0].TopFlagged.Select(m => m.Developer.DisplayName));
        Assert.Equal(2, cards[0].TopFlagged[0].FlagCount);
    }

    [Fact]
    public void Build_TopFlagged_ExcludesMembersWithNoFlags()
    {
        var team = new Team("Platform", [new DeveloperLogin("alice")]);
        var summaries = new List<ActivitySummary> { Summary(Dev("alice")) };
        var events = new List<ActivityEvent>();

        var cards = TeamCardBuilder.Build([team], summaries, events, DefaultOptions, FourteenDayPeriod);

        Assert.Empty(cards[0].TopFlagged);
    }

    [Theory]
    [InlineData(7, 1)]
    [InlineData(14, 2)]
    [InlineData(30, 5)]
    public void Build_WeeklySeries_HasCeilingOfPeriodDaysOverSevenBuckets(int periodDays, int expectedBucketCount)
    {
        var team = new Team("Platform", [new DeveloperLogin("alice")]);
        var summaries = new List<ActivitySummary> { Summary(Dev("alice")) };
        var events = new List<ActivityEvent>();
        var period = new Period(periodDays, PeriodEnd);

        var cards = TeamCardBuilder.Build([team], summaries, events, DefaultOptions, period);

        Assert.Equal(expectedBucketCount, cards[0].WeeklySeries.Count);
    }

    [Fact]
    public void Build_WeeklySeries_BucketsCommitsIntoTheCorrectWeekOldestFirst()
    {
        var alice = new DeveloperLogin("alice");
        var team = new Team("Platform", [alice]);
        var summaries = new List<ActivitySummary> { Summary(Dev("alice")) };
        // Fourteen-day period ending at PeriodEnd (2024-01-15): older bucket [01-01, 01-08), newer bucket [01-08, 01-15).
        var events = new List<ActivityEvent>
        {
            CommitAt(alice, "proj", new DateTimeOffset(2024, 1, 2, 10, 0, 0, TimeSpan.Zero), "sha-old-1"),
            CommitAt(alice, "proj", new DateTimeOffset(2024, 1, 3, 10, 0, 0, TimeSpan.Zero), "sha-old-2"),
            CommitAt(alice, "proj", new DateTimeOffset(2024, 1, 10, 10, 0, 0, TimeSpan.Zero), "sha-new-1"),
        };

        var cards = TeamCardBuilder.Build([team], summaries, events, DefaultOptions, FourteenDayPeriod);

        Assert.Equal([2, 1], cards[0].WeeklySeries);
    }

    [Fact]
    public void Build_WeeklySeries_CountsOnlyCommitsFromTheTeamsOwnMembers()
    {
        var alice = new DeveloperLogin("alice");
        var outsider = new DeveloperLogin("outsider");
        var team = new Team("Platform", [alice]);
        var summaries = new List<ActivitySummary> { Summary(Dev("alice")), Summary(Dev("outsider")) };
        var events = new List<ActivityEvent>
        {
            CommitAt(alice, "proj", new DateTimeOffset(2024, 1, 10, 10, 0, 0, TimeSpan.Zero), "sha-alice"),
            CommitAt(outsider, "proj", new DateTimeOffset(2024, 1, 10, 10, 0, 0, TimeSpan.Zero), "sha-outsider"),
        };

        var cards = TeamCardBuilder.Build([team], summaries, events, DefaultOptions, FourteenDayPeriod);

        Assert.Equal(1, cards[0].WeeklySeries[^1]);
    }
}
