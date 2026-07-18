using DeveloperWellness.Domain.Model;
using DeveloperWellness.Domain.Options;
using DeveloperWellness.Domain.Signals;

namespace DeveloperWellness.UnitTests;

/// <summary>
/// Unit tests for <see cref="RushingCalculator"/> (tasks.md T042): raw commit/PR counts, SHA dedup,
/// changes-requested share and average-review-rounds arithmetic, the (project, PR number) linkage that
/// prevents cross-repository PR-number collisions, the minimum-PR-sample boundary, the four
/// above/at-median crossed with above/exactly-at-threshold quadrants (both conditions must hold, both
/// strictly), even-sized-roster median arithmetic, unmatched-author exclusion, inclusion of authors whose
/// only activity is reviewing or commenting, and the flag reason's numeric content.
/// </summary>
public class RushingCalculatorTests
{
    private static readonly DeveloperLogin Alice = new("alice");
    private static readonly DeveloperLogin Bob = new("bob");
    private static readonly DeveloperLogin Carol = new("carol");
    private static readonly DeveloperLogin Dave = new("dave");

    private const string ProjectA = "proj-a";
    private const string ProjectB = "proj-b";

    private static readonly DateTimeOffset Instant = new(2024, 1, 1, 10, 0, 0, TimeSpan.Zero);

    private static WellnessOptions CreateOptions() => new(); // MinPrSample = 3, ChangesRequestedThreshold = 0.40

    private static CommitEvent Commit(DeveloperLogin author, string sha, string projectName = ProjectA) =>
        new(author, projectName, Instant, sha, hasUsableOffset: true);

    private static PrOpenedEvent Opened(DeveloperLogin author, int prNumber, string projectName = ProjectA) =>
        new(author, projectName, Instant, prNumber);

    private static ReviewEvent Review(DeveloperLogin author, int prNumber, ReviewState state, string projectName = ProjectA) =>
        new(author, projectName, Instant, prNumber, state);

    [Fact]
    public void Calculate_ComputesRawCommitAndPrsOpenedCounts()
    {
        var events = new List<ActivityEvent>
        {
            Commit(Alice, "a1"),
            Commit(Alice, "a2"),
            Opened(Alice, prNumber: 1),
        };
        var options = CreateOptions();

        var results = RushingCalculator.Calculate(events, options, periodDays: 14);

        Assert.Equal(2, results[Alice].Snapshot.Commits);
        Assert.Equal(1, results[Alice].Snapshot.PrsOpened);
    }

    [Fact]
    public void Calculate_WithDuplicateShaFromMultipleBranches_CountsTheCommitOnce()
    {
        var events = new List<ActivityEvent>
        {
            new CommitEvent(Alice, ProjectA, Instant, "sha-shared", hasUsableOffset: true),
            new CommitEvent(Alice, ProjectA, Instant, "sha-shared", hasUsableOffset: true), // same commit, reachable from another branch
            new CommitEvent(Alice, ProjectA, Instant, "sha-other", hasUsableOffset: true),
        };
        var options = CreateOptions();

        var results = RushingCalculator.Calculate(events, options, periodDays: 14);

        Assert.Equal(2, results[Alice].Snapshot.Commits);
    }

    [Fact]
    public void Calculate_WithMultipleChangesRequestedReviewsOnOnePr_CountsThatPrOnceTowardsShare()
    {
        var events = new List<ActivityEvent>
        {
            Opened(Alice, prNumber: 1),
            Opened(Alice, prNumber: 2),
            Opened(Alice, prNumber: 3),
            Review(Bob, prNumber: 1, ReviewState.ChangesRequested),
            Review(Bob, prNumber: 1, ReviewState.ChangesRequested), // second CR round on the same PR
            Review(Bob, prNumber: 2, ReviewState.Approved),
            // PR 3 has no reviews.
        };
        var options = CreateOptions();

        var results = RushingCalculator.Calculate(events, options, periodDays: 14);

        // Only PR #1 saw a changes-requested review, despite it seeing two: 1 of 3 PRs.
        Assert.Equal(1m / 3m, results[Alice].Snapshot.ChangesRequestedShare);
    }

    [Fact]
    public void Calculate_ComputesAvgReviewRoundsAsMeanReviewsPerOpenedPr()
    {
        var events = new List<ActivityEvent>
        {
            Opened(Alice, prNumber: 1),
            Opened(Alice, prNumber: 2),
            Opened(Alice, prNumber: 3),
            Review(Bob, prNumber: 1, ReviewState.ChangesRequested),
            Review(Bob, prNumber: 1, ReviewState.Approved),
            Review(Bob, prNumber: 2, ReviewState.Approved),
            // PR 3 has no reviews.
        };
        var options = CreateOptions();

        var results = RushingCalculator.Calculate(events, options, periodDays: 14);

        // 3 total review rounds across 3 opened PRs.
        Assert.Equal(1m, results[Alice].Snapshot.AvgReviewRounds);
    }

