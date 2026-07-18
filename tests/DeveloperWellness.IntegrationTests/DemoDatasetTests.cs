using DeveloperWellness.Domain.Model;
using DeveloperWellness.Infrastructure.Demo;

namespace DeveloperWellness.IntegrationTests;

/// <summary>
/// Verifies the fixed-seed demo dataset built by <see cref="DemoActivitySource"/> (T008): determinism,
/// and that every seeded wellbeing case actually trips the arithmetic its flag would evaluate. Logins are
/// duplicated here as string constants (rather than referenced from the internal <c>DemoSeed</c> type)
/// because they cross the assembly boundary; matching is by <see cref="DeveloperLogin"/> value equality.
/// </summary>
public class DemoDatasetTests
{
    private static readonly DeveloperLogin NovaLogin = new("nova-stardust-demo");
    private static readonly DeveloperLogin RemyLogin = new("remy-afterglow-demo");
    private static readonly DeveloperLogin JuniperLogin = new("juniper-dataforge-demo");
    private static readonly DeveloperLogin MarloweLogin = new("marlowe-critique-demo");
    private static readonly DeveloperLogin RiverLogin = new("river-hurrybrook-demo");
    private static readonly DeveloperLogin SableLogin = new("sable-querywise-demo");
    private static readonly DeveloperLogin DexLogin = new("dex-quietstorm-demo");
    private static readonly DeveloperLogin KodaLogin = new("koda-driftwood-demo");
    private static readonly DeveloperLogin BrynnLogin = new("brynn-edgecase-demo");
    private static readonly DeveloperLogin PulseBotLogin = new("pulsebot-ci-demo");

    private const string ProjectScopeName = "pulse-api-demo";

    private static Period FourteenDayPeriod() => new(14, DateTimeOffset.UtcNow);

    private static Period SevenDayPeriod() => new(7, DateTimeOffset.UtcNow);

    /// <summary>
    /// True when <paramref name="occurredAt"/> falls outside the configured working hours (09:00-18:00,
    /// Monday-Friday), read directly off the timestamp's own offset. Every event in this dataset carries
    /// its relevant local offset (author-local for commits, organisation UTC+02:00 for PR events)
    /// embedded directly, so no timezone conversion step is needed here.
    /// </summary>
    private static bool IsOutOfHours(DateTimeOffset occurredAt)
    {
        if (occurredAt.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            return true;
        }

        return occurredAt.Hour < 9 || occurredAt.Hour >= 18;
    }

    private static double Median(IReadOnlyCollection<int> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        var mid = sorted.Count / 2;

        return sorted.Count % 2 == 0
            ? (sorted[mid - 1] + sorted[mid]) / 2.0
            : sorted[mid];
    }

    [Fact]
    public async Task GetActivityAsync_CalledTwiceWithSameScopeAndPeriod_ProducesIdenticalRosterEventCountsAndWeeklySeries()
    {
        var source = new DemoActivitySource();
        var period = FourteenDayPeriod();

        var first = await source.GetActivityAsync(ScopeKey.Organisation, period, CancellationToken.None);
        var second = await source.GetActivityAsync(ScopeKey.Organisation, period, CancellationToken.None);

        Assert.Equal(first.Roster.Select(d => d.Login), second.Roster.Select(d => d.Login));
        Assert.Equal(first.Events.Count, second.Events.Count);
        Assert.Equal(first.Events, second.Events);
        Assert.Equal(first.WeeklyCommitCounts, second.WeeklyCommitCounts);
    }

