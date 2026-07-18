using DeveloperWellness.Domain.Model;
using DeveloperWellness.Domain.Options;

namespace DeveloperWellness.Domain.Signals;

/// <summary>
/// Computes the author-local out-of-hours commit share per developer and raises
/// <see cref="FlagKind.OverworkCommits"/> above the configured threshold (data-model.md ActivitySummary
/// and WellbeingFlag; ui-design.md section 5 "Overwork (commits)"; FR-005, FR-026). Pure, synchronous,
/// and reference-free: every rule here reads only the events and options it is given.
/// </summary>
/// <remarks>
/// <see cref="IsOutOfHours(CommitEvent, WellnessOptions)"/> and
/// <see cref="GetLocalTime(CommitEvent, WellnessOptions)"/> are exposed as the single source of truth for
/// "is this commit out of hours, and what is its evaluated local time" — the developer-detail heatmap
/// (T019) buckets by the same two members rather than re-deriving the rule.
/// </remarks>
public static class OutOfHoursCommitCalculator
{
    /// <summary>
    /// Computes one <see cref="OutOfHoursCommitResult"/> per developer who authored at least one commit in
    /// <paramref name="events"/>. Commits are defensively deduplicated by <see cref="CommitEvent.Sha"/>
    /// dataset-wide (first occurrence wins), mirroring <see cref="ActivityAggregator"/>'s own dedup, even
    /// though a well-behaved caller has already deduplicated once. Developers with zero commits — including
    /// <see cref="DeveloperLogin.Unmatched"/>, which is always skipped — are simply absent from the
    /// returned dictionary, so a null out-of-hours share reads as "no entry" rather than a stored null.
    /// </summary>
    /// <param name="events">Every activity event for the scope and period; non-commit events are ignored.</param>
    /// <param name="options">Wellness configuration: working hours, working days, and the out-of-hours threshold.</param>
    /// <param name="periodDays">The period length in days, quoted in each flag's reason text.</param>
    /// <exception cref="ArgumentNullException"><paramref name="events"/> or <paramref name="options"/> is null.</exception>
    public static IReadOnlyDictionary<DeveloperLogin, OutOfHoursCommitResult> Calculate(
        IReadOnlyList<ActivityEvent> events, WellnessOptions options, int periodDays)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(options);

        var commitsByAuthor = GroupDedupedCommitsByAuthor(events);
        var results = new Dictionary<DeveloperLogin, OutOfHoursCommitResult>();

        foreach (var (author, commits) in commitsByAuthor)
        {
            var total = commits.Count;
            var outOfHours = commits.Count(commit => IsOutOfHours(commit, options));
            var share = (decimal)outOfHours / total;
            var flag = share > options.OutOfHoursThreshold
                ? new WellbeingFlag(FlagKind.OverworkCommits, BuildReason(outOfHours, total, share, periodDays))
                : null;

            results[author] = new OutOfHoursCommitResult(total, outOfHours, share, flag);
        }

        return results;
    }

    /// <summary>
    /// True when <paramref name="commit"/>, evaluated at <see cref="GetLocalTime"/>, falls on a
    /// non-working day (per <see cref="WellnessOptions.WorkingDays"/>) or outside
    /// <see cref="WellnessOptions.WorkingHoursStart"/>–<see cref="WellnessOptions.WorkingHoursEnd"/>. The
    /// working-hours window is start-inclusive, end-exclusive: a commit landing exactly at the end hour
    /// (e.g. 18:00) counts as out of hours, since the working day has finished by then.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="commit"/> or <paramref name="options"/> is null.</exception>
    public static bool IsOutOfHours(CommitEvent commit, WellnessOptions options)
    {
        ArgumentNullException.ThrowIfNull(commit);
        ArgumentNullException.ThrowIfNull(options);

        var local = GetLocalTime(commit, options);

        if (!options.WorkingDays.Contains(local.DayOfWeek))
        {
            return true;
        }

        var timeOfDay = TimeOnly.FromTimeSpan(local.TimeOfDay);
        return timeOfDay < options.WorkingHoursStart || timeOfDay >= options.WorkingHoursEnd;
    }

    /// <summary>
    /// The instant <see cref="ActivityEvent.OccurredAt"/> evaluated in the timezone the out-of-hours rule
    /// uses for this commit (FR-005): when <see cref="CommitEvent.HasUsableOffset"/> is true, the
    /// author-local offset already carried on <see cref="ActivityEvent.OccurredAt"/> is used as-is; otherwise
    /// the instant is converted to <see cref="WellnessOptions.OrganisationTimeZone"/> as the fallback basis.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="commit"/> or <paramref name="options"/> is null.</exception>
    /// <exception cref="TimeZoneNotFoundException"><see cref="WellnessOptions.OrganisationTimeZone"/> cannot be resolved.</exception>
    public static DateTimeOffset GetLocalTime(CommitEvent commit, WellnessOptions options)
    {
        ArgumentNullException.ThrowIfNull(commit);
        ArgumentNullException.ThrowIfNull(options);

        if (commit.HasUsableOffset)
        {
            return commit.OccurredAt;
        }

        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(options.OrganisationTimeZone);
        var utcInstant = commit.OccurredAt.UtcDateTime;
        var localDateTime = TimeZoneInfo.ConvertTimeFromUtc(utcInstant, timeZone);
        var offset = timeZone.GetUtcOffset(utcInstant);

        return new DateTimeOffset(localDateTime, offset);
    }

    /// <summary>Groups commit events by author, deduplicated dataset-wide by SHA (first occurrence wins), skipping unmatched authors.</summary>
    private static Dictionary<DeveloperLogin, List<CommitEvent>> GroupDedupedCommitsByAuthor(IReadOnlyList<ActivityEvent> events)
    {
        var seenShas = new HashSet<string>();
        var commitsByAuthor = new Dictionary<DeveloperLogin, List<CommitEvent>>();

        foreach (var activityEvent in events)
        {
            if (activityEvent is not CommitEvent commit || commit.Author.IsUnmatched)
            {
                continue;
            }

            if (!seenShas.Add(commit.Sha))
            {
                continue;
            }

            if (!commitsByAuthor.TryGetValue(commit.Author, out var authoredCommits))
            {
                authoredCommits = [];
                commitsByAuthor[commit.Author] = authoredCommits;
            }

            authoredCommits.Add(commit);
        }

        return commitsByAuthor;
    }

    /// <summary>Builds the design's observation-context-suggestion reason (ui-design.md section 2, SC-010).</summary>
    private static string BuildReason(int outOfHours, int total, decimal share, int periodDays)
    {
        var percent = (int)Math.Round(share * 100m, MidpointRounding.AwayFromZero);
        return $"{outOfHours} of {total} commits ({percent}%) landed out of hours in their local time over the last {periodDays} days. " +
               "It might be worth a quiet check-in about workload.";
    }
}

/// <summary>
/// One developer's out-of-hours commit result (data-model.md ActivitySummary.OutOfHoursCommitShare): the
/// raw counts feeding stat-tile subtext, the computed share, and the <see cref="FlagKind.OverworkCommits"/>
/// flag when the share clears the threshold.
/// </summary>
public sealed record OutOfHoursCommitResult(int TotalCommits, int OutOfHoursCommits, decimal Share, WellbeingFlag? Flag);
