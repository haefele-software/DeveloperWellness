using DeveloperWellness.Application.Ports;
using DeveloperWellness.Infrastructure.GitHub;

namespace DeveloperWellness.IntegrationTests;

/// <summary>
/// Unit-level coverage (no network) for the rate-limit hardening's pure decision helpers on
/// <see cref="GitHubActivitySource"/>: <see cref="GitHubActivitySource.ClassifyForbidden"/> (the bug fix
/// that stops an account-level throttle disguised as a <c>ForbiddenException</c> from being told to the
/// user as a credentials problem) and <see cref="GitHubActivitySource.SelectRateLimitMessage"/> (which of
/// the two rate-limit messages is shown). Both are <c>internal</c> to
/// <see cref="GitHubActivitySource"/>'s own project, visible here via <c>InternalsVisibleTo</c>, matching
/// <see cref="GitHubActivitySourceBranchOrderingTests"/>.
/// </summary>
public class GitHubActivitySourceRateLimitClassificationTests
{
    [Theory]
    [InlineData("API rate limit exceeded for user ID 12345.")]
    [InlineData("You have exceeded a secondary rate limit. Please wait a few minutes before you try again.")]
    [InlineData("RATE LIMIT EXCEEDED")]
    [InlineData("Rate Limit exceeded, slow down")]
    public void ClassifyForbidden_MessageMentionsRateLimit_ReturnsRateLimited(string message)
    {
        var kind = GitHubActivitySource.ClassifyForbidden(message);

        Assert.Equal(ActivitySourceFailureKind.RateLimited, kind);
    }

    [Theory]
    [InlineData("Must have admin rights to Repository.")]
    [InlineData("Resource not accessible by integration")]
    [InlineData("")]
    public void ClassifyForbidden_MessageDoesNotMentionRateLimit_ReturnsCredentialsRejected(string message)
    {
        var kind = GitHubActivitySource.ClassifyForbidden(message);

        Assert.Equal(ActivitySourceFailureKind.CredentialsRejected, kind);
    }

    [Fact]
    public void SelectRateLimitMessage_Secondary_MentionsTheBurstLimit()
    {
        var message = GitHubActivitySource.SelectRateLimitMessage(isSecondary: true);

        Assert.Contains("burst limit", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SelectRateLimitMessage_Primary_MentionsTheHourlyBudget()
    {
        var message = GitHubActivitySource.SelectRateLimitMessage(isSecondary: false);

        Assert.Contains("hourly request budget", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SelectRateLimitMessage_PrimaryAndSecondary_AreDistinctMessages()
    {
        var primaryMessage = GitHubActivitySource.SelectRateLimitMessage(isSecondary: false);
        var secondaryMessage = GitHubActivitySource.SelectRateLimitMessage(isSecondary: true);

        Assert.NotEqual(primaryMessage, secondaryMessage);
    }
}