    [Fact]
    public async Task GetActivityAsync_OverworkCommitsCase_NovasOutOfHoursCommitShareExceedsQuarter()
    {
        var source = new DemoActivitySource();
        var dataset = await source.GetActivityAsync(ScopeKey.Organisation, FourteenDayPeriod(), CancellationToken.None);

        var novaCommits = dataset.Events.OfType<CommitEvent>().Where(e => e.Author == NovaLogin).ToList();
        var outOfHoursShare = (double)novaCommits.Count(e => IsOutOfHours(e.OccurredAt)) / novaCommits.Count;

        Assert.True(novaCommits.Count > 0);
        Assert.All(novaCommits, e => Assert.True(e.HasUsableOffset));
        Assert.True(outOfHoursShare > 0.25, $"Expected out-of-hours share > 0.25 but was {outOfHoursShare}.");
    }

    [Fact]
    public async Task GetActivityAsync_OverworkPrActivityCase_RemysOutOfHoursPrShareExceedsQuarter()
    {
        var source = new DemoActivitySource();
        var dataset = await source.GetActivityAsync(ScopeKey.Organisation, FourteenDayPeriod(), CancellationToken.None);

        var remyPrEvents = dataset.Events
            .Where(e => e.Author == RemyLogin && e is ReviewEvent or PrOpenedEvent)
            .ToList();
        var outOfHoursShare = (double)remyPrEvents.Count(e => IsOutOfHours(e.OccurredAt)) / remyPrEvents.Count;

        Assert.True(remyPrEvents.Count >= 3, "PR-overwork requires at least 3 PR events (MinPrEvents).");
        Assert.True(outOfHoursShare > 0.25, $"Expected out-of-hours PR share > 0.25 but was {outOfHoursShare}.");
    }

    [Fact]
    public async Task GetActivityAsync_MultipleReviewRoundsCase_SamePrReceivesTwoReviewEventsFromRemy()
    {
        var source = new DemoActivitySource();
        var dataset = await source.GetActivityAsync(ScopeKey.Organisation, FourteenDayPeriod(), CancellationToken.None);

        var reviewsByPr = dataset.Events.OfType<ReviewEvent>()
            .Where(e => e.Author == RemyLogin)
            .GroupBy(e => e.PrNumber)
            .ToList();

        Assert.Contains(reviewsByPr, g => g.Count() >= 2);
    }

    [Fact]
    public async Task GetActivityAsync_SpreadThinCase_JuniperHasActivityInAtLeastFourDistinctProjects()
    {
        var source = new DemoActivitySource();
        var dataset = await source.GetActivityAsync(ScopeKey.Organisation, FourteenDayPeriod(), CancellationToken.None);

        var distinctProjects = dataset.Events
            .Where(e => e.Author == JuniperLogin)
            .Select(e => e.ProjectName)
            .Distinct()
            .Count();

        Assert.True(distinctProjects >= 4, $"Expected at least 4 distinct projects but found {distinctProjects}.");
    }

    [Fact]
    public async Task GetActivityAsync_FrustratedCommenterCase_MarloweAuthorsAtLeastTenComments()
    {
        var source = new DemoActivitySource();
        var dataset = await source.GetActivityAsync(ScopeKey.Organisation, FourteenDayPeriod(), CancellationToken.None);

        var marloweComments = dataset.Events.OfType<CommentEvent>().Where(e => e.Author == MarloweLogin).ToList();

        Assert.True(marloweComments.Count >= 10, $"Expected at least 10 comments but found {marloweComments.Count}.");
        Assert.All(marloweComments, e => Assert.False(string.IsNullOrWhiteSpace(e.BodyText)));
    }

    [Fact]
    public async Task GetActivityAsync_PossibleRushingCase_RiverHasSufficientPrsAndHighChangesRequestedShare()
    {
        var source = new DemoActivitySource();
        var dataset = await source.GetActivityAsync(ScopeKey.Organisation, FourteenDayPeriod(), CancellationToken.None);

        var riverPrNumbers = dataset.Events.OfType<PrOpenedEvent>()
            .Where(e => e.Author == RiverLogin)
            .Select(e => e.PrNumber)
            .ToHashSet();
        var reviewsOnRiversPrs = dataset.Events.OfType<ReviewEvent>()
            .Where(e => riverPrNumbers.Contains(e.PrNumber))
            .ToList();
        var changesRequestedShare = (double)reviewsOnRiversPrs.Count(e => e.State == ReviewState.ChangesRequested) / reviewsOnRiversPrs.Count;

        Assert.True(riverPrNumbers.Count >= 3, "Possible rushing requires at least 3 PRs opened (MinPrSample).");
        Assert.True(changesRequestedShare > 0.40, $"Expected changes-requested share > 0.40 but was {changesRequestedShare}.");
    }

