using DeveloperWellness.Application.Ports;
using DeveloperWellness.Application.Services;
using DeveloperWellness.Domain.Model;
using DeveloperWellness.Domain.Options;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace DeveloperWellness.UnitTests;

/// <summary>
/// Service-level coverage for <see cref="DashboardQueryService"/>'s background retry loop (retry-success
/// UI wiring fix, part 2): that a scheduled retry's outcome always reaches <see cref="DashboardQueryService.Latest"/>
/// and <see cref="DashboardQueryService.StateChanged"/> when it should (success, or a non-rate-limited
/// failure), stays silent when it should (still rate-limited — the timer already rescheduled itself), and
/// that an unexpected (non-<see cref="ActivitySourceException"/>) failure reschedules rather than silently
/// killing the loop, up to a bounded number of consecutive attempts. Drives the retry logic directly via
/// the internal <c>TriggerRetryForTestAsync</c> hook rather than waiting on the real timer, so these tests
/// run instantly and deterministically (visible via <c>InternalsVisibleTo</c>).
/// </summary>
public class DashboardQueryServiceRetryLoopTests
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

    private static ActivitySourceException RateLimited(DateTimeOffset? retryAfter = null) =>
        new("Rate limited.", innerException: null, ActivitySourceFailureKind.RateLimited, retryAfter);

    private static ActivitySourceException CredentialsMissing() =>
        new("Credentials missing.", innerException: null, ActivitySourceFailureKind.CredentialsMissing);

    [Fact]
    public async Task TriggerRetryForTestAsync_AfterRateLimitedFailureThenSuccess_UpdatesLatestAndRaisesStateChangedOnce()
    {
        var source = new ScriptedActivitySource();
        source.EnqueueFailure(RateLimited(DateTimeOffset.UtcNow.AddSeconds(120)));
        source.EnqueueSuccess(EmptyDataset(DateTimeOffset.UtcNow));

        var service = CreateService(source);
        var raisedCount = 0;
        service.StateChanged += () => raisedCount++;

        var initial = await service.GetAsync(OrgScope, 14, CancellationToken.None);
        Assert.Equal(DashboardErrorKind.RateLimited, initial.Kind);

        await service.TriggerRetryForTestAsync();

        Assert.Equal(1, raisedCount);
        Assert.NotNull(service.Latest);
        Assert.Equal(DashboardErrorKind.None, service.Latest!.Kind);
        Assert.NotNull(service.Latest.Snapshot);
    }

    [Fact]
    public async Task TriggerRetryForTestAsync_StillRateLimited_DoesNotRaiseStateChangedOrReplaceLatest()
    {
        var source = new ScriptedActivitySource();
        var firstFailure = RateLimited(DateTimeOffset.UtcNow.AddSeconds(60));
        source.EnqueueFailure(firstFailure);
        source.EnqueueFailure(RateLimited(DateTimeOffset.UtcNow.AddSeconds(300)));

        var service = CreateService(source);
        var raisedCount = 0;
        service.StateChanged += () => raisedCount++;

        var initial = await service.GetAsync(OrgScope, 14, CancellationToken.None);
        var latestAfterInitial = service.Latest;

        await service.TriggerRetryForTestAsync();

        Assert.Equal(0, raisedCount);
        // Latest is untouched by a still-rate-limited retry attempt: it is exactly the same result
        // instance the initial failure produced, not a newer one carrying the retry's own RetryAt.
        Assert.Same(latestAfterInitial, service.Latest);
        Assert.Equal(DashboardErrorKind.RateLimited, initial.Kind);
    }

    [Fact]
    public async Task TriggerRetryForTestAsync_UnexpectedException_DoesNotThrowAndLeavesRetryScheduled()
    {
        var source = new ScriptedActivitySource();
        source.EnqueueFailure(RateLimited(DateTimeOffset.UtcNow.AddSeconds(60)));
        source.EnqueueThrow(new InvalidOperationException("Simulated bug during fetch."));
        source.EnqueueSuccess(EmptyDataset(DateTimeOffset.UtcNow));

        var service = CreateService(source);
        var raisedCount = 0;
        service.StateChanged += () => raisedCount++;

        await service.GetAsync(OrgScope, 14, CancellationToken.None);

        // The unexpected exception must not propagate out of the retry hook, must not raise
        // StateChanged, and must not abandon the retry loop (rate-limit hardening: the old code's
        // stale "the timer fires again" comment was false once the timer became one-shot, so a bare
        // catch that just returned silently killed the loop for good).
        await service.TriggerRetryForTestAsync();
        Assert.Equal(0, raisedCount);
        Assert.Equal(DashboardErrorKind.RateLimited, service.Latest!.Kind);

        // The loop is still alive: a further retry attempt still reaches the activity source and can
        // succeed.
        await service.TriggerRetryForTestAsync();
        Assert.Equal(1, raisedCount);
        Assert.Equal(DashboardErrorKind.None, service.Latest!.Kind);
    }

    [Fact]
    public async Task TriggerRetryForTestAsync_MoreThanFiveConsecutiveUnexpectedExceptions_GivesUpAndStopsCallingTheSource()
    {
        var source = new ScriptedActivitySource();
        source.EnqueueFailure(RateLimited(DateTimeOffset.UtcNow.AddSeconds(60)));
        for (var i = 0; i < 6; i++)
        {
            source.EnqueueThrow(new InvalidOperationException($"Simulated bug #{i}."));
        }

        var service = CreateService(source);
        var raisedCount = 0;
        service.StateChanged += () => raisedCount++;

        await service.GetAsync(OrgScope, 14, CancellationToken.None);

        for (var i = 0; i < 6; i++)
        {
            await service.TriggerRetryForTestAsync();
        }

        // The initial GetAsync call plus 6 retry attempts is 7 calls to the activity source in total.
        Assert.Equal(7, source.CallCount);

        // The budget (5 consecutive unexpected failures) is exhausted by the 6th retry attempt above,
        // so the loop gave up: a further retry attempt must not reach the activity source at all (no
        // scripted response is queued for it either — if it tried, ScriptedActivitySource would throw
        // "ran out of responses", which the retry hook would itself swallow, masking the real
        // assertion, so CallCount not moving is what proves the loop stayed stopped).
        await service.TriggerRetryForTestAsync();

        Assert.Equal(7, source.CallCount);
        Assert.Equal(0, raisedCount);
    }

    [Fact]
    public async Task TriggerRetryForTestAsync_RetryReturnsNonRateLimitedFailureKind_UpdatesLatestRaisesStateChangedAndStopsRetrying()
    {
        var source = new ScriptedActivitySource();
        source.EnqueueFailure(RateLimited(DateTimeOffset.UtcNow.AddSeconds(60)));
        source.EnqueueFailure(CredentialsMissing());

        var service = CreateService(source);
        var raisedCount = 0;
        service.StateChanged += () => raisedCount++;

        await service.GetAsync(OrgScope, 14, CancellationToken.None);
        await service.TriggerRetryForTestAsync();

        Assert.Equal(1, raisedCount);
        Assert.Equal(DashboardErrorKind.CredentialsMissing, service.Latest!.Kind);

        // A non-rate-limited failure kind stops the retry loop by design (a timer cannot fix a missing
        // credential): a further retry attempt must not reach the activity source.
        await service.TriggerRetryForTestAsync();
        Assert.Equal(2, source.CallCount);
        Assert.Equal(1, raisedCount);
    }

    /// <summary>A scripted <see cref="IActivitySource"/> stub returning a pre-programmed sequence of results (success datasets, mapped <see cref="ActivitySourceException"/> failures, or arbitrary thrown exceptions) in call order.</summary>
    private sealed class ScriptedActivitySource : IActivitySource
    {
        private readonly Queue<Func<ActivityDataset>> _script = new();

        public int CallCount { get; private set; }

        public void EnqueueSuccess(ActivityDataset dataset) => _script.Enqueue(() => dataset);

        public void EnqueueFailure(ActivitySourceException exception) => _script.Enqueue(() => throw exception);

        public void EnqueueThrow(Exception exception) => _script.Enqueue(() => throw exception);

        public Task<ActivityDataset> GetActivityAsync(ScopeKey scope, Period period, CancellationToken cancellationToken)
        {
            CallCount++;

            if (_script.Count == 0)
            {
                throw new InvalidOperationException("ScriptedActivitySource ran out of scripted responses.");
            }

            var next = _script.Dequeue();
            return Task.FromResult(next());
        }
    }
}