    [Fact]
    public void Calculate_WithSamePrNumberInDifferentProjects_DoesNotLinkReviewsAcrossProjects()
    {
        var events = new List<ActivityEvent>
        {
            Opened(Alice, prNumber: 1, projectName: ProjectA),
            Opened(Alice, prNumber: 2, projectName: ProjectA),
            Opened(Alice, prNumber: 3, projectName: ProjectA),
            // A changes-requested review on PR #1, but in a different project - must not link.
            Review(Bob, prNumber: 1, ReviewState.ChangesRequested, projectName: ProjectB),
        };
        var options = CreateOptions();

        var results = RushingCalculator.Calculate(events, options, periodDays: 14);

        Assert.Equal(0m, results[Alice].Snapshot.ChangesRequestedShare);
        Assert.Equal(0m, results[Alice].Snapshot.AvgReviewRounds);
    }

    [Fact]
    public void Calculate_WithExactlyThreePrsOpened_MarksSufficientSample()
    {
        var events = new List<ActivityEvent>
        {
            Opened(Alice, prNumber: 1),
            Opened(Alice, prNumber: 2),
            Opened(Alice, prNumber: 3),
        };
        var options = CreateOptions();

        var results = RushingCalculator.Calculate(events, options, periodDays: 14);

        Assert.True(results[Alice].Snapshot.SufficientSample);
        Assert.NotNull(results[Alice].Snapshot.ChangesRequestedShare);
        Assert.NotNull(results[Alice].Snapshot.AvgReviewRounds);
    }

    [Fact]
    public void Calculate_WithOnlyTwoPrsOpened_MarksInsufficientSampleWithNullProxiesAndNoJudgement()
    {
        var events = new List<ActivityEvent>
        {
            Opened(Alice, prNumber: 1),
            Opened(Alice, prNumber: 2),
        };
        var options = CreateOptions();

        var results = RushingCalculator.Calculate(events, options, periodDays: 14);

        Assert.False(results[Alice].Snapshot.SufficientSample);
        Assert.Null(results[Alice].Snapshot.ChangesRequestedShare);
        Assert.Null(results[Alice].Snapshot.AvgReviewRounds);
        Assert.False(results[Alice].Snapshot.PossibleRushing);
        Assert.Null(results[Alice].Flag);
    }

    [Fact]
    public void Calculate_WithVolumeAboveMedianAndChangesRequestedShareAboveThreshold_RaisesPossibleRushingFlag()
    {
        var events = new List<ActivityEvent>
        {
            // Alice: 7 commits + 3 PRs opened = volume 10; 2 of 3 PRs saw changes requested (~66.7%).
            Commit(Alice, "a1"), Commit(Alice, "a2"), Commit(Alice, "a3"), Commit(Alice, "a4"),
            Commit(Alice, "a5"), Commit(Alice, "a6"), Commit(Alice, "a7"),
            Opened(Alice, prNumber: 1), Opened(Alice, prNumber: 2), Opened(Alice, prNumber: 3),
            Review(Carol, prNumber: 1, ReviewState.ChangesRequested),
            Review(Carol, prNumber: 2, ReviewState.ChangesRequested),
            Review(Carol, prNumber: 3, ReviewState.Approved),

            // Carol also has volume 2 of her own (no PRs) - roster median = (2 + 10) / 2 = 6.
            Commit(Carol, "c1"), Commit(Carol, "c2"),
        };
        var options = CreateOptions();

        var results = RushingCalculator.Calculate(events, options, periodDays: 14);

        Assert.True(results[Alice].Snapshot.PossibleRushing);
        Assert.NotNull(results[Alice].Flag);
        Assert.Equal(FlagKind.PossibleRushing, results[Alice].Flag!.Kind);
    }