    [Fact]
    public async Task GetActivityAsync_PossibleRushingCase_RiversVolumeIsAboveTheRosterMedian()
    {
        var source = new DemoActivitySource();
        var dataset = await source.GetActivityAsync(ScopeKey.Organisation, FourteenDayPeriod(), CancellationToken.None);

        var volumeByLogin = dataset.Roster
            .Where(d => !d.IsBot)
            .ToDictionary(
                d => d.Login,
                d => dataset.Events.Count(e => e.Author == d.Login && e is CommitEvent)
                     + dataset.Events.Count(e => e.Author == d.Login && e is PrOpenedEvent));

        var median = Median(volumeByLogin.Values.ToList());

        Assert.True(
            volumeByLogin[RiverLogin] > median,
            $"Expected River's volume ({volumeByLogin[RiverLogin]}) to exceed the roster median ({median}).");
    }

    /// <summary>
    /// Seeded case: steady quality vs quantity (T046 gap fix). Sable opens exactly 3 PRs — meeting the
    /// minimum-3 sample — with only 1 of 3 receiving a changes-requested review (33%, below the 40%
    /// rushing threshold), and her total volume (commits + PRs opened) stays strictly below River's 13, so
    /// the sufficient-sample, not-flagged "steady" state (ui-design.md 4.6) has a developer to render for.
    /// </summary>
    [Fact]
    public async Task GetActivityAsync_SteadyQualityCase_SableHasSufficientSampleLowChangesRequestedShareAndVolumeBelowRiver()
    {
        var source = new DemoActivitySource();
        var dataset = await source.GetActivityAsync(ScopeKey.Organisation, FourteenDayPeriod(), CancellationToken.None);

        var sablePrOpens = dataset.Events.OfType<PrOpenedEvent>().Where(e => e.Author == SableLogin).ToList();
        var sablePrNumbers = sablePrOpens.Select(e => e.PrNumber).ToHashSet();
        var reviewsOnSablesPrs = dataset.Events.OfType<ReviewEvent>()
            .Where(e => sablePrNumbers.Contains(e.PrNumber))
            .ToList();
        var changesRequestedShare = (double)reviewsOnSablesPrs.Count(e => e.State == ReviewState.ChangesRequested) / reviewsOnSablesPrs.Count;

        Assert.True(sablePrNumbers.Count >= 3, $"Expected at least 3 PRs opened (MinPrSample) but found {sablePrNumbers.Count}.");
        Assert.True(changesRequestedShare < 0.40, $"Expected changes-requested share < 0.40 but was {changesRequestedShare}.");
        Assert.All(sablePrOpens, e => Assert.False(IsOutOfHours(e.OccurredAt), $"Expected PR #{e.PrNumber} to be opened in hours."));
        Assert.All(reviewsOnSablesPrs, e => Assert.False(IsOutOfHours(e.OccurredAt), $"Expected the review on PR #{e.PrNumber} to be in hours."));

        var sableVolume = dataset.Events.Count(e => e.Author == SableLogin && e is CommitEvent)
            + dataset.Events.Count(e => e.Author == SableLogin && e is PrOpenedEvent);
        var riverVolume = dataset.Events.Count(e => e.Author == RiverLogin && e is CommitEvent)
            + dataset.Events.Count(e => e.Author == RiverLogin && e is PrOpenedEvent);

        Assert.True(
            sableVolume < riverVolume,
            $"Expected Sable's volume ({sableVolume}) to stay below River's ({riverVolume}).");
    }

