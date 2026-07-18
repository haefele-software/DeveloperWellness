using DeveloperWellness.Domain.Model;

namespace DeveloperWellness.Application.Ports;

/// <summary>
/// The aggregated-only statistics passed to <see cref="IAiInsightService.SummariseAsync"/>. Carries
/// counts, shares, and flag reasons — never raw repository content such as commit messages, diffs, or
/// comment bodies (FR-022) — and is a purpose-built shape for the AI consumer rather than a mirror of
/// any entity. Every member is a primitive, a primitive collection, or <see cref="GroundingFlag"/>, so
/// the whole record serialises to compact JSON for the grounding payload sent to the model.
/// </summary>
public sealed record SummaryGrounding
{
    /// <summary>Creates a grounding payload from already-aggregated statistics.</summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="subjectDescriptor"/> or <paramref name="scopeLabel"/> is null, empty, or whitespace.
    /// </exception>
    public SummaryGrounding(
        string subjectDescriptor,
        string scopeLabel,
        int periodDays,
        int commitCount,
        int reviewCount,
        int commentCount,
        int prsOpenedCount,
        decimal? outOfHoursCommitShare,
        decimal? outOfHoursPrShare,
        int? distinctProjectCount,
        IReadOnlyList<GroundingFlag> flags,
        string? teamName = null,
        bool hasActivity = true)
    {
        if (string.IsNullOrWhiteSpace(subjectDescriptor))
        {
            throw new ArgumentException("Subject descriptor must not be empty.", nameof(subjectDescriptor));
        }

        if (string.IsNullOrWhiteSpace(scopeLabel))
        {
            throw new ArgumentException("Scope label must not be empty.", nameof(scopeLabel));
        }

        SubjectDescriptor = subjectDescriptor;
        ScopeLabel = scopeLabel;
        PeriodDays = periodDays;
        CommitCount = commitCount;
        ReviewCount = reviewCount;
        CommentCount = commentCount;
        PrsOpenedCount = prsOpenedCount;
        OutOfHoursCommitShare = outOfHoursCommitShare;
        OutOfHoursPrShare = outOfHoursPrShare;
        DistinctProjectCount = distinctProjectCount;
        Flags = flags ?? throw new ArgumentNullException(nameof(flags));
        TeamName = teamName;
        HasActivity = hasActivity;
    }

    /// <summary>A short human-readable name for the subject (e.g. a project name or a developer's display name).</summary>
    public string SubjectDescriptor { get; }

    /// <summary>A short human-readable label for the scope (e.g. "organisation" or a project name).</summary>
    public string ScopeLabel { get; }

    /// <summary>The period length in days (7, 14, or 30).</summary>
    public int PeriodDays { get; }

    /// <summary>Total commits in the period.</summary>
    public int CommitCount { get; }

    /// <summary>Total submitted reviews in the period.</summary>
    public int ReviewCount { get; }

    /// <summary>Total authored comments in the period.</summary>
    public int CommentCount { get; }

    /// <summary>Total pull requests opened in the period.</summary>
    public int PrsOpenedCount { get; }

    /// <summary>Author-local out-of-hours commit share; null when there were no commits.</summary>
    public decimal? OutOfHoursCommitShare { get; }

    /// <summary>Organisation-timezone out-of-hours PR-activity share; null when the PR-event sample was too small.</summary>
    public decimal? OutOfHoursPrShare { get; }

    /// <summary>Distinct active projects; only populated at organisation scope, null at project scope.</summary>
    public int? DistinctProjectCount { get; }

    /// <summary>The subject's current wellbeing flags, kind and reason only.</summary>
    public IReadOnlyList<GroundingFlag> Flags { get; }

    /// <summary>Optional roster-level context: the subject's team name, when relevant and known.</summary>
    public string? TeamName { get; }

    /// <summary>False when the subject has no activity at all in the period (the no-activity marker).</summary>
    public bool HasActivity { get; }
}

/// <summary>
/// A single wellbeing signal surfaced to the model: its kind and plain-language reason
/// (data-model.md WellbeingFlag), stripped of any other developer detail.
/// </summary>
public sealed record GroundingFlag(FlagKind Kind, string Reason);