    [Fact]
    public void Calculate_WithVolumeAboveMedianButChangesRequestedShareExactlyAtThreshold_DoesNotRaiseFlagBecauseTheRuleIsStrict()
    {
        var events = new List<ActivityEvent>
        {
            // Alice: 5 commits + 5 PRs opened = volume 10; exactly 2 of 5 PRs (40%) saw changes requested.
            Commit(Alice, "a1"), Commit(Alice, "a2"), Commit(Alice, "a3"), Commit(Alice, "a4"), Commit(Alice, "a5"),
            Opened(Alice, prNumber: 1), Opened(Alice, prNumber: 2), Opened(Alice, prNumber: 3),
            Opened(Alice, prNumber: 4), Opened(Alice, prNumber: 5),
            Review(Carol, prNumber: 1, ReviewState.ChangesRequested),
            Review(Carol, prNumber: 2, ReviewState.ChangesRequested),
            Review(Carol, prNumber: 3, ReviewState.Approved),
            Review(Carol, prNumber: 4, ReviewState.Approved),
            Review(Carol, prNumber: 5, ReviewState.Approved),

            // Roster median = (2 + 10) / 2 = 6; Alice's volume (10) is still above it.
            Commit(Carol, "c1"), Commit(Carol, "c2"),
        };
        var options = CreateOptions();

        var results = RushingCalculator.Calculate(events, options, periodDays: 14);

        Assert.Equal(0.40m, results[Alice].Snapshot.ChangesRequestedShare);
        Assert.False(results[Alice].Snapshot.PossibleRushing);
        Assert.Null(results[Alice].Flag);
    }

    [Fact]
    public void Calculate_WithVolumeExactlyAtMedianAndChangesRequestedShareAboveThreshold_DoesNotRaiseFlagBecauseTheRuleIsStrict()
    {
        var events = new List<ActivityEvent>
        {
            // Alice: 2 commits + 3 PRs opened = volume 5; 2 of 3 PRs saw changes requested (~66.7%).
            Commit(Alice, "a1"), Commit(Alice, "a2"),
            Opened(Alice, prNumber: 1), Opened(Alice, prNumber: 2), Opened(Alice, prNumber: 3),
            Review(Dave, prNumber: 1, ReviewState.ChangesRequested),
            Review(Dave, prNumber: 2, ReviewState.ChangesRequested),
            Review(Dave, prNumber: 3, ReviewState.Approved),

            // Carol: volume 2 (no PRs).
            Commit(Carol, "c1"), Commit(Carol, "c2"),

            // Dave: volume 8 (no PRs of his own; he only reviews Alice's above).
            Commit(Dave, "d1"), Commit(Dave, "d2"), Commit(Dave, "d3"), Commit(Dave, "d4"),
            Commit(Dave, "d5"), Commit(Dave, "d6"), Commit(Dave, "d7"), Commit(Dave, "d8"),
        };
        var options = CreateOptions();

        var results = RushingCalculator.Calculate(events, options, periodDays: 14);

        // Roster volumes sorted: 2 (Carol), 5 (Alice), 8 (Dave) - median is 5, Alice's own volume, so she
        // sits at the median rather than above it.
        Assert.False(results[Alice].Snapshot.PossibleRushing);
        Assert.Null(results[Alice].Flag);
    }

    [Fact]
    public void Calculate_WithVolumeBelowMedianAndChangesRequestedShareBelowThreshold_DoesNotRaiseFlag()
    {
        var events = new List<ActivityEvent>
        {
            // Alice: 0 commits + 3 PRs opened = volume 3; all reviews approved, so a 0% CR share.
            Opened(Alice, prNumber: 1), Opened(Alice, prNumber: 2), Opened(Alice, prNumber: 3),
            Review(Bob, prNumber: 1, ReviewState.Approved),
            Review(Bob, prNumber: 2, ReviewState.Approved),
            Review(Bob, prNumber: 3, ReviewState.Approved),

            // Bob: volume 10 (no PRs of his own; he only reviews Alice's above).
            Commit(Bob, "b1"), Commit(Bob, "b2"), Commit(Bob, "b3"), Commit(Bob, "b4"), Commit(Bob, "b5"),
            Commit(Bob, "b6"), Commit(Bob, "b7"), Commit(Bob, "b8"), Commit(Bob, "b9"), Commit(Bob, "b10"),

            // Carol: volume 8 (no PRs).
            Commit(Carol, "c1"), Commit(Carol, "c2"), Commit(Carol, "c3"), Commit(Carol, "c4"),
            Commit(Carol, "c5"), Commit(Carol, "c6"), Commit(Carol, "c7"), Commit(Carol, "c8"),
        };
        var options = CreateOptions();

        var results = RushingCalculator.Calculate(events, options, periodDays: 14);

        // Roster volumes sorted: 3 (Alice), 8 (Carol), 10 (Bob) - median is 8; Alice sits below it, and
        // her changes-requested share is 0%, below the 40% threshold too.
        Assert.False(results[Alice].Snapshot.PossibleRushing);
        Assert.Null(results[Alice].Flag);
    }

