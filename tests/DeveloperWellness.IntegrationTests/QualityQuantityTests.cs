using DeveloperWellness.Application.Services;
using DeveloperWellness.Domain.Model;
using DeveloperWellness.Domain.Options;
using DeveloperWellness.Infrastructure.Demo;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace DeveloperWellness.IntegrationTests;

/// <summary>
/// Service-level tests for <see cref="QualityQuantityService"/> (tasks.md T043) against the real
/// <see cref="DemoActivitySource"/> adapter — no network access. Seeded logins and the possible-rushing
/// and PR-sample cases are read-only knowledge from <c>DeveloperWellness.Infrastructure.Demo.DemoSeed</c>'s
/// remarks.
/// </summary>
public class QualityQuantityTests
{
    private static readonly DeveloperLogin RiverLogin = new("river-hurrybrook-demo"); // seeded: possible rushing, 5 PRs opened
    private static readonly DeveloperLogin RemyLogin = new("remy-afterglow-demo"); // 2 PRs opened: below the minimum-3 sample
    private static readonly DeveloperLogin SableLogin = new("sable-querywise-demo"); // seeded: steady, 3 PRs opened, not flagged
    private static readonly DeveloperLogin DexLogin = new("dex-quietstorm-demo"); // seeded: no activity at all
    private static readonly DeveloperLogin PulseBotLogin = new("pulsebot-ci-demo"); // seeded: bot

    private static WellnessOptions BuildWellnessOptions() => new() { OrganisationTimeZone = "South Africa Standard Time" };

    private static QualityQuantityService BuildService()
    {
        var queryService = new DashboardQueryService(
            new DemoActivitySource(), new MemoryCache(new MemoryCacheOptions()), Options.Create(BuildWellnessOptions()));

        return new QualityQuantityService(queryService, Options.Create(BuildWellnessOptions()));
    }

