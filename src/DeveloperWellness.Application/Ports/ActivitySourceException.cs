namespace DeveloperWellness.Application.Ports;

/// <summary>
/// Discriminates why an <see cref="ActivitySourceException"/> was thrown, so callers (notably
/// <c>DashboardQueryService</c>) can distinguish a missing-credentials failure from a rate limit from any
/// other connectivity problem without parsing <see cref="Exception.Message"/>. Defaults to
/// <see cref="Unavailable"/>: the demo source never throws, and the live GitHub adapter is responsible
/// for setting the specific kind on the failures it can identify.
/// </summary>
public enum ActivitySourceFailureKind
{
    /// <summary>A failure other than missing credentials or a rate limit (e.g. connectivity, an unexpected API error).</summary>
    Unavailable,

    /// <summary>Live mode is on but the required GitHub credentials are missing from configuration.</summary>
    CredentialsMissing,

    /// <summary>Credentials are configured but GitHub refused them (invalid, expired, revoked, or missing a required scope).</summary>
    CredentialsRejected,

    /// <summary>GitHub is reachable but the request was rejected due to rate limiting.</summary>
    RateLimited,
}

/// <summary>
/// Thrown by <see cref="IActivitySource"/> implementations for credential, rate-limit, and connectivity
/// failures (FR-011). <see cref="Exception.Message"/> is always user-presentable; callers keep the
/// previously loaded dataset visible rather than clearing the UI on this failure.
/// </summary>
public sealed class ActivitySourceException : Exception
{
    /// <summary>Creates the exception with a user-presentable message and an unspecified (<see cref="ActivitySourceFailureKind.Unavailable"/>) failure kind.</summary>
    public ActivitySourceException(string message)
        : this(message, innerException: null, ActivitySourceFailureKind.Unavailable)
    {
    }

    /// <summary>Creates the exception with a user-presentable message, the underlying cause, and an unspecified (<see cref="ActivitySourceFailureKind.Unavailable"/>) failure kind.</summary>
    public ActivitySourceException(string message, Exception? innerException)
        : this(message, innerException, ActivitySourceFailureKind.Unavailable)
    {
    }

    /// <summary>Creates the exception with a user-presentable message, the underlying cause, and its specific failure kind.</summary>
    public ActivitySourceException(string message, Exception? innerException, ActivitySourceFailureKind kind)
        : this(message, innerException, kind, retryAfter: null)
    {
    }

    /// <summary>
    /// Creates the exception with a user-presentable message, the underlying cause, its specific failure
    /// kind, and (for a rate-limited failure) when the source expects its budget to recover.
    /// </summary>
    public ActivitySourceException(string message, Exception? innerException, ActivitySourceFailureKind kind, DateTimeOffset? retryAfter)
        : base(message, innerException)
    {
        Kind = kind;
        RetryAfter = retryAfter;
    }

    /// <summary>Why this exception was thrown; defaults to <see cref="ActivitySourceFailureKind.Unavailable"/>.</summary>
    public ActivitySourceFailureKind Kind { get; }

    /// <summary>
    /// When <see cref="Kind"/> is <see cref="ActivitySourceFailureKind.RateLimited"/>, the time the source
    /// expects its request budget to recover (from GitHub's own reset time where available, otherwise a
    /// conservative estimate); null when unknown or not applicable to this failure.
    /// </summary>
    public DateTimeOffset? RetryAfter { get; }
}
