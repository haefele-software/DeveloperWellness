using DeveloperWellness.Domain.Model;
using DeveloperWellness.Domain.Signals;

namespace DeveloperWellness.UnitTests;

/// <summary>
/// Unit tests for <see cref="ActivityAggregator"/> (tasks.md T011): per-developer counts across every
/// event type, SHA-dedup of commit input, zero-activity grouping, bot exclusion, unmatched-author and
/// off-roster bucketing, and multi-round review counting. Every dataset here is hand-rolled inline
/// (Domain tests stay Infrastructure-free; no <c>DemoActivitySource</c> dependency).
/// </summary>
public class ActivityAggregatorTests
{
    private static readonly DeveloperLogin Alice = new("alice");
    private static readonly DeveloperLogin Bob = new("bob");
    private static readonly DeveloperLogin BotLogin = new("ci-bot");
    private static readonly DeveloperLogin OffRoster = new("someone-else");

    private const string ProjectName = "demo-project";

    private static ActivityDataset BuildDataset(
        IReadOnlyList<Developer> roster,
        IReadOnlyList<ActivityEvent> events) =>
        new(
            roster: roster,
            projects: [new Project(ProjectName, DateTimeOffset.UtcNow)],
            teams: [],
            events: events,
            weeklyCommitCounts: [],
            coveredProjectNames: [ProjectName],
            loadedAt: DateTimeOffset.UtcNow,
            isDemoData: true);

    [Fact]
    public void Aggregate_WithOneEventOfEachType_ProducesCorrectPerDeveloperCounts()
    {
        var roster = new List<Developer> { new(Alice, "Alice", isBot: false) };
        var events = new List<ActivityEvent>
        {
            new CommitEvent(Alice, ProjectName, DateTimeOffset.UtcNow, "sha-1", hasUsableOffset: true),
            new ReviewEvent(Alice, ProjectName, DateTimeOffset.UtcNow, prNumber: 1, ReviewState.Approved),
            new CommentEvent(Alice, ProjectName, DateTimeOffset.UtcNow, commentId: 1, "nice work"),
            new PrOpenedEvent(Alice, ProjectName, DateTimeOffset.UtcNow, prNumber: 2),
        };
        var dataset = BuildDataset(roster, events);

        var result = ActivityAggregator.Aggregate(dataset);

        var summary = Assert.Single(result.Summaries);
        Assert.Equal(1, summary.CommitCount);
        Assert.Equal(1, summary.ReviewCount);
        Assert.Equal(1, summary.CommentCount);
        Assert.Equal(1, summary.PrsOpenedCount);
        Assert.True(summary.HasActivity);
    }

    [Fact]
    public void Aggregate_WithDuplicateShaCommits_CountsTheCommitOnce()
    {
        var roster = new List<Developer> { new(Alice, "Alice", isBot: false) };
        var events = new List<ActivityEvent>
        {
            new CommitEvent(Alice, ProjectName, DateTimeOffset.UtcNow, "sha-shared", hasUsableOffset: true),
            new CommitEvent(Alice, ProjectName, DateTimeOffset.UtcNow.AddMinutes(1), "sha-shared", hasUsableOffset: true),
            new CommitEvent(Alice, ProjectName, DateTimeOffset.UtcNow.AddMinutes(2), "sha-shared", hasUsableOffset: true),
        };
        var dataset = BuildDataset(roster, events);

        var result = ActivityAggregator.Aggregate(dataset);

        var summary = Assert.Single(result.Summaries);
        Assert.Equal(1, summary.CommitCount);
    }

    [Fact]
    public void Aggregate_WithMultipleReviewRoundsOnOnePr_CountsEachRoundSeparately()
    {
        var roster = new List<Developer> { new(Alice, "Alice", isBot: false) };
        var events = new List<ActivityEvent>
        {
            new ReviewEvent(Alice, ProjectName, DateTimeOffset.UtcNow, prNumber: 42, ReviewState.ChangesRequested),
            new ReviewEvent(Alice, ProjectName, DateTimeOffset.UtcNow.AddHours(1), prNumber: 42, ReviewState.Commented),
            new ReviewEvent(Alice, ProjectName, DateTimeOffset.UtcNow.AddHours(2), prNumber: 42, ReviewState.Approved),
        };
        var dataset = BuildDataset(roster, events);

        var result = ActivityAggregator.Aggregate(dataset);

        var summary = Assert.Single(result.Summaries);
        Assert.Equal(3, summary.ReviewCount);
    }

