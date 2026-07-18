using DeveloperWellness.Application.Ports;
using DeveloperWellness.Domain.Model;

namespace DeveloperWellness.Infrastructure.Demo;

/// <summary>
/// Deterministic, fixed-seed demo implementation of <see cref="IActivitySource"/> (FR-013, research R5).
/// Returns a fully synthetic, hand-authored dataset — fictitious identities, projects, and events built
/// by <see cref="DemoSeed"/> — with no network access of any kind. Swapped in behind
/// <c>WellnessOptions.DemoMode</c> so the rest of the application never distinguishes demo mode from a
/// live GitHub-backed source at the type level.
/// </summary>
/// <remarks>
/// This adapter never fails: unlike a live activity source there is no credential, rate-limit, or
/// connectivity path to fail on, so it never throws <see cref="ActivitySourceException"/>.
/// </remarks>
public sealed class DemoActivitySource : IActivitySource
{
    /// <inheritdoc />
    /// <remarks>
    /// Identical <paramref name="scope"/> and <paramref name="period"/> values always produce a dataset
    /// with identical roster, events, and weekly commit series (the fixed-seed determinism this adapter
    /// exists to provide); only <see cref="ActivityDataset.LoadedAt"/> varies between calls, by design
    /// (FR-011: it feeds the freshness line).
    /// </remarks>
    public Task<ActivityDataset> GetActivityAsync(ScopeKey scope, Period period, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var dataset = DemoSeed.BuildDataset(scope, period);
        return Task.FromResult(dataset);
    }
}
