using DeveloperWellness.Application.Ports;
using DeveloperWellness.Application.Services;
using DeveloperWellness.Domain.Model;
using DeveloperWellness.Domain.Options;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace DeveloperWellness.UnitTests;

/// <summary>
/// Coverage for <see cref="QualityQuantityService"/>'s lines-changed mapping (commit-size/volume metric,
/// <see cref="QualityQuantityRow.LinesChanged"/>): a matched author's dataset-wide total maps straight
/// through, an author absent from an otherwise non-empty dictionary maps to zero (they genuinely changed no
/// default-branch lines this period), and every row maps to null when the whole dictionary is empty
/// (GitHub's statistics endpoint was unavailable for every covered repository).
/// </summary>
public class QualityQuantityServiceLinesChangedTests
{
    private static readonly ScopeKey OrgScope = ScopeKey.Organisation;
    private static readonly DeveloperLogin Alice = new("alice");
    private static readonly DeveloperLogin Bob = new("bob");

    private static WellnessOptions BuildWellnessOptions() => new() { OrganisationTimeZone = "UTC" };

    private static QualityQuantityService BuildService(IActivitySource activitySource)
    {
        var queryService = new DashboardQueryService(
            activitySource, new MemoryCache(new MemoryCacheOptions()), Options.Create(BuildWellnessOptions()));

        return new QualityQuantityService(queryService, Options.Create(BuildWellnessOptions()));
    }

    private static ActivityDataset BuildDataset(IReadOnlyDictionary<DeveloperLogin, int> linesChangedByAuthor)
    {
        var roster = new List<Developer>
        {
            new(Alice, "Alice", isBot: false),
            new(Bob, "Bob", isBot: false),
        };

        var events = new List<ActivityEvent>
        {
            new CommitEvent(Alice, "demo-project", DateTimeOffset.UtcNow, "sha-alice-1", hasUsableOffset: true),
            new CommitEvent(Bob, "demo-project", DateTimeOffset.UtcNow, "sha-bob-1", hasUsableOffset: true),
        };

        return new ActivityDataset(
            roster: roster,
            projects: [new Project("demo-project", DateTimeOffset.UtcNow)],
            teams: [],
            events: events,
            weeklyCommitCounts: [],
            coveredProjectNames: ["demo-project"],
            linesChangedByAuthor: linesChangedByAuthor,
            loadedAt: DateTimeOffset.UtcNow,
            isDemoData: true);
    }

    [Fact]
    public async Task GetAsync_LinesChangedDictionaryHasAnEntryForAnAuthor_MapsItsValue()
    {
        var linesChangedByAuthor = new Dictionary<DeveloperLogin, int> { [Alice] = 250 };
        var source = new OneShotActivitySource(BuildDataset(linesChangedByAuthor));
        var service = BuildService(source);

        var result = await service.GetAsync(OrgScope, periodDays: 14, CancellationToken.None);

        var alice = Assert.Single(result.Rows!, row => row.Developer.Login == Alice);
        Assert.Equal(250, alice.LinesChanged);
    }

    [Fact]
    public async Task GetAsync_LinesChangedDictionaryIsNonEmptyButMissingAnAuthor_MapsZeroForThatAuthor()
    {
        var linesChangedByAuthor = new Dictionary<DeveloperLogin, int> { [Alice] = 250 };
        var source = new OneShotActivitySource(BuildDataset(linesChangedByAuthor));
        var service = BuildService(source);

        var result = await service.GetAsync(OrgScope, periodDays: 14, CancellationToken.None);

        var bob = Assert.Single(result.Rows!, row => row.Developer.Login == Bob);
        Assert.Equal(0, bob.LinesChanged);
    }

    [Fact]
    public async Task GetAsync_LinesChangedDictionaryIsEmpty_MapsNullForEveryRow()
    {
        var source = new OneShotActivitySource(BuildDataset(new Dictionary<DeveloperLogin, int>()));
        var service = BuildService(source);

        var result = await service.GetAsync(OrgScope, periodDays: 14, CancellationToken.None);

        Assert.All(result.Rows!, row => Assert.Null(row.LinesChanged));
    }

    /// <summary>A one-shot <see cref="IActivitySource"/> stub returning the same pre-built dataset for every call.</summary>
    private sealed class OneShotActivitySource(ActivityDataset dataset) : IActivitySource
    {
        public Task<ActivityDataset> GetActivityAsync(ScopeKey scope, Period period, CancellationToken cancellationToken) =>
            Task.FromResult(dataset);
    }
}
