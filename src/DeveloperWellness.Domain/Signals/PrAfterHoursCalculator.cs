using DeveloperWellness.Domain.Model;
using DeveloperWellness.Domain.Options;

namespace DeveloperWellness.Domain.Signals;

/// <summary>
/// Computes the organisation-timezone out-of-hours PR-activity share per developer and raises
/// <see cref="FlagKind.OverworkPrActivity"/> above the configured threshold, guarded by a minimum
/// PR-event sample (data-model.md ActivitySummary.OutOfHoursPrShare and WellbeingFlag; ui-design.md
/// section 5 "Overwork (PR activity)"; FR-024, FR-025). Pure, synchronous, and reference-free: every
/// rule here reads only the events and options it is given.
/// </summary>
/// <remarks>
/// PR events carry no usable author-local time offset (spec edge case), so unlike
/// <see cref="OutOfHoursCommitCalculator"/>, classification always uses
/// <see cref="WellnessOptions.OrganisationTimeZone"/> — never a per-event fallback decision.
/// <see cref="IsOutOfHours(ActivityEvent, WellnessOptions)"/> and
/// <see cref="GetLocalTime(ActivityEvent, WellnessOptions)"/> are exposed as the single source of truth
/// for "is this PR event out of hours, and what is its evaluated organisation-local time" — the
/// developer-detail PR bar (US6 wiring) buckets by the same two members rather than re-deriving the rule.
/// </remarks>
public static class PrAfterHoursCalculator
{
    /// <summary>
    /// Computes one <see cref="PrAfterHoursResult"/> per developer who authored at least one PR event
    /// (<see cref="PrOpenedEvent"/> or <see cref="ReviewEvent"/>) in <paramref name="events"/>. Comment
    /// and commit events are not PR events (FR-024) and are ignored. Developers with zero PR events —
    /// including <see cref="DeveloperLogin.Unmatched"/>, which is always skipped — are simply absent from
    /// the returned dictionary, so a null out-of-hours share reads as "no entry" rather than a stored null.
    /// </summary>
    /// <param name="events">Every activity event for the scope and period; non-PR events are ignored.</param>
    /// <param name="options">
    /// Wellness configuration: working hours, working days, the organisation timezone, the out-of-hours
    /// threshold, and the minimum PR-event guard (FR-025).
    /// </param>
    /// <param name="periodDays">The period length in days, quoted in each flag's reason text.</param>
    /// <exception cref="ArgumentNullException"><paramref name="events"/> or <paramref name="options"/> is null.</exception>
    public static IReadOnlyDictionary<DeveloperLogin, PrAfterHoursResult> Calculate(
        IReadOnlyList<ActivityEvent> events, WellnessOptions options, int periodDays)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(options);

        var prEventsByAuthor = GroupPrEventsByAuthor(events);
        var results = new Dictionary<DeveloperLogin, PrAfterHoursResult>();

        foreach (var (author, prEvents) in prEventsByAuthor)
        {
            var total = prEvents.Count;
            var outOfHours = prEvents.Count(prEvent => IsOutOfHours(prEvent, options));
            var share = (decimal)outOfHours / total;
            var flag = share > options.OutOfHoursThreshold && total >= options.MinPrEvents
                ? new WellbeingFlag(FlagKind.OverworkPrActivity, BuildReason(outOfHours, total, share, periodDays))
                : null;

            results[author] = new PrAfterHoursResult(total, outOfHours, share, flag);
        }

