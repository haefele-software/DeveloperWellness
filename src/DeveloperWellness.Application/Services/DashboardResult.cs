namespace DeveloperWellness.Application.Services;

/// <summary>Discriminates why a <see cref="DashboardResult"/> carries no fresh snapshot (FR-011).</summary>
public enum DashboardErrorKind
{
    /// <summary>The fetch succeeded; <see cref="DashboardResult.Snapshot"/> is fresh.</summary>
    None,

    /// <summary>Live mode is on but the required GitHub credentials are missing from configuration.</summary>
    CredentialsMissing,

    /// <summary>Credentials are configured but GitHub refused them (invalid, expired, revoked, or missing a required scope).</summary>
    CredentialsRejected,

    /// <summary>GitHub is reachable but the request was rejected due to rate limiting.</summary>
    RateLimited,

    /// <summary>The activity source failed for a reason other than missing credentials or a rate limit.</summary>
    Unavailable,
}

/// <summary>
/// The outcome of a <see cref="DashboardQueryService"/> fetch, always returned rather than thrown so
/// dashboard pages render a state instead of an unhandled exception (FR-011, contracts/
/// application-ports.md: "Failures surface as Result-style outcomes ... never used for ordinary control
/// flow"). On failure, when a snapshot was previously loaded for the same scope and period,
/// <see cref="Snapshot"/> carries that stale snapshot and <see cref="IsStale"/> is true, rather than
/// clearing the UI; with no previous snapshot, <see cref="Snapshot"/> is null and the caller renders an
/// inline error instead.
/// </summary>
/// <param name="RetryAt">
/// When <see cref="Kind"/> is <see cref="DashboardErrorKind.RateLimited"/>, the time the automatic
/// background retry is scheduled for (reset-aware retry scheduling); null otherwise, or when no reset time
/// was known and the default retry delay applies.
/// </param>
public sealed record DashboardResult(
    DashboardSnapshot? Snapshot,
    string? ErrorMessage,
    DashboardErrorKind Kind,
    bool IsStale,
    DateTimeOffset? RetryAt = null);
