using DeveloperWellness.Domain.Model;
using DeveloperWellness.Domain.Options;

namespace DeveloperWellness.Domain.Signals;

/// <summary>
/// Computes each author's distinct active-project count from a set of activity events and raises
/// <see cref="FlagKind.SpreadThin"/> at or above <see cref="WellnessOptions.SpreadThinThreshold"/>
/// (data-model.md WellbeingFlag rules; design contract section 5). Pure, synchronous, and
/// reference-free like every Domain signal calculator.
/// </summary>
/// <remarks>
/// This calculator is scope-agnostic: it counts distinct <see cref="ActivityEvent.ProjectName"/> values
/// per author across whatever events it is given, with no notion of "organisation" or "project" scope.
/// Spread-thin is only a meaningful signal at organisation scope — data-model.md fixes
/// <c>ActivitySummary.DistinctProjectCount</c> as null at project scope, since a project-scoped fetch
/// cannot see a developer's activity elsewhere, so a count computed from project-scoped events would be
/// artificially low and any resulting flag would be misleading. Callers (the Application-layer wiring,
/// e.g. task T021) must invoke <see cref="Calculate"/>, and apply its result onto an
/// <see cref="ActivitySummary"/>, only when the dataset in hand is organisation-scoped.
/// </remarks>
public static class SpreadThinCalculator
{
    /// <summary>
    /// Groups <paramref name="events"/> by non-<see cref="DeveloperLogin.Unmatched"/> author and counts
    /// each author's distinct <see cref="ActivityEvent.ProjectName"/> values across every event type — a
    /// commit, review, comment, or PR-opened event in a project all count as activity there, and a
    /// project touched by several events still counts once. Raises <see cref="FlagKind.SpreadThin"/> when
    /// that count is at or above <see cref="WellnessOptions.SpreadThinThreshold"/> (data-model.md: "at or
    /// above the threshold" — a developer sitting exactly on the threshold is flagged).
    /// </summary>
    /// <param name="events">
    /// The events to count projects from. See the type-level remarks: only pass organisation-scoped
    /// events here, and only apply the result at organisation scope.
    /// </param>
    /// <param name="options">Supplies <see cref="WellnessOptions.SpreadThinThreshold"/>.</param>
    /// <returns>One result per author who authored at least one of the given events.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="events"/> or <paramref name="options"/> is null.</exception>
    public static IReadOnlyDictionary<DeveloperLogin, SpreadThinResult> Calculate(
        IReadOnlyList<ActivityEvent> events,
        WellnessOptions options)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(options);

        var projectNamesByAuthor = new Dictionary<DeveloperLogin, HashSet<string>>();

        foreach (var activityEvent in events)
        {
            var author = activityEvent.Author;
            if (author.IsUnmatched)
            {
                continue; // spread-thin is never assessed for the unmatched bucket
            }

            if (!projectNamesByAuthor.TryGetValue(author, out var projectNames))
            {
                projectNames = new HashSet<string>(StringComparer.Ordinal);
                projectNamesByAuthor[author] = projectNames;
            }

            projectNames.Add(activityEvent.ProjectName);
        }

        var results = new Dictionary<DeveloperLogin, SpreadThinResult>(projectNamesByAuthor.Count);

        foreach (var (author, projectNames) in projectNamesByAuthor)
        {
            var distinctProjectCount = projectNames.Count;
            var flag = distinctProjectCount >= options.SpreadThinThreshold
                ? new WellbeingFlag(FlagKind.SpreadThin, BuildReason(distinctProjectCount))
                : null;

            results[author] = new SpreadThinResult(distinctProjectCount, flag);
        }

        return results;
    }

    /// <summary>
    /// Builds the design contract's observation-context-suggestion reason (section 2, SC-010) for a
    /// spread-thin flag, naming the developer's actual distinct-project count. Supportive in tone, never
    /// accusatory, per the design's binding principle.
    /// </summary>
    private static string BuildReason(int distinctProjectCount) =>
        $"Active in {distinctProjectCount} different projects this period — that's a lot of context " +
        "switching. It might be worth checking whether the load could be rebalanced.";
}

/// <summary>
/// One author's spread-thin computation: their distinct active-project count and, when it met the
/// configured threshold, the resulting <see cref="FlagKind.SpreadThin"/> flag (data-model.md WellbeingFlag
/// rules). <see cref="Flag"/> is null below the threshold.
/// </summary>
public sealed record SpreadThinResult(int DistinctProjectCount, WellbeingFlag? Flag);
