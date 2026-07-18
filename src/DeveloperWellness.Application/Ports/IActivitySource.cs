using DeveloperWellness.Domain.Model;

namespace DeveloperWellness.Application.Ports;

/// <summary>
/// Fetches the full activity dataset for a scope and period (contracts/application-ports.md, Port 1).
/// Implementations MUST NOT let Octokit, Azure, or any other transport type leak through this interface,
/// so demo mode (FR-013) is a pure adapter swap behind <c>WellnessOptions.DemoMode</c>.
/// </summary>
public interface IActivitySource
{
    /// <summary>
    /// Returns the roster, projects, teams, events, weekly commit counts, covered project names, and load
    /// time for <paramref name="scope"/> and <paramref name="period"/> (data-model.md ActivityDataset).
    /// Implementations MUST apply the repo cap with recently-active ordering (FR-007), the branch cap with
    /// SHA deduplication (FR-002), bot exclusion (FR-010), the full member roster including no-activity
    /// members (FR-012), and unmatched-author bucketing (edge case).
    /// </summary>
    /// <exception cref="ActivitySourceException">
    /// Credential, rate-limit, or connectivity failure, with a user-presentable message (FR-011). Callers
    /// keep the previously loaded dataset visible rather than clearing the UI.
    /// </exception>
    Task<ActivityDataset> GetActivityAsync(ScopeKey scope, Period period, CancellationToken cancellationToken);
}
