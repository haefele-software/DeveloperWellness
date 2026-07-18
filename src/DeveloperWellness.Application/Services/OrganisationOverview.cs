using DeveloperWellness.Domain.Model;

namespace DeveloperWellness.Application.Services;

/// <summary>
/// The Pulse Overview landing snapshot for a scope and period (tasks.md T029; data-model.md
/// OrganisationOverview; FR-035..FR-039; ui-design.md 4.1). Built by <see cref="OverviewService"/> from an
/// already-fetched <see cref="DashboardSnapshot"/> and its <see cref="CheckInComposition"/> roster.
/// </summary>
public sealed record OrganisationOverview(
    OverviewKpis Kpis,
    IReadOnlyList<ProjectRow> ProjectRows,
    IReadOnlyList<TeamCard> Teams,
    IReadOnlyList<Recommendation> Recommendations,
    DevelopmentTrend? Trend,
    SentimentReading Sentiment);

/// <summary>
/// The Overview's wellbeing KPI tiles row (data-model.md OrganisationOverview.Kpis; ui-design.md 4.1;
/// FR-035). <see cref="ProjectsPerDeveloper"/> is populated at organisation scope only;
/// <see cref="Contributors"/> at project scope only — the design swaps one tile for the other, so exactly
/// one of the pair is non-null for any given <see cref="OrganisationOverview"/>.
/// </summary>
public sealed record OverviewKpis(
    int MightNeedCheckIn,
    int NewSinceViewed,
    decimal? AfterHoursCommitShare,
    decimal? AfterHoursPrShare,
    decimal? ProjectsPerDeveloper,
    int? Contributors);

/// <summary>
/// One row of the Overview's projects table (data-model.md ProjectRow; ui-design.md 4.1). Built for every
/// name in <see cref="ActivityDataset.CoveredProjectNames"/>, including projects with no activity in the
/// period as an all-zero row, so the table always reflects the full covered set rather than only the
/// projects that happened to have events. Ordered by <see cref="Commits"/> descending by
/// <see cref="OverviewService"/> — projects may be ranked; people never (design contract principle 1).
/// </summary>
public sealed record ProjectRow(
    string Name,
    int People,
    int Commits,
    int PrsOpened,
    int Reviews,
    int Comments,
    decimal? AfterHoursShare,
    string SignalNote);
