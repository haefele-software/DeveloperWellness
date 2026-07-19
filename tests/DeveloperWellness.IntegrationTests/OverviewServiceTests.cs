using DeveloperWellness.Application.Services;
using DeveloperWellness.Domain.Model;
using DeveloperWellness.Domain.Options;
using DeveloperWellness.Infrastructure.Demo;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace DeveloperWellness.IntegrationTests;

/// <summary>
/// Service-level tests for <see cref="OverviewService.RefreshAsync"/> (code-review finding: the Overview
/// page's "Try again" action must bypass the cache like every sibling page's retry path) against the real
/// <see cref="DemoActivitySource"/> and <see cref="DemoAiInsightService"/> adapters — no network access.
/// Mirrors <see cref="QualityQuantityTests"/>'s direct-construction pattern.
/// </summary>
public class OverviewServiceTests
{
    private static WellnessOptions BuildWellnessOptions() => new() { OrganisationTimeZone = "South Africa Standard Time" };

    private static OverviewService BuildService()
    {
        var wellnessOptions = Options.Create(BuildWellnessOptions());
        var queryService = new DashboardQueryService(new DemoActivitySource(), new MemoryCache(new MemoryCacheOptions()), wellnessOptions);
        var toneAnalysisService = new ToneAnalysisService(queryService, new DemoAiInsightService(), new MemoryCache(new MemoryCacheOptions()), wellnessOptions);
        var checkInService = new CheckInService(queryService, toneAnalysisService, new DemoAiInsightService(), wellnessOptions);
        var alertService = new CheckInAlertService();

        return new OverviewService(checkInService, alertService, toneAnalysisService, wellnessOptions);
    }

    [Fact]
    public async Task GetAsync_CalledTwice_ReturnsTheSameCachedSnapshotBothTimes()
    {
        var service = BuildService();

        var first = await service.GetAsync(ScopeKey.Organisation, periodDays: 14, CancellationToken.None);
        var second = await service.GetAsync(ScopeKey.Organisation, periodDays: 14, CancellationToken.None);

        Assert.NotNull(first.Source.Snapshot);
        Assert.NotNull(second.Source.Snapshot);
        Assert.Equal(first.Source.Snapshot!.LoadedAt, second.Source.Snapshot!.LoadedAt);
    }

    /// <summary>
    /// Code-review finding: before this fix, <see cref="OverviewService"/> exposed only the cache-first
    /// <see cref="OverviewService.GetAsync"/>, so the Overview page's "Try again" button silently re-served
    /// the same cached snapshot instead of forcing a fresh fetch, unlike every sibling page's retry path.
    /// <see cref="OverviewService.RefreshAsync"/> routes through <see cref="CheckInService.RefreshRosterAsync"/>,
    /// which itself evicts the cache via <see cref="DashboardQueryService.RefreshAsync"/> before fetching —
    /// <see cref="DemoActivitySource"/> stamps a fresh <c>DateTimeOffset.UtcNow</c> onto every fetch it
    /// serves, so a call after a warm <see cref="OverviewService.GetAsync"/> call must observe a strictly
    /// newer <c>LoadedAt</c> rather than the same cached one.
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
    }

    /// <summary>Refreshing must still compose a full Overview snapshot, not just bridge the underlying dashboard result.</summary>
    [Fact]
    public async Task RefreshAsync_ComposesTheOverviewSnapshotJustLikeGetAsync()
    {
        var service = BuildService();

        var result = await service.RefreshAsync(ScopeKey.Organisation, periodDays: 14, CancellationToken.None);

        Assert.NotNull(result.Overview);
        Assert.NotEmpty(result.Overview!.ProjectRows);
    }
}
