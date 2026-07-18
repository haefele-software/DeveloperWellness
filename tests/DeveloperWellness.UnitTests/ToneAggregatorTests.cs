using DeveloperWellness.Domain.Model;
using DeveloperWellness.Domain.Options;
using DeveloperWellness.Domain.Signals;

namespace DeveloperWellness.UnitTests;

/// <summary>
/// Unit tests for <see cref="ToneAggregator"/> (tasks.md T039): per-author tone counting with Unanalysed
/// excluded from the analysed sample, negative-share arithmetic, both guard boundaries (the
/// strictly-greater threshold and the at-least-ten minimum), the flag reason's counts/percent and
/// analysed-sample note, the total-authored-count fallback and floor, the unmatched-author skip, and the
/// organisation-level sentiment reading's arithmetic and availability.
/// </summary>
public class ToneAggregatorTests
{
    private static readonly DeveloperLogin Alice = new("alice");
    private static readonly DeveloperLogin Bob = new("bob");

    private static readonly WellnessOptions DefaultOptions = new(); // NegativeToneThreshold = 0.20, MinAnalysedComments = 10

    private static readonly IReadOnlyDictionary<DeveloperLogin, int> NoTotals = new Dictionary<DeveloperLogin, int>();

    private static List<(DeveloperLogin Author, ToneClass Tone)> Entries(DeveloperLogin author, ToneClass tone, int count) =>
        Enumerable.Repeat((author, tone), count).ToList();

    [Fact]
    public void Calculate_WithMixedTonesForOneAuthor_CountsEachClassAndExcludesUnanalysedFromAnalysedCount()
    {
        var classified = new List<(DeveloperLogin Author, ToneClass Tone)>
        {
            (Alice, ToneClass.Positive),
            (Alice, ToneClass.Positive),
            (Alice, ToneClass.Neutral),
            (Alice, ToneClass.Negative),
            (Alice, ToneClass.Unanalysed),
            (Alice, ToneClass.Unanalysed),
        };

        var result = ToneAggregator.Calculate(classified, NoTotals, DefaultOptions);

        var aggregate = result.ByAuthor[Alice];
        Assert.Equal(2, aggregate.Positive);
        Assert.Equal(1, aggregate.Neutral);
        Assert.Equal(1, aggregate.Negative);
        Assert.Equal(2, aggregate.Unanalysed);
        Assert.Equal(4, aggregate.AnalysedCount); // Unanalysed excluded from the analysed sample
    }

    [Fact]
    public void Calculate_ComputesNegativeShareOverAnalysedCommentsOnlyNotOverTotal()
    {
        var classified = new List<(DeveloperLogin Author, ToneClass Tone)>
        {
            (Alice, ToneClass.Positive),
            (Alice, ToneClass.Positive),
            (Alice, ToneClass.Positive),
            (Alice, ToneClass.Negative),
            (Alice, ToneClass.Unanalysed),
            (Alice, ToneClass.Unanalysed),
        };

        var result = ToneAggregator.Calculate(classified, NoTotals, DefaultOptions);

        // 1 negative of 4 analysed = 0.25, not 1 of 6 total.
        Assert.Equal(0.25m, result.ByAuthor[Alice].NegativeShare);
    }

    [Fact]
    public void Calculate_WithNegativeShareExactlyAtTwentyPercent_ProducesNoFlag()
    {
        var classified = new List<(DeveloperLogin Author, ToneClass Tone)>();
        classified.AddRange(Entries(Alice, ToneClass.Positive, 8));
        classified.AddRange(Entries(Alice, ToneClass.Negative, 2));

        var result = ToneAggregator.Calculate(classified, NoTotals, DefaultOptions);

        var aggregate = result.ByAuthor[Alice];
        Assert.Equal(10, aggregate.AnalysedCount);
        Assert.Equal(0.20m, aggregate.NegativeShare);
        Assert.False(aggregate.Flagged);
        Assert.Null(aggregate.Flag);
    }

