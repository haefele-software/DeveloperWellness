using DeveloperWellness.Domain.Model;
using DeveloperWellness.Domain.Signals;

namespace DeveloperWellness.Application.Services;

/// <summary>
/// A fetched-and-aggregated dataset for one scope and period (contracts/application-ports.md
/// DashboardQueryService; FR-021). Combines the raw <see cref="ActivityDataset"/> with its computed
/// <see cref="ActivitySummary"/> rows and unmatched bucket, so pages never need to re-run
/// <see cref="ActivityAggregator"/> themselves.
/// </summary>
public sealed record DashboardSnapshot(
    ActivityDataset Dataset,
    IReadOnlyList<ActivitySummary> Summaries,
    UnmatchedActivity Unmatched,
    DateTimeOffset LoadedAt,
    bool IsDemoData);
