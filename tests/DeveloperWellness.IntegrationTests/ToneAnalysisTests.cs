using DeveloperWellness.Application.Services;
using DeveloperWellness.Domain.Model;
using DeveloperWellness.Domain.Options;
using DeveloperWellness.Infrastructure.Demo;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace DeveloperWellness.IntegrationTests;

/// <summary>
/// Service-level tests for <see cref="ToneAnalysisService"/> (tasks.md T040) against the demo adapters
/// (<see cref="DemoActivitySource"/>, <see cref="DemoAiInsightService"/>) — no network involved. Covers
/// FR-019..FR-021: the seeded frustrated commenter (marlowe-critique-demo) crosses the negative-tone guard
/// at organisation scope while nobody else does (the demo seed authors comment events only for Marlowe,
/// per <see cref="ToneClassificationTests"/>'s own remarks), the organisation-level sentiment reading is
/// available with a positive negative share, and results are reused across calls within the sliding cache
/// window (FR-021).
/// </summary>
public class ToneAnalysisTests
{
    private static readonly DeveloperLogin MarloweLogin = new("marlowe-critique-demo");

    private static WellnessOptions BuildWellnessOptions() => new() { OrganisationTimeZone = "South Africa Standard Time" };

    private static ToneAnalysisService CreateService(IMemoryCache? cache = null)
    {
        var options = Options.Create(BuildWellnessOptions());
        var queryService = new DashboardQueryService(new DemoActivitySource(), new MemoryCache(new MemoryCacheOptions()), options);
        var aiInsightService = new DemoAiInsightService();

        return new ToneAnalysisService(queryService, aiInsightService, cache ?? new MemoryCache(new MemoryCacheOptions()), options);
    }

    [Fact]
    public async Task GetTonesAsync_SeededOrganisationRoster_IsAvailableAndFlagsOnlyMarlowe()
    {
        var service = CreateService();

        var result = await service.GetTonesAsync(ScopeKey.Organisation, 14, CancellationToken.None);

        Assert.True(result.Available);
        Assert.NotNull(result.Aggregation);

        var marlowe = result.Aggregation!.ByAuthor[MarloweLogin];
        Assert.True(marlowe.Flagged);
        Assert.NotNull(marlowe.Flag);
        Assert.Equal(FlagKind.NegativeTone, marlowe.Flag!.Kind);

        var otherFlagged = result.Aggregation.ByAuthor
            .Where(pair => !pair.Key.Equals(MarloweLogin) && pair.Value.Flagged);

        Assert.Empty(otherFlagged);
    }

    [Fact]
    public async Task GetTonesAsync_MarloweCrossesGuardWithFullSampleAnalysedAndNoCapApplied()
    {
        var service = CreateService();

        var result = await service.GetTonesAsync(ScopeKey.Organisation, 14, CancellationToken.None);

        var marlowe = result.Aggregation!.ByAuthor[MarloweLogin];
        Assert.True(marlowe.AnalysedCount >= 10);
        Assert.True(marlowe.NegativeShare > 0.20m);
        Assert.Equal(marlowe.AnalysedCount, marlowe.TotalCount); // 13 seeded comments, well under the 200 cap: nothing left unanalysed
    }

    [Fact]
    public async Task GetTonesAsync_SeededOrganisationRoster_SentimentAvailableWithPositiveNegativeShare()
    {
        var service = CreateService();

        var result = await service.GetTonesAsync(ScopeKey.Organisation, 14, CancellationToken.None);

        Assert.NotNull(result.Aggregation);
        Assert.True(result.Aggregation!.Sentiment.Available);
        Assert.True(result.Aggregation.Sentiment.Negative > 0m);
    }

    [Fact]
    public async Task GetTonesAsync_CalledTwice_ReusesTheCachedResultInstance()
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        var service = CreateService(cache);

        var first = await service.GetTonesAsync(ScopeKey.Organisation, 14, CancellationToken.None);
        var second = await service.GetTonesAsync(ScopeKey.Organisation, 14, CancellationToken.None);

        Assert.Same(first, second);
    }

    [Fact]
    public async Task GetTonesAsync_AiServiceUnavailable_ReturnsUnavailableWithoutFetchingOrClassifying()
    {
        var options = Options.Create(BuildWellnessOptions());
        var queryService = new DashboardQueryService(new DemoActivitySource(), new MemoryCache(new MemoryCacheOptions()), options);
        var service = new ToneAnalysisService(
            queryService, new UnavailableAiInsightService(), new MemoryCache(new MemoryCacheOptions()), options);

        var result = await service.GetTonesAsync(ScopeKey.Organisation, 14, CancellationToken.None);

        Assert.False(result.Available);
        Assert.Null(result.Aggregation);
    }

    /// <summary>Deliberately unconfigured double: never asked to classify, so a stray call would fail the test outright.</summary>
    private sealed class UnavailableAiInsightService : DeveloperWellness.Application.Ports.IAiInsightService
    {
        public bool IsAvailable => false;

        public Task<AiSummary> SummariseAsync(AiSubject subject, DeveloperWellness.Application.Ports.SummaryGrounding grounding, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("SummariseAsync should never be called while IsAvailable is false.");

        public Task<IReadOnlyList<ToneClass>> ClassifyToneAsync(IReadOnlyList<string> commentBodies, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("ClassifyToneAsync should never be called while IsAvailable is false.");
    }
}
