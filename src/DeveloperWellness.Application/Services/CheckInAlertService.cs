using DeveloperWellness.Domain.Model;

namespace DeveloperWellness.Application.Services;

/// <summary>
/// Circuit-scoped seen-state machine behind the in-app check-in alert indicator (tasks.md T026;
/// data-model.md CheckInAlertState; FR-030, FR-031). Tracks which flagged developers the lead has already
/// seen, keyed per scope and period so switching scope or period never leaks seen-state across them.
/// </summary>
/// <remarks>
/// Registered scoped: this state deliberately dies with the Blazor circuit (data-model.md's session-scoped
/// assumption). Restarting the app, or starting a new circuit, may re-raise an alert already seen in a
/// previous session; accepted per spec Assumptions ("Alert state is session-scoped").
/// </remarks>
public sealed class CheckInAlertService
{
    private readonly HashSet<(ScopeKey Scope, int PeriodDays, DeveloperLogin Login)> _seenFlagged = [];

    /// <summary>Raised after every <see cref="MarkSeen"/> call; the shell's alert pill (T027) subscribes to re-render.</summary>
    public event Action? Changed;

    /// <summary>
    /// The count of <paramref name="currentFlagged"/> not yet marked seen for this exact
    /// <paramref name="scope"/> and <paramref name="periodDays"/> (FR-030). Drives the alert indicator's
    /// count.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="scope"/> or <paramref name="currentFlagged"/> is null.</exception>
    public int UnseenCount(ScopeKey scope, int periodDays, IReadOnlyCollection<DeveloperLogin> currentFlagged)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(currentFlagged);

        return currentFlagged.Count(login => !_seenFlagged.Contains((scope, periodDays, login)));
    }

    /// <summary>
    /// Marks every login in <paramref name="currentFlagged"/> as seen for this exact <paramref name="scope"/>
    /// and <paramref name="periodDays"/> (FR-031: viewing the roster clears the indicator for the developers
    /// it currently shows; a developer flagged afterwards is not in <paramref name="currentFlagged"/> yet,
    /// so the indicator reappears for them alone). Raises <see cref="Changed"/> unconditionally, even when
    /// every login was already seen, so a subscriber can always re-read <see cref="UnseenCount"/> after a
    /// roster view.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="scope"/> or <paramref name="currentFlagged"/> is null.</exception>
    public void MarkSeen(ScopeKey scope, int periodDays, IReadOnlyCollection<DeveloperLogin> currentFlagged)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(currentFlagged);

        foreach (var login in currentFlagged)
        {
            _seenFlagged.Add((scope, periodDays, login));
        }

        Changed?.Invoke();
    }
}
