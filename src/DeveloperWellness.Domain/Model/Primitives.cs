namespace DeveloperWellness.Domain.Model;

/// <summary>
/// Canonical developer identity: the organisation member's GitHub login. Comparisons are
/// case-insensitive because GitHub logins are not case-sensitive identifiers.
/// </summary>
/// <remarks>
/// Modelled as a struct wrapping a string with a reserved <see cref="Unmatched"/> sentinel rather
/// than a nullable login, so an event's author is always representable without a null check at every
/// call site. Equality compares the <see cref="IsUnmatched"/> flag first, so the sentinel can never be
/// spoofed by an ordinary login that happens to share its backing text. The default (uninitialized)
/// value of this struct — reachable via <c>default(DeveloperLogin)</c> — reads as an empty, non-unmatched
/// login rather than throwing, because struct default construction cannot be intercepted in C#; callers
/// should always construct through the public constructor or <see cref="Unmatched"/>.
/// </remarks>
public readonly record struct DeveloperLogin
{
    private const string UnmatchedValue = "(unmatched)";

    /// <summary>
    /// Reserved marker used as an <see cref="ActivityEvent.Author"/> when the activity source could not
    /// match an event's author to a roster login (data-model.md edge case: unmatched-author bucketing).
    /// </summary>
    public static readonly DeveloperLogin Unmatched = new(UnmatchedValue, isUnmatched: true);

    private readonly string? _value;

    /// <summary>Creates a login for the given non-empty GitHub login text.</summary>
    /// <exception cref="ArgumentException">The value is null, empty, or whitespace.</exception>
    public DeveloperLogin(string value)
        : this(value, isUnmatched: false)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Developer login must not be empty.", nameof(value));
        }
    }

    private DeveloperLogin(string value, bool isUnmatched)
    {
        _value = value;
        IsUnmatched = isUnmatched;
    }

    /// <summary>The raw login text, or the unmatched marker text when <see cref="IsUnmatched"/> is true.</summary>
    public string Value => _value ?? string.Empty;

    /// <summary>True only for the <see cref="Unmatched"/> sentinel.</summary>
    public bool IsUnmatched { get; }

    /// <inheritdoc />
    public bool Equals(DeveloperLogin other) =>
        IsUnmatched == other.IsUnmatched &&
        string.Equals(Value, other.Value, StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public override int GetHashCode() =>
        HashCode.Combine(IsUnmatched, StringComparer.OrdinalIgnoreCase.GetHashCode(Value));

    /// <inheritdoc />
    public override string ToString() => Value;
}

/// <summary>Discriminates the two <see cref="ScopeKey"/> shapes.</summary>
public enum ScopeKind
{
    /// <summary>The whole organisation.</summary>
    Organisation,

    /// <summary>A single named project.</summary>
    Project,
}

/// <summary>
/// The selected dashboard scope: the whole organisation, or a single project (FR-007). A small closed
/// union modelled as a record with a kind discriminator and an optional project name, constructed only
/// through the <see cref="Organisation"/> singleton or the <see cref="Project(string)"/> factory. Value
/// equality makes it safe to use as a component of a cache key alongside <see cref="Period"/> (FR-021).
/// </summary>
public sealed record ScopeKey
{
    private ScopeKey(ScopeKind kind, string? projectName)
    {
        Kind = kind;
        ProjectName = projectName;
    }

    /// <summary>Which shape this scope key holds.</summary>
    public ScopeKind Kind { get; }

    /// <summary>The project name when <see cref="Kind"/> is <see cref="ScopeKind.Project"/>; otherwise null.</summary>
    public string? ProjectName { get; }

    /// <summary>The organisation-wide scope.</summary>
    public static ScopeKey Organisation { get; } = new(ScopeKind.Organisation, projectName: null);

    /// <summary>Creates a scope limited to the named project.</summary>
    /// <exception cref="ArgumentException">The name is null, empty, or whitespace.</exception>
    public static ScopeKey Project(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Project name must not be empty.", nameof(name));
        }

        return new ScopeKey(ScopeKind.Project, name);
    }

    /// <inheritdoc />
    public override string ToString() => Kind switch
    {
        ScopeKind.Organisation => "organisation",
        ScopeKind.Project => $"project:{ProjectName}",
        _ => Kind.ToString(),
    };
}

/// <summary>
/// The lookback window for a dashboard query: 7, 14, or 30 days ending at <see cref="End"/>, defaulting
/// to 14 (FR-009). A small immutable value usable as a component of a cache key alongside
/// <see cref="ScopeKey"/> (FR-021).
/// </summary>
public readonly record struct Period
{
    /// <summary>The only day-count values a period may take.</summary>
    private static readonly int[] AllowedDayValues = [7, 14, 30];

    /// <summary>The default period length in days (FR-009).</summary>
    public const int DefaultDays = 14;

    /// <summary>Creates a period of <paramref name="days"/> days ending at <paramref name="end"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="days"/> is not 7, 14, or 30.</exception>
    public Period(int days, DateTimeOffset end)
    {
        if (Array.IndexOf(AllowedDayValues, days) < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(days), days, "Period must be 7, 14, or 30 days.");
        }

        Days = days;
        End = end;
        Start = end - TimeSpan.FromDays(days);
    }

    /// <summary>The period length: 7, 14, or 30.</summary>
    public int Days { get; }

    /// <summary>The inclusive-start instant the period covers from.</summary>
    public DateTimeOffset Start { get; }

    /// <summary>The instant the period covers up to.</summary>
    public DateTimeOffset End { get; }

    /// <inheritdoc />
    public override string ToString() => $"{Days}d ending {End:O}";
}

