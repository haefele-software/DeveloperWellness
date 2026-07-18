namespace DeveloperWellness.Domain.Model;

/// <summary>
/// An organisation member. Bots are represented (<see cref="IsBot"/>) rather than dropped at the type
/// level; excluding them from summaries, rosters, and tone aggregates is an application-level filtering
/// concern (FR-010), not an invariant this entity enforces on its own.
/// </summary>
public sealed record Developer
{
    /// <summary>Creates a developer, falling back to the login text when no display name is supplied.</summary>
    /// <exception cref="ArgumentException"><paramref name="login"/> is the <see cref="DeveloperLogin.Unmatched"/> sentinel.</exception>
    public Developer(DeveloperLogin login, string? displayName, bool isBot)
    {
        if (login.IsUnmatched)
        {
            throw new ArgumentException("A roster developer cannot use the unmatched-author sentinel.", nameof(login));
        }

        Login = login;
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? login.Value : displayName;
        IsBot = isBot;
    }

    /// <summary>The developer's canonical, unique-within-organisation login.</summary>
    public DeveloperLogin Login { get; }

    /// <summary>The display name shown in the UI; falls back to <see cref="Login"/>'s text when unset.</summary>
    public string DisplayName { get; }

    /// <summary>True for automation accounts; excluded from summaries, rosters, and tone aggregates (FR-010).</summary>
    public bool IsBot { get; }
}

/// <summary>A repository tracked for activity, unique by name within the organisation.</summary>
public sealed record Project
{
    /// <summary>Creates a project with the given unique, non-empty name.</summary>
    /// <exception cref="ArgumentException"><paramref name="name"/> is null, empty, or whitespace.</exception>
    public Project(string name, DateTimeOffset lastPushedAt)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Project name must not be empty.", nameof(name));
        }

        Name = name;
        LastPushedAt = lastPushedAt;
    }

    /// <summary>The unique-within-organisation project name.</summary>
    public string Name { get; }

    /// <summary>The most recent push instant, driving recently-active ordering for the repo cap (FR-007).</summary>
    public DateTimeOffset LastPushedAt { get; }
}

/// <summary>
/// An organisation team and its roster of member logins (FR-036). Multi-team members count under their
/// first team alphabetically; developers on no team form a separate no-team group; both rules are
/// applied by the callers that build <see cref="ActivityDataset.Teams"/>, not by this type.
/// </summary>
public sealed record Team
{
    /// <summary>Creates a team with the given unique, non-empty name and member roster.</summary>
    /// <exception cref="ArgumentException"><paramref name="name"/> is null, empty, or whitespace.</exception>
    public Team(string name, IReadOnlyList<DeveloperLogin> members)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Team name must not be empty.", nameof(name));
        }

        Name = name;
        Members = members ?? throw new ArgumentNullException(nameof(members));
    }

    /// <summary>The unique-within-organisation team name.</summary>
    public string Name { get; }

    /// <summary>The team's member logins.</summary>
    public IReadOnlyList<DeveloperLogin> Members { get; }
}

/// <summary>
/// Closed hierarchy of raw activity facts ingested from an activity source. Every subtype is sealed;
/// new event shapes require a new subtype here rather than an open extension point.
/// </summary>
public abstract record ActivityEvent
{
    /// <summary>Initialises the fields common to every activity event.</summary>
    /// <exception cref="ArgumentException"><paramref name="projectName"/> is null, empty, or whitespace.</exception>
    private protected ActivityEvent(DeveloperLogin author, string projectName, DateTimeOffset occurredAt)
    {
        if (string.IsNullOrWhiteSpace(projectName))
        {
            throw new ArgumentException("Project name must not be empty.", nameof(projectName));
        }

        Author = author;
        ProjectName = projectName;
        OccurredAt = occurredAt;
    }

    /// <summary>
    /// The event's author, or <see cref="DeveloperLogin.Unmatched"/> when the source could not match it
    /// to a roster login (edge case).
    /// </summary>
    public DeveloperLogin Author { get; }

    /// <summary>The project the event occurred in.</summary>
    public string ProjectName { get; }

    /// <summary>
    /// The instant the event occurred. For <see cref="CommitEvent"/>, this preserves the author-local
    /// offset when available (FR-005), reflected by <see cref="CommitEvent.HasUsableOffset"/>.
    /// </summary>
    public DateTimeOffset OccurredAt { get; }
}

/// <summary>A commit on any branch, deduplicated across branches by <see cref="Sha"/> (FR-002).</summary>
public sealed record CommitEvent : ActivityEvent
{
    /// <summary>Creates a commit event with its dedup key and offset-usability flag.</summary>
    /// <exception cref="ArgumentException"><paramref name="sha"/> is null, empty, or whitespace.</exception>
    public CommitEvent(DeveloperLogin author, string projectName, DateTimeOffset occurredAt, string sha, bool hasUsableOffset)
        : base(author, projectName, occurredAt)
    {
        if (string.IsNullOrWhiteSpace(sha))
        {
            throw new ArgumentException("Commit SHA must not be empty.", nameof(sha));
        }

        Sha = sha;
        HasUsableOffset = hasUsableOffset;
    }