    [Fact]
    public async Task GetActivityAsync_NoActivityMembers_HaveZeroEventsInThePeriod()
    {
        var source = new DemoActivitySource();
        var dataset = await source.GetActivityAsync(ScopeKey.Organisation, FourteenDayPeriod(), CancellationToken.None);

        DeveloperLogin[] noActivityLogins = [DexLogin, KodaLogin, BrynnLogin];

        Assert.Equal(3, noActivityLogins.Length);
        Assert.All(noActivityLogins, login => Assert.DoesNotContain(dataset.Events, e => e.Author == login));
        Assert.All(noActivityLogins, login => Assert.Contains(dataset.Roster, d => d.Login == login));
    }

    [Fact]
    public async Task GetActivityAsync_WeeklyCommitCounts_HasExactlyTwelveValuesWithRiseAboveQuarter()
    {
        var source = new DemoActivitySource();
        var dataset = await source.GetActivityAsync(ScopeKey.Organisation, FourteenDayPeriod(), CancellationToken.None);

        Assert.Equal(12, dataset.WeeklyCommitCounts.Count);

        var first = dataset.WeeklyCommitCounts[0];
        var last = dataset.WeeklyCommitCounts[^1];
        var rise = (last - first) / (double)first;

        Assert.True(rise > 0.25, $"Expected a rise above 0.25 but was {rise}.");
    }

    [Fact]
    public async Task GetActivityAsync_UnmatchedAuthorCase_AtLeastOneCommitEventCarriesTheUnmatchedSentinel()
    {
        var source = new DemoActivitySource();
        var dataset = await source.GetActivityAsync(ScopeKey.Organisation, FourteenDayPeriod(), CancellationToken.None);

        Assert.Contains(dataset.Events, e => e is CommitEvent && e.Author.IsUnmatched);
    }

    [Fact]
    public async Task GetActivityAsync_BotCase_RosterContainsABotWithAuthoredEvents()
    {
        var source = new DemoActivitySource();
        var dataset = await source.GetActivityAsync(ScopeKey.Organisation, FourteenDayPeriod(), CancellationToken.None);

        var bot = Assert.Single(dataset.Roster, d => d.Login == PulseBotLogin);

        Assert.True(bot.IsBot);
        Assert.Contains(dataset.Events, e => e.Author == PulseBotLogin);
    }

    [Fact]
    public async Task GetActivityAsync_ProjectScope_FiltersEventsAndProjectsToThatProjectOnly()
    {
        var source = new DemoActivitySource();
        var orgDataset = await source.GetActivityAsync(ScopeKey.Organisation, FourteenDayPeriod(), CancellationToken.None);
        var projectDataset = await source.GetActivityAsync(ScopeKey.Project(ProjectScopeName), FourteenDayPeriod(), CancellationToken.None);

        Assert.All(projectDataset.Events, e => Assert.Equal(ProjectScopeName, e.ProjectName));
        Assert.Equal([ProjectScopeName], projectDataset.CoveredProjectNames);
        Assert.Equal(ProjectScopeName, Assert.Single(projectDataset.Projects).Name);
        Assert.Equal(orgDataset.Roster.Count, projectDataset.Roster.Count);
        Assert.NotEmpty(projectDataset.Events);
    }

    [Fact]
    public async Task GetActivityAsync_SevenDayPeriod_EveryEventFallsWithinThePeriodBounds()
    {
        var source = new DemoActivitySource();
        var period = SevenDayPeriod();

        var dataset = await source.GetActivityAsync(ScopeKey.Organisation, period, CancellationToken.None);

        Assert.NotEmpty(dataset.Events);
        Assert.All(dataset.Events, e => Assert.InRange(e.OccurredAt, period.Start, period.End));
    }