    [Fact]
    public void Calculate_WithNineAnalysedCommentsWellAboveThreshold_ProducesNoFlagBecauseMinimumNotMet()
    {
        var classified = new List<(DeveloperLogin Author, ToneClass Tone)>();
        classified.AddRange(Entries(Alice, ToneClass.Positive, 4));
        classified.AddRange(Entries(Alice, ToneClass.Negative, 5));

        var result = ToneAggregator.Calculate(classified, NoTotals, DefaultOptions);

        var aggregate = result.ByAuthor[Alice];
        Assert.Equal(9, aggregate.AnalysedCount);
        Assert.True(aggregate.NegativeShare > DefaultOptions.NegativeToneThreshold);
        Assert.False(aggregate.Flagged); // below MinAnalysedComments (10) despite a high share
        Assert.Null(aggregate.Flag);
    }

    [Fact]
    public void Calculate_WithTenAnalysedCommentsThirtyPercentNegative_ProducesFlag()
    {
        var classified = new List<(DeveloperLogin Author, ToneClass Tone)>();
        classified.AddRange(Entries(Alice, ToneClass.Positive, 7));
        classified.AddRange(Entries(Alice, ToneClass.Negative, 3));

        var result = ToneAggregator.Calculate(classified, NoTotals, DefaultOptions);

        var aggregate = result.ByAuthor[Alice];
        Assert.Equal(10, aggregate.AnalysedCount);
        Assert.Equal(0.30m, aggregate.NegativeShare);
        Assert.True(aggregate.Flagged);
        Assert.NotNull(aggregate.Flag);
        Assert.Equal(FlagKind.NegativeTone, aggregate.Flag!.Kind);
    }

