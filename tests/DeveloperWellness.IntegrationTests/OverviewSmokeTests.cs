using Microsoft.AspNetCore.Mvc.Testing;

namespace DeveloperWellness.IntegrationTests;

/// <summary>
/// Integration smoke test for the Team overview page (tasks.md T015, updated by T030 when the landing
/// route moved to the Pulse Overview): with demo mode on (the default), <c>/team</c> renders the seeded
/// roster, the no-activity group, the "never a ranking" caption, and the unmatched-activity line, in the
/// initial server-rendered response. Logins and the no-activity/unmatched cases are read-only knowledge
/// from <c>DeveloperWellness.Infrastructure.Demo.DemoSeed</c>'s remarks. The root route's own contract is
/// covered by <see cref="OverviewLandingSmokeTests"/>.
/// </summary>
public class OverviewSmokeTests
{
    [Theory]
    [InlineData("/team")]
    public async Task Get_TeamOverviewRoute_ReturnsOkWithSeededRosterAndTeamOverviewContract(string route)
    {
        using var factory = new DemoModeWebApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync(route);
        var html = await response.Content.ReadAsStringAsync();

        Assert.True(response.IsSuccessStatusCode, $"Expected a success status code but got {response.StatusCode}. Body: {html}");

        // Seeded roster display names (DemoSeed): two ordinary active members, one no-activity member.
        Assert.Contains("Flynn Circuitry", html, StringComparison.Ordinal);
        Assert.Contains("Wren Ironforge", html, StringComparison.Ordinal);
        Assert.Contains("Dex Quietstorm", html, StringComparison.Ordinal);

        // No-activity group (FR-012).
        Assert.Contains("No recorded activity this period", html, StringComparison.Ordinal);
        Assert.Contains("No activity — still part of the team", html, StringComparison.Ordinal);

        // Default-order caption (design principle 1, FR-028/FR-029).
        Assert.Contains("Default order: flagged first, then alphabetical — never a ranking", html, StringComparison.Ordinal);

        // Unmatched-activity line: DemoSeed always seeds exactly one unmatched commit event.
        Assert.Contains("Unmatched activity:", html, StringComparison.Ordinal);
        Assert.Contains("counted here, never against anyone", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// Tasks.md T019 checklist: the seeded overwork-commits developer (DemoSeed's "nova-stardust-demo",
    /// display name "Nova Stardust") shows an amber out-of-hours share cell and an Overwork (commits) flag
    /// chip carrying the design's observation-context-suggestion reason, on the Team overview table.
    /// </summary>
    [Fact]
    public async Task Get_TeamOverview_ShowsAmberOutOfHoursShareAndFlagChipForTheSeededOverworkDeveloper()
    {
        using var factory = new DemoModeWebApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/team");
        var html = await response.Content.ReadAsStringAsync();

        Assert.True(response.IsSuccessStatusCode, $"Expected a success status code but got {response.StatusCode}. Body: {html}");
        Assert.Contains("Nova Stardust", html, StringComparison.Ordinal);
        Assert.Contains("ooh-cell--high", html, StringComparison.Ordinal);
        Assert.Contains("Overwork (commits)", html, StringComparison.Ordinal);
        Assert.Contains("It might be worth a quiet check-in about workload.", html, StringComparison.Ordinal);
    }
}
