using DeveloperWellness.Application.Ports;
using DeveloperWellness.Application.Services;
using DeveloperWellness.Domain.Model;
using DeveloperWellness.Domain.Options;
using DeveloperWellness.Infrastructure.Ai;
using DeveloperWellness.Infrastructure.Demo;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace DeveloperWellness.IntegrationTests;

/// <summary>
/// Service-level tests for <see cref="AiSummaryService"/> (tasks.md T035/T037) against the real
/// <see cref="DemoActivitySource"/> and <see cref="DemoAiInsightService"/> adapters — no network access —
/// plus two page-level smoke checks for the AI summary panel's idle state (T036/T037). Seeded logins and
/// the project name are read-only knowledge from <c>DeveloperWellness.Infrastructure.Demo.DemoSeed</c>'s
/// remarks.
/// </summary>
public class AiSummaryTests
{
    private const string PulseApiProject = "pulse-api-demo";

    private static readonly DeveloperLogin NovaLogin = new("nova-stardust-demo"); // seeded: overwork (commits); also a pulse-api-demo contributor
    private static readonly DeveloperLogin DexLogin = new("dex-quietstorm-demo"); // seeded: no activity

    private static int CountWords(string text) => text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

    private static WellnessOptions BuildWellnessOptions() => new() { OrganisationTimeZone = "South Africa Standard Time" };

    private static AiSummaryService BuildService(IAiInsightService? aiInsightService = null) =>
        new(
            new DashboardQueryService(new DemoActivitySource(), new MemoryCache(new MemoryCacheOptions()), Options.Create(BuildWellnessOptions())),
            aiInsightService ?? new DemoAiInsightService(),
            new MemoryCache(new MemoryCacheOptions()),
            Options.Create(BuildWellnessOptions()));

    [Fact]
    public async Task GetSummaryAsync_ProjectSubjectAtProjectScope_ReturnsReadyDemoSummaryWithMatchingScopeAndPeriod()
    {
        var service = BuildService();

        var result = await service.GetSummaryAsync(
            AiSubject.Project(PulseApiProject), ScopeKey.Project(PulseApiProject), periodDays: 14, refresh: false, CancellationToken.None);

        Assert.Equal(AiSummaryState.Ready, result.State);
        Assert.NotNull(result.Summary);
        Assert.True(result.Summary!.IsDemo);
        Assert.True(CountWords(result.Summary.Text) <= 120, $"Expected at most 120 words. Text: {result.Summary.Text}");
        Assert.Equal(ScopeKind.Project, result.Summary.Scope.Kind);
        Assert.Equal(PulseApiProject, result.Summary.Scope.ProjectName);
        Assert.Equal(14, result.Summary.Period.Days);
    }

