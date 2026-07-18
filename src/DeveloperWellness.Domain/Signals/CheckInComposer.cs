using DeveloperWellness.Domain.Model;

namespace DeveloperWellness.Domain.Signals;

/// <summary>
/// Composes the check-in roster from activity summaries, ordering it most-concurrent-flags-first
/// (data-model.md CheckInStatus; FR-026, FR-028, FR-029). Pure, synchronous, and reference-free like every
/// Domain signal calculator.
/// </summary>
/// <remarks>
/// Scope-awareness (FR-026) is entirely the upstream summaries' responsibility: each
/// <see cref="ActivitySummary"/> passed in already carries only the flags computable in its scope — for
/// example <see cref="FlagKind.SpreadThin"/> is simply never present at project scope — so this composer
/// never re-filters by <see cref="FlagKind"/>; it only merges and orders. <c>additionalFlags</c> is how a
/// caller (e.g. the check-in service, T024) layers in flags computed separately from the summaries
/// themselves, such as a tone-based frustration mention (FR-019); those flags are appended after each
/// summary's own flags, and a developer who appears only in <c>additionalFlags</c> (edge case: a tone
/// flag for someone with no recorded activity) still receives a roster entry, backed by a placeholder
/// <see cref="Developer"/> built from their login alone since no roster <see cref="Developer"/> is
/// available for them.
/// </remarks>
public static class CheckInComposer
{
    /// <summary>
    /// Builds one <see cref="CheckInStatus"/> per developer in <paramref name="summaries"/> — flagged or
    /// not, so the UI can render both groups — plus one for every login present only in
    /// <paramref name="additionalFlags"/>. The roster is ordered per FR-028: flagged developers first, by
    /// flag count descending, ties broken alphabetically by <see cref="Model.Developer.DisplayName"/>
    /// (ordinal, case-insensitive); then unflagged developers alphabetically the same way. Flags are
    /// deduplicated by exact <c>(Kind, Reason)</c> pairs when merging, so the same signal reported twice
    /// collapses to one entry while two distinct reasons for the same <see cref="FlagKind"/> are both kept
    /// (spec edge case: several signals list their reasons together under one roster entry, never
    /// duplicated).
    /// </summary>
    /// <param name="summaries">Every summarised developer for the selected scope and period.</param>
    /// <param name="additionalFlags">
    /// Optional extra flags to merge per developer, keyed by login (for example tone-based frustration
    /// mentions computed outside <see cref="ActivitySummary"/>). Appended after each summary's own flags.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="summaries"/> is null.</exception>
    public static CheckInComposition Compose(
        IReadOnlyList<ActivitySummary> summaries,
        IReadOnlyDictionary<DeveloperLogin, IReadOnlyList<WellbeingFlag>>? additionalFlags = null)
    {
        ArgumentNullException.ThrowIfNull(summaries);

        var extraByLogin = additionalFlags ?? new Dictionary<DeveloperLogin, IReadOnlyList<WellbeingFlag>>();
        var coveredLogins = new HashSet<DeveloperLogin>();
        var statuses = new List<CheckInStatus>(summaries.Count);

        foreach (var summary in summaries)
        {
            var login = summary.Developer.Login;
            coveredLogins.Add(login);
            var extra = extraByLogin.GetValueOrDefault(login, []);
            statuses.Add(new CheckInStatus(summary.Developer, MergeFlags(summary.Flags, extra)));
        }

        foreach (var (login, extraFlags) in extraByLogin)
        {
            if (!coveredLogins.Add(login))
            {
                continue; // already covered by a summary above
            }

            var placeholderDeveloper = new Developer(login, displayName: null, isBot: false);
            statuses.Add(new CheckInStatus(placeholderDeveloper, MergeFlags([], extraFlags)));
        }

        var roster = OrderRoster(statuses);
        var needsCheckInCount = roster.Count(status => status.NeedsCheckIn);

        return new CheckInComposition(roster, needsCheckInCount);
    }

    /// <summary>Flagged first by flag count descending then alphabetical; then unflagged alphabetically (FR-028).</summary>
    private static IReadOnlyList<CheckInStatus> OrderRoster(IReadOnlyList<CheckInStatus> statuses)
    {
        var flagged = statuses
            .Where(status => status.NeedsCheckIn)
            .OrderByDescending(status => status.Flags.Count)
            .ThenBy(status => status.Developer.DisplayName, StringComparer.OrdinalIgnoreCase);

        var unflagged = statuses
            .Where(status => !status.NeedsCheckIn)
            .OrderBy(status => status.Developer.DisplayName, StringComparer.OrdinalIgnoreCase);

        return flagged.Concat(unflagged).ToList();
    }

    /// <summary>Appends <paramref name="additional"/> after <paramref name="primary"/>, dropping exact <c>(Kind, Reason)</c> repeats.</summary>
    private static IReadOnlyList<WellbeingFlag> MergeFlags(
        IReadOnlyList<WellbeingFlag> primary, IReadOnlyList<WellbeingFlag> additional)
    {
        var seen = new HashSet<(FlagKind Kind, string Reason)>();
        var merged = new List<WellbeingFlag>(primary.Count + additional.Count);

        foreach (var flag in primary.Concat(additional))
        {
            if (seen.Add((flag.Kind, flag.Reason)))
            {
                merged.Add(flag);
            }
        }

        return merged;
    }
}

/// <summary>
/// The composed check-in roster (data-model.md CheckInStatus roster shape): every summarised developer's
/// status, ordered per FR-028, plus the count of those who need a check-in.
/// </summary>
public sealed record CheckInComposition(IReadOnlyList<CheckInStatus> Roster, int NeedsCheckInCount);
