using DeveloperWellness.Application.Services;

namespace DeveloperWellness.Web.Services;

/// <summary>
/// Bridges a <see cref="DashboardResult"/> into the shell's <see cref="DashboardState"/> after a page
/// loads dashboard data (contracts/ui-design.md section 3): freshness, demo flag, covered projects, and
/// connection state all flow from the query result into the shell chrome every page shares. Reused by
/// every page that calls <c>DashboardQueryService</c> (T014, and later T019, T022, T025, T030).
/// </summary>
public static class DashboardBridge
{
    /// <summary>
    /// Applies <paramref name="result"/> to <paramref name="state"/>. A fresh load reports
    /// <see cref="ConnectionState.Connected"/>; credentials-missing and rate-limited failures map onto
    /// their matching shell states. Any other failure kind (a non-rate-limit connectivity error) leaves
    /// the shell's connection state untouched — there is no dedicated shell surface for it yet, so the
    /// calling page renders its own inline notice instead of overloading the rate-limit banner's wording.
    /// </summary>
    public static void Apply(this DashboardState state, DashboardResult result)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(result);

        if (result.Snapshot is { } snapshot)
        {
            state.SetDataLoaded(snapshot.LoadedAt, snapshot.IsDemoData, snapshot.Dataset.CoveredProjectNames);
        }

        switch (result.Kind)
        {
            case DashboardErrorKind.None:
                state.SetConnection(ConnectionState.Connected);
                break;
            case DashboardErrorKind.CredentialsMissing:
                state.SetConnection(ConnectionState.CredentialsMissing);
                break;
            case DashboardErrorKind.RateLimited:
                state.SetConnection(ConnectionState.RateLimited);
                break;
            case DashboardErrorKind.Unavailable:
            default:
                break;
        }
    }
}
