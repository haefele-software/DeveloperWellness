using DeveloperWellness.Application.Ports;
using DeveloperWellness.Domain.Model;
using DeveloperWellness.Domain.Options;
using DeveloperWellness.Domain.Signals;
using DeveloperWellness.Infrastructure.Ai;
using DeveloperWellness.Infrastructure.Demo;
using Microsoft.Extensions.Options;

namespace DeveloperWellness.IntegrationTests;

/// <summary>
/// Verifies T038's tone classification: <see cref="ToneResponseParser"/>'s lenient JSON-array parsing,
/// <see cref="DemoAiInsightService.ClassifyToneAsync"/>'s deterministic keyword heuristic (including the
/// seeded frustrated commenter crossing the negative-tone guard, FR-019), and
/// <see cref="FoundryAiInsightService.ClassifyToneAsync"/>'s no-network unconfigured guard.
/// </summary>
public class ToneClassificationTests
{
    private static readonly DeveloperLogin MarloweLogin = new("marlowe-critique-demo");

    // -----------------------------------------------------------------------------------------------
    // ToneResponseParser
    // -----------------------------------------------------------------------------------------------

    [Fact]
    public void Parse_CleanJsonArray_MapsEachEntryInOrder()
    {
        var result = ToneResponseParser.Parse("""["positive", "neutral", "negative"]""", expectedCount: 3);

        Assert.Equal([ToneClass.Positive, ToneClass.Neutral, ToneClass.Negative], result);
    }

    [Fact]
    public void Parse_FencedJsonArrayWithLanguageTag_StripsFenceAndParses()
    {
        const string response = "```json\n[\"positive\", \"negative\"]\n```";

        var result = ToneResponseParser.Parse(response, expectedCount: 2);

        Assert.Equal([ToneClass.Positive, ToneClass.Negative], result);
    }

    [Fact]
    public void Parse_FencedJsonArrayWithoutLanguageTag_StripsFenceAndParses()
    {
        const string response = "```\n[\"neutral\", \"neutral\"]\n```";

        var result = ToneResponseParser.Parse(response, expectedCount: 2);

        Assert.Equal([ToneClass.Neutral, ToneClass.Neutral], result);
    }

    [Fact]
    public void Parse_SurroundingWhitespace_IsTolerated()
    {
        const string response = "\n\n   [\"positive\", \"neutral\"]   \n";

        var result = ToneResponseParser.Parse(response, expectedCount: 2);

        Assert.Equal([ToneClass.Positive, ToneClass.Neutral], result);
    }

    [Fact]
    public void Parse_ArrayShorterThanExpected_PadsMissingEntriesWithUnanalysed()
    {
        var result = ToneResponseParser.Parse("""["positive"]""", expectedCount: 3);

        Assert.Equal([ToneClass.Positive, ToneClass.Unanalysed, ToneClass.Unanalysed], result);
    }

    [Fact]
    public void Parse_ArrayLongerThanExpected_TruncatesExtraEntries()
    {
        var result = ToneResponseParser.Parse(
            """["positive", "negative", "neutral", "positive"]""", expectedCount: 2);

        Assert.Equal([ToneClass.Positive, ToneClass.Negative], result);
    }

    [Fact]
    public void Parse_UnparseableGarbage_ReturnsAllUnanalysedNeverNegative()
    {
        var result = ToneResponseParser.Parse("I'm not able to classify these comments right now.", expectedCount: 4);

        Assert.Equal(4, result.Count);
        Assert.All(result, tone => Assert.Equal(ToneClass.Unanalysed, tone));
    }

    [Fact]
    public void Parse_EmptyResponseText_ReturnsAllUnanalysed()
    {
        var result = ToneResponseParser.Parse(string.Empty, expectedCount: 2);

        Assert.Equal([ToneClass.Unanalysed, ToneClass.Unanalysed], result);
    }

    [Fact]
    public void Parse_MixedCasing_MapsCaseInsensitively()
    {
        var result = ToneResponseParser.Parse("""["POSITIVE", "Negative", "nEUtral"]""", expectedCount: 3);

        Assert.Equal([ToneClass.Positive, ToneClass.Negative, ToneClass.Neutral], result);
    }

    [Fact]
    public void Parse_UnanalysableLabel_MapsToUnanalysed()
    {
        var result = ToneResponseParser.Parse("""["unanalysable", "UNANALYSABLE"]""", expectedCount: 2);

        Assert.Equal([ToneClass.Unanalysed, ToneClass.Unanalysed], result);
    }

    [Fact]
    public void Parse_UnrecognisedLabel_MapsToUnanalysedNeverNegative()
    {
        var result = ToneResponseParser.Parse("""["mixed", "sarcastic"]""", expectedCount: 2);

        Assert.Equal([ToneClass.Unanalysed, ToneClass.Unanalysed], result);
    }

    [Fact]
    public void Parse_ZeroExpectedCount_ReturnsEmpty()
    {
        var result = ToneResponseParser.Parse("""["positive"]""", expectedCount: 0);

        Assert.Empty(result);
    }

    // -----------------------------------------------------------------------------------------------
    // DemoAiInsightService.ClassifyToneAsync
    // -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task ClassifyToneAsync_CalledTwiceWithSameInput_ProducesIdenticalResults()
    {
        var service = new DemoAiInsightService();
        string[] bodies =
        [
            "This is the third time this null check has come back broken.",
            "Nice cleanup here, this reads much better than before.",
            "Can you add a short comment explaining the retry logic?",
        ];

        var first = await service.ClassifyToneAsync(bodies, CancellationToken.None);
        var second = await service.ClassifyToneAsync(bodies, CancellationToken.None);

        Assert.Equal(first, second);
    }

