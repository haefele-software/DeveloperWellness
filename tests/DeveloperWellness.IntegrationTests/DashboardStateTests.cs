using DeveloperWellness.Domain.Options;
using DeveloperWellness.Web.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace DeveloperWellness.IntegrationTests;

/// <summary>
/// Unit coverage for <see cref="DashboardState.SetLoading"/>: the shell's top loading bar (retry-loading
/// UX fix) depends on this flipping <see cref="DashboardState.IsLoading"/> and raising
/// <see cref="DashboardState.Changed"/> so every subscriber (each page, and <c>MainLayout</c> indirectly
/// via its own subscription) re-renders.
/// </summary>
public class DashboardStateTests
{
    private static DashboardState CreateState() =>
        new(Options.Create(new WellnessOptions { DemoMode = true }), new ConfigurationBuilder().Build());

    [Fact]
    public void SetLoading_True_FlipsIsLoadingAndRaisesChanged()
    {
        var state = CreateState();
        var raised = false;
        state.Changed += () => raised = true;

        state.SetLoading(true);

        Assert.True(state.IsLoading);
        Assert.True(raised);
    }

    [Fact]
    public void SetLoading_False_FlipsIsLoadingAndRaisesChanged()
    {
        var state = CreateState();
        state.SetLoading(true);
        var raised = false;
        state.Changed += () => raised = true;

        state.SetLoading(false);

        Assert.False(state.IsLoading);
        Assert.True(raised);
    }

    [Fact]
    public void SetLoading_DoesNotChangeScopePeriodOrDataVersion()
    {
        // The state-change loop guard every page uses keys its refetch decision off (Scope, PeriodDays,
        // DataVersion); SetLoading must never touch any of the three, or a page's loading-bar wrapping
        // around its own fetch would trigger a refetch loop via its own State.Changed subscription.
        var state = CreateState();
        var scopeBefore = state.Scope;
        var periodBefore = state.PeriodDays;
        var versionBefore = state.DataVersion;

        state.SetLoading(true);
        state.SetLoading(false);

        Assert.Equal(scopeBefore, state.Scope);
        Assert.Equal(periodBefore, state.PeriodDays);
        Assert.Equal(versionBefore, state.DataVersion);
    }
}
