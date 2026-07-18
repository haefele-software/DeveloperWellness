using DeveloperWellness.Domain.Model;
using DeveloperWellness.Domain.Options;

namespace DeveloperWellness.Domain.Signals;

/// <summary>
/// Computes each developer's quality-versus-quantity snapshot and raises
/// <see cref="FlagKind.PossibleRushing"/> only when output volume sits strictly above the roster median
/// AND the changes-requested share strictly exceeds the configured threshold, both guarded by the minimum
/// PR sample (data-model.md QualityQuantitySnapshot; FR-027; ui-design.md sections 4.6 and 5). Pure,
/// synchronous, and reference-free like every Domain signal calculator.
/// </summary>
/// <remarks>
/// <para>
/// <b>PR linkage.</b> "The PR of author A" means a <see cref="PrOpenedEvent"/> authored by A. Reviews "on"
/// that PR are every <see cref="ReviewEvent"/> in the full event set whose <see cref="ReviewEvent.PrNumber"/>
/// and <see cref="ActivityEvent.ProjectName"/> both match the opened PR — PR numbers repeat across
/// repositories, so matching on the number alone would wrongly link, for example, PR #12 in one repository
/// to a review submitted on an unrelated PR #12 elsewhere; matching the (project, number) pair together
/// avoids that collision. Review authorship is deliberately not part of the key: a review submitted by the
/// PR's own author (unusual, but possible) still counts as a review round and can still carry a
/// changes-requested outcome, and a review from an author the source could not match
/// (<see cref="DeveloperLogin.Unmatched"/>) still counts, because the review itself genuinely happened on
/// that PR — this is the simplest reading consistent with FR-027's "average review rounds per PR", which
/// counts submissions, not identities.
/// </para>
/// <para>
/// <b>Volume and the median.</b> Volume is <see cref="QualityQuantitySnapshot.Commits"/> plus
/// <see cref="QualityQuantitySnapshot.PrsOpened"/>. The roster median is computed once, across every
/// author's volume in the returned result set — including authors below the minimum PR sample, who still
/// have a volume even though their rework proxies are suppressed, per data-model.md's "above the roster
/// median" wording, which names the whole roster rather than a sufficient-sample-only subset. An
/// even-sized roster's median is the mean of its two middle volumes.
/// </para>
/// </remarks>
public static class RushingCalculator
{
    /// <summary>
    /// Computes one <see cref="RushingResult"/> per non-<see cref="DeveloperLogin.Unmatched"/> author who
    /// authored at least one event of any kind in <paramref name="events"/> — commits, reviews, comments,
    /// and PR opens all count towards inclusion, so a developer whose only activity is reviewing or
    /// commenting still gets a (zero-volume) snapshot rather than being silently absent, letting the
    /// <c>/quality</c> page show volume for below-sample people too.
    /// </summary>
    /// <param name="events">Every activity event for the scope and period.</param>
    /// <param name="options">Supplies <see cref="WellnessOptions.MinPrSample"/> and <see cref="WellnessOptions.ChangesRequestedThreshold"/>.</param>
    /// <param name="periodDays">The period length in days, quoted in each flag's reason text.</param>
    /// <exception cref="ArgumentNullException"><paramref name="events"/> or <paramref name="options"/> is null.</exception>
    public static IReadOnlyDictionary<DeveloperLogin, RushingResult> Calculate(
        IReadOnlyList<ActivityEvent> events, WellnessOptions options, int periodDays)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(options);

        var reviewsByPrKey = GroupReviewsByPrKey(events);
        var dedupedCommitsByAuthor = GroupDedupedCommitsByAuthor(events);
        var openedPrsByAuthor = GroupOpenedPrsByAuthor(events);
        var authors = CollectAuthors(events);

        var metricsByAuthor = new Dictionary<DeveloperLogin, AuthorMetrics>(authors.Count);

        foreach (var author in authors)
        {
            var commits = dedupedCommitsByAuthor.GetValueOrDefault(author, []).Count;
            var openedPrs = openedPrsByAuthor.GetValueOrDefault(author, []);
            var prsOpened = openedPrs.Count;
            var sufficientSample = prsOpened >= options.MinPrSample;

            decimal? changesRequestedShare = null;
            decimal? avgReviewRounds = null;

            if (sufficientSample)
            {
                var prsWithChangesRequested = 0;
                var totalReviewRounds = 0;

                foreach (var pr in openedPrs)
                {
                    var reviewsOnPr = reviewsByPrKey.GetValueOrDefault(pr, []);
                    totalReviewRounds += reviewsOnPr.Count;

                    if (reviewsOnPr.Any(review => review.State == ReviewState.ChangesRequested))
                    {
                        prsWithChangesRequested++;
                    }
                }

                changesRequestedShare = (decimal)prsWithChangesRequested / prsOpened;
                avgReviewRounds = (decimal)totalReviewRounds / prsOpened;
            }

            metricsByAuthor[author] = new AuthorMetrics(
                commits, prsOpened, changesRequestedShare, avgReviewRounds, sufficientSample, commits + prsOpened);
        }

        var median = ComputeMedian(metricsByAuthor.Values.Select(metrics => metrics.Volume).ToList());
        var results = new Dictionary<DeveloperLogin, RushingResult>(metricsByAuthor.Count);

