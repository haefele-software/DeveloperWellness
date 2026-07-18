using Microsoft.AspNetCore.Mvc.Testing;

namespace DeveloperWellness.IntegrationTests;

/// <summary>
/// Integration smoke test for the check-in roster page (tasks.md T025, T041, T032, T044) with demo mode
/// on: the count headline, the seeded flagged developers rendered in most-flags-first (tie-broken
/// alphabetically) order with their full readable reasons (not hover-only, SC-010), the tone-based
/// frustration paragraph (ui-design.md 4.3), the "View ... detail" links, and no tone-unavailable note
/// (demo mode's <c>DemoAiInsightService.IsAvailable</c> is always true).
/// </summary>
/// <remarks>
/// The seeded logins, teams, and signal cases are read-only knowledge from
/// <c>DeveloperWellness.Infrastructure.Demo.DemoSeed</c>'s remarks; the exact reason wording is read-only
/// knowledge from <c>DeveloperWellness.Domain.Signals.OutOfHoursCommitCalculator</c>,
/// <c>SpreadThinCalculator</c>, <c>ToneAggregator</c>, <c>PrAfterHoursCalculator</c>, and
/// <c>RushingCalculator</c>. With the out-of-hours-commit (Nova Stardust), spread-thin (Juniper Dataforge),
/// negative-tone (Marlowe Critique), after-hours-PR (Remy Afterglow, T032), and possible-rushing (River
/// Hurrybrook, T044) signals all wired into the roster, exactly five developers are flagged at the default
/// organisation scope and 14-day period — each carrying exactly one flag, so the tie breaks alphabetically:
/// Juniper Dataforge, Marlowe Critique, Nova Stardust, Remy Afterglow, then River Hurrybrook. Verified
/// against the running application (curl) at the time this test was written.
/// </remarks>
public class CheckInsSmokeTests
{
    [Fact]
    public async Task Get_CheckIns_ReturnsOkWithCountHeadlineAndSeededFlaggedEntriesInOrder()
    {
        using var factory = new DemoModeWebApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/checkins");
        var html = await response.Content.ReadAsStringAsync();

        Assert.True(response.IsSuccessStatusCode, $"Expected a success status code but got {response.StatusCode}. Body: {html}");
        Assert.Contains("5 people might need a check-in", html, StringComparison.Ordinal);

        // The frustration paragraph names Marlowe Critique before the roster list does, so ordering below
        // is checked from the roster list's own start rather than each name's first occurrence anywhere.
        var rosterStartIndex = html.IndexOf("checkin-list", StringComparison.Ordinal);
        Assert.True(rosterStartIndex >= 0, $"Expected the checkin-list roster to render. Body: {html}");

        var juniperIndex = html.IndexOf("Juniper Dataforge", rosterStartIndex, StringComparison.Ordinal);
        var marloweIndex = html.IndexOf("Marlowe Critique", rosterStartIndex, StringComparison.Ordinal);
        var novaIndex = html.IndexOf("Nova Stardust", rosterStartIndex, StringComparison.Ordinal);
        var remyIndex = html.IndexOf("Remy Afterglow", rosterStartIndex, StringComparison.Ordinal);
        var riverIndex = html.IndexOf("River Hurrybrook", rosterStartIndex, StringComparison.Ordinal);

        Assert.True(juniperIndex >= 0, $"Expected Juniper Dataforge (seeded spread-thin case) in the roster. Body: {html}");
        Assert.True(marloweIndex >= 0, $"Expected Marlowe Critique (seeded frustrated-commenter case) in the roster. Body: {html}");
        Assert.True(novaIndex >= 0, $"Expected Nova Stardust (seeded overwork-commits case) in the roster. Body: {html}");
        Assert.True(remyIndex >= 0, $"Expected Remy Afterglow (seeded after-hours-PR case) in the roster. Body: {html}");
        Assert.True(riverIndex >= 0, $"Expected River Hurrybrook (seeded possible-rushing case) in the roster. Body: {html}");
        Assert.True(
            juniperIndex < marloweIndex && marloweIndex < novaIndex && novaIndex < remyIndex && remyIndex < riverIndex,
            "All five carry exactly one flag today, so the tie breaks alphabetically: Juniper Dataforge, " +
            "Marlowe Critique, Nova Stardust, Remy Afterglow, then River Hurrybrook.");

        Assert.Contains("Overwork (commits)", html, StringComparison.Ordinal);
        Assert.Contains("Spread thin", html, StringComparison.Ordinal);
        Assert.Contains(">Tone<", html, StringComparison.Ordinal);
        Assert.Contains("Overwork (PR activity)", html, StringComparison.Ordinal);
        Assert.Contains("Possible rushing", html, StringComparison.Ordinal);

        // Full, readable reason fragments (not hover-only) for each seeded case, per SC-010. Apostrophe-free
        // substrings, since Razor HTML-encodes apostrophes in the surrounding sentence text.
        Assert.Contains("landed out of hours in their local time", html, StringComparison.Ordinal);
        Assert.Contains("different projects this period", html, StringComparison.Ordinal);
        Assert.Contains("5 of 13 analysed comments (38%) read more negative than usual this period", html, StringComparison.Ordinal);
        Assert.Contains("climate, not character", html, StringComparison.Ordinal);
        Assert.Contains("3 of 7 PR reviews and opens (43%) happened outside working hours in organisation time", html, StringComparison.Ordinal);
        Assert.Contains("worth a chat about after-hours review load", html, StringComparison.Ordinal);
        Assert.Contains("High output (13 commits and PRs) with 60% of PRs seeing changes requested", html, StringComparison.Ordinal);
        Assert.Contains("pace pressure", html, StringComparison.Ordinal);

        // Roster-level frustration paragraph (ui-design.md 4.3), separate from Marlowe's own flag reason above.
        Assert.Contains("frustration showing in review comments", html, StringComparison.Ordinal);
        Assert.Contains("Marlowe Critique", html, StringComparison.Ordinal);

        Assert.Contains("detail →", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/developer/nova-stardust-demo\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/developer/juniper-dataforge-demo\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/developer/marlowe-critique-demo\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/developer/remy-afterglow-demo\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/developer/river-hurrybrook-demo\"", html, StringComparison.Ordinal);

        // Demo mode's AI service is always available, so no tone-unavailable note appears.
        Assert.DoesNotContain("Tone signals are currently unavailable", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Get_CheckIns_ShowsEachFlaggedDevelopersTeamName()
    {
        using var factory = new DemoModeWebApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/checkins");
        var html = await response.Content.ReadAsStringAsync();

        Assert.True(response.IsSuccessStatusCode, $"Expected a success status code but got {response.StatusCode}. Body: {html}");
        Assert.Contains("Platform", html, StringComparison.Ordinal); // Nova Stardust's team
        Assert.Contains("Data", html, StringComparison.Ordinal); // Juniper Dataforge's team
        Assert.Contains("Mobile", html, StringComparison.Ordinal); // Remy Afterglow's team
        Assert.Contains("QA", html, StringComparison.Ordinal); // River Hurrybrook's team
    }
}