    [Fact]
    public async Task ClassifyToneAsync_MixedList_PreservesOrderPerInput()
    {
        var service = new DemoAiInsightService();
        string[] bodies =
        [
            "Good catch on the edge case, thanks for adding a test.",              // positive
            "This keeps breaking staging and nobody seems to notice before merge.", // negative
            "Minor style nit, otherwise this looks solid.",                         // neutral
            "   ",                                                                  // unanalysed: whitespace only
        ];

        var result = await service.ClassifyToneAsync(bodies, CancellationToken.None);

        Assert.Equal(
            [ToneClass.Positive, ToneClass.Negative, ToneClass.Neutral, ToneClass.Unanalysed],
            result);
    }

    [Fact]
    public async Task ClassifyToneAsync_EmptyInput_ReturnsEmptyList()
    {
        var service = new DemoAiInsightService();

        var result = await service.ClassifyToneAsync([], CancellationToken.None);

        Assert.Empty(result);
    }

    /// <summary>
    /// Pulls Marlowe's real seeded comment bodies through the public <see cref="IActivitySource"/> API
    /// (never DemoSeed's internal literals) and proves the demo heuristic's classification of them
    /// crosses FR-019's guard: at least 10 analysed comments and a negative share strictly above 20
    /// percent, so <see cref="ToneAggregator"/> raises the NegativeTone flag exactly as it would for a
    /// live-classified frustrated commenter.
    /// </summary>
    [Fact]
    public async Task ClassifyToneAsync_SeededFrustratedCommenter_CrossesTheNegativeToneGuard()
    {
        var marloweBodies = await GetMarloweCommentBodiesAsync();

        // Sanity check on the seed itself (task contract: 13-16 comments) before asserting on the classifier.
        Assert.InRange(marloweBodies.Count, 13, 16);

        var service = new DemoAiInsightService();
        var classified = await service.ClassifyToneAsync(marloweBodies, CancellationToken.None);

        var analysedCount = classified.Count(tone => tone != ToneClass.Unanalysed);
        var negativeCount = classified.Count(tone => tone == ToneClass.Negative);
        var negativeShare = (decimal)negativeCount / analysedCount;

        Assert.True(analysedCount >= 10, $"Expected at least 10 analysed comments but got {analysedCount}.");
        Assert.True(negativeShare > 0.20m, $"Expected negative share to exceed 20% but got {negativeShare:P0}.");

        var aggregation = ToneAggregator.Calculate(
            classified.Select(tone => (MarloweLogin, tone)).ToList(),
            new Dictionary<DeveloperLogin, int> { [MarloweLogin] = marloweBodies.Count },
            new WellnessOptions());

        Assert.True(aggregation.ByAuthor[MarloweLogin].Flagged);
        Assert.NotNull(aggregation.ByAuthor[MarloweLogin].Flag);
    }

    /// <summary>
    /// The demo seed authors comment events only for the frustrated commenter (Marlowe); every other
    /// roster member has zero comment events in the dataset. The negative-tone guard therefore can never
    /// be crossed for anyone else, regardless of heuristic tuning, because there is nothing to classify.
    /// </summary>
    [Fact]
    public async Task ClassifyToneAsync_NoOtherSeededAuthorHasAnyComments_SoNoneCanCrossTheGuard()
    {
        var source = new DemoActivitySource();
        var period = new Period(14, DateTimeOffset.UtcNow);
        var dataset = await source.GetActivityAsync(ScopeKey.Organisation, period, CancellationToken.None);

        var otherCommentAuthors = dataset.Events
            .OfType<CommentEvent>()
            .Select(e => e.Author)
            .Where(author => !author.Equals(MarloweLogin))
            .Distinct()
            .ToList();

        Assert.Empty(otherCommentAuthors);
    }

    private static async Task<List<string>> GetMarloweCommentBodiesAsync()
    {
        var source = new DemoActivitySource();
        var period = new Period(14, DateTimeOffset.UtcNow);
        var dataset = await source.GetActivityAsync(ScopeKey.Organisation, period, CancellationToken.None);

        return dataset.Events
            .OfType<CommentEvent>()
            .Where(e => e.Author.Equals(MarloweLogin))
            .Select(e => e.BodyText)
            .ToList();
    }

    // -----------------------------------------------------------------------------------------------
    // FoundryAiInsightService.ClassifyToneAsync (unconfigured guard, no network)
    // -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task ClassifyToneAsync_FoundryWithEmptyOptions_ThrowsAiInsightExceptionWithoutNetworkCall()
    {
        var service = new FoundryAiInsightService(Options.Create(new AiOptions()));

        var exception = await Assert.ThrowsAsync<AiInsightException>(
            () => service.ClassifyToneAsync(["a comment"], CancellationToken.None));

        Assert.Equal("The tone analysis service isn't configured.", exception.Message);
    }

    [Fact]
    public async Task ClassifyToneAsync_FoundryWithEmptyOptionsAndEmptyInput_StillThrowsBeforeReturningEmpty()
    {
        var service = new FoundryAiInsightService(Options.Create(new AiOptions()));

        await Assert.ThrowsAsync<AiInsightException>(
            () => service.ClassifyToneAsync([], CancellationToken.None));
    }
}
