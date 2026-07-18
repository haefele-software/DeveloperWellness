using DeveloperWellness.Application.Ports;
using DeveloperWellness.Application.Services;
using DeveloperWellness.Domain.Model;
using DeveloperWellness.Domain.Options;
using DeveloperWellness.Infrastructure.Demo;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace DeveloperWellness.IntegrationTests;

/// <summary>
/// End-to-end SC-013 smoke coverage (tasks.md T045) with demo mode on: the Overview landing, the
/// check-in roster, the quality-versus-quantity view, and the AI summary mechanism (service level), all
/// exercised together against the seeded demo dataset. Individual pages already carry their own focused
/// smoke tests (<see cref="OverviewLandingSmokeTests"/>, <see cref="CheckInsSmokeTests"/>,
/// <see cref="AiSummaryTests"/>); this file asserts the SC-013 checklist as one connected story instead.
/// Seeded logins, project, and team names are read-only knowledge from
/// <c>DeveloperWellness.Infrastructure.Demo.DemoSeed</c>'s remarks.
/// </summary>
/// <remarks>
/// The demo dataset seeds two sufficient-PR-sample developers at organisation scope: River Hurrybrook (5
/// PRs opened, always flagged possible-rushing) and Sable Querywise (3 PRs opened, only 1 changes-requested
/// — a 33% share below the 40% threshold — so she reads "steady" instead, T046 gap fix). Remy Afterglow (2
/// PRs opened) stays below the minimum-3 sample. Verified against the running application (curl) at the
/// time this test was written.
/// </remarks>
public class PagesSmokeTests
{
    private const string PulseApiProject = "pulse-api-demo";

    private static readonly DeveloperLogin NovaLogin = new("nova-stardust-demo"); // seeded: overwork (commits)

    private static int CountWords(string text) => text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

    private static WellnessOptions BuildWellnessOptions() => new() { OrganisationTimeZone = "South Africa Standard Time" };

    private static AiSummaryService BuildAiSummaryService() =>
        new(
            new DashboardQueryService(new DemoActivitySource(), new MemoryCache(new MemoryCacheOptions()), Options.Create(BuildWellnessOptions())),
            new DemoAiInsightService(),
            new MemoryCache(new MemoryCacheOptions()),
            Options.Create(BuildWellnessOptions()));