        foreach (var (author, metrics) in metricsByAuthor)
        {
            var possibleRushing = false;
            WellbeingFlag? flag = null;

            if (metrics.SufficientSample
                && metrics.Volume > median
                && metrics.ChangesRequestedShare is { } share
                && share > options.ChangesRequestedThreshold)
            {
                possibleRushing = true;
                flag = new WellbeingFlag(FlagKind.PossibleRushing, BuildReason(metrics.Volume, share, periodDays));
            }

            var snapshot = new QualityQuantitySnapshot(
                metrics.Commits,
                metrics.PrsOpened,
                metrics.ChangesRequestedShare,
                metrics.AvgReviewRounds,
                metrics.SufficientSample,
                possibleRushing);

            results[author] = new RushingResult(snapshot, flag);
        }

        return results;
    }

    /// <summary>Groups every <see cref="ReviewEvent"/> by its (project, PR number) key, regardless of reviewer identity (see type remarks on PR linkage).</summary>
    private static Dictionary<(string ProjectName, int PrNumber), List<ReviewEvent>> GroupReviewsByPrKey(
        IReadOnlyList<ActivityEvent> events)
    {
        var reviewsByPrKey = new Dictionary<(string ProjectName, int PrNumber), List<ReviewEvent>>();

        foreach (var activityEvent in events)
        {
            if (activityEvent is not ReviewEvent review)
            {
                continue;
            }

            var key = (review.ProjectName, review.PrNumber);
            if (!reviewsByPrKey.TryGetValue(key, out var reviewsOnPr))
            {
                reviewsOnPr = [];
                reviewsByPrKey[key] = reviewsOnPr;
            }

            reviewsOnPr.Add(review);
        }

        return reviewsByPrKey;
    }

    /// <summary>Groups commit events by author, deduplicated dataset-wide by SHA (first occurrence wins), skipping unmatched authors.</summary>
    private static Dictionary<DeveloperLogin, List<CommitEvent>> GroupDedupedCommitsByAuthor(
        IReadOnlyList<ActivityEvent> events)
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

    /// <summary>
    /// Groups PR-opened events by author, keyed by (project, PR number) for review linkage, skipping
    /// unmatched authors. Not deduplicated: a repeated <see cref="PrOpenedEvent"/> for the same PR counts
    /// as a separate opened-PR occurrence, mirroring <see cref="QualityQuantitySnapshot.PrsOpened"/>'s
    /// literal event count.
    /// </summary>
    private static Dictionary<DeveloperLogin, List<(string ProjectName, int PrNumber)>> GroupOpenedPrsByAuthor(
        IReadOnlyList<ActivityEvent> events)
    {
        var openedPrsByAuthor = new Dictionary<DeveloperLogin, List<(string ProjectName, int PrNumber)>>();

        foreach (var activityEvent in events)
        {
            if (activityEvent is not PrOpenedEvent prOpened || prOpened.Author.IsUnmatched)
            {
                continue;
            }

            if (!openedPrsByAuthor.TryGetValue(prOpened.Author, out var openedPrs))
            {
                openedPrs = [];
                openedPrsByAuthor[prOpened.Author] = openedPrs;
            }

            openedPrs.Add((prOpened.ProjectName, prOpened.PrNumber));
        }

        return openedPrsByAuthor;
    }

    /// <summary>Every non-<see cref="DeveloperLogin.Unmatched"/> author appearing on any event, of any kind.</summary>
    private static HashSet<DeveloperLogin> CollectAuthors(IReadOnlyList<ActivityEvent> events)
    {
        var authors = new HashSet<DeveloperLogin>();

        foreach (var activityEvent in events)
        {
            if (!activityEvent.Author.IsUnmatched)
            {
                authors.Add(activityEvent.Author);
            }
        }

        return authors;
    }

    /// <summary>The median of <paramref name="volumes"/>; the mean of the two middle values when the count is even. Zero when empty.</summary>
    private static decimal ComputeMedian(List<int> volumes)
    {
        if (volumes.Count == 0)
        {
            return 0m;
        }

        volumes.Sort();
        var midIndex = volumes.Count / 2;

        return volumes.Count % 2 == 1
            ? volumes[midIndex]
            : (volumes[midIndex - 1] + volumes[midIndex]) / 2m;
    }

    /// <summary>
    /// Builds the design's pace-pressure reason (ui-design.md section 4.6 closing caption; SC-010):
    /// observation of the raw volume and changes-requested percentage, then the supportive
    /// "pace pressure, not carelessness" framing.
    /// </summary>
    private static string BuildReason(int volume, decimal changesRequestedShare, int periodDays)
    {
        var percent = (int)Math.Round(changesRequestedShare * 100m, MidpointRounding.AwayFromZero);
        return $"High output ({volume} commits and PRs) with {percent}% of PRs seeing changes requested over the last {periodDays} days. " +
               "That pattern usually means pace pressure — it might be worth easing the load rather than pushing harder.";
    }

    /// <summary>One author's intermediate metrics, computed before the roster median is known.</summary>
    private sealed record AuthorMetrics(
        int Commits,
        int PrsOpened,
        decimal? ChangesRequestedShare,
        decimal? AvgReviewRounds,
        bool SufficientSample,
        int Volume);
}

/// <summary>
/// One author's rushing computation: their quality-versus-quantity snapshot (data-model.md
/// QualityQuantitySnapshot) and, when <see cref="QualityQuantitySnapshot.PossibleRushing"/> holds, the
/// resulting <see cref="FlagKind.PossibleRushing"/> flag. <see cref="Flag"/> is null whenever the snapshot
/// is below sample or the rushing rule does not hold (FR-027).
/// </summary>
public sealed record RushingResult(QualityQuantitySnapshot Snapshot, WellbeingFlag? Flag);
