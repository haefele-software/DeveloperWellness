using DeveloperWellness.Application.Ports;
using DeveloperWellness.Domain.Model;
using DeveloperWellness.Domain.Options;
using DeveloperWellness.Domain.Signals;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace DeveloperWellness.Application.Services;

/// <summary>
/// Fetches the underlying dashboard dataset, classifies its authored comments' tone, and builds the
/// per-author tone aggregation and organisation-level sentiment reading for a scope and period (tasks.md
/// T040; data-model.md ToneAggregate and SentimentReading; FR-018..FR-021, FR-039). Bridges
/// <see cref="DashboardQueryService"/>'s already-fetched dataset and <see cref="IAiInsightService"/>'s
/// classifier through <see cref="ToneAggregator"/>, so <see cref="CheckInService"/> (the roster's
/// frustration mention, FR-019) and <see cref="OverviewService"/> (the organisation-level sentiment
/// reading, FR-039) share exactly one tone computation and cache per scope and period.
/// </summary>
/// <remarks>
/// Registered scoped, matching <see cref="DashboardQueryService"/>'s circuit lifetime. Caches successful
/// results in <see cref="IMemoryCache"/> keyed by scope and period, with a session-length sliding TTL
/// mirroring <see cref="DashboardQueryService"/>'s own cache (FR-021); failures (AI unavailable, AI
/// unreachable, or no snapshot to classify from) are never cached, so the next call always gets a fresh
/// attempt.
/// </remarks>
/// <param name="queryService">Fetches (cache permitting) the dashboard dataset whose authored comments feed tone classification.</param>
/// <param name="aiInsightService">Classifies comment tone; consulted first for <see cref="IAiInsightService.IsAvailable"/> (SC-009).</param>
/// <param name="cache">Backs the scope-and-period tone-result cache (FR-021).</param>
/// <param name="wellnessOptions">Supplies <see cref="WellnessOptions.ToneCommentCap"/> and the guards <see cref="ToneAggregator"/> applies.</param>
public sealed class ToneAnalysisService(
    DashboardQueryService queryService,
    IAiInsightService aiInsightService,
    IMemoryCache cache,
    IOptions<WellnessOptions> wellnessOptions)
{
    private static readonly TimeSpan CacheSlidingExpiration = TimeSpan.FromMinutes(30);

    private readonly DashboardQueryService _queryService = queryService ?? throw new ArgumentNullException(nameof(queryService));
    private readonly IAiInsightService _aiInsightService = aiInsightService ?? throw new ArgumentNullException(nameof(aiInsightService));
    private readonly IMemoryCache _cache = cache ?? throw new ArgumentNullException(nameof(cache));
    private readonly WellnessOptions _wellnessOptions = wellnessOptions is null
        ? throw new ArgumentNullException(nameof(wellnessOptions))
        : wellnessOptions.Value;

    /// <summary>
    /// Returns the cached tone aggregation for <paramref name="scope"/> and <paramref name="periodDays"/>
    /// when present; otherwise fetches, classifies, aggregates, and caches a fresh one.
    /// </summary>
    /// <remarks>
    /// Degrades to <c>(Aggregation: null, Available: false)</c> — never throws — whenever tone signals
    /// cannot be produced this call: the AI service is unconfigured (checked first, before any fetch or
    /// classification call, per SC-009); the dashboard dataset itself could not be fetched; or the
    /// classification request failed outright (<see cref="AiInsightException"/>). Callers (the roster and
    /// the Overview) render their own tone-unavailable states from <see cref="ToneAnalysisResult.Available"/>
    /// rather than from a thrown exception.
    /// <para/>
    /// Selection follows FR-020: every non-bot, non-<see cref="DeveloperLogin.Unmatched"/> authored comment
    /// in the period is ordered most-recent-first and capped at <see cref="WellnessOptions.ToneCommentCap"/>
    /// before classification, while each author's <em>total</em> authored-comment count (uncapped) still
    /// feeds <see cref="ToneAggregate.TotalCount"/> so the frustration mention can state its analysed
    /// sample. <see cref="IAiInsightService.ClassifyToneAsync"/> may return a shorter prefix than it was
    /// given on partial failure; the returned classifications are zipped back onto the same-length prefix
    /// of the capped, most-recent-first comment list, and any comments beyond that prefix are simply left
    /// unclassified for this call (they still count toward <see cref="ToneAggregate.TotalCount"/>).
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="scope"/> is null.</exception>
    public async Task<ToneAnalysisResult> GetTonesAsync(ScopeKey scope, int periodDays, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);

        if (!_aiInsightService.IsAvailable)
        {
            return new ToneAnalysisResult(Aggregation: null, Available: false);
        }

        var cacheKey = BuildCacheKey(scope, periodDays);

        if (_cache.TryGetValue(cacheKey, out ToneAnalysisResult? cached) && cached is not null)
        {
            return cached;
        }

        var dashboardResult = await _queryService.GetAsync(scope, periodDays, cancellationToken).ConfigureAwait(false);

        if (dashboardResult.Snapshot is not { } snapshot)
        {
            // No data to classify from this call; the AI service itself is up (checked above), so this is
            // a data-fetch failure rather than a tone-unavailable state — but with nothing to aggregate,
            // null Aggregation is the only honest result. Never cached: the next call should retry the fetch.
            return new ToneAnalysisResult(Aggregation: null, Available: true);
        }

        var botLogins = snapshot.Dataset.Roster.Where(developer => developer.IsBot).Select(developer => developer.Login).ToHashSet();

        var authoredComments = snapshot.Dataset.Events
            .OfType<CommentEvent>()
            .Where(comment => !comment.Author.IsUnmatched && !botLogins.Contains(comment.Author))
            .ToList();

        var totalsByAuthor = authoredComments
            .GroupBy(comment => comment.Author)
            .ToDictionary(group => group.Key, group => group.Count());

        var sampled = authoredComments
            .OrderByDescending(comment => comment.OccurredAt)
            .Take(_wellnessOptions.ToneCommentCap)
            .ToList();

        IReadOnlyList<ToneClass> classified;

        try
        {
            var bodies = sampled.Select(comment => comment.BodyText).ToList();
            classified = await _aiInsightService.ClassifyToneAsync(bodies, cancellationToken).ConfigureAwait(false);
        }
        catch (AiInsightException)
        {
            return new ToneAnalysisResult(Aggregation: null, Available: false);
        }

        var pairs = new List<(DeveloperLogin Author, ToneClass Tone)>(classified.Count);
        for (var i = 0; i < classified.Count; i++)
        {
            pairs.Add((sampled[i].Author, classified[i]));
        }

        var aggregation = ToneAggregator.Calculate(pairs, totalsByAuthor, _wellnessOptions);
        var result = new ToneAnalysisResult(aggregation, Available: true);

        _cache.Set(cacheKey, result, new MemoryCacheEntryOptions { SlidingExpiration = CacheSlidingExpiration });

        return result;
    }

    private static string BuildCacheKey(ScopeKey scope, int periodDays) => $"tones:{scope}:{periodDays}d";
}

/// <summary>
/// The outcome of one <see cref="ToneAnalysisService.GetTonesAsync"/> call. <see cref="Aggregation"/> is
/// null whenever tone signals cannot be produced this call (see <see cref="ToneAnalysisService.GetTonesAsync"/>'s
/// remarks for every such case); <see cref="Available"/> is false only when the AI service itself is the
/// reason (unconfigured or a failed classification request, SC-009) — callers should treat a null
/// <see cref="Aggregation"/> as "no tone data to show" regardless of <see cref="Available"/>.
/// </summary>
public sealed record ToneAnalysisResult(ToneAggregation? Aggregation, bool Available);