    [Fact]
    public async Task GetSummaryAsync_DeveloperSubjectAtOrganisationScope_ReturnsReadySummaryScopedToOrganisation()
    {
        var service = BuildService();

        var result = await service.GetSummaryAsync(
            AiSubject.Developer(NovaLogin), ScopeKey.Organisation, periodDays: 14, refresh: false, CancellationToken.None);

        Assert.Equal(AiSummaryState.Ready, result.State);
        Assert.NotNull(result.Summary);
        Assert.True(result.Summary!.IsDemo);
        Assert.Equal(ScopeKind.Organisation, result.Summary.Scope.Kind);
        Assert.Equal(14, result.Summary.Period.Days);
        Assert.False(string.IsNullOrWhiteSpace(result.Summary.Text));

        // Grounded in Nova's actual seeded overwork-commits narrative (DemoAiInsightService selects it by login).
        Assert.Contains("Nova Stardust", result.Summary.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetSummaryAsync_DeveloperSubjectAtProjectScope_ReturnsSummaryScopedToThatProject()
    {
        var service = BuildService();

        var result = await service.GetSummaryAsync(
            AiSubject.Developer(NovaLogin), ScopeKey.Project(PulseApiProject), periodDays: 14, refresh: false, CancellationToken.None);

        Assert.Equal(AiSummaryState.Ready, result.State);
        Assert.NotNull(result.Summary);
        Assert.Equal(ScopeKind.Project, result.Summary!.Scope.Kind);
        Assert.Equal(PulseApiProject, result.Summary.Scope.ProjectName);
    }

    [Fact]
    public async Task GetSummaryAsync_NoActivityDeveloper_ReturnsNoActivityWithoutCallingTheAiService()
    {
        var countingAi = new CountingAiInsightService(new DemoAiInsightService());
        var service = BuildService(countingAi);

        var result = await service.GetSummaryAsync(
            AiSubject.Developer(DexLogin), ScopeKey.Organisation, periodDays: 14, refresh: false, CancellationToken.None);

        Assert.Equal(AiSummaryState.NoActivity, result.State);
        Assert.Null(result.Summary);
        Assert.Equal(0, countingAi.SummariseCallCount);
    }

    [Fact]
    public async Task GetSummaryAsync_CalledTwiceWithoutRefresh_ReturnsTheSameCachedGeneratedAt()
    {
        var service = BuildService();
        var subject = AiSubject.Developer(NovaLogin);

        var first = await service.GetSummaryAsync(subject, ScopeKey.Organisation, 14, refresh: false, CancellationToken.None);
        await Task.Delay(10);
        var second = await service.GetSummaryAsync(subject, ScopeKey.Organisation, 14, refresh: false, CancellationToken.None);

        Assert.Equal(AiSummaryState.Ready, first.State);
        Assert.Equal(AiSummaryState.Ready, second.State);
        Assert.Equal(first.Summary!.GeneratedAt, second.Summary!.GeneratedAt);
    }

    [Fact]
    public async Task GetSummaryAsync_RefreshTrue_ReturnsANewGeneratedAt()
    {
        var service = BuildService();
        var subject = AiSubject.Developer(NovaLogin);

        var first = await service.GetSummaryAsync(subject, ScopeKey.Organisation, 14, refresh: false, CancellationToken.None);
        await Task.Delay(10);
        var refreshed = await service.GetSummaryAsync(subject, ScopeKey.Organisation, 14, refresh: true, CancellationToken.None);

        Assert.Equal(AiSummaryState.Ready, first.State);
        Assert.Equal(AiSummaryState.Ready, refreshed.State);
        Assert.True(refreshed.Summary!.GeneratedAt > first.Summary!.GeneratedAt);
    }

    [Fact]
    public void IsAvailable_UnconfiguredFoundryService_ReturnsFalse()
    {
        var service = BuildService(new FoundryAiInsightService(Options.Create(new AiOptions())));

        Assert.False(service.IsAvailable);
    }

    [Fact]
    public async Task GetSummaryAsync_UnconfiguredFoundryService_ReturnsUnavailableWithoutThrowing()
    {
        var service = BuildService(new FoundryAiInsightService(Options.Create(new AiOptions())));

        var result = await service.GetSummaryAsync(
            AiSubject.Developer(NovaLogin), ScopeKey.Organisation, periodDays: 14, refresh: false, CancellationToken.None);

        Assert.Equal(AiSummaryState.Unavailable, result.State);
        Assert.Null(result.Summary);
    }

    [Fact]
    public async Task Get_ProjectDetailForSeededProject_ShowsTheIdleAiSummaryPrompt()
    {
        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/project/{PulseApiProject}");
        var html = await response.Content.ReadAsStringAsync();

        Assert.True(response.IsSuccessStatusCode, $"Expected a success status code but got {response.StatusCode}. Body: {html}");
        Assert.Contains("Nothing runs until you ask", html, StringComparison.Ordinal);
        Assert.Contains("Generate summary", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Get_DeveloperDetailForSeededLogin_ShowsTheIdleAiSummaryPrompt()
    {
        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/developer/{NovaLogin.Value}");
        var html = await response.Content.ReadAsStringAsync();

        Assert.True(response.IsSuccessStatusCode, $"Expected a success status code but got {response.StatusCode}. Body: {html}");
        Assert.Contains("Nothing runs until you ask", html, StringComparison.Ordinal);
        Assert.Contains("Generate summary", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// Thin counting wrapper over a real <see cref="IAiInsightService"/>, so the no-activity test can
    /// assert the AI service was never invoked rather than merely trusting the returned state.
    /// </summary>
    private sealed class CountingAiInsightService(IAiInsightService inner) : IAiInsightService
    {
        public int SummariseCallCount { get; private set; }

        public bool IsAvailable => inner.IsAvailable;

        public Task<AiSummary> SummariseAsync(AiSubject subject, SummaryGrounding grounding, CancellationToken cancellationToken)
        {
            SummariseCallCount++;
            return inner.SummariseAsync(subject, grounding, cancellationToken);
        }

        public Task<IReadOnlyList<ToneClass>> ClassifyToneAsync(IReadOnlyList<string> commentBodies, CancellationToken cancellationToken) =>
            inner.ClassifyToneAsync(commentBodies, cancellationToken);
    }
}