/// <summary>Wellbeing signal categories surfaced on rosters and detail pages (FR-026).</summary>
public enum FlagKind
{
    /// <summary>Out-of-hours commit share above the configured threshold.</summary>
    OverworkCommits,

    /// <summary>Out-of-hours PR-activity share above the configured threshold.</summary>
    OverworkPrActivity,

    /// <summary>Distinct active projects at or above the spread-thin threshold (organisation scope only).</summary>
    SpreadThin,

    /// <summary>Authored-comment negative tone share above the configured threshold.</summary>
    NegativeTone,

    /// <summary>Volume above the roster median combined with a high changes-requested share.</summary>
    PossibleRushing,
}

/// <summary>
/// Sentiment classification for a single comment (FR-018). Unparseable AI results map to
/// <see cref="Unanalysed"/>, never <see cref="Negative"/> (edge case).
/// </summary>
public enum ToneClass
{
    /// <summary>The comment reads as positive.</summary>
    Positive,

    /// <summary>The comment reads as neutral.</summary>
    Neutral,

    /// <summary>The comment reads as negative.</summary>
    Negative,

    /// <summary>The comment could not be classified; never treated as negative.</summary>
    Unanalysed,
}

/// <summary>Submitted pull-request review outcome, feeding the rework proxies in FR-027.</summary>
public enum ReviewState
{
    /// <summary>The review approved the pull request.</summary>
    Approved,

    /// <summary>The review requested changes.</summary>
    ChangesRequested,

    /// <summary>The review left comments without approving or requesting changes.</summary>
    Commented,
}

/// <summary>Discriminates the two <see cref="AiSubject"/> shapes.</summary>
public enum AiSubjectKind
{
    /// <summary>A single named project.</summary>
    Project,

    /// <summary>A single developer.</summary>
    Developer,
}

/// <summary>
/// The subject of an AI-generated summary: either a project or a developer (FR-015, FR-016). A small
/// closed union modelled as a record with a kind discriminator and an optional project name or login,
/// constructed only through the <see cref="Project(string)"/> or <see cref="Developer(DeveloperLogin)"/>
/// factories.
/// </summary>
public sealed record AiSubject
{
    private AiSubject(AiSubjectKind kind, string? projectName, DeveloperLogin? login)
    {
        Kind = kind;
        ProjectName = projectName;
        Login = login;
    }

    /// <summary>Which shape this subject holds.</summary>
    public AiSubjectKind Kind { get; }

    /// <summary>The project name when <see cref="Kind"/> is <see cref="AiSubjectKind.Project"/>; otherwise null.</summary>
    public string? ProjectName { get; }

    /// <summary>The developer login when <see cref="Kind"/> is <see cref="AiSubjectKind.Developer"/>; otherwise null.</summary>
    public DeveloperLogin? Login { get; }

    /// <summary>Creates a subject naming the given project.</summary>
    /// <exception cref="ArgumentException">The name is null, empty, or whitespace.</exception>
    public static AiSubject Project(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Project name must not be empty.", nameof(name));
        }

        return new AiSubject(AiSubjectKind.Project, name, login: null);
    }

    /// <summary>Creates a subject naming the given developer.</summary>
    public static AiSubject Developer(DeveloperLogin login) => new(AiSubjectKind.Developer, projectName: null, login);

    /// <inheritdoc />
    public override string ToString() => Kind switch
    {
        AiSubjectKind.Project => $"project:{ProjectName}",
        AiSubjectKind.Developer => $"developer:{Login}",
        _ => Kind.ToString(),
    };
}

/// <summary>
/// A single wellbeing signal for a developer: its category and the mandatory plain-language reason
/// shown wherever the flag is surfaced (data-model.md WellbeingFlag, SC-010).
/// </summary>
public sealed record WellbeingFlag
{
    /// <summary>Creates a flag of the given kind with a mandatory, non-empty reason.</summary>
    /// <exception cref="ArgumentException"><paramref name="reason"/> is null, empty, or whitespace.</exception>
    public WellbeingFlag(FlagKind kind, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("Wellbeing flag reason must not be empty.", nameof(reason));
        }

        Kind = kind;
        Reason = reason;
    }

    /// <summary>The signal category.</summary>
    public FlagKind Kind { get; }

    /// <summary>The plain-language, mandatory explanation for why this flag was raised.</summary>
    public string Reason { get; }
}
