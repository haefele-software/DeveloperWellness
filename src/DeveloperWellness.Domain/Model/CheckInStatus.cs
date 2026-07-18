namespace DeveloperWellness.Domain.Model;

/// <summary>
/// Per-developer, per-scope, per-period check-in status (data-model.md CheckInStatus; FR-026). Carries
/// every active wellbeing flag for the developer and the resulting needs-check-in state. There is no score
/// or ranking value here (FR-029): flag count is only an ordering criterion applied by
/// <see cref="Signals.CheckInComposer"/> when it builds the roster, never a field on this type.
/// </summary>
/// <remarks>
/// Scope-awareness (FR-026) is entirely upstream of this type: a flag kind that does not apply at the
/// current scope — for example <see cref="FlagKind.SpreadThin"/> outside organisation scope — is simply
/// never present among the flags this status was built from, so <see cref="CheckInStatus"/> never
/// re-filters by <see cref="FlagKind"/> itself.
/// </remarks>
public sealed record CheckInStatus
{
    /// <summary>Creates a status for the given developer and its active flags.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="developer"/> or <paramref name="flags"/> is null.</exception>
    public CheckInStatus(Developer developer, IReadOnlyList<WellbeingFlag> flags)
    {
        Developer = developer ?? throw new ArgumentNullException(nameof(developer));
        Flags = flags ?? throw new ArgumentNullException(nameof(flags));
        NeedsCheckIn = Flags.Count > 0;
    }

    /// <summary>The developer this status describes.</summary>
    public Developer Developer { get; }

    /// <summary>Every active wellbeing flag for this developer, each carrying its own plain-language reason (SC-010).</summary>
    public IReadOnlyList<WellbeingFlag> Flags { get; }

    /// <summary>True when this developer has one or more active flags (FR-026).</summary>
    public bool NeedsCheckIn { get; }
}
