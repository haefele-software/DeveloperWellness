namespace DeveloperWellness.Domain.Model;

/// <summary>
/// Per-developer, per-scope, per-period computed activity summary (data-model.md ActivitySummary).
/// Built by <see cref="Signals.ActivityAggregator"/> from an <see cref="ActivityDataset"/> and
/// progressively enriched by later signal calculators as their tasks land: <see cref="OutOfHoursCommitShare"/>
/// (T016/T018), <see cref="OutOfHoursPrShare"/> (T031/T032), and <see cref="DistinctProjectCount"/> plus
/// <see cref="Flags"/> (T018/T020/T021/T032) all start out null or empty. Carrying the full shape up
/// front means downstream consumers never need a type change as those calculators land.
/// </summary>
public sealed record ActivitySummary
{
    /// <summary>Creates a summary from already-computed counts, shares, and flags.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="developer"/> or <paramref name="flags"/> is null.</exception>
    public ActivitySummary(
        Developer developer,
        int commitCount,
        int reviewCount,
        int commentCount,
        int prsOpenedCount,
        decimal? outOfHoursCommitShare,
        decimal? outOfHoursPrShare,
        int? distinctProjectCount,
        IReadOnlyList<WellbeingFlag> flags,
        bool hasActivity)
    {
        Developer = developer ?? throw new ArgumentNullException(nameof(developer));
        CommitCount = commitCount;
        ReviewCount = reviewCount;
        CommentCount = commentCount;
        PrsOpenedCount = prsOpenedCount;
        OutOfHoursCommitShare = outOfHoursCommitShare;
        OutOfHoursPrShare = outOfHoursPrShare;
        DistinctProjectCount = distinctProjectCount;
        Flags = flags ?? throw new ArgumentNullException(nameof(flags));
        HasActivity = hasActivity;
    }

    /// <summary>The developer this summary describes.</summary>
    public Developer Developer { get; }

    /// <summary>Total commits in the period, deduplicated by SHA (FR-002).</summary>
    public int CommitCount { get; }

    /// <summary>Total submitted reviews in the period; each submission counts on its own (FR-003).</summary>
    public int ReviewCount { get; }

    /// <summary>Total authored comments in the period.</summary>
    public int CommentCount { get; }

    /// <summary>Total pull requests opened in the period (FR-024).</summary>
    public int PrsOpenedCount { get; }

    /// <summary>Author-local out-of-hours commit share; null when there were no commits. Filled by T016/T018.</summary>
    public decimal? OutOfHoursCommitShare { get; }

    /// <summary>Organisation-timezone out-of-hours PR-activity share; null below the PR-event guard. Filled by T031/T032.</summary>
    public decimal? OutOfHoursPrShare { get; }

    /// <summary>Distinct active projects; organisation scope only, otherwise null. Filled by T020/T021.</summary>
    public int? DistinctProjectCount { get; }

    /// <summary>The developer's current wellbeing flags (FR-026); empty until signal calculators land.</summary>
    public IReadOnlyList<WellbeingFlag> Flags { get; }

    /// <summary>False places this developer in the no-activity group (FR-012).</summary>
    public bool HasActivity { get; }
}
