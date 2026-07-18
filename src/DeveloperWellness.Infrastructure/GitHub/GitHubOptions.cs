namespace DeveloperWellness.Infrastructure.GitHub;

/// <summary>
/// GitHub connection configuration (data-model.md GitHubOptions). Bound from the "GitHub" configuration
/// section inside <see cref="DependencyInjection.AddInfrastructure"/>. Unlike <c>WellnessOptions</c>,
/// neither property is validated at application start-up: live mode is a per-call concern, not a
/// start-up gate, so a fresh clone with demo mode off but no GitHub configuration still starts, and
/// <see cref="GitHubActivitySource"/> throws a user-presentable <c>ActivitySourceException</c> the first
/// time a load is attempted (research R2).
/// </summary>
public sealed record GitHubOptions
{
    /// <summary>The configuration section name this record binds from.</summary>
    public const string SectionName = "GitHub";

    /// <summary>The GitHub organisation login Pulse reads activity from. Required in live mode.</summary>
    public string Organisation { get; set; } = string.Empty;

    /// <summary>
    /// The fine-grained personal access token used to authenticate against the GitHub REST API (secret,
    /// user-secrets in development). Needs <c>read:org</c> to resolve teams (FR-036); missing that scope
    /// specifically degrades teams to empty rather than failing the whole load. Required in live mode.
    /// </summary>
    public string Token { get; set; } = string.Empty;
}
