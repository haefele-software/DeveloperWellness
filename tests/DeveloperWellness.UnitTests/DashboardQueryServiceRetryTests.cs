using DeveloperWellness.Application.Services;

namespace DeveloperWellness.UnitTests;

/// <summary>
/// Unit coverage for <see cref="DashboardQueryService.ComputeRetryAt"/>, the pure retry-time computation
/// behind the reset-aware retry scheduling (rate-limit hardening, Fix B): no timer, no I/O, just the
/// max(reset + jitter, now + default delay) rule, visible here via <c>InternalsVisibleTo</c>.
/// </summary>
public class DashboardQueryServiceRetryTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 17, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ComputeRetryAt_NoRetryAfterKnown_FallsBackToSixtySecondsFromNow()
    {
        var retryAt = DashboardQueryService.ComputeRetryAt(Now, retryAfter: null);

        Assert.Equal(Now + TimeSpan.FromSeconds(60), retryAt);
    }

    [Fact]
    public void ComputeRetryAt_RetryAfterPlusJitterIsSoonerThanTheDefaultDelay_UsesTheDefaultDelayInstead()
    {
        // Reset in 10s + 15s jitter = 25s from now, which is sooner than the 60s default floor.
        var retryAfter = Now + TimeSpan.FromSeconds(10);

        var retryAt = DashboardQueryService.ComputeRetryAt(Now, retryAfter);

        Assert.Equal(Now + TimeSpan.FromSeconds(60), retryAt);
    }

    [Fact]
    public void ComputeRetryAt_RetryAfterPlusJitterIsLaterThanTheDefaultDelay_UsesRetryAfterPlusJitter()
    {
        // Reset in 120s + 15s jitter = 135s from now, which is later than the 60s default floor.
        var retryAfter = Now + TimeSpan.FromSeconds(120);

        var retryAt = DashboardQueryService.ComputeRetryAt(Now, retryAfter);

        Assert.Equal(Now + TimeSpan.FromSeconds(135), retryAt);
    }

    [Fact]
    public void ComputeRetryAt_RetryAfterIsInThePast_FallsBackToTheDefaultDelayRatherThanRetryingImmediately()
    {
        var staleRetryAfter = Now - TimeSpan.FromSeconds(100);

        var retryAt = DashboardQueryService.ComputeRetryAt(Now, staleRetryAfter);

        Assert.Equal(Now + TimeSpan.FromSeconds(60), retryAt);
    }

    [Fact]
    public void ComputeRetryAt_RetryAfterExactlyAtTheDefaultDelayBoundary_ReturnsTheSameInstant()
    {
        // Reset in 45s + 15s jitter = exactly 60s from now: ties the default floor.
        var retryAfter = Now + TimeSpan.FromSeconds(45);

        var retryAt = DashboardQueryService.ComputeRetryAt(Now, retryAfter);

        Assert.Equal(Now + TimeSpan.FromSeconds(60), retryAt);
    }
}