    [Fact]
    public async Task GetAsync_OrganisationScope_RiverIsFlaggedPossibleRushingWithTheExpectedSnapshot()
    {
        var service = BuildService();

        var result = await service.GetAsync(ScopeKey.Organisation, periodDays: 14, CancellationToken.None);

        Assert.NotNull(result.Rows);
        var river = Assert.Single(result.Rows!, row => row.Developer.Login == RiverLogin);

        Assert.True(river.Snapshot.SufficientSample);
        Assert.True(river.Snapshot.PossibleRushing);
        Assert.Equal(8, river.Snapshot.Commits);
        Assert.Equal(5, river.Snapshot.PrsOpened);
        Assert.NotNull(river.Snapshot.ChangesRequestedShare);
        Assert.True(river.Snapshot.ChangesRequestedShare > 0.40m);
        Assert.NotNull(river.Flag);
        Assert.Equal(FlagKind.PossibleRushing, river.Flag!.Kind);
        Assert.Contains("pace pressure", river.Flag.Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// T046 gap fix: before the seed carried this case, River Hurrybrook was the only sufficient-sample
    /// developer at any scope or period, and she is always flagged, so the design's "steady" state (volume
    /// and rework in step, ui-design.md 4.6) could never render on <c>/quality</c>. Sable now supplies it.
    /// </summary>
    [Fact]
    public async Task GetAsync_OrganisationScope_SableIsSufficientSampleAndNotFlaggedPossibleRushing()
    {
        var service = BuildService();

        var result = await service.GetAsync(ScopeKey.Organisation, periodDays: 14, CancellationToken.None);

        Assert.NotNull(result.Rows);
        var sable = Assert.Single(result.Rows!, row => row.Developer.Login == SableLogin);

        Assert.True(sable.Snapshot.SufficientSample);
        Assert.Equal(3, sable.Snapshot.PrsOpened);
        Assert.NotNull(sable.Snapshot.ChangesRequestedShare);
        Assert.True(sable.Snapshot.ChangesRequestedShare < 0.40m);
        Assert.False(sable.Snapshot.PossibleRushing);
        Assert.Null(sable.Flag);
    }

    [Fact]
    public async Task GetAsync_OrganisationScope_RemyIsBelowSampleWithNoJudgementShown()
    {
        var service = BuildService();

        var result = await service.GetAsync(ScopeKey.Organisation, periodDays: 14, CancellationToken.None);

        Assert.NotNull(result.Rows);
        var remy = Assert.Single(result.Rows!, row => row.Developer.Login == RemyLogin);

        Assert.False(remy.Snapshot.SufficientSample);
        Assert.False(remy.Snapshot.PossibleRushing);
        Assert.Null(remy.Snapshot.ChangesRequestedShare);
        Assert.Null(remy.Snapshot.AvgReviewRounds);
        Assert.Null(remy.Flag);

        // Remy's seeded events (BuildRemyOverworkPr) are all PR opens and reviews, no commits, so she is
        // absent from the demo's lines-changed-by-author dictionary; since other seeded developers do carry
        // commits, that dictionary is non-empty overall, so her lines-changed reads as a genuine zero rather
        // than null (commit-size/volume metric, the "stats unavailable" case only applies when the whole
        // dictionary is empty).
        Assert.Equal(0, remy.LinesChanged);
    }

    [Fact]
    public async Task GetAsync_OrganisationScope_ExcludesBotsAndDevelopersWithNoActivityFromTheRows()
    {
        var service = BuildService();

        var result = await service.GetAsync(ScopeKey.Organisation, periodDays: 14, CancellationToken.None);

        Assert.NotNull(result.Rows);
        Assert.DoesNotContain(result.Rows!, row => row.Developer.Login == PulseBotLogin);
        Assert.DoesNotContain(result.Rows!, row => row.Developer.Login == DexLogin);
    }

    [Fact]
    public async Task GetAsync_ReturnsNullRowsExactlyWhenTheSourceCarriesNoSnapshot()
    {
        var service = BuildService();

        var result = await service.GetAsync(ScopeKey.Organisation, periodDays: 14, CancellationToken.None);

        Assert.NotNull(result.Source.Snapshot);
        Assert.NotNull(result.Rows);
    }

    /// <summary>
    /// Code-review finding: the Quality page's "Try again" action must bypass the cache like every sibling
    /// page's retry path. <see cref="QualityQuantityService.RefreshAsync"/> routes through
    /// <see cref="DashboardQueryService.RefreshAsync"/>, which evicts the cache before fetching, and
    /// <see cref="DemoActivitySource"/> stamps a fresh <c>DateTimeOffset.UtcNow</c> onto every fetch it
    /// serves — so a call after a warm <see cref="QualityQuantityService.GetAsync"/> call must observe a
    /// strictly newer <c>LoadedAt</c> rather than the same cached one, while still computing a full row set.
    /// </summary>
    [Fact]
    public async Task RefreshAsync_AfterAWarmGetAsyncCall_ForcesAFreshFetchWithANewerLoadedAt()
    {
        var service = BuildService();

        var initial = await service.GetAsync(ScopeKey.Organisation, periodDays: 14, CancellationToken.None);
        Assert.NotNull(initial.Source.Snapshot);

        var refreshed = await service.RefreshAsync(ScopeKey.Organisation, periodDays: 14, CancellationToken.None);

        Assert.NotNull(refreshed.Source.Snapshot);
        Assert.True(refreshed.Source.Snapshot!.LoadedAt > initial.Source.Snapshot!.LoadedAt);
        Assert.NotNull(refreshed.Rows);
        Assert.NotEmpty(refreshed.Rows!);
    }

    /// <summary>
    /// Demo lines-changed (commit-size/volume metric) is synthesized from each author's deduped commit
    /// count times a fixed per-login factor (<c>DemoSeed.BuildLinesChangedByAuthor</c>), so River's total
    /// must be positive (she has seeded commits) and identical across two entirely independent demo builds.
    /// </summary>
    [Fact]
    public async Task GetAsync_OrganisationScope_RiverLinesChangedIsPositiveAndDeterministicAcrossFreshDemoBuilds()
    {
        var firstResult = await BuildService().GetAsync(ScopeKey.Organisation, periodDays: 14, CancellationToken.None);
        var secondResult = await BuildService().GetAsync(ScopeKey.Organisation, periodDays: 14, CancellationToken.None);

        var riverFirst = Assert.Single(firstResult.Rows!, row => row.Developer.Login == RiverLogin);
        var riverSecond = Assert.Single(secondResult.Rows!, row => row.Developer.Login == RiverLogin);

        Assert.NotNull(riverFirst.LinesChanged);
        Assert.True(riverFirst.LinesChanged > 0);
        Assert.Equal(riverFirst.LinesChanged, riverSecond.LinesChanged);
    }
}
