using DeveloperWellness.Domain.Model;
using DeveloperWellness.Domain.Options;
using DeveloperWellness.Domain.Signals;

namespace DeveloperWellness.UnitTests;

/// <summary>
/// Unit tests for <see cref="SpreadThinCalculator"/> (tasks.md T020): distinct-project counting across
/// mixed event types, the at-or-above threshold boundary, reason content, unmatched-author exclusion,
/// and independent per-developer counts. Every dataset here is hand-rolled inline (Domain tests stay
/// Infrastructure-free).
/// </summary>
public class SpreadThinCalculatorTests
{
    private static readonly DeveloperLogin Alice = new("alice");
    private static readonly DeveloperLogin Bob = new("bob");

    private static readonly WellnessOptions DefaultOptions = new(); // SpreadThinThreshold = 4

    private static CommitEvent Commit(DeveloperLogin author, string projectName) =>
        new(author, projectName, DateTimeOffset.UtcNow, sha: Guid.NewGuid().ToString(), hasUsableOffset: true);

    private static ReviewEvent Review(DeveloperLogin author, string projectName) =>
        new(author, projectName, DateTimeOffset.UtcNow, prNumber: 1, ReviewState.Approved);

    private static CommentEvent Comment(DeveloperLogin author, string projectName) =>
        new(author, projectName, DateTimeOffset.UtcNow, commentId: 1, bodyText: "looks good");

    private static PrOpenedEvent PrOpened(DeveloperLogin author, string projectName) =>
        new(author, projectName, DateTimeOffset.UtcNow, prNumber: 2);

    [Fact]
    public void Calculate_WithMixedEventTypesTouchingTheSameProject_CountsThatProjectOnce()
    {
        var events = new List<ActivityEvent>
        {
            Commit(Alice, "proj-a"),
            Review(Alice, "proj-a"),
            Comment(Alice, "proj-a"),
            PrOpened(Alice, "proj-a"),
            Commit(Alice, "proj-b"),
        };

        var result = SpreadThinCalculator.Calculate(events, DefaultOptions);

        Assert.Equal(2, result[Alice].DistinctProjectCount);
    }

    [Fact]
    public void Calculate_WithDistinctProjectCountExactlyAtThreshold_RaisesSpreadThinFlag()
    {
        var events = new List<ActivityEvent>
        {
            Commit(Alice, "proj-a"),
            Commit(Alice, "proj-b"),
            Commit(Alice, "proj-c"),
            Commit(Alice, "proj-d"),
        };

        var result = SpreadThinCalculator.Calculate(events, DefaultOptions);

        Assert.Equal(4, result[Alice].DistinctProjectCount);
        Assert.NotNull(result[Alice].Flag);
        Assert.Equal(FlagKind.SpreadThin, result[Alice].Flag!.Kind);
    }

    [Fact]
    public void Calculate_WithDistinctProjectCountOneBelowThreshold_DoesNotRaiseFlag()
    {
        var events = new List<ActivityEvent>
        {
            Commit(Alice, "proj-a"),
            Commit(Alice, "proj-b"),
            Commit(Alice, "proj-c"),
        };

        var result = SpreadThinCalculator.Calculate(events, DefaultOptions);

        Assert.Equal(3, result[Alice].DistinctProjectCount);
        Assert.Null(result[Alice].Flag);
    }

    [Fact]
    public void Calculate_WhenFlagged_ReasonContainsTheDistinctProjectCount()
    {
        var events = new List<ActivityEvent>
        {
            Commit(Alice, "proj-a"),
            Commit(Alice, "proj-b"),
            Commit(Alice, "proj-c"),
            Commit(Alice, "proj-d"),
            Commit(Alice, "proj-e"),
        };

        var result = SpreadThinCalculator.Calculate(events, DefaultOptions);

        Assert.Contains("5", result[Alice].Flag!.Reason);
    }

    [Fact]
    public void Calculate_WithUnmatchedAuthorEvents_ExcludesThemFromResults()
    {
        var events = new List<ActivityEvent>
        {
            Commit(DeveloperLogin.Unmatched, "proj-a"),
            Commit(DeveloperLogin.Unmatched, "proj-b"),
            Commit(DeveloperLogin.Unmatched, "proj-c"),
            Commit(DeveloperLogin.Unmatched, "proj-d"),
            Commit(DeveloperLogin.Unmatched, "proj-e"),
        };

        var result = SpreadThinCalculator.Calculate(events, DefaultOptions);

        Assert.Empty(result);
    }

    [Fact]
    public void Calculate_WithActivityInOneProjectOnly_ReturnsCountOneWithNoFlag()
    {
        var events = new List<ActivityEvent>
        {
            Commit(Alice, "proj-a"),
            Review(Alice, "proj-a"),
        };

        var result = SpreadThinCalculator.Calculate(events, DefaultOptions);

        Assert.Equal(1, result[Alice].DistinctProjectCount);
        Assert.Null(result[Alice].Flag);
    }

    [Fact]
    public void Calculate_WithNoEvents_ReturnsEmptyResult()
    {
        var events = new List<ActivityEvent>();

        var result = SpreadThinCalculator.Calculate(events, DefaultOptions);

        Assert.Empty(result);
    }

    [Fact]
    public void Calculate_WithMultipleDevelopers_ComputesIndependentCounts()
    {
        var events = new List<ActivityEvent>
        {
            Commit(Alice, "proj-a"),
            Commit(Alice, "proj-b"),
            Commit(Alice, "proj-c"),
            Commit(Alice, "proj-d"),
            Commit(Alice, "proj-e"),
            Commit(Bob, "proj-a"),
            Commit(Bob, "proj-b"),
        };

        var result = SpreadThinCalculator.Calculate(events, DefaultOptions);

        Assert.Equal(5, result[Alice].DistinctProjectCount);
        Assert.NotNull(result[Alice].Flag);
        Assert.Equal(2, result[Bob].DistinctProjectCount);
        Assert.Null(result[Bob].Flag);
    }
}