    [Fact]
    public async Task GetActivityAsync_DoesNotThrow_ForAnyOfTheThreePeriodLengths()
    {
        var source = new DemoActivitySource();

        foreach (var days in new[] { 7, 14, 30 })
        {
            var dataset = await source.GetActivityAsync(ScopeKey.Organisation, new Period(days, DateTimeOffset.UtcNow), CancellationToken.None);

            Assert.True(dataset.IsDemoData);
            Assert.NotEmpty(dataset.Events);
        }
    }

    /// <summary>
    /// The generator classifies each of the last 6 calendar days before <c>period.End</c> as a weekday or
    /// weekend day by inspecting its real <see cref="DayOfWeek"/>, so the weekday/weekend split (and thus
    /// every seeded ratio) can differ slightly depending on which day of the week <c>period.End</c> falls
    /// on. This test pins <c>period.End</c> to all seven possible weekday phases (not just "whatever day
    /// the test happens to run on") so every seeded case's arithmetic is proven robust across the full
    /// cycle, not just today's.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public async Task GetActivityAsync_ForEveryPossibleWeekdayPhaseOfPeriodEnd_EverySeededCaseStillHolds(int dayOffset)
    {
        var source = new DemoActivitySource();
        var end = DateTimeOffset.UtcNow.AddDays(dayOffset);
        var period = new Period(14, end);

        var dataset = await source.GetActivityAsync(ScopeKey.Organisation, period, CancellationToken.None);

        var novaCommits = dataset.Events.OfType<CommitEvent>().Where(e => e.Author == NovaLogin).ToList();
        var novaShare = (double)novaCommits.Count(e => IsOutOfHours(e.OccurredAt)) / novaCommits.Count;
        Assert.True(novaShare > 0.25, $"[offset {dayOffset}] Nova's out-of-hours commit share was {novaShare}.");

        var remyPrEvents = dataset.Events.Where(e => e.Author == RemyLogin && e is ReviewEvent or PrOpenedEvent).ToList();
        var remyShare = (double)remyPrEvents.Count(e => IsOutOfHours(e.OccurredAt)) / remyPrEvents.Count;
        Assert.True(remyPrEvents.Count >= 3, $"[offset {dayOffset}] Remy had only {remyPrEvents.Count} PR events.");
        Assert.True(remyShare > 0.25, $"[offset {dayOffset}] Remy's out-of-hours PR share was {remyShare}.");

        var distinctJuniperProjects = dataset.Events.Where(e => e.Author == JuniperLogin).Select(e => e.ProjectName).Distinct().Count();
        Assert.True(distinctJuniperProjects >= 4, $"[offset {dayOffset}] Juniper touched {distinctJuniperProjects} projects.");

        var marloweComments = dataset.Events.OfType<CommentEvent>().Count(e => e.Author == MarloweLogin);
        Assert.True(marloweComments >= 10, $"[offset {dayOffset}] Marlowe authored {marloweComments} comments.");

        var riverPrNumbers = dataset.Events.OfType<PrOpenedEvent>().Where(e => e.Author == RiverLogin).Select(e => e.PrNumber).ToHashSet();
        var reviewsOnRiversPrs = dataset.Events.OfType<ReviewEvent>().Where(e => riverPrNumbers.Contains(e.PrNumber)).ToList();
        var changesRequestedShare = (double)reviewsOnRiversPrs.Count(e => e.State == ReviewState.ChangesRequested) / reviewsOnRiversPrs.Count;
        Assert.True(riverPrNumbers.Count >= 3, $"[offset {dayOffset}] River opened {riverPrNumbers.Count} PRs.");
        Assert.True(changesRequestedShare > 0.40, $"[offset {dayOffset}] River's changes-requested share was {changesRequestedShare}.");

        Assert.All(dataset.Events, e => Assert.InRange(e.OccurredAt, period.Start, period.End));
    }
}