    [Fact]
    public async Task Get_Root_ShowsKpiTilesProjectsTableTeamCardsRecommendationTrendAndSentiment()
    {
        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/");
        var html = await response.Content.ReadAsStringAsync();

        Assert.True(response.IsSuccessStatusCode, $"Expected a success status code but got {response.StatusCode}. Body: {html}");

        // KPI tiles, now carrying real after-hours-PR data since T032 wired PrAfterHoursCalculator into
        // OverviewService's KPI computation.
        Assert.Contains("Might need a check-in", html, StringComparison.Ordinal);
        Assert.Contains("After-hours commits", html, StringComparison.Ordinal);
        Assert.Contains("After-hours PR activity", html, StringComparison.Ordinal);
        Assert.Contains("organisation time", html, StringComparison.Ordinal);
        Assert.Contains("Projects per developer", html, StringComparison.Ordinal);
        Assert.Contains("Review sentiment", html, StringComparison.Ordinal);

        // Projects table.
        Assert.Contains("overall stats per project", html, StringComparison.Ordinal);
        Assert.Contains(PulseApiProject, html, StringComparison.Ordinal);

        // Team cards (organisation scope only).
        Assert.Contains("Platform", html, StringComparison.Ordinal);
        Assert.Contains("devs", html, StringComparison.Ordinal);

        // At least one recommendation.
        Assert.Contains("suggestions, not instructions", html, StringComparison.Ordinal);
        Assert.Contains("Encourage real time off", html, StringComparison.Ordinal);

        // Development trend.
        Assert.Contains("across the window", html, StringComparison.Ordinal);

        // Review sentiment reading.
        Assert.Contains("% negative", html, StringComparison.Ordinal);
        Assert.Contains("across analysed comments", html, StringComparison.Ordinal);

        // Alert pill (US5) in the shell around the page body.
        Assert.Contains("newly need a check-in", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// Viewing the roster itself clears the alert pill for every currently flagged developer within the
    /// same request (FR-031: "the indicator clears once the roster is viewed"), so this page's own
    /// rendered HTML never carries "newly need a check-in" — <see cref="Get_Root_ShowsKpiTilesProjectsTableTeamCardsRecommendationTrendAndSentiment"/>
    /// already proves the pill appears before the roster has been viewed; the full appear-clear-reappear
    /// lifecycle is covered elsewhere. Verified against the running application (curl) at the time this
    /// test was written.
    /// </summary>
    [Fact]
    public async Task Get_CheckIns_ShowsAtLeastTwoEntriesWithDistinctReasonsAndTheFrustrationParagraph()
    {
        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/checkins");
        var html = await response.Content.ReadAsStringAsync();

        Assert.True(response.IsSuccessStatusCode, $"Expected a success status code but got {response.StatusCode}. Body: {html}");
        Assert.Contains("people might need a check-in", html, StringComparison.Ordinal);

        // At least two roster entries with distinct reason sets: Nova's overwork-commits reason and
        // Juniper's spread-thin reason are two different fragments, both drawn from the live roster.
        Assert.Contains("landed out of hours in their local time", html, StringComparison.Ordinal);
        Assert.Contains("different projects this period", html, StringComparison.Ordinal);

        // Frustration paragraph (ui-design.md 4.3).
        Assert.Contains("frustration showing in review comments", html, StringComparison.Ordinal);
        Assert.Contains("climate, not character", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Get_Quality_ShowsTheSeededRushingRowSteadyRowAndTheBelowSampleNote()
    {
        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/quality");
        var html = await response.Content.ReadAsStringAsync();

        Assert.True(response.IsSuccessStatusCode, $"Expected a success status code but got {response.StatusCode}. Body: {html}");
        Assert.Contains("no score, no index, no ranking", html, StringComparison.Ordinal);

        // The seeded possible-rushing row (River Hurrybrook).
        Assert.Contains("River Hurrybrook", html, StringComparison.Ordinal);
        Assert.Contains("Possible rushing", html, StringComparison.Ordinal);

        // The seeded steady row (Sable Querywise, T046 gap fix): sufficient sample, not flagged.
        Assert.Contains("Sable Querywise", html, StringComparison.Ordinal);
        Assert.Contains("Volume and rework look in step", html, StringComparison.Ordinal);

        // The below-sample note, naming at least one below-sample developer with real activity.
        Assert.Contains("Not enough review data to say anything useful", html, StringComparison.Ordinal);
        Assert.Contains("fewer than 3 PRs this period, which is perfectly normal", html, StringComparison.Ordinal);
        Assert.Contains("Remy Afterglow", html, StringComparison.Ordinal); // 2 PRs opened: below the minimum-3 sample.

        // Closing pace-pressure caption.
        Assert.Contains("pace pressure", html, StringComparison.Ordinal);
        Assert.Contains("never carelessness", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetSummaryAsync_ProjectSubject_ReturnsReadySummaryUnderTheWordLimitWithCorrectLabelFields()
    {
        var service = BuildAiSummaryService();

        var result = await service.GetSummaryAsync(
            AiSubject.Project(PulseApiProject), ScopeKey.Project(PulseApiProject), periodDays: 14, refresh: false, CancellationToken.None);

        Assert.Equal(AiSummaryState.Ready, result.State);
        Assert.NotNull(result.Summary);
        Assert.True(result.Summary!.IsDemo);
        Assert.True(CountWords(result.Summary.Text) <= 120, $"Expected at most 120 words. Text: {result.Summary.Text}");
        Assert.Equal(ScopeKind.Project, result.Summary.Scope.Kind);
        Assert.Equal(PulseApiProject, result.Summary.Scope.ProjectName);
        Assert.Equal(14, result.Summary.Period.Days);
        Assert.Equal(AiSubjectKind.Project, result.Summary.Subject.Kind);
        Assert.Equal(PulseApiProject, result.Summary.Subject.ProjectName);
    }

    [Fact]
    public async Task GetSummaryAsync_DeveloperSubject_ReturnsReadySummaryUnderTheWordLimitWithCorrectLabelFields()
    {
        var service = BuildAiSummaryService();

        var result = await service.GetSummaryAsync(
            AiSubject.Developer(NovaLogin), ScopeKey.Organisation, periodDays: 14, refresh: false, CancellationToken.None);

        Assert.Equal(AiSummaryState.Ready, result.State);
        Assert.NotNull(result.Summary);
        Assert.True(result.Summary!.IsDemo);
        Assert.True(CountWords(result.Summary.Text) <= 120, $"Expected at most 120 words. Text: {result.Summary.Text}");
        Assert.Equal(ScopeKind.Organisation, result.Summary.Scope.Kind);
        Assert.Equal(14, result.Summary.Period.Days);
        Assert.Equal(AiSubjectKind.Developer, result.Summary.Subject.Kind);
        Assert.Equal(NovaLogin, result.Summary.Subject.Login);
        Assert.False(string.IsNullOrWhiteSpace(result.Summary.Text));
    }
}
