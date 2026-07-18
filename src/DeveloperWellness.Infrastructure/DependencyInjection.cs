using DeveloperWellness.Application.Ports;
using DeveloperWellness.Domain.Options;
using DeveloperWellness.Infrastructure.Ai;
using DeveloperWellness.Infrastructure.Demo;
using DeveloperWellness.Infrastructure.GitHub;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DeveloperWellness.Infrastructure;

/// <summary>
/// Infrastructure-layer service registration (tasks.md T009). Selects the demo or live
/// <see cref="IActivitySource"/> and <see cref="IAiInsightService"/> adapters behind
/// <see cref="WellnessOptions.DemoMode"/> at registration time, before the service provider exists — so
/// only raw <see cref="IConfiguration"/> is available here, never a built
/// <c>IOptions&lt;WellnessOptions&gt;</c>. Nothing here is ever moved into Program.cs.
/// </summary>
public static class DependencyInjection
{
    /// <summary>Registers Infrastructure-layer adapters, selecting demo or live implementations by configuration.</summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var demoModeKey = $"{WellnessOptions.SectionName}:{nameof(WellnessOptions.DemoMode)}";
        var demoMode = configuration.GetValue(demoModeKey, defaultValue: true);

        // GitHubOptions and AiOptions are never start-up-validated (unlike WellnessOptions): live mode is
        // a per-call concern, so a fresh clone with demo mode off but no GitHub or Ai configuration still
        // starts. GitHubActivitySource throws its own user-presentable message on first load (research
        // R2); FoundryAiInsightService instead exposes IsAvailable = false and every UI surface degrades
        // to the friendly unavailable state (FR-014) — an unconfigured Foundry deployment IS the
        // unavailable state, so no separate placeholder implementation exists for it.
        services.AddOptions<GitHubOptions>().Bind(configuration.GetSection(GitHubOptions.SectionName));
        services.AddOptions<AiOptions>().Bind(configuration.GetSection(AiOptions.SectionName));

        if (demoMode)
        {
            services.AddSingleton<IActivitySource, DemoActivitySource>();
            services.AddSingleton<IAiInsightService, DemoAiInsightService>();
        }
        else
        {
            services.AddSingleton<IActivitySource, GitHubActivitySource>();
            services.AddSingleton<IAiInsightService, FoundryAiInsightService>();
        }

        return services;
    }
}
