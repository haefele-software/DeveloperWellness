using DeveloperWellness.Application.Ports;
using DeveloperWellness.Domain.Model;
using DeveloperWellness.Domain.Options;
using DeveloperWellness.Domain.Signals;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace DeveloperWellness.Application.Services;

/// <summary>
/// Fetches, aggregates, and caches the dashboard dataset for a scope and period (contracts/
/// application-ports.md DashboardQueryService; FR-002..FR-012, FR-021). Caches by scope and period
/// length only — never the full <see cref="Period"/>, whose <see cref="Period.End"/> moves on every call
/// and would never hit — with a session-length sliding TTL. Keeps the last snapshot successfully loaded
/// for a given key visible when a later fetch for that same key fails (FR-011); a rate-limited failure
/// additionally schedules a reset-aware automatic retry (near GitHub's own reported reset time rather than
/// a fixed interval) that stops itself and raises <see cref="StateChanged"/> once it succeeds.
/// </summary>
/// <remarks>
/// Registered scoped (one instance per Blazor circuit, matching the retry loop's intended lifetime);
/// <see cref="Dispose"/> stops the pending retry timer, if any, when the circuit ends.
/// </remarks>
/// <param name="activitySource">Fetches the raw dataset for a scope and period.</param>
/// <param name="cache">Backs the scope-and-period-days cache (FR-021).</param>
/// <param name="wellnessOptions">Wellness configuration used by signal-calculator enrichment (T018, T021, T032).</param>
public sealed class DashboardQueryService(IActivitySource activitySource, IMemoryCache cache, IOptions<WellnessOptions> wellnessOptions) : IDisposable
{
    private static readonly TimeSpan CacheSlidingExpiration = TimeSpan.FromMinutes(30);

    /// <summary>Retry delay used when a rate-limited failure carried no <see cref="ActivitySourceException.RetryAfter"/> (reset-aware retry scheduling).</summary>
    private static readonly TimeSpan DefaultRetryDelay = TimeSpan.FromSeconds(60);

    /// <summary>Added after a known reset time so many circuits retrying the same key don't all hit GitHub in the same instant.</summary>
    private static readonly TimeSpan RetryJitter = TimeSpan.FromSeconds(15);

    private readonly IActivitySource _activitySource = activitySource ?? throw new ArgumentNullException(nameof(activitySource));
    private readonly IMemoryCache _cache = cache ?? throw new ArgumentNullException(nameof(cache));
    private readonly WellnessOptions _wellnessOptions = wellnessOptions is null
        ? throw new ArgumentNullException(nameof(wellnessOptions))
        : wellnessOptions.Value;

    private readonly Dictionary<(ScopeKey Scope, int PeriodDays), DashboardSnapshot> _lastGoodSnapshots = [];
    private readonly object _retryGate = new();

    private Timer? _retryTimer;
    private (ScopeKey Scope, int PeriodDays)? _retryKey;
    private bool _disposed;

    /// <summary>
    /// Raised after a scheduled retry following a rate-limited failure succeeds; subscribers should
    /// re-read <see cref="Latest"/> and re-render.
    /// </summary>
    public event Action? StateChanged;

    /// <summary>The most recent outcome returned by <see cref="GetAsync"/>, <see cref="RefreshAsync"/>, or a background retry.</summary>
    public DashboardResult? Latest { get; private set; }

    /// <summary>
    /// Returns the cached snapshot for <paramref name="scope"/> and <paramref name="periodDays"/> when
    /// present; otherwise fetches, aggregates, and caches a fresh one. On failure, falls back to the last
    /// snapshot successfully loaded for this exact key, if any (FR-011).
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="scope"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="periodDays"/> is not 7, 14, or 30.</exception>
    public async Task<DashboardResult> GetAsync(ScopeKey scope, int periodDays, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ValidatePeriodDays(periodDays);

        var cacheKey = BuildCacheKey(scope, periodDays);

        if (_cache.TryGetValue(cacheKey, out DashboardSnapshot? cached) && cached is not null)
        {
            var hit = new DashboardResult(cached, ErrorMessage: null, DashboardErrorKind.None, IsStale: false);
            Latest = hit;
            return hit;
        }

        var result = await LoadAsync(scope, periodDays, cacheKey, cancellationToken).ConfigureAwait(false);
        Latest = result;
        return result;
    }