    [Fact]
    public void Aggregate_WithNoActivityRosterMember_ProducesZeroCountSummaryWithHasActivityFalse()
    {
        var roster = new List<Developer>
        {
            new(Alice, "Alice", isBot: false),
            new(Bob, "Bob", isBot: false),
        };
        var events = new List<ActivityEvent>
        {
            new CommitEvent(Alice, ProjectName, DateTimeOffset.UtcNow, "sha-1", hasUsableOffset: true),
        };
        var dataset = BuildDataset(roster, events);

        var result = ActivityAggregator.Aggregate(dataset);

        Assert.Equal(2, result.Summaries.Count);
        var bobSummary = Assert.Single(result.Summaries, s => s.Developer.Login == Bob);
        Assert.Equal(0, bobSummary.CommitCount);
        Assert.Equal(0, bobSummary.ReviewCount);
        Assert.Equal(0, bobSummary.CommentCount);
        Assert.Equal(0, bobSummary.PrsOpenedCount);
        Assert.False(bobSummary.HasActivity);
    }

    [Fact]
    public void Aggregate_WithBotRosterMemberAuthoringEvents_ExcludesTheBotFromSummariesAndUnmatched()
    {
        var roster = new List<Developer>
        {
            new(Alice, "Alice", isBot: false),
            new(BotLogin, "CI Bot", isBot: true),
        };
        var events = new List<ActivityEvent>
        {
            new CommitEvent(BotLogin, ProjectName, DateTimeOffset.UtcNow, "sha-bot-1", hasUsableOffset: true),
            new CommitEvent(BotLogin, ProjectName, DateTimeOffset.UtcNow, "sha-bot-2", hasUsableOffset: true),
        };
        var dataset = BuildDataset(roster, events);

        var result = ActivityAggregator.Aggregate(dataset);

        Assert.DoesNotContain(result.Summaries, s => s.Developer.Login == BotLogin);
        Assert.Equal(0, result.Unmatched.CommitCount);
    }

    [Fact]
    public void Aggregate_WithUnmatchedAuthorEvents_PlacesThemInTheUnmatchedBucketWithCorrectCounts()
    {
        var roster = new List<Developer> { new(Alice, "Alice", isBot: false) };
        var events = new List<ActivityEvent>
        {
            new CommitEvent(DeveloperLogin.Unmatched, ProjectName, DateTimeOffset.UtcNow, "sha-1", hasUsableOffset: false),
            new CommentEvent(DeveloperLogin.Unmatched, ProjectName, DateTimeOffset.UtcNow, commentId: 1, "orphaned comment"),
        };
        var dataset = BuildDataset(roster, events);

        var result = ActivityAggregator.Aggregate(dataset);

        Assert.Equal(1, result.Unmatched.CommitCount);
        Assert.Equal(1, result.Unmatched.CommentCount);
        Assert.Equal(0, result.Unmatched.ReviewCount);
        Assert.Equal(0, result.Unmatched.PrsOpenedCount);

        var aliceSummary = Assert.Single(result.Summaries);
        Assert.Equal(0, aliceSummary.CommitCount);
        Assert.Equal(0, aliceSummary.CommentCount);
    }

    [Fact]
    public void Aggregate_WithEventAuthoredByLoginNotOnTheRoster_BucketsItAsUnmatchedRatherThanDropped()
    {
        var roster = new List<Developer> { new(Alice, "Alice", isBot: false) };
        var events = new List<ActivityEvent>
        {
            new CommitEvent(OffRoster, ProjectName, DateTimeOffset.UtcNow, "sha-off-roster", hasUsableOffset: true),
        };
        var dataset = BuildDataset(roster, events);

        var result = ActivityAggregator.Aggregate(dataset);

        Assert.Equal(1, result.Unmatched.CommitCount);
        var aliceSummary = Assert.Single(result.Summaries);
        Assert.Equal(0, aliceSummary.CommitCount);
    }

    [Fact]
    public void Aggregate_WithMixedCaseLogin_MatchesTheRosterMemberCaseInsensitively()
    {
        var roster = new List<Developer> { new(Alice, "Alice", isBot: false) };
        var events = new List<ActivityEvent>
        {
            new CommitEvent(new DeveloperLogin("ALICE"), ProjectName, DateTimeOffset.UtcNow, "sha-1", hasUsableOffset: true),
        };
        var dataset = BuildDataset(roster, events);

        var result = ActivityAggregator.Aggregate(dataset);

        Assert.Equal(0, result.Unmatched.CommitCount);
        var aliceSummary = Assert.Single(result.Summaries);
        Assert.Equal(1, aliceSummary.CommitCount);
    }
}
