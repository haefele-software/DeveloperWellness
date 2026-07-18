namespace DeveloperWellness.Domain.Model;

/// <summary>
/// Per-developer, per-scope, per-period quality-versus-quantity snapshot (data-model.md
/// QualityQuantitySnapshot; FR-027; ui-design.md section 4.6). Built by
/// <see cref="Signals.RushingCalculator"/> from a developer's commit and pull-request activity. The rework
/// proxies are suppressed (<see langword="null"/>) below the minimum PR sample so a low-data developer is
/// marked insufficient data rather than judged (spec edge case, SC-012).
/// </summary>
/// <param name="Commits">Deduplicated commit count for the period (FR-002 dedup basis).</param>
/// <param name="PrsOpened">Pull requests opened by this developer in the period.</param>
/// <param name="ChangesRequestedShare">
/// The share of <see cref="PrsOpened"/> that received at least one changes-requested review;
/// <see langword="null"/> unless <see cref="SufficientSample"/> is true.
/// </param>
/// <param name="AvgReviewRounds">
/// The mean number of submitted reviews per opened pull request; <see langword="null"/> unless
/// <see cref="SufficientSample"/> is true.
/// </param>
/// <param name="SufficientSample">True when <see cref="PrsOpened"/> meets <c>WellnessOptions.MinPrSample</c> (FR-027).</param>
/// <param name="PossibleRushing">
/// True only when <see cref="SufficientSample"/> holds, output volume (<see cref="Commits"/> plus
/// <see cref="PrsOpened"/>) sits strictly above the roster median, and <see cref="ChangesRequestedShare"/>
/// strictly exceeds <c>WellnessOptions.ChangesRequestedThreshold</c> — both conditions together, per the
/// 2026-07-17 clarification, never either alone.
/// </param>
public sealed record QualityQuantitySnapshot(
    int Commits,
    int PrsOpened,
    decimal? ChangesRequestedShare,
    decimal? AvgReviewRounds,
    bool SufficientSample,
    bool PossibleRushing);
