using DeveloperWellness.Domain.Model;

namespace DeveloperWellness.Domain.Signals;

/// <summary>
/// Computes one <see cref="ActivitySummary"/> per non-bot roster member from a scope-and-period-filtered
/// <see cref="ActivityDataset"/> (data-model.md ActivitySummary; FR-002, FR-003, FR-010, FR-012). Pure,
/// synchronous, and reference-free: every rule here reads only the dataset it is given, never Application
/// or Infrastructure types.
/// </summary>
public static class ActivityAggregator
{
    /// <summary>
    /// Builds one <see cref="ActivitySummary"/> per non-bot roster member — including members with zero
    /// events, who land in the no-activity group (FR-012, <see cref="ActivitySummary.HasActivity"/> false)
    /// — plus the <see cref="UnmatchedActivity"/> bucket for events that could not be attributed to any
    /// roster login. Commits are defensively deduplicated by <see cref="CommitEvent.Sha"/> even though a
    /// well-behaved source has already deduplicated them (FR-002); every submitted review counts as its
    /// own event, so multiple review rounds on one pull request each count (FR-003); bots are excluded
    /// entirely, never appearing in a summary or the unmatched bucket (FR-010). Login matching is
    /// case-insensitive via <see cref="DeveloperLogin"/> equality.
    /// </summary>
    public static ActivityAggregation Aggregate(ActivityDataset dataset)
    {
        ArgumentNullException.ThrowIfNull(dataset);

        var dedupedEvents = DeduplicateCommits(dataset.Events);

        var botLogins = dataset.Roster.Where(d => d.IsBot).Select(d => d.Login).ToHashSet();
        var rosterLogins = dataset.Roster.Where(d => !d.IsBot).Select(d => d.Login).ToHashSet();

        var eventsByAuthor = new Dictionary<DeveloperLogin, List<ActivityEvent>>();
        var unmatchedEvents = new List<ActivityEvent>();

        foreach (var activityEvent in dedupedEvents)
        {
            var author = activityEvent.Author;

            if (botLogins.Contains(author))
            {
                continue; // bots excluded entirely: not a summary, not the unmatched bucket (FR-010)
            }

            if (author.IsUnmatched || !rosterLogins.Contains(author))
            {
                unmatchedEvents.Add(activityEvent); // unmatched or off-roster author: never misattribute
                continue;
            }

            if (!eventsByAuthor.TryGetValue(author, out var authoredEvents))
            {
                authoredEvents = [];
                eventsByAuthor[author] = authoredEvents;
            }

            authoredEvents.Add(activityEvent);
        }

        var summaries = dataset.Roster
            .Where(developer => !developer.IsBot)
            .Select(developer => BuildSummary(developer, eventsByAuthor.GetValueOrDefault(developer.Login, [])))
            .ToList();

        var unmatched = BuildUnmatched(unmatchedEvents);

        return new ActivityAggregation(summaries, unmatched);
    }

    /// <summary>Removes duplicate commit events sharing the same SHA, keeping the first occurrence.</summary>
    private static IReadOnlyList<ActivityEvent> DeduplicateCommits(IReadOnlyList<ActivityEvent> events)
    {
        var seenShas = new HashSet<string>();
        var deduplicated = new List<ActivityEvent>(events.Count);

        foreach (var activityEvent in events)
        {
            if (activityEvent is CommitEvent commit && !seenShas.Add(commit.Sha))
            {
                continue;
            }

            deduplicated.Add(activityEvent);
        }

        return deduplicated;
    }

    private static ActivitySummary BuildSummary(Developer developer, IReadOnlyList<ActivityEvent> events) =>
        new(
            developer: developer,
            commitCount: events.Count(e => e is CommitEvent),
            reviewCount: events.Count(e => e is ReviewEvent),
            commentCount: events.Count(e => e is CommentEvent),
            prsOpenedCount: events.Count(e => e is PrOpenedEvent),
            outOfHoursCommitShare: null, // filled by OutOfHoursCommitCalculator (T016/T018)
            outOfHoursPrShare: null, // filled by the PR out-of-hours calculator (T031/T032)
            distinctProjectCount: null, // filled by SpreadThinCalculator (T020/T021)
            flags: [], // filled once signal calculators land
            hasActivity: events.Count > 0);

    private static UnmatchedActivity BuildUnmatched(IReadOnlyList<ActivityEvent> events) =>
        new(
            CommitCount: events.Count(e => e is CommitEvent),
            ReviewCount: events.Count(e => e is ReviewEvent),
            CommentCount: events.Count(e => e is CommentEvent),
            PrsOpenedCount: events.Count(e => e is PrOpenedEvent));
}

/// <summary>
/// The result of <see cref="ActivityAggregator.Aggregate"/>: one summary per non-bot roster member plus
/// the events that could not be attributed to anyone on the roster.
/// </summary>
public sealed record ActivityAggregation(IReadOnlyList<ActivitySummary> Summaries, UnmatchedActivity Unmatched);

/// <summary>
/// Counts for events whose author was <see cref="DeveloperLogin.Unmatched"/> or did not match any non-bot
/// roster login (data-model.md edge case: unmatched-author bucketing). Never attributed to, or counted
/// against, any individual developer.
/// </summary>
public sealed record UnmatchedActivity(int CommitCount, int ReviewCount, int CommentCount, int PrsOpenedCount);