    /// <summary>The commit SHA; the dataset-wide deduplication key (FR-002).</summary>
    public string Sha { get; }

    /// <summary>
    /// True when <see cref="ActivityEvent.OccurredAt"/> carries a genuine author-local UTC offset rather
    /// than a fallback (e.g. the organisation timezone), per FR-005.
    /// </summary>
    public bool HasUsableOffset { get; }
}

/// <summary>One submitted pull-request review (FR-003); each submission is its own event.</summary>
public sealed record ReviewEvent : ActivityEvent
{
    /// <summary>Creates a review event for the given pull request and outcome.</summary>
    public ReviewEvent(DeveloperLogin author, string projectName, DateTimeOffset occurredAt, int prNumber, ReviewState state)
        : base(author, projectName, occurredAt)
    {
        PrNumber = prNumber;
        State = state;
    }

    /// <summary>The pull request number the review was submitted on.</summary>
    public int PrNumber { get; }

    /// <summary>The review's outcome.</summary>
    public ReviewState State { get; }
}

/// <summary>
/// An issue or pull-request comment. <see cref="BodyText"/> is held transiently for tone
/// classification only (FR-022); it is never rendered with a per-comment verdict.
/// </summary>
public sealed record CommentEvent : ActivityEvent
{
    /// <summary>Creates a comment event carrying its body text for tone analysis.</summary>
    public CommentEvent(DeveloperLogin author, string projectName, DateTimeOffset occurredAt, long commentId, string bodyText)
        : base(author, projectName, occurredAt)
    {
        CommentId = commentId;
        BodyText = bodyText ?? string.Empty;
    }

    /// <summary>The comment's identifier.</summary>
    public long CommentId { get; }

    /// <summary>The comment body, used only as tone-classification input (FR-022).</summary>
    public string BodyText { get; }
}

/// <summary>A pull request being opened (FR-024).</summary>
public sealed record PrOpenedEvent : ActivityEvent
{
    /// <summary>Creates a PR-opened event for the given pull request.</summary>
    public PrOpenedEvent(DeveloperLogin author, string projectName, DateTimeOffset occurredAt, int prNumber)
        : base(author, projectName, occurredAt)
    {
        PrNumber = prNumber;
    }

    /// <summary>The pull request number that was opened.</summary>
    public int PrNumber { get; }
}

/// <summary>
/// The full fetch result for one scope and period (data-model.md ActivityDataset). Cached by
/// <c>(ScopeKey, Period)</c> (FR-021); <see cref="LoadedAt"/> feeds the freshness line and rate-limit
/// banner (FR-011).
/// </summary>
public sealed record ActivityDataset
{
    /// <summary>Creates a dataset from its already-computed constituent parts.</summary>
    public ActivityDataset(
        IReadOnlyList<Developer> roster,
        IReadOnlyList<Project> projects,
        IReadOnlyList<Team> teams,
        IReadOnlyList<ActivityEvent> events,
        IReadOnlyList<int> weeklyCommitCounts,
        IReadOnlyList<string> coveredProjectNames,
        DateTimeOffset loadedAt,
        bool isDemoData)
    {
        Roster = roster ?? throw new ArgumentNullException(nameof(roster));
        Projects = projects ?? throw new ArgumentNullException(nameof(projects));
        Teams = teams ?? throw new ArgumentNullException(nameof(teams));
        Events = events ?? throw new ArgumentNullException(nameof(events));
        WeeklyCommitCounts = weeklyCommitCounts ?? throw new ArgumentNullException(nameof(weeklyCommitCounts));
        CoveredProjectNames = coveredProjectNames ?? throw new ArgumentNullException(nameof(coveredProjectNames));
        LoadedAt = loadedAt;
        IsDemoData = isDemoData;
    }

    /// <summary>The full member roster, including developers with no activity in the period (FR-012).</summary>
    public IReadOnlyList<Developer> Roster { get; }

    /// <summary>The projects covered by this fetch, ordered per the repo cap's recently-active rule (FR-007).</summary>
    public IReadOnlyList<Project> Projects { get; }

    /// <summary>The organisation's teams (FR-036).</summary>
    public IReadOnlyList<Team> Teams { get; }

    /// <summary>Every ingested activity fact for the scope and period.</summary>
    public IReadOnlyList<ActivityEvent> Events { get; }

    /// <summary>Weekly commit counts feeding the development trend, most recent last (up to <c>TrendWeeks</c>).</summary>
    public IReadOnlyList<int> WeeklyCommitCounts { get; }

    /// <summary>The names of the projects actually covered by this fetch (post repo-cap).</summary>
    public IReadOnlyList<string> CoveredProjectNames { get; }

    /// <summary>When this dataset was fetched; feeds the freshness line and rate-limit banner (FR-011).</summary>
    public DateTimeOffset LoadedAt { get; }

    /// <summary>True when this dataset came from the deterministic demo adapter rather than GitHub (FR-013).</summary>
    public bool IsDemoData { get; }
}
