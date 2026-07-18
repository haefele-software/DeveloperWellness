using DeveloperWellness.Application.Ports;
using DeveloperWellness.Application.Services;
using DeveloperWellness.Domain.Model;
using DeveloperWellness.Domain.Options;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace DeveloperWellness.UnitTests;

/// <summary>
/// Coverage for <see cref="DashboardQueryService"/>'s single-flight coalescing (startup-load fix): on a
/// cold cache, concurrent <see cref="DashboardQueryService.GetAsync"/> callers for the same scope and
/// period share one underlying fetch instead of each starting their own — the stampede that, combined with
/// Blazor prerendering blocking on the result, made a cold live start take minutes.
/// <see cref="DashboardQueryService.HasCachedSnapshot"/> is the cache probe the eager-load gate uses to
/// decide whether a page's first load is safe to run during prerendering; its false-then-true transition
/// around a load is covered here too.
/// </summary>
public class DashboardQueryServiceConcurrencyTests
{
    private static readonly ScopeKey OrgScope = ScopeKey.Organisation;
    private static readonly WellnessOptions Options = new() { OrganisationTimeZone = "UTC" };

    private static DashboardQueryService CreateService(IActivitySource activitySource) =>
        new(activitySource, new MemoryCache(new MemoryCacheOptions()), Microsoft.Extensions.Options.Options.Create(Options));

    private static ActivityDataset EmptyDataset(DateTimeOffset loadedAt) => new(
        roster: [],
        projects: [],
        teams: [],
        events: [],
        weeklyCommitCounts: [],
        coveredProjectNames: [],
        loadedAt: loadedAt,
        isDemoData: false);

    [Fact]
    public async Task GetAsync_TwoConcurrentCallsOnAColdCache_InvokesTheActivitySourceExactlyOnceAndBothCallersShareTheResult()
    {
        var source = new GatedActivitySource();
        var service = CreateService(source);

        // Neither call can complete yet — the source is gated — so if coalescing works, starting the
        // second call before releasing the gate must not have started a second fetch.
        var firstCall = service.GetAsync(OrgScope, 14, CancellationToken.None);
        var secondCall = service.GetAsync(OrgScope, 14, CancellationToken.None);

        Assert.Equal(1, source.CallCount);

        var dataset = EmptyDataset(DateTimeOffset.UtcNow);
        source.Release(dataset);

        var firstResult = await firstCall;
        var secondResult = await secondCall;

        Assert.Equal(1, source.CallCount);
        Assert.Equal(DashboardErrorKind.None, firstResult.Kind);
        Assert.Equal(DashboardErrorKind.None, secondResult.Kind);
        // Both joiners observe the exact same snapshot instance produced by the one shared fetch.
        Assert.Same(firstResult.Snapshot, secondResult.Snapshot);
    }

    [Fact]
    public async Task RefreshAsync_AfterACompletedLoad_ForcesASecondActivitySourceInvocationRatherThanServingTheCache()
    {
        var source = new QueueActivitySource();
        source.Enqueue(EmptyDataset(DateTimeOffset.UtcNow));
        source.Enqueue(EmptyDataset(DateTimeOffset.UtcNow));

        var service = CreateService(source);

        var initial = await service.GetAsync(OrgScope, 14, CancellationToken.None);
        Assert.Equal(1, source.CallCount);
        Assert.Equal(DashboardErrorKind.None, initial.Kind);

        // A second GetAsync call would be a cache hit and must not be used to prove this — RefreshAsync
        // itself is what must bypass the cache unconditionally.
        var refreshed = await service.RefreshAsync(OrgScope, 14, CancellationToken.None);

        Assert.Equal(2, source.CallCount);
        Assert.Equal(DashboardErrorKind.None, refreshed.Kind);
    }

    [Fact]
    public async Task HasCachedSnapshot_IsFalseBeforeALoadAndTrueAfterOneCompletes()
    {
        var source = new QueueActivitySource();
        source.Enqueue(EmptyDataset(DateTimeOffset.UtcNow));

        var service = CreateService(source);

        Assert.False(service.HasCachedSnapshot(OrgScope, 14));

        await service.GetAsync(OrgScope, 14, CancellationToken.None);

        Assert.True(service.HasCachedSnapshot(OrgScope, 14));
    }

    /// <summary>
    /// An <see cref="IActivitySource"/> whose single call is held open behind a <see cref="TaskCompletionSource{TResult}"/>
    /// until <see cref="Release"/> is called, so a test can assert nothing else reached the source while a
    /// first call is still in flight before letting it complete.
    /// </summary>
    private sealed class GatedActivitySource : IActivitySource
    {
        private readonly TaskCompletionSource<ActivityDataset> _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int CallCount { get; private set; }

        public Task<ActivityDataset> GetActivityAsync(ScopeKey scope, Period period, CancellationToken cancellationToken)
        {
            CallCount++;
            return _gate.Task;
        }

        public void Release(ActivityDataset dataset) => _gate.SetResult(dataset);
    }

    /// <summary>A simple <see cref="IActivitySource"/> stub returning pre-enqueued datasets in call order, counting how many times it was called.</summary>
    private sealed class QueueActivitySource : IActivitySource
    {
        private readonly Queue<ActivityDataset> _queue = new();

        public int CallCount { get; private set; }

        public void Enqueue(ActivityDataset dataset) => _queue.Enqueue(dataset);

        public Task<ActivityDataset> GetActivityAsync(ScopeKey scope, Period period, CancellationToken cancellationToken)
        {
            CallCount++;

            if (_queue.Count == 0)
            {
                throw new InvalidOperationException("QueueActivitySource ran out of enqueued datasets.");
            }

            return Task.FromResult(_queue.Dequeue());
        }
    }
}