    [Fact]
    public void Calculate_WithEvenSizedRoster_UsesMeanOfTwoMiddleVolumesAsTheMedian()
    {
        var events = new List<ActivityEvent>
        {
            // Alice: 6 commits + 3 PRs opened = volume 9; 2 of 3 PRs saw changes requested (~66.7%).
            Commit(Alice, "a1"), Commit(Alice, "a2"), Commit(Alice, "a3"),
            Commit(Alice, "a4"), Commit(Alice, "a5"), Commit(Alice, "a6"),
            Opened(Alice, prNumber: 1), Opened(Alice, prNumber: 2), Opened(Alice, prNumber: 3),
            Review(Dave, prNumber: 1, ReviewState.ChangesRequested),
            Review(Dave, prNumber: 2, ReviewState.ChangesRequested),
            Review(Dave, prNumber: 3, ReviewState.Approved),

            // Bob: volume 6 (one of the two middle values).
            Commit(Bob, "b1"), Commit(Bob, "b2"), Commit(Bob, "b3"), Commit(Bob, "b4"), Commit(Bob, "b5"), Commit(Bob, "b6"),

            // Carol: volume 2 (the other middle value).
            Commit(Carol, "c1"), Commit(Carol, "c2"),

            // Dave: volume 1; he is also the reviewer above, adding no volume of his own beyond this commit.
            Commit(Dave, "d1"),
        };
        var options = CreateOptions();

        var results = RushingCalculator.Calculate(events, options, periodDays: 14);

        // Sorted volumes: 1 (Dave), 2 (Carol), 6 (Bob), 9 (Alice) - median is (2 + 6) / 2 = 4. Alice's
        // volume (9) sits clearly above it.
        Assert.True(results[Alice].Snapshot.PossibleRushing);
    }

    [Fact]
    public void Calculate_WithUnmatchedAuthorEvents_ExcludesThemFromResults()
    {
        var events = new List<ActivityEvent>
        {
            new CommitEvent(DeveloperLogin.Unmatched, ProjectA, Instant, "sha-1", hasUsableOffset: true),
            new PrOpenedEvent(DeveloperLogin.Unmatched, ProjectA, Instant, prNumber: 1),
        };
        var options = CreateOptions();

        var results = RushingCalculator.Calculate(events, options, periodDays: 14);

        Assert.Empty(results);
    }

    [Fact]
    public void Calculate_WithDeveloperWhoOnlyReviewsOthersPrs_StillReturnsAZeroVolumeSnapshot()
    {
        var events = new List<ActivityEvent>
        {
            Opened(Alice, prNumber: 1),
            Opened(Alice, prNumber: 2),
            Opened(Alice, prNumber: 3),
            Review(Bob, prNumber: 1, ReviewState.Approved),
        };
        var options = CreateOptions();

        var results = RushingCalculator.Calculate(events, options, periodDays: 14);

        Assert.True(results.ContainsKey(Bob));
        Assert.Equal(0, results[Bob].Snapshot.Commits);
        Assert.Equal(0, results[Bob].Snapshot.PrsOpened);
        Assert.False(results[Bob].Snapshot.SufficientSample);
    }

    [Fact]
    public void Calculate_WithDeveloperWhoOnlyComments_StillReturnsAZeroVolumeSnapshot()
    {
        var events = new List<ActivityEvent>
        {
            new CommentEvent(Alice, ProjectA, Instant, commentId: 1, bodyText: "nice work"),
        };
        var options = CreateOptions();

        var results = RushingCalculator.Calculate(events, options, periodDays: 14);

        Assert.True(results.ContainsKey(Alice));
        Assert.Equal(0, results[Alice].Snapshot.Commits);
        Assert.Equal(0, results[Alice].Snapshot.PrsOpened);
    }

    [Fact]
    public void Calculate_WithFlaggedDeveloper_ReasonContainsVolumeAndPercentNumbers()
    {
        var events = new List<ActivityEvent>
        {
            Commit(Alice, "a1"), Commit(Alice, "a2"), Commit(Alice, "a3"), Commit(Alice, "a4"),
            Commit(Alice, "a5"), Commit(Alice, "a6"), Commit(Alice, "a7"),
            Opened(Alice, prNumber: 1), Opened(Alice, prNumber: 2), Opened(Alice, prNumber: 3),
            Review(Carol, prNumber: 1, ReviewState.ChangesRequested),
            Review(Carol, prNumber: 2, ReviewState.ChangesRequested),
            Review(Carol, prNumber: 3, ReviewState.Approved),
            Commit(Carol, "c1"), Commit(Carol, "c2"),
        };
        var options = CreateOptions();

        var results = RushingCalculator.Calculate(events, options, periodDays: 14);

        var reason = results[Alice].Flag!.Reason;
        Assert.Contains("10 commits and PRs", reason, StringComparison.Ordinal);
        Assert.Contains("67%", reason, StringComparison.Ordinal); // 2 of 3 (~66.7%) rounds away from zero to 67%.
        Assert.Contains("over the last 14 days", reason, StringComparison.Ordinal);
        Assert.Contains("pace pressure", reason, StringComparison.Ordinal);
    }
}
