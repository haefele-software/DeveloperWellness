using DeveloperWellness.Domain.Options;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace DeveloperWellness.IntegrationTests;

/// <summary>
/// Boots the Web shell (tasks.md T009) end-to-end via <see cref="WebApplicationFactory{TEntryPoint}"/>:
/// the demo-mode home page renders the Pulse brand, demo banner, and coverage line; an invalid
/// <c>Wellness</c> option fails the host at start-up rather than serving a broken app.
/// </summary>
public class ShellSmokeTests
{
    [Fact]
    public async Task Get_RootInDemoMode_ReturnsOkWithBrandDemoBannerAndCoverageLine()
    {
        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/");
        var html = await response.Content.ReadAsStringAsync();

        Assert.True(response.IsSuccessStatusCode, $"Expected a success status code but got {response.StatusCode}. Body: {html}");
        Assert.Contains("Pulse — Developer wellness", html, StringComparison.Ordinal);
        Assert.Contains("Demo data — fictitious identities", html, StringComparison.Ordinal);
        Assert.Contains("most recently active repositories", html, StringComparison.Ordinal);
    }

    [Fact]
    public void CreatingTheHost_WithAnInvalidWellnessOption_ThrowsOptionsValidationException()
    {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Wellness:OutOfHoursThreshold"] = "5",
                })));

        var thrown = Assert.ThrowsAny<Exception>(() => factory.CreateClient());
        var validationException = FindOptionsValidationException(thrown);

        Assert.NotNull(validationException);
        Assert.Contains(
            validationException.Failures,
            failure => failure.StartsWith(nameof(WellnessOptions.OutOfHoursThreshold), StringComparison.Ordinal));
    }

    private static OptionsValidationException? FindOptionsValidationException(Exception? exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is OptionsValidationException validationException)
            {
                return validationException;
            }

            if (current is AggregateException aggregate)
            {
                foreach (var inner in aggregate.InnerExceptions)
                {
                    if (FindOptionsValidationException(inner) is { } found)
                    {
                        return found;
                    }
                }
            }
        }

        return null;
    }
}