    /// <summary>
    /// Evicts the cached snapshot for <paramref name="scope"/> and <paramref name="periodDays"/> and
    /// fetches a fresh one unconditionally (FR-021 explicit refresh).
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="scope"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="periodDays"/> is not 7, 14, or 30.</exception>
    public async Task<DashboardResult> RefreshAsync(ScopeKey scope, int periodDays, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ValidatePeriodDays(periodDays);

        var cacheKey = BuildCacheKey(scope, periodDays);
        _cache.Remove(cacheKey);

        var result = await LoadAsync(scope, periodDays, cacheKey, cancellationToken).ConfigureAwait(false);
        Latest = result;
        return result;
    }

    /// <summary>Stops the pending retry timer, if any.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        lock (_retryGate)
        {
            _retryTimer?.Dispose();
            _retryTimer = null;
        }

        _disposed = true;
    }

    private async Task<DashboardResult> LoadAsync(ScopeKey scope, int periodDays, string cacheKey, CancellationToken cancellationToken)
    {
        var key = (scope, periodDays);

        try
        {
            var period = new Period(periodDays, DateTimeOffset.UtcNow);
            var dataset = await _activitySource.GetActivityAsync(scope, period, cancellationToken).ConfigureAwait(false);
            var aggregation = ActivityAggregator.Aggregate(dataset);
            var enrichedSummaries = EnrichSummaries(aggregation.Summaries, dataset, scope, periodDays);

            var snapshot = new DashboardSnapshot(dataset, enrichedSummaries, aggregation.Unmatched, dataset.LoadedAt, dataset.IsDemoData);

            _cache.Set(cacheKey, snapshot, new MemoryCacheEntryOptions { SlidingExpiration = CacheSlidingExpiration });
            _lastGoodSnapshots[key] = snapshot;
            StopRetry();

            return new DashboardResult(snapshot, ErrorMessage: null, DashboardErrorKind.None, IsStale: false);
        }
        catch (ActivitySourceException ex)
        {
            var kind = MapErrorKind(ex.Kind);
            var hasPrevious = _lastGoodSnapshots.TryGetValue(key, out var previous);

            DateTimeOffset? retryAt = null;
            if (kind == DashboardErrorKind.RateLimited)
            {
                retryAt = ScheduleRetry(scope, periodDays, ex.RetryAfter);
            }

            return new DashboardResult(
                Snapshot: hasPrevious ? previous : null,
                ErrorMessage: ex.Message,
                Kind: kind,
                IsStale: hasPrevious,
                RetryAt: retryAt);
        }
    }

    /// <summary>
    /// Schedules (or reschedules, replacing any pending timer for this key) a one-shot background retry at
    /// <see cref="ComputeRetryAt"/>'s due time, and returns that time. Every rate-limited failure —
    /// the first one and every subsequent retry attempt that is still rate-limited — calls this afresh with
    /// its own exception's <see cref="ActivitySourceException.RetryAfter"/>, so the schedule always reflects
    /// the most recently known reset time rather than a fixed periodic interval.
    /// </summary>
    private DateTimeOffset ScheduleRetry(ScopeKey scope, int periodDays, DateTimeOffset? retryAfter)
    {
        var now = DateTimeOffset.UtcNow;
        var retryAt = ComputeRetryAt(now, retryAfter);

        lock (_retryGate)
        {
            if (_disposed)
            {
                return retryAt;
            }

            _retryKey = (scope, periodDays);
            _retryTimer?.Dispose();

            var dueTime = retryAt - now;
            if (dueTime < TimeSpan.Zero)
            {
                dueTime = TimeSpan.Zero;
            }

            // One-shot: Timeout.InfiniteTimeSpan as the period means this timer fires exactly once. A retry
            // that is still rate-limited reschedules a fresh one-shot timer itself (see the catch clause
            // above), rather than relying on a fixed-interval repeating timer.
            _retryTimer = new Timer(OnRetryTick, state: null, dueTime, Timeout.InfiniteTimeSpan);
        }

        return retryAt;
    }

    /// <summary>
    /// Computes when the next automatic retry should fire after a rate-limited failure (reset-aware retry
    /// scheduling). When <paramref name="retryAfter"/> is known (GitHub's own reset time, or a source's
    /// conservative estimate), retries <see cref="RetryJitter"/> after it, but never sooner than
    /// <paramref name="now"/> plus <see cref="DefaultRetryDelay"/>, in case a stale or already-past reset
    /// time was supplied. Falls back to exactly <paramref name="now"/> plus <see cref="DefaultRetryDelay"/>
    /// when no reset time is known. Extracted as a pure, internally testable helper (no timer, no I/O).
    /// </summary>
    internal static DateTimeOffset ComputeRetryAt(DateTimeOffset now, DateTimeOffset? retryAfter)
    {
        var earliestDefault = now + DefaultRetryDelay;

        if (retryAfter is not { } reset)
        {
            return earliestDefault;
        }

        var afterJitter = reset + RetryJitter;
        return afterJitter > earliestDefault ? afterJitter : earliestDefault;
    }

    private void StopRetry()
    {
        lock (_retryGate)
        {
            _retryKey = null;
            _retryTimer?.Dispose();
            _retryTimer = null;
        }
    }

    private void OnRetryTick(object? state) => _ = RetryAsync();

    private async Task RetryAsync()
    {
        (ScopeKey Scope, int PeriodDays)? key;
        lock (_retryGate)
        {
            key = _disposed ? null : _retryKey;
        }

        if (key is not { } retryKey)
        {
            return;
        }

        DashboardResult result;
        try
        {
            var cacheKey = BuildCacheKey(retryKey.Scope, retryKey.PeriodDays);
            result = await LoadAsync(retryKey.Scope, retryKey.PeriodDays, cacheKey, CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            return; // best-effort background retry; the timer fires again on the next tick
        }

        if (result.Kind != DashboardErrorKind.None)
        {
            return; // still failing; StopRetry() was not called, so the timer keeps ticking
        }

        Latest = result;
        StateChanged?.Invoke();
    }

    /// <summary>
    /// Applies every landed signal calculator to <paramref name="summaries"/> in one pass:
    /// <see cref="OutOfHoursCommitCalculator"/> (T018), <c>SpreadThinCalculator</c> (T021), then
    /// <see cref="PrAfterHoursCalculator"/> (T032) — appended after spread-thin so a summary's
    /// <see cref="ActivitySummary.Flags"/> list carries out-of-hours-commits, then spread-thin, then
    /// after-hours-PR-activity, in that fixed order (<see cref="RecommendationMapper"/>'s remarks document
    /// why this append order matters: it trusts flag order as "leading signal"). Both
    /// <see cref="LoadAsync"/> call sites — the normal fetch and the background rate-limit retry — build
    /// snapshots through this single path, so a cached snapshot's summaries are always already enriched.
    /// </summary>
    /// <param name="scope">
    /// The scope this dataset was fetched for. Spread-thin is only computed, and
    /// <see cref="ActivitySummary.DistinctProjectCount"/> only ever set, when this is
    /// <see cref="ScopeKind.Organisation"/> (data-model.md; <see cref="SpreadThinCalculator"/>'s remarks;
    /// FR-008): a project-scoped dataset only sees a developer's activity in that one project, so a count
    /// computed from it would be artificially low and any resulting flag misleading.
    /// </param>
    private IReadOnlyList<ActivitySummary> EnrichSummaries(
        IReadOnlyList<ActivitySummary> summaries, ActivityDataset dataset, ScopeKey scope, int periodDays)
    {
        var outOfHoursCommitResults = OutOfHoursCommitCalculator.Calculate(dataset.Events, _wellnessOptions, periodDays);
        var spreadThinResults = scope.Kind == ScopeKind.Organisation
            ? SpreadThinCalculator.Calculate(dataset.Events, _wellnessOptions)
            : null;
        var prAfterHoursResults = PrAfterHoursCalculator.Calculate(dataset.Events, _wellnessOptions, periodDays);

        return summaries
            .Select(summary => EnrichWithOutOfHoursCommits(summary, outOfHoursCommitResults))
            .Select(summary => EnrichWithSpreadThin(summary, spreadThinResults))
            .Select(summary => EnrichWithPrAfterHours(summary, prAfterHoursResults))
            .ToList();
    }

    /// <summary>Adds the out-of-hours commit share and, above threshold, the <see cref="FlagKind.OverworkCommits"/> flag to one summary.</summary>
    private static ActivitySummary EnrichWithOutOfHoursCommits(
        ActivitySummary summary, IReadOnlyDictionary<DeveloperLogin, OutOfHoursCommitResult> outOfHoursCommitResults)
    {
        if (!outOfHoursCommitResults.TryGetValue(summary.Developer.Login, out var commitResult))
        {
            return summary;
        }

        var flags = commitResult.Flag is { } flag
            ? [.. summary.Flags, flag]
            : summary.Flags;

        return new ActivitySummary(
            developer: summary.Developer,
            commitCount: summary.CommitCount,
            reviewCount: summary.ReviewCount,
            commentCount: summary.CommentCount,
            prsOpenedCount: summary.PrsOpenedCount,
            outOfHoursCommitShare: commitResult.Share,
            outOfHoursPrShare: summary.OutOfHoursPrShare,
            distinctProjectCount: summary.DistinctProjectCount,
            flags: flags,
            hasActivity: summary.HasActivity);
    }

    /// <summary>
    /// Adds the distinct-project count and, at or above threshold, the <see cref="FlagKind.SpreadThin"/>
    /// flag to one summary. A no-op (summary unchanged, <see cref="ActivitySummary.DistinctProjectCount"/>
    /// stays null) when <paramref name="spreadThinResults"/> is null (project scope, per
    /// <see cref="EnrichSummaries"/>'s scope guard) or this developer authored no events in the dataset.
    /// </summary>
    private static ActivitySummary EnrichWithSpreadThin(
        ActivitySummary summary, IReadOnlyDictionary<DeveloperLogin, SpreadThinResult>? spreadThinResults)
    {
        if (spreadThinResults is null || !spreadThinResults.TryGetValue(summary.Developer.Login, out var spreadThinResult))
        {
            return summary;
        }

        var flags = spreadThinResult.Flag is { } flag
            ? [.. summary.Flags, flag]
            : summary.Flags;

        return new ActivitySummary(
            developer: summary.Developer,
            commitCount: summary.CommitCount,
            reviewCount: summary.ReviewCount,
            commentCount: summary.CommentCount,
            prsOpenedCount: summary.PrsOpenedCount,
            outOfHoursCommitShare: summary.OutOfHoursCommitShare,
            outOfHoursPrShare: summary.OutOfHoursPrShare,
            distinctProjectCount: spreadThinResult.DistinctProjectCount,
            flags: flags,
            hasActivity: summary.HasActivity);
    }

    /// <summary>
    /// Adds the organisation-timezone out-of-hours PR-activity share and, above threshold with the minimum
    /// PR-event guard met, the <see cref="FlagKind.OverworkPrActivity"/> flag to one summary (T032, FR-024,
    /// FR-025). A no-op when this developer authored no PR events in the dataset.
    /// </summary>
    private static ActivitySummary EnrichWithPrAfterHours(
        ActivitySummary summary, IReadOnlyDictionary<DeveloperLogin, PrAfterHoursResult> prAfterHoursResults)
    {
        if (!prAfterHoursResults.TryGetValue(summary.Developer.Login, out var prResult))
        {
            return summary;
        }

        var flags = prResult.Flag is { } flag
            ? [.. summary.Flags, flag]
            : summary.Flags;

        return new ActivitySummary(
            developer: summary.Developer,
            commitCount: summary.CommitCount,
            reviewCount: summary.ReviewCount,
            commentCount: summary.CommentCount,
            prsOpenedCount: summary.PrsOpenedCount,
            outOfHoursCommitShare: summary.OutOfHoursCommitShare,
            outOfHoursPrShare: prResult.Share,
            distinctProjectCount: summary.DistinctProjectCount,
            flags: flags,
            hasActivity: summary.HasActivity);
    }

    private static DashboardErrorKind MapErrorKind(ActivitySourceFailureKind kind) => kind switch
    {
        ActivitySourceFailureKind.CredentialsMissing => DashboardErrorKind.CredentialsMissing,
        ActivitySourceFailureKind.RateLimited => DashboardErrorKind.RateLimited,
        _ => DashboardErrorKind.Unavailable,
    };

    private static string BuildCacheKey(ScopeKey scope, int periodDays) => $"dashboard:{scope}:{periodDays}d";

    private static void ValidatePeriodDays(int periodDays)
    {
        if (periodDays is not (7 or 14 or 30))
        {
            throw new ArgumentOutOfRangeException(nameof(periodDays), periodDays, "Period must be 7, 14, or 30 days.");
        }
    }
}
