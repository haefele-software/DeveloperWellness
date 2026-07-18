using DeveloperWellness.Application.Services;
using DeveloperWellness.Domain.Model;
using DeveloperWellness.Domain.Options;
using DeveloperWellness.Infrastructure.Demo;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace DeveloperWellness.IntegrationTests;

/// <summary>
/// Integration smoke tests for User Story 3's project-scope recomputation and the Project detail page
/// (tasks.md T021, T022): a seeded project with activity renders its stat tiles, flag note, and
/// spread-thin caption; an unknown project name renders the friendly not-covered state; the Team overview
/// Projects column appears only at organisation scope; and <see cref="DashboardQueryService"/> itself
/// leaves <see cref="ActivitySummary.DistinctProjectCount"/> null with no <see cref="FlagKind.SpreadThin"/>
/// flag at project scope, while computing both correctly at organisation scope. Seeded logins, project
/// names, and the spread-thin case are read-only knowledge from
/// <c>DeveloperWellness.Infrastructure.Demo.DemoSeed</c>'s remarks.
/// </summary>
public class ProjectScopeSmokeTests
{
    private const string SeededProjectWithActivity = "pulse-api-demo";
    private const string UnknownProjectName = "not-a-real-project-demo";

    private static readonly DeveloperLogin JuniperLogin = new("juniper-dataforge-demo");

    private static WellnessOptions BuildWellnessOptions() => new() { OrganisationTimeZone = "South Africa Standard Time" };

    [Fact]
    public async Task Get_ProjectDetailForSeededProjectWithActivity_ReturnsOkWithStatTilesFlagNoteAndCaption()
    {
        using var factory = new DemoModeWebApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/project/{SeededProjectWithActivity}");
        var html = await response.Content.ReadAsStringAsync();

        Assert.True(response.IsSuccessStatusCode, $"Expected a success status code but got {response.StatusCode}. Body: {html}");
        Assert.Contains(SeededProjectWithActivity, html, StringComparison.Ordinal);
        Assert.Contains("Commits", html, StringComparison.Ordinal);
        Assert.Contains("PRs opened", html, StringComparison.Ordinal);
        Assert.Contains("Reviews", html, StringComparison.Ordinal);
        Assert.Contains("Comments", html, StringComparison.Ordinal);
        Assert.Contains("carry a flag in this scope", html, StringComparison.Ordinal);
        Assert.Contains(
            "Spread-thin isn't assessed at project scope — it only makes sense across the whole organisation.",
            html,
            StringComparison.Ordinal);

        // At least one seeded contributor of pulse-api-demo (DemoSeed: Nova, Flynn, Wren, Juniper all commit there).
        Assert.Contains("Flynn Circuitry", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Get_ProjectDetailForUnknownProjectName_ReturnsOkWithNotCoveredState()
    {
        using var factory = new DemoModeWebApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/project/{UnknownProjectName}");
        var html = await response.Content.ReadAsStringAsync();

        Assert.True(response.IsSuccessStatusCode, $"Expected a success status code but got {response.StatusCode}. Body: {html}");
        Assert.Contains("is not in the covered set of projects", html, StringComparison.Ordinal);
        Assert.Contains("← Back", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Get_TeamOverviewAtOrganisationScope_ShowsTheProjectsColumnHeader()
    {
        using var factory = new DemoModeWebApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/team");
        var html = await response.Content.ReadAsStringAsync();

        Assert.True(response.IsSuccessStatusCode, $"Expected a success status code but got {response.StatusCode}. Body: {html}");
        Assert.Contains("Projects</button>", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetAsync_AtProjectScope_LeavesDistinctProjectCountNullWithNoSpreadThinFlagForEveryDeveloper()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = new DashboardQueryService(new DemoActivitySource(), cache, Options.Create(BuildWellnessOptions()));

        var result = await service.GetAsync(ScopeKey.Project(SeededProjectWithActivity), periodDays: 14, CancellationToken.None);

        Assert.NotNull(result.Snapshot);
        Assert.NotEmpty(result.Snapshot!.Summaries);
        Assert.All(result.Snapshot.Summaries, summary => Assert.Null(summary.DistinctProjectCount));
        Assert.All(result.Snapshot.Summaries, summary => Assert.DoesNotContain(summary.Flags, flag => flag.Kind == FlagKind.SpreadThin));
    }

    [Fact]
    public async Task GetAsync_AtOrganisationScope_ComputesDistinctProjectCountAndSpreadThinFlagForTheSeededDeveloper()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = new DashboardQueryService(new DemoActivitySource(), cache, Options.Create(BuildWellnessOptions()));

        var result = await service.GetAsync(ScopeKey.Organisation, periodDays: 14, CancellationToken.None);

        Assert.NotNull(result.Snapshot);
        var juniper = Assert.Single(result.Snapshot!.Summaries, s => s.Developer.Login == JuniperLogin);

        Assert.NotNull(juniper.DistinctProjectCount);
        Assert.True(juniper.DistinctProjectCount >= 4, $"Expected Juniper's distinct project count to be at least 4 but was {juniper.DistinctProjectCount}.");
        Assert.Contains(juniper.Flags, flag => flag.Kind == FlagKind.SpreadThin);
    }
}