    [Fact]
    public void Calculate_WhenTotalCountEqualsAnalysedCount_ReasonOmitsTheSampleNote()
    {
        var classified = new List<(DeveloperLogin Author, ToneClass Tone)>();
        classified.AddRange(Entries(Alice, ToneClass.Positive, 7));
        classified.AddRange(Entries(Alice, ToneClass.Negative, 3));
        var totals = new Dictionary<DeveloperLogin, int> { [Alice] = 10 };

        var result = ToneAggregator.Calculate(classified, totals, DefaultOptions);

        var reason = result.ByAuthor[Alice].Flag!.Reason;
        Assert.Contains("3 of 10 analysed comments (30%)", reason, StringComparison.Ordinal);
        Assert.DoesNotContain("written were analysed", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Calculate_WhenTotalCountExceedsAnalysedCount_ReasonStatesTheAnalysedSample()
    {
        // The task's own example ratio: 4 of 12 analysed (33%), noting 12 of 47 written were analysed.
        var classified = new List<(DeveloperLogin Author, ToneClass Tone)>();
        classified.AddRange(Entries(Alice, ToneClass.Positive, 8));
        classified.AddRange(Entries(Alice, ToneClass.Negative, 4));
        var totals = new Dictionary<DeveloperLogin, int> { [Alice] = 47 };

        var result = ToneAggregator.Calculate(classified, totals, DefaultOptions);

        var reason = result.ByAuthor[Alice].Flag!.Reason;
        Assert.Equal(
            "4 of 12 analysed comments (33%) read more negative than usual this period " +
            "(12 of 47 written were analysed). It is climate, not character — review pressure is usually the cause.",
            reason);
    }

    [Fact]
    public void Calculate_WithAuthorMissingFromTotalAuthoredCounts_FallsBackToTheClassifiedCount()
    {
        var classified = new List<(DeveloperLogin Author, ToneClass Tone)>
        {
            (Alice, ToneClass.Positive),
            (Alice, ToneClass.Positive),
            (Alice, ToneClass.Neutral),
            (Alice, ToneClass.Negative),
            (Alice, ToneClass.Unanalysed),
            (Alice, ToneClass.Unanalysed),
        };

        var result = ToneAggregator.Calculate(classified, NoTotals, DefaultOptions);

        Assert.Equal(6, result.ByAuthor[Alice].TotalCount); // 4 analysed + 2 unanalysed; dictionary lacked Alice
    }

    [Fact]
    public void Calculate_WhenTotalAuthoredCountsUndercountsTheAuthor_TotalCountFloorsAtTheClassifiedCount()
    {
        var classified = new List<(DeveloperLogin Author, ToneClass Tone)>
        {
            (Alice, ToneClass.Positive),
            (Alice, ToneClass.Positive),
            (Alice, ToneClass.Positive),
            (Alice, ToneClass.Neutral),
            (Alice, ToneClass.Negative),
        };
        var totals = new Dictionary<DeveloperLogin, int> { [Alice] = 2 }; // deliberately understates the classified count of 5

        var result = ToneAggregator.Calculate(classified, totals, DefaultOptions);

        Assert.Equal(5, result.ByAuthor[Alice].TotalCount); // never below the classified count
    }

    [Fact]
    public void Calculate_WithUnmatchedAuthorEntries_ExcludesThemFromByAuthorAndFromSentiment()
    {
        var classified = new List<(DeveloperLogin Author, ToneClass Tone)>
        {
            (DeveloperLogin.Unmatched, ToneClass.Positive),
            (DeveloperLogin.Unmatched, ToneClass.Negative),
            (DeveloperLogin.Unmatched, ToneClass.Negative),
            (Alice, ToneClass.Positive),
        };

        var result = ToneAggregator.Calculate(classified, NoTotals, DefaultOptions);

        Assert.False(result.ByAuthor.ContainsKey(DeveloperLogin.Unmatched));
        Assert.Single(result.ByAuthor);
        Assert.Equal(1.0m, result.Sentiment.Positive); // Unmatched's negatives never reach the org totals
        Assert.Equal(0m, result.Sentiment.Negative);
    }

    [Fact]
    public void Calculate_WithAnalysedCommentsFromMultipleAuthors_ComputesTheOrganisationSentimentDistribution()
    {
        var classified = new List<(DeveloperLogin Author, ToneClass Tone)>();
        classified.AddRange(Entries(Alice, ToneClass.Positive, 6));
        classified.AddRange(Entries(Alice, ToneClass.Neutral, 3));
        classified.AddRange(Entries(Alice, ToneClass.Negative, 1));
        classified.AddRange(Entries(Bob, ToneClass.Positive, 2));
        classified.AddRange(Entries(Bob, ToneClass.Neutral, 2));
        classified.AddRange(Entries(Bob, ToneClass.Negative, 6));

        var result = ToneAggregator.Calculate(classified, NoTotals, DefaultOptions);

        Assert.True(result.Sentiment.Available);
        Assert.Equal(0.40m, result.Sentiment.Positive); // (6 + 2) of 20 analysed
        Assert.Equal(0.25m, result.Sentiment.Neutral);  // (3 + 2) of 20 analysed
        Assert.Equal(0.35m, result.Sentiment.Negative); // (1 + 6) of 20 analysed
    }

    [Fact]
    public void Calculate_WithNothingAnalysedForAnyAuthor_SentimentIsUnavailable()
    {
        var classified = Entries(Alice, ToneClass.Unanalysed, 5);

        var result = ToneAggregator.Calculate(classified, NoTotals, DefaultOptions);

        Assert.False(result.Sentiment.Available);
        Assert.Equal(0m, result.Sentiment.Positive);
        Assert.Equal(0m, result.Sentiment.Neutral);
        Assert.Equal(0m, result.Sentiment.Negative);
    }

    [Fact]
    public void Calculate_WithNoClassifiedEntries_ReturnsEmptyByAuthorAndUnavailableSentiment()
    {
        var classified = new List<(DeveloperLogin Author, ToneClass Tone)>();

        var result = ToneAggregator.Calculate(classified, NoTotals, DefaultOptions);

        Assert.Empty(result.ByAuthor);
        Assert.False(result.Sentiment.Available);
    }
}
