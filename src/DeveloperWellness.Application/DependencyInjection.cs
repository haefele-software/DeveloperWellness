using DeveloperWellness.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace DeveloperWellness.Application;

/// <summary>
/// Application-layer service registration (tasks.md T009). T012 <c>DashboardQueryService</c> registered
/// below; later use-case-service tasks append one registration each, in dependency order: T024
/// <c>CheckInService</c>, T026 <c>CheckInAlertService</c>, T029 <c>OverviewService</c>, T035
/// <c>AiSummaryService</c>, T040 <c>ToneAnalysisService</c>, T043 <c>QualityQuantityService</c> (this
/// registration list's last entry).
/// Program.cs calls <see cref="AddApplication"/> exactly once and is never edited again.
/// </summary>
public static class DependencyInjection
{
    /// <summary>Registers Application-layer services.</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // T012: scoped so the retry loop it owns lives and dies with the Blazor circuit.
        services.AddScoped<DashboardQueryService>();

        // T024: scoped like DashboardQueryService, whose enriched summaries it composes into the roster.
        services.AddScoped<CheckInService>();

        // T026: scoped (circuit-scoped) seen-state machine behind the alert indicator (FR-030, FR-031).
        services.AddScoped<CheckInAlertService>();

        // T029: scoped like CheckInService, whose fetched snapshot and roster composition it composes into the Overview.
        services.AddScoped<OverviewService>();

        // T035: scoped like DashboardQueryService, whose enriched snapshot grounds the summaries this service requests.
        services.AddScoped<AiSummaryService>();

        // T040: scoped like DashboardQueryService, whose fetched dataset's authored comments it classifies and aggregates.
        services.AddScoped<ToneAnalysisService>();

        // T043: scoped like DashboardQueryService, whose fetched dataset it computes quality-vs-quantity rows from.
        services.AddScoped<QualityQuantityService>();

        return services;
    }
}
