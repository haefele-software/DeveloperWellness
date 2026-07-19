using DeveloperWellness.Domain.Model;
using DeveloperWellness.Domain.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace DeveloperWellness.Web.Services;

/// <summary>
/// Shell-level connectivity state driving the credentials-missing and rate-limit surfaces
/// (contracts/ui-design.md section 3, FR-011).
/// </summary>
public enum ConnectionState
{
    /// <summary>The activity source is reachable (or demo mode is on).</summary>
    Connected,

    /// <summary>Live mode is on but GitHub:Organisation or GitHub:Token is missing from configuration.</summary>
    CredentialsMissing,

    /// <summary>Live mode is on and credentials are configured, but GitHub refused them.</summary>
    CredentialsRejected,

    /// <summary>GitHub is reachable but currently rate-limited; the previously loaded dataset stays visible.</summary>
    RateLimited,
}

/// <summary>
/// Scoped (per-circuit) shell UI state (tasks.md T009): the selected scope and period, plus the
/// connection, freshness, and coverage surfaces later tasks (T012, T021, T027) drive. Kept
/// dependency-light by design: it holds no activity data itself, only the small set of primitives the
/// shell renders.
/// </summary>
public sealed class DashboardState
{
    private readonly WellnessOptions _wellnessOptions;
    private readonly IConfiguration _configuration;

    /// <summary>Creates the state, seeding the period from configuration and the connection from demo mode plus GitHub credential presence.</summary>
    public DashboardState(IOptions<WellnessOptions> wellnessOptions, IConfiguration configuration)
    {
        _wellnessOptions = wellnessOptions.Value;
        _configuration = configuration;

        PeriodDays = _wellnessOptions.PeriodDaysDefault;
        IsDemoData = _wellnessOptions.DemoMode;
        Connection = DetermineConnection();
    }

    /// <summary>Raised after any state change; subscribers should re-render via <c>InvokeAsync(StateHasChanged)</c>.</summary>
    public event Action? Changed;

    /// <summary>The selected dashboard scope (FR-007). Defaults to the whole organisation.</summary>
    public ScopeKey Scope { get; private set; } = ScopeKey.Organisation;

    /// <summary>The selected period length in days: 7, 14, or 30 (FR-009).</summary>
    public int PeriodDays { get; private set; }

    /// <summary>The current period, computed against "now" at read time.</summary>
    public Period CurrentPeriod => new(PeriodDays, DateTimeOffset.UtcNow);

    /// <summary>The shell's connectivity state (FR-011).</summary>
    public ConnectionState Connection { get; private set; }

    /// <summary>
    /// When <see cref="Connection"/> is <see cref="ConnectionState.RateLimited"/>, when the automatic
    /// background retry is scheduled for (reset-aware retry scheduling); null otherwise, or when no
    /// specific retry time is known.
    /// </summary>
    public DateTimeOffset? RetryAt { get; private set; }

    /// <summary>When the currently shown dataset was loaded; null before any data has loaded.</summary>
    public DateTimeOffset? DataLoadedAt { get; private set; }

    /// <summary>The projects actually covered by the most recently loaded dataset (post repo-cap).</summary>
    public IReadOnlyList<string> CoveredProjectNames { get; private set; } = [];

    /// <summary>True while the shown dataset came from the demo adapter rather than live GitHub (FR-013).</summary>
    public bool IsDemoData { get; private set; }

    /// <summary>True while a page-level data fetch is in flight; drives the shell's slim progress bar under the top bar.</summary>
    public bool IsLoading { get; private set; }

    /// <summary>
    /// Bumped by <see cref="NotifyDataRefreshed"/> whenever data changed underneath the current
    /// scope/period without the user changing either (e.g. a background rate-limit retry succeeding).
    /// Pages that gate their refetch on a <c>(Scope, PeriodDays)</c> load key include this in that key so
    /// a bump alone — with scope and period unchanged — is still recognised as "reload".
    /// </summary>
    public int DataVersion { get; private set; }

    /// <summary>Changes the selected scope and notifies subscribers.</summary>
    public void SetScope(ScopeKey scope)
    {
        Scope = scope;
        RaiseChanged();
    }

    /// <summary>Changes the selected period length and notifies subscribers.</summary>
    public void SetPeriod(int days)
    {
        PeriodDays = days;
        RaiseChanged();
    }

    /// <summary>
    /// Sets the connection state (e.g. after a fetch succeeds or hits a rate limit) and notifies
    /// subscribers. <paramref name="retryAt"/> carries the reset-aware retry time for a rate-limited
    /// connection; other connection states pass null, clearing any previously shown retry time.
    /// </summary>
    public void SetConnection(ConnectionState connection, DateTimeOffset? retryAt = null)
    {
        Connection = connection;
        RetryAt = retryAt;
        RaiseChanged();
    }

    /// <summary>Records a freshly loaded dataset's load time, demo flag, and covered projects, and notifies subscribers.</summary>
    public void SetDataLoaded(DateTimeOffset loadedAt, bool isDemoData, IReadOnlyList<string> coveredProjectNames)
    {
        DataLoadedAt = loadedAt;
        IsDemoData = isDemoData;
        CoveredProjectNames = coveredProjectNames;
        RaiseChanged();
    }

    /// <summary>Sets the in-flight loading flag driving the shell's progress bar and notifies subscribers.</summary>
    public void SetLoading(bool isLoading)
    {
        IsLoading = isLoading;
        RaiseChanged();
    }

    /// <summary>Re-evaluates the connection state from current configuration; the credentials-missing panel's "Re-check" action.</summary>
    public void RecheckConnection()
    {
        Connection = DetermineConnection();
        RaiseChanged();
    }

    /// <summary>
    /// Bumps <see cref="DataVersion"/> and notifies subscribers. Called after a background refresh (the
    /// GitHub rate-limit retry succeeding) applies fresh data to this state via <c>DashboardBridge.Apply</c>,
    /// so every page sharing this circuit's scope and period picks the change up through its own existing
    /// <see cref="Changed"/> subscription instead of showing kept-last data until the user next changes
    /// scope, period, or reloads the page.
    /// </summary>
    public void NotifyDataRefreshed()
    {
        DataVersion++;
        RaiseChanged();
    }

    private ConnectionState DetermineConnection()
    {
        if (_wellnessOptions.DemoMode)
        {
            return ConnectionState.Connected;
        }

        var organisation = _configuration["GitHub:Organisation"];
        var token = _configuration["GitHub:Token"];

        return string.IsNullOrWhiteSpace(organisation) || string.IsNullOrWhiteSpace(token)
            ? ConnectionState.CredentialsMissing
            : ConnectionState.Connected;
    }

    private void RaiseChanged() => Changed?.Invoke();
}
