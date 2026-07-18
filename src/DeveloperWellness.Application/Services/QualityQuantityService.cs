using DeveloperWellness.Domain.Model;
using DeveloperWellness.Domain.Options;
using DeveloperWellness.Domain.Signals;
using Microsoft.Extensions.Options;

namespace DeveloperWellness.Application.Services;

/// <summary>
/// Computes the quality-versus-quantity rows for a scope and period (tasks.md T043; data-model.md
/// QualityQuantitySnapshot; FR-027; ui-design.md section 4.6), bridging
/// <see cref="DashboardQueryService"/>'s already-fetched dataset and <see cref="RushingCalculator"/>'s pure
/// computation.
/// </summary>
/// <remarks>
/// Registered scoped, matching <see cref="DashboardQueryService"/>'s circuit lifetime; reads that
/// service's own cached snapshot rather than fetching a second copy of the same data.
/// </remarks>
/// <param name="queryService">Fetches (cache permitting) the dataset the rows are computed from.</param>
/// <param name="wellnessOptions">Supplies <see cref="WellnessOptions.MinPrSample"/> and <see cref="WellnessOptions.ChangesRequestedThreshold"/> to <see cref="RushingCalculator"/>.</param>
public sealed class QualityQuantityService(DashboardQueryService queryService, IOptions<WellnessOptions> wellnessOptions)
{
    private readonly DashboardQueryService _queryService = queryService ?? throw new ArgumentNullException(nameof(queryService));
    private readonly WellnessOptions _wellnessOptions = wellnessOptions is null
        ? throw new ArgumentNullException(nameof(wellnessOptions))
        : wellnessOptions.Value;

    /// <summary>
    /// Fetches the dashboard dataset for <paramref name="scope"/> and <paramref name="periodDays"/> (cache
    /// permitting) and computes one <see cref="QualityQuantityRow"/> per roster developer
    /// <see cref="RushingCalculator"/> produced a result for. Bots are skipped by resolving each result's
    /// login against the non-bot roster only; <see cref="RushingCalculator"/> already excludes
    /// <see cref="DeveloperLogin.Unmatched"/> on its own, so no separate check is needed for that.
    /// <see cref="QualityQuantityResult.Rows"/> is null exactly when <see cref="QualityQuantityResult.Source"/>
    /// carries no snapshot, mirroring every other per-page result record in this codebase.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="scope"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="periodDays"/> is not 7, 14, or 30.</exception>
    public async Task<QualityQuantityResult> GetAsync(ScopeKey scope, int periodDays, CancellationToken cancellationToken)
    {
        var result = await _queryService.GetAsync(scope, periodDays, cancellationToken).ConfigureAwait(false);

        if (result.Snapshot is not { } snapshot)
        {
            return new QualityQuantityResult(null, result);
        }

        var rushingResults = RushingCalculator.Calculate(snapshot.Dataset.Events, _wellnessOptions, periodDays);
        var rosterByLogin = snapshot.Dataset.Roster
            .Where(developer => !developer.IsBot)
            .ToDictionary(developer => developer.Login);

        var rows = rushingResults
            .Where(pair => rosterByLogin.ContainsKey(pair.Key))
            .Select(pair => new QualityQuantityRow(rosterByLogin[pair.Key], pair.Value.Snapshot, pair.Value.Flag))
            .ToList();

        return new QualityQuantityResult(rows, result);
    }
}

/// <summary>
/// The outcome of a <see cref="QualityQuantityService"/> fetch: the computed rows (null when
/// <see cref="Source"/> carries no snapshot), and the underlying dashboard result so the quality page can
/// bridge shell state and render stale/error surfaces exactly like every other page.
/// </summary>
public sealed record QualityQuantityResult(IReadOnlyList<QualityQuantityRow>? Rows, DashboardResult Source);

/// <summary>
/// One developer's quality-versus-quantity row for the <c>/quality</c> table (ui-design.md section 4.6):
/// the roster developer, their volume-and-rework snapshot, and the <see cref="FlagKind.PossibleRushing"/>
/// flag when raised.
/// </summary>
public sealed record QualityQuantityRow(Developer Developer, QualityQuantitySnapshot Snapshot, WellbeingFlag? Flag);
