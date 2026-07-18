using Microsoft.AspNetCore.Mvc.Testing;

namespace DeveloperWellness.IntegrationTests;

/// <summary>
/// Integration smoke test for the Developer detail page (tasks.md T019): with demo mode on, a known
/// roster login renders the header, flag chip, heatmap, and out-of-hours share bar; an unknown login
/// renders the friendly "not on the roster" state instead of an error. The seeded overwork-commits
/// login ("nova-stardust-demo") is read-only knowledge from
/// <c>DeveloperWellness.Infrastructure.Demo.DemoSeed</c>'s remarks.
/// </summary>
public class DeveloperDetailSmokeTests
{
    [Fact]
    public async Task Get_DeveloperDetailForSeededOverworkLogin_ReturnsOkWithHeaderHeatmapAndShareBar()
    {
        using var factory = new DemoModeWebApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/developer/nova-stardust-demo");
        var html = await response.Content.ReadAsStringAsync();

        Assert.True(response.IsSuccessStatusCode, $"Expected a success status code but got {response.StatusCode}. Body: {html}");
        Assert.Contains("Nova Stardust", html, StringComparison.Ordinal);
        Assert.Contains("← Team overview", html, StringComparison.Ordinal);
        Assert.Contains("heatmap-grid", html, StringComparison.Ordinal);
        Assert.Contains("Within working hours (09–18, Mon–Fri)", html, StringComparison.Ordinal);
        Assert.Contains("Out-of-hours commit share", html, StringComparison.Ordinal);
        Assert.Contains("Overwork (commits)", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Get_DeveloperDetailForUnknownLogin_ReturnsOkWithNotOnRosterState()
    {
        using var factory = new DemoModeWebApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/developer/not-a-real-login-demo");
        var html = await response.Content.ReadAsStringAsync();

        Assert.True(response.IsSuccessStatusCode, $"Expected a success status code but got {response.StatusCode}. Body: {html}");
        Assert.Contains("is not on the roster for this scope", html, StringComparison.Ordinal);
        Assert.Contains("Team overview", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Get_TeamOverview_LinksDeveloperNameToTheirDetailPage()
    {
        using var factory = new DemoModeWebApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/team");
        var html = await response.Content.ReadAsStringAsync();

        Assert.True(response.IsSuccessStatusCode, $"Expected a success status code but got {response.StatusCode}. Body: {html}");
        Assert.Contains("href=\"/developer/nova-stardust-demo\"", html, StringComparison.Ordinal);
    }
}
