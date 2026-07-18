using Microsoft.Extensions.DependencyInjection;

namespace DeveloperWellness.Web.Services;

/// <summary>
/// Web-layer shell service registration (tasks.md T009), kept separate from Program.cs so Program.cs
/// never needs editing again after this task (tasks.md sequential-touch files). Any future Web-only
/// scoped state services are appended here rather than in Program.cs.
/// </summary>
public static class WebServiceCollectionExtensions
{
    /// <summary>Registers the Web-layer shell state services.</summary>
    public static IServiceCollection AddWebShell(this IServiceCollection services)
    {
        services.AddScoped<DashboardState>();

        return services;
    }
}
