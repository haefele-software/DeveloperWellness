using DeveloperWellness.Infrastructure.GitHub;

namespace DeveloperWellness.IntegrationTests;

/// <summary>
/// Unit-level coverage (no network) for <see cref="GitHubActivitySource.OrderDefaultBranchFirst"/>, the
/// pure branch-ordering helper behind the rate-limit hardening's branch-head skip (Fix A1): the default
/// branch is fetched first so its commits populate the seen-SHA set before any feature branch is walked,
/// maximising how often a feature branch's listing can be skipped outright. Lives in this project rather
/// than UnitTests because the helper is typed against Octokit's <c>Branch</c>/<c>GitReference</c> models
/// (Infrastructure-typed), and is <c>internal</c> to <see cref="GitHubActivitySource"/>'s own project,
/// visible here via <c>InternalsVisibleTo</c>.
/// </summary>
public class GitHubActivitySourceBranchOrderingTests
{
    [Fact]
    public void OrderDefaultBranchFirst_DefaultBranchInTheMiddle_MovesItToTheFrontKeepingOthersInOrder()
    {
        var feature1 = MakeBranch("feature-1", "sha-feature-1");
        var main = MakeBranch("main", "sha-main");
        var feature2 = MakeBranch("feature-2", "sha-feature-2");
        Octokit.Branch[] branches = [feature1, main, feature2];

        var ordered = GitHubActivitySource.OrderDefaultBranchFirst(branches, "main");

        Assert.Equal([main, feature1, feature2], ordered);
    }

    [Fact]
    public void OrderDefaultBranchFirst_DefaultBranchAlreadyFirst_LeavesOrderUnchanged()
    {
        var main = MakeBranch("main", "sha-main");
        var feature1 = MakeBranch("feature-1", "sha-feature-1");
        Octokit.Branch[] branches = [main, feature1];

        var ordered = GitHubActivitySource.OrderDefaultBranchFirst(branches, "main");

        Assert.Equal([main, feature1], ordered);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void OrderDefaultBranchFirst_NoDefaultBranchNameSupplied_ReturnsTheOriginalListUnchanged(string? defaultBranchName)
    {
        var feature1 = MakeBranch("feature-1", "sha-feature-1");
        var feature2 = MakeBranch("feature-2", "sha-feature-2");
        Octokit.Branch[] branches = [feature1, feature2];

        var ordered = GitHubActivitySource.OrderDefaultBranchFirst(branches, defaultBranchName);

        Assert.Equal([feature1, feature2], ordered);
    }

    [Fact]
    public void OrderDefaultBranchFirst_DefaultBranchNameMatchesNoFetchedBranch_ReturnsTheOriginalListUnchanged()
    {
        var feature1 = MakeBranch("feature-1", "sha-feature-1");
        var feature2 = MakeBranch("feature-2", "sha-feature-2");
        Octokit.Branch[] branches = [feature1, feature2];

        var ordered = GitHubActivitySource.OrderDefaultBranchFirst(branches, "main");

        Assert.Equal([feature1, feature2], ordered);
    }

    [Fact]
    public void OrderDefaultBranchFirst_MatchIsCaseSensitive_SoADifferentlyCasedNameIsNotTreatedAsTheDefaultBranch()
    {
        var main = MakeBranch("Main", "sha-main");
        var feature1 = MakeBranch("feature-1", "sha-feature-1");
        Octokit.Branch[] branches = [feature1, main];

        var ordered = GitHubActivitySource.OrderDefaultBranchFirst(branches, "main");

        Assert.Equal([feature1, main], ordered);
    }

    private static Octokit.Branch MakeBranch(string name, string headSha) =>
        new(name, new Octokit.GitReference(null!, null!, null!, null!, headSha, null!, null!), @protected: false);
}