        return results;
    }

    /// <summary>
    /// True when <paramref name="prEvent"/>, evaluated at <see cref="GetLocalTime"/>, falls on a
    /// non-working day (per <see cref="WellnessOptions.WorkingDays"/>) or outside
    /// <see cref="WellnessOptions.WorkingHoursStart"/>–<see cref="WellnessOptions.WorkingHoursEnd"/>. The
    /// working-hours window is start-inclusive, end-exclusive: a PR event landing exactly at the end hour
    /// (e.g. 18:00) counts as out of hours, since the working day has finished by then.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="prEvent"/> or <paramref name="options"/> is null.</exception>
    public static bool IsOutOfHours(ActivityEvent prEvent, WellnessOptions options)
    {
        ArgumentNullException.ThrowIfNull(prEvent);
        ArgumentNullException.ThrowIfNull(options);

        var local = GetLocalTime(prEvent, options);

        if (!options.WorkingDays.Contains(local.DayOfWeek))
        {
            return true;
        }

        var timeOfDay = TimeOnly.FromTimeSpan(local.TimeOfDay);
        return timeOfDay < options.WorkingHoursStart || timeOfDay >= options.WorkingHoursEnd;
    }

    /// <summary>
    /// <see cref="ActivityEvent.OccurredAt"/> converted to <see cref="WellnessOptions.OrganisationTimeZone"/>.
    /// PR events carry no usable author-local offset (spec edge case), so organisation time is always the
    /// classification basis here — never a per-event fallback decision, unlike
    /// <see cref="OutOfHoursCommitCalculator.GetLocalTime(CommitEvent, WellnessOptions)"/>.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="prEvent"/> or <paramref name="options"/> is null.</exception>
    /// <exception cref="TimeZoneNotFoundException"><see cref="WellnessOptions.OrganisationTimeZone"/> cannot be resolved.</exception>
    public static DateTimeOffset GetLocalTime(ActivityEvent prEvent, WellnessOptions options)
    {
        ArgumentNullException.ThrowIfNull(prEvent);
        ArgumentNullException.ThrowIfNull(options);

        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(options.OrganisationTimeZone);
        var utcInstant = prEvent.OccurredAt.UtcDateTime;
        var localDateTime = TimeZoneInfo.ConvertTimeFromUtc(utcInstant, timeZone);
        var offset = timeZone.GetUtcOffset(utcInstant);

        return new DateTimeOffset(localDateTime, offset);
    }

    /// <summary>Groups PR events (opens and reviews) by author, skipping unmatched authors and every other event kind.</summary>
    private static Dictionary<DeveloperLogin, List<ActivityEvent>> GroupPrEventsByAuthor(IReadOnlyList<ActivityEvent> events)
    {
        var prEventsByAuthor = new Dictionary<DeveloperLogin, List<ActivityEvent>>();

        foreach (var activityEvent in events)
        {
            if (activityEvent is not (PrOpenedEvent or ReviewEvent) || activityEvent.Author.IsUnmatched)
            {
                continue;
            }

            if (!prEventsByAuthor.TryGetValue(activityEvent.Author, out var authoredPrEvents))
            {
                authoredPrEvents = [];
                prEventsByAuthor[activityEvent.Author] = authoredPrEvents;
            }

            authoredPrEvents.Add(activityEvent);
        }

        return prEventsByAuthor;
    }

    /// <summary>
    /// Builds the design's observation-context-suggestion reason (ui-design.md section 2, SC-010),
    /// stating the organisation-time basis per the spec edge case that PR events carry no author-local
    /// offset.
    /// </summary>
    private static string BuildReason(int outOfHours, int total, decimal share, int periodDays)
    {
        var percent = (int)Math.Round(share * 100m, MidpointRounding.AwayFromZero);
        return $"{outOfHours} of {total} PR reviews and opens ({percent}%) happened outside working hours in organisation time over the last {periodDays} days. " +
               "It might be worth a chat about after-hours review load.";
    }
}

/// <summary>
/// One developer's out-of-hours PR-activity result (data-model.md ActivitySummary.OutOfHoursPrShare):
/// the raw counts feeding stat-tile subtext, the computed share, and the
/// <see cref="FlagKind.OverworkPrActivity"/> flag when the share clears the threshold and the minimum
/// PR-event guard (FR-025) is met.
/// </summary>
public sealed record PrAfterHoursResult(int TotalPrEvents, int OutOfHoursPrEvents, decimal Share, WellbeingFlag? Flag);
