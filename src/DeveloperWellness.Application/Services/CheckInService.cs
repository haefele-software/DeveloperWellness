using DeveloperWellness.Application.Ports;
using DeveloperWellness.Domain.Model;
using DeveloperWellness.Domain.Options;
using DeveloperWellness.Domain.Signals;
using Microsoft.Extensions.Options;

namespace DeveloperWellness.Application.Services;

/// <summary>
/// Composes the check-in roster for a scope and period (tasks.md T024, T041, T044; FR-026..FR-028,
/// FR-018..FR-020, FR-027), bridging <see cref="DashboardQueryService"/>'s fetched-and-enriched summaries,
/// <see cref="ToneAnalysisService"/>'s tone aggregation, and a direct <see cref="RushingCalculator"/> call
/// through <see cref="CheckInComposer"/>.
/// </summary>
/// <remarks>
/// Registered scoped, matching <see cref="DashboardQueryService"/>'s circuit lifetime; this type owns no
/// state of its own. Rushing is computed directly from the fetched snapshot's events rather than through
/// <see cref="QualityQuantityService"/>, keeping this service's own dependency graph a straight line
/// (<see cref="DashboardQueryService"/> plus <see cref="ToneAnalysisService"/>, both already present)
/// instead of adding a second Application-layer service as a dependency for one calculator call.
/// </remarks>
/// <param name="queryService">Fetches and enriches the underlying dashboard dataset.</param>
/// <param name="toneAnalysisService">Supplies the per-author tone aggregation feeding the roster's frustration mention (FR-019, FR-020).</param>
/// <param name="aiInsightService">Consulted only for <see cref="IAiInsightService.IsAvailable"/> today.</param>
/// <param name="wellnessOptions">Supplies <see cref="WellnessOptions.MinPrSample"/> and <see cref="WellnessOptions.ChangesRequestedThreshold"/> to the rushing calculation (FR-027).</param>
public sealed class CheckInService(
    DashboardQueryService queryService,
    ToneAnalysisService toneAnalysisService,
    IAiInsightService aiInsightService,
    IOptions<WellnessOptions> wellnessOptions)
{
    private readonly DashboardQueryService _queryService = queryService ?? throw new ArgumentNullException(nameof(queryService));
    private readonly ToneAnalysisService _toneAnalysisService = toneAnalysisService ?? throw new ArgumentNullException(nameof(toneAnalysisService));
    private readonly IAiInsightService _aiInsightService = aiInsightService ?? throw new ArgumentNullException(nameof(aiInsightService));
    private readonly WellnessOptions _wellnessOptions = wellnessOptions is null
        ? throw new ArgumentNullException(nameof(wellnessOptions))
        : wellnessOptions.Value;

    /// <summary>
    /// Fetches the dashboard dataset and the tone aggregation for <paramref name="scope"/> and
    /// <paramref name="periodDays"/> (cache permitting for both) and composes the check-in roster, merging
    /// any tone-based frustration flags and any rushing flags in alongside each summary's own flags
    /// (FR-019, FR-027).
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="scope"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="periodDays"/> is not 7, 14, or 30.</exception>
    public async Task<CheckInRosterResult> GetRosterAsync(ScopeKey scope, int periodDays, CancellationToken cancellationToken)
    {
        var result = await _queryService.GetAsync(scope, periodDays, cancellationToken).ConfigureAwait(false);
        var tones = await _toneAnalysisService.GetTonesAsync(scope, periodDays, cancellationToken).ConfigureAwait(false);
        return ComposeResult(result, tones, periodDays);
    }

    /// <summary>
    /// Evicts any cached dataset for <paramref name="scope"/> and <paramref name="periodDays"/>, fetches a
    /// fresh one unconditionally, and composes the roster from it (the tone aggregation still reuses its
    /// own sliding cache, FR-021, unless it too has expired). The roster page's "Try again" action
    /// (mirroring every other page's <c>DashboardQueryService.RefreshAsync</c> retry pattern) uses this
    /// rather than <see cref="GetRosterAsync"/>.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="scope"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="periodDays"/> is not 7, 14, or 30.</exception>
    public async Task<CheckInRosterResult> RefreshRosterAsync(ScopeKey scope, int periodDays, CancellationToken cancellationToken)
    {
        var result = await _queryService.RefreshAsync(scope, periodDays, cancellationToken).ConfigureAwait(false);
        var tones = await _toneAnalysisService.GetTonesAsync(scope, periodDays, cancellationToken).ConfigureAwait(false);
        return ComposeResult(result, tones, periodDays);
    }

    /// <summary>
    /// <see cref="CheckInRosterResult.Composition"/> is null exactly when <paramref name="result"/> carries
    /// no snapshot (no data to compose from; the caller renders <see cref="DashboardResult"/>'s error
    /// state instead). <see cref="CheckInRosterResult.ToneFlaggedNames"/> resolves each tone-flagged
    /// author's display name from the roster (ui-design.md 4.3), so the roster page can compose the
    /// frustration paragraph without re-deriving it from <see cref="CheckInComposition"/>. Tone and rushing
    /// flags are merged into one additional-flags map, tone first then rushing per login (T044), before
    /// being handed to <see cref="CheckInComposer.Compose"/>, so a summary's own flags (out-of-hours
    /// commits, then spread-thin, then out-of-hours PR activity) are always followed by tone, then rushing —
    /// the fixed order <see cref="RecommendationMapper"/>'s remarks document as "leading signal".
    /// </summary>
    private CheckInRosterResult ComposeResult(DashboardResult result, ToneAnalysisResult tones, int periodDays)
    {
        if (result.Snapshot is not { } snapshot)
        {
            return new CheckInRosterResult(null, tones.Available, result, []);
        }

        var toneFlagsByAuthor = BuildToneFlags(tones.Aggregation);
        var rushingFlagsByAuthor = BuildRushingFlags(snapshot.Dataset.Events, periodDays);
        var additionalFlags = MergeAdditionalFlags(toneFlagsByAuthor, rushingFlagsByAuthor);
        var composition = CheckInComposer.Compose(snapshot.Summaries, additionalFlags);

        var toneFlaggedNames = toneFlagsByAuthor.Keys
            .Select(author => ResolveDisplayName(author, snapshot.Dataset.Roster))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new CheckInRosterResult(composition, tones.Available, result, toneFlaggedNames);
    }

    /// <summary>Extracts one <see cref="WellbeingFlag"/> per tone-flagged author from <paramref name="aggregation"/> (FR-019); empty when tone is unavailable or nobody crossed the guard.</summary>
    private static IReadOnlyDictionary<DeveloperLogin, IReadOnlyList<WellbeingFlag>> BuildToneFlags(ToneAggregation? aggregation)
    {
        if (aggregation is null)
        {
            return new Dictionary<DeveloperLogin, IReadOnlyList<WellbeingFlag>>();
        }

        return aggregation.ByAuthor
            .Where(pair => pair.Value.Flag is not null)
            .ToDictionary(pair => pair.Key, pair => (IReadOnlyList<WellbeingFlag>)[pair.Value.Flag!]);
    }

    /// <summary>Extracts one <see cref="WellbeingFlag"/> per rushing-flagged author from a direct <see cref="RushingCalculator"/> call over the snapshot's events (FR-027, T044); empty when nobody crosses the guard.</summary>
    private IReadOnlyDictionary<DeveloperLogin, IReadOnlyList<WellbeingFlag>> BuildRushingFlags(
        IReadOnlyList<ActivityEvent> events, int periodDays)
    {
        var rushingResults = RushingCalculator.Calculate(events, _wellnessOptions, periodDays);

        return rushingResults
            .Where(pair => pair.Value.Flag is not null)
            .ToDictionary(pair => pair.Key, pair => (IReadOnlyList<WellbeingFlag>)[pair.Value.Flag!]);
    }

    /// <summary>
    /// Concatenates <paramref name="toneFlags"/> then <paramref name="rushingFlags"/> per login, so a
    /// developer flagged by both carries their tone reason before their rushing reason (T044's documented
    /// append order). A login present in only one map still gets an entry carrying just that map's flags.
    /// </summary>
    private static IReadOnlyDictionary<DeveloperLogin, IReadOnlyList<WellbeingFlag>> MergeAdditionalFlags(
        IReadOnlyDictionary<DeveloperLogin, IReadOnlyList<WellbeingFlag>> toneFlags,
        IReadOnlyDictionary<DeveloperLogin, IReadOnlyList<WellbeingFlag>> rushingFlags)
    {
        var merged = new Dictionary<DeveloperLogin, IReadOnlyList<WellbeingFlag>>();

        foreach (var login in toneFlags.Keys.Concat(rushingFlags.Keys).Distinct())
        {
            var combined = new List<WellbeingFlag>();
            combined.AddRange(toneFlags.GetValueOrDefault(login, []));
            combined.AddRange(rushingFlags.GetValueOrDefault(login, []));
            merged[login] = combined;
        }

        return merged;
    }

    /// <summary>
    /// Resolves a login's display name from the roster; falls back to the raw login text for a
    /// tone-flagged author with no roster entry (edge case, mirrors <see cref="CheckInComposer"/>'s own
    /// placeholder-developer fallback for logins present only in additional flags).
    /// </summary>
    private static string ResolveDisplayName(DeveloperLogin login, IReadOnlyList<Developer> roster) =>
        roster.FirstOrDefault(developer => developer.Login.Equals(login))?.DisplayName ?? login.Value;
}

/// <summary>
/// The outcome of a <see cref="CheckInService"/> fetch: the composed roster (null when
/// <see cref="Source"/> carries no snapshot), whether tone signals are currently available (SC-009), the
/// underlying dashboard result so the check-in roster page can bridge shell state and render stale/error
/// surfaces exactly like every other page, and the display names of every tone-flagged developer (FR-018,
/// ui-design.md 4.3) so the page can compose the frustration paragraph without re-deriving it.
/// </summary>
public sealed record CheckInRosterResult(
    CheckInComposition? Composition,
    bool ToneAvailable,
    DashboardResult Source,
    IReadOnlyList<string> ToneFlaggedNames);
