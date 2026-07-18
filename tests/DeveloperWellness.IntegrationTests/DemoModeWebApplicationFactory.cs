using DeveloperWellness.Application.Ports;
using DeveloperWellness.Infrastructure.Demo;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DeveloperWellness.IntegrationTests;

/// <summary>
/// The <see cref="WebApplicationFactory{TEntryPoint}"/> every test in this project must use in place of a
/// bare <c>new WebApplicationFactory&lt;Program&gt;()</c> (rate-limit hardening, Fix C). A developer's
/// working-tree <c>appsettings.json</c> override can carry live GitHub and Foundry credentials for manual
/// live-mode testing; this factory forces demo mode on and blanks every live-mode setting so a test run
/// can never reach the real GitHub or Foundry API no matter what a developer's local configuration
/// currently holds.
/// </summary>
/// <remarks>
/// Two separate mechanisms are needed, not one: <c>DependencyInjection.AddInfrastructure</c> (Program.cs)
/// reads <c>Wellness:DemoMode</c> eagerly, straight off <c>IConfiguration</c>, to decide which
/// <see cref="IActivitySource"/>/<see cref="IAiInsightService"/> to register — and that decision runs as
/// part of the app's own top-level start-up code, before a <see cref="WebApplicationFactory{TEntryPoint}"/>
/// test host gets a chance to layer in the <see cref="ConfigureAppConfiguration"/> override below, so it
/// can still see a developer's real local configuration and register the live GitHub adapter. Re-registering
/// the demo adapters directly via <see cref="ConfigureServices"/>, which runs after the app's own service
/// registration, guarantees the demo adapters win regardless of what that eager read decided. The
/// configuration override still matters in its own right: it is what makes <c>WellnessOptions.DemoMode</c>
/// (bound lazily through the options system, and read as such by the shell's connection/demo-banner state)
/// and any code reading <c>GitHub:*</c>/<c>Ai:*</c> directly both agree with the forced demo adapters.
/// </remarks>
public sealed class DemoModeWebApplicationFactory : WebApplicationFactory<Program>
{
    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Wellness:DemoMode"] = "true",
                ["GitHub:Organisation"] = string.Empty,
                ["GitHub:Token"] = string.Empty,
                ["Ai:Endpoint"] = string.Empty,
                ["Ai:ApiKey"] = string.Empty,
                ["Ai:DeploymentName"] = string.Empty,
            }));

        builder.ConfigureServices(services =>
        {
            services.AddSingleton<IActivitySource, DemoActivitySource>();
            services.AddSingleton<IAiInsightService, DemoAiInsightService>();
        });
    }
}
