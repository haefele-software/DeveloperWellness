using DeveloperWellness.Domain.Model;
using DeveloperWellness.Infrastructure.GitHub;

namespace DeveloperWellness.IntegrationTests;

/// <summary>
/// Unit-level coverage (no network) for <see cref="GitHubActivitySource.SumLinesChangedWithinPeriod"/> and
/// <see cref="GitHubActivitySource.MergeLinesChangedByAuthor"/>, the pure helpers behind the commit-size/
/// volume lines-changed metric. Lives in this project rather than UnitTests because both helpers are typed
/// against Octokit's <c>Contributor</c>/<c>WeeklyHash</c>/<c>Author</c> models (Infrastructure-typed), and
/// are <c>internal</c> to <see cref="GitHubActivitySource"/>'s own project, visible here via
/// <c>InternalsVisibleTo</c>, matching <see cref="GitHubActivitySourceBranchOrderingTests"/> and
/// <see cref="GitHubActivitySourceRateLimitClassificationTests"/>.
/// </summary>
public class GitHubActivitySourceLinesChangedTests
{
    private static readonly DateTimeOffset PeriodEnd = new(2026, 7, 19, 0, 0, 0, TimeSpan.Zero);

    // Period.Start = PeriodEnd - 14 days = 2026-07-05T00:00:00Z.
    private static readonly Period FourteenDayPeriod = new(14, PeriodEnd);

    private static readonly DeveloperLogin Nova = new("nova");

    [Fact]
    public void SumLinesChangedWithinPeriod_WeekFullyInsidePeriod_IsIncluded()
    {
        // 2026-07-10 to 2026-07-17 sits entirely within [2026-07-05, 2026-07-19].
        var weekStart = new DateTimeOffset(2026, 7, 10, 0, 0, 0, TimeSpan.Zero);
        var contributor = MakeContributor("nova", MakeWeek(weekStart, additions: 100, deletions: 40, commits: 5));

        var result = GitHubActivitySource.SumLinesChangedWithinPeriod([contributor], FourteenDayPeriod);

        Assert.Equal(140, result[Nova]);
    }

    [Fact]
    public void SumLinesChangedWithinPeriod_WeekStraddlingThePeriodStart_IsIncluded()
    {
        // The week [2026-07-01, 2026-07-08) starts before Period.Start (2026-07-05) but its window still
        // overlaps the period, so it counts in full despite starting earlier.
        var weekStart = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);
        var contributor = MakeContributor("nova", MakeWeek(weekStart, additions: 30, deletions: 10, commits: 2));

        var result = GitHubActivitySource.SumLinesChangedWithinPeriod([contributor], FourteenDayPeriod);

        Assert.Equal(40, result[Nova]);
    }

    [Fact]
    public void SumLinesChangedWithinPeriod_WeekEntirelyBeforePeriod_ContributesZero()
    {
        // The week [2026-06-14, 2026-06-21) ends well before Period.Start (2026-07-05): no overlap at all,
        // so it contributes nothing to Nova's total. Nova still appears in the result at zero rather than
        // being omitted entirely: GitHub's statistics call for this contributor genuinely succeeded, it
        // just had nothing to say about this particular period, which must read differently downstream
        // from "the statistics endpoint was unavailable" (an empty result set, not a zero one).
        var weekStart = new DateTimeOffset(2026, 6, 14, 0, 0, 0, TimeSpan.Zero);
        var contributor = MakeContributor("nova", MakeWeek(weekStart, additions: 500, deletions: 500, commits: 20));

        var result = GitHubActivitySource.SumLinesChangedWithinPeriod([contributor], FourteenDayPeriod);

        Assert.Equal(0, result[Nova]);
    }

    [Fact]
    public void SumLinesChangedWithinPeriod_WeekEntirelyAfterPeriod_ContributesZero()
    {
        // The week [2026-08-01, 2026-08-08) starts after Period.End (2026-07-19): no overlap at all, so it
        // contributes nothing to Nova's total (see the "before period" case above for why Nova still
        // appears in the result at zero rather than being omitted).
        var weekStart = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);
        var contributor = MakeContributor("nova", MakeWeek(weekStart, additions: 500, deletions: 500, commits: 20));

        var result = GitHubActivitySource.SumLinesChangedWithinPeriod([contributor], FourteenDayPeriod);

        Assert.Equal(0, result[Nova]);
    }

    [Fact]
    public void SumLinesChangedWithinPeriod_ContributorWithNoMatchedAuthor_IsSkipped()
    {
        var weekStart = new DateTimeOffset(2026, 7, 10, 0, 0, 0, TimeSpan.Zero);
        var contributor = new Octokit.Contributor(
            author: null!, total: 5, weeks: [MakeWeek(weekStart, additions: 100, deletions: 40, commits: 5)]);

        var result = GitHubActivitySource.SumLinesChangedWithinPeriod([contributor], FourteenDayPeriod);

        Assert.Empty(result);
    }

    [Fact]
    public void MergeLinesChangedByAuthor_MultipleRepositories_SumsPerAuthorAcrossRepositories()
    {
        var flynn = new DeveloperLogin("flynn");
        var repo1Totals = new Dictionary<DeveloperLogin, int> { [Nova] = 100, [flynn] = 50 };
        var repo2Totals = new Dictionary<DeveloperLogin, int> { [Nova] = 30 };

        var merged = GitHubActivitySource.MergeLinesChangedByAuthor([repo1Totals, repo2Totals]);

        Assert.Equal(130, merged[Nova]);
        Assert.Equal(50, merged[flynn]);
    }

    [Fact]
    public void MergeLinesChangedByAuthor_NoRepositories_ReturnsEmpty()
    {
        var merged = GitHubActivitySource.MergeLinesChangedByAuthor([]);

        Assert.Empty(merged);
    }

    private static Octokit.Contributor MakeContributor(string login, params Octokit.WeeklyHash[] weeks) =>
        new(MakeAuthor(login), total: weeks.Sum(w => w.Commits), weeks);

    private static Octokit.Author MakeAuthor(string login) =>
        new(login, 0, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, false);

    private static Octokit.WeeklyHash MakeWeek(DateTimeOffset weekStart, int additions, int deletions, int commits) =>
        new(weekStart.ToUnixTimeSeconds(), additions, deletions, commits);
}
