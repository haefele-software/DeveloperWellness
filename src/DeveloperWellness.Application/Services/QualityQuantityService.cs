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
        return ComposeResult(result, periodDays);
    }

    /// <summary>
    /// Evicts any cached dataset for <paramref name="scope"/> and <paramref name="periodDays"/> via
    /// <see cref="DashboardQueryService.RefreshAsync"/> and computes the rows from the fresh dataset
    /// unconditionally (FR-021 explicit refresh). The Quality page's "Try again" action uses this rather
    /// than <see cref="GetAsync"/>, mirroring every other page's cache-bypassing retry path
    /// (<see cref="OverviewService.RefreshAsync"/> pairs the same way around
    /// <see cref="CheckInService.RefreshRosterAsync"/>).
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="scope"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="periodDays"/> is not 7, 14, or 30.</exception>
    public async Task<QualityQuantityResult> RefreshAsync(ScopeKey scope, int periodDays, CancellationToken cancellationToken)
    {
        var result = await _queryService.RefreshAsync(scope, periodDays, cancellationToken).ConfigureAwait(false);
        return ComposeResult(result, periodDays);
    }

    /// <summary>
    /// Shared tail of <see cref="GetAsync"/> and <see cref="RefreshAsync"/>: they differ only in which
    /// <see cref="DashboardQueryService"/> fetch call produced <paramref name="result"/>.
    /// </summary>
    private QualityQuantityResult ComposeResult(DashboardResult result, int periodDays)
    {
        if (result.Snapshot is not { } snapshot)
        {
            return new QualityQuantityResult(null, result);
        }

        var rushingResults = RushingCalculator.Calculate(snapshot.Dataset.Events, _wellnessOptions, periodDays);
        var rosterByLogin = snapshot.Dataset.Roster
            .Where(developer => !developer.IsBot)
            .ToDictionary(developer => developer.Login);

        // Lines changed (commit-size/volume metric): the dataset-wide dictionary is empty exactly when
        // GitHub's statistics endpoint was unavailable for every covered repository (ActivityDataset.
        // LinesChangedByAuthor's remarks), in which case every row surfaces null (unknown) rather than a
        // misleading zero. Otherwise a developer simply absent from the dictionary genuinely changed zero
        // default-branch lines in the period, so GetValueOrDefault's zero is the correct reading.
        var linesChangedByAuthor = snapshot.Dataset.LinesChangedByAuthor;
        var statsUnavailable = linesChangedByAuthor.Count == 0;

        var rows = rushingResults
            .Where(pair => rosterByLogin.ContainsKey(pair.Key))
            .Select(pair => new QualityQuantityRow(
                rosterByLogin[pair.Key],
                pair.Value.Snapshot,
                pair.Value.Flag,
                statsUnavailable ? null : linesChangedByAuthor.GetValueOrDefault(pair.Key)))
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
/// the roster developer, their volume-and-rework snapshot, the <see cref="FlagKind.PossibleRushing"/> flag
/// when raised, and their lines-changed total for the period (commit-size/volume metric).
/// </summary>
/// <param name="LinesChanged">
/// Total lines changed (additions plus deletions) on the default branch for the period
/// (<see cref="ActivityDataset.LinesChangedByAuthor"/>); null when GitHub's statistics
/// endpoint was unavailable for the whole dataset, zero when it was available but this developer changed
/// no default-branch lines in the period. Shown alongside <see cref="QualityQuantitySnapshot"/> — never
/// folded into <see cref="QualityQuantitySnapshot.PossibleRushing"/>'s volume, which stays commits plus PRs
/// opened per FR-027.
/// </param>
public sealed record QualityQuantityRow(Developer Developer, QualityQuantitySnapshot Snapshot, WellbeingFlag? Flag, int? LinesChanged);
