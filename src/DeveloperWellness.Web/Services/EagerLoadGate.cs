using DeveloperWellness.Application.Services;
using DeveloperWellness.Domain.Model;

namespace DeveloperWellness.Web.Services;

/// <summary>
/// Decides whether a page's (or the shell's) first data load may run synchronously inside
/// <c>OnInitializedAsync</c>/<c>OnParametersSetAsync</c> — which also execute during Blazor Server's static
/// prerender pass, before any HTTP response is sent, and never actually reach the browser's JavaScript
/// runtime — or must instead be deferred to <c>OnAfterRenderAsync(firstRender: true)</c>, which is
/// documented to never run during prerendering (startup-load fix: "the app loads indefinitely at
/// startup"). Eager loading is safe exactly when the load is effectively instant: demo mode, whose activity
/// source is in-memory, or a warm cache for this exact scope and period
/// (<see cref="DashboardQueryService.HasCachedSnapshot"/>). Anything else — a cold cache against the live
/// GitHub source, which can take tens of seconds — must not block the prerendered response behind it;
/// deferring to <c>OnAfterRenderAsync</c> lets that response stream immediately with the page's existing
/// skeleton, and the fetch then runs on the live circuit with the shell's loading bar visible.
/// </summary>
public static class EagerLoadGate
{
    /// <summary>
    /// True when <paramref name="demoMode"/> is on, or <paramref name="queryService"/> already holds a
    /// cached snapshot for <paramref name="scope"/> and <paramref name="periodDays"/> — either way, the
    /// first load this gate covers is a cache hit (or the in-memory demo fetch), never a cold live fetch.
    /// </summary>
    public static bool ShouldLoadEagerly(DashboardQueryService queryService, ScopeKey scope, int periodDays, bool demoMode) =>
        demoMode || queryService.HasCachedSnapshot(scope, periodDays);
}
