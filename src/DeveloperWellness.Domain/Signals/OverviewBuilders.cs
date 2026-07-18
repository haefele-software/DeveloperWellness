using DeveloperWellness.Domain.Model;
using DeveloperWellness.Domain.Options;

namespace DeveloperWellness.Domain.Signals;

/// <summary>
/// Maps flagged developers onto the Overview's "Recommendations for managers" list: up to six supportive
/// suggestions, each derived from one developer's leading wellbeing signal (data-model.md Recommendation;
/// FR-037; ui-design.md 4.1). Pure, synchronous, and reference-free like every Domain signal calculator.
/// </summary>
/// <remarks>
/// "Leading signal" is simply the first entry in the developer's flag list — <see cref="ActivitySummary.Flags"/>
/// for the <see cref="Map(IReadOnlyList{ActivitySummary}, IReadOnlyList{Team})"/> overload, or
/// <see cref="CheckInStatus.Flags"/> for the <see cref="Map(IReadOnlyList{CheckInStatus}, IReadOnlyList{Team})"/>
/// overload. This mapper never reorders or ranks flags itself; it trusts whatever order it is given. In
/// the running application that order is always the same stable sequence — out-of-hours commits, then
/// spread-thin, then out-of-hours PR activity (appended onto a summary's own flags by the dashboard query
/// service's enrichment step), then tone, then possible rushing (appended after those when the check-in
/// composer builds a <see cref="CheckInStatus"/>) — so "leading" in practice means "earliest-detected in
/// that fixed order", never "most severe". Callers are responsible for that ordering; this mapper only
/// reads it.
/// <para>
/// The two overloads exist because tone and possible-rushing flags never reach
/// <see cref="ActivitySummary.Flags"/> itself — they are computed separately (a tone aggregation and a
/// direct rushing calculation) and only merged in once the check-in roster is composed. A caller building
/// "Recommendations for managers" (FR-037) should prefer the <see cref="CheckInStatus"/> overload, since
/// FR-026 counts tone and possible-rushing as wellbeing flags too: without it, a developer flagged only by
/// one of those two signals would never appear in the recommendations list.
/// </para>
/// </remarks>
public static class RecommendationMapper
{
    /// <summary>The maximum number of recommendations the Overview ever shows (FR-037, design contract 4.1).</summary>
    public const int MaxRecommendations = 6;

    private const string NoTeamName = "No team";

    /// <summary>
    /// Builds up to <see cref="MaxRecommendations"/> recommendations from every flagged developer in
    /// <paramref name="summaries"/>, ordered by concurrent-signal count descending then
    /// <see cref="Developer.DisplayName"/> alphabetically (FR-037). Developers with no active flag are
    /// never included — there is no ranking of unflagged people (design contract principle 1). Each
    /// recommendation's <see cref="Recommendation.Reason"/> is the first sentence of every one of that
    /// developer's flag reasons, in flag order, joined with a single space (data-model.md: "first
    /// sentences of flag reasons").
    /// </summary>
    /// <param name="summaries">Every summarised developer for the selected scope and period.</param>
    /// <param name="teams">The organisation's teams, used to resolve each recommended developer's team name.</param>
    /// <exception cref="ArgumentNullException"><paramref name="summaries"/> or <paramref name="teams"/> is null.</exception>
    public static IReadOnlyList<Recommendation> Map(IReadOnlyList<ActivitySummary> summaries, IReadOnlyList<Team> teams)
    {
        ArgumentNullException.ThrowIfNull(summaries);
        ArgumentNullException.ThrowIfNull(teams);

        return MapCore(summaries.Select(summary => (summary.Developer, summary.Flags)), teams);
    }

    /// <summary>
    /// Overload for the composed check-in roster (FR-026, FR-037): identical ordering and mapping rules to
    /// the <see cref="ActivitySummary"/> overload above, but reads each developer's flags from
    /// <see cref="CheckInStatus.Flags"/> — the merged list that also carries tone and rushing flags (see
    /// the type remarks). Prefer this overload wherever a composed roster is already in hand, so nobody
    /// flagged only by tone or only by possible rushing goes missing from the recommendations list.
    /// </summary>
    /// <param name="roster">The composed check-in roster for the selected scope and period.</param>
    /// <param name="teams">The organisation's teams, used to resolve each recommended developer's team name.</param>
    /// <exception cref="ArgumentNullException"><paramref name="roster"/> or <paramref name="teams"/> is null.</exception>
    public static IReadOnlyList<Recommendation> Map(IReadOnlyList<CheckInStatus> roster, IReadOnlyList<Team> teams)
    {
        ArgumentNullException.ThrowIfNull(roster);
        ArgumentNullException.ThrowIfNull(teams);

        return MapCore(roster.Select(status => (status.Developer, status.Flags)), teams);
    }

    /// <summary>
    /// Shared ordering and mapping core for both <see cref="Map(IReadOnlyList{ActivitySummary}, IReadOnlyList{Team})"/>
    /// and <see cref="Map(IReadOnlyList{CheckInStatus}, IReadOnlyList{Team})"/>: filters to developers with
    /// at least one flag, orders by concurrent-signal count descending then <see cref="Developer.DisplayName"/>
    /// alphabetically, and caps at <see cref="MaxRecommendations"/> (FR-037).
    /// </summary>
    private static IReadOnlyList<Recommendation> MapCore(
        IEnumerable<(Developer Developer, IReadOnlyList<WellbeingFlag> Flags)> entries, IReadOnlyList<Team> teams) =>
        entries
            .Where(entry => entry.Flags.Count > 0)
            .OrderByDescending(entry => entry.Flags.Count)
            .ThenBy(entry => entry.Developer.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Take(MaxRecommendations)
            .Select(entry => BuildRecommendation(entry.Developer, entry.Flags, teams))
            .ToList();

    /// <summary>
    /// Extracts the first sentence of <paramref name="reason"/>: everything up to and including the first
    /// '.', or the whole (trimmed) text when it carries no '.'. Exposed publicly so it can be verified as
    /// its own unit (data-model.md: "first sentences of flag reasons").
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="reason"/> is null.</exception>
    public static string ExtractFirstSentence(string reason)
    {
        ArgumentNullException.ThrowIfNull(reason);

        var trimmed = reason.Trim();
        var periodIndex = trimmed.IndexOf('.');
        return periodIndex < 0 ? trimmed : trimmed[..(periodIndex + 1)];
    }

    private static Recommendation BuildRecommendation(Developer developer, IReadOnlyList<WellbeingFlag> flags, IReadOnlyList<Team> teams)
    {
        var leadingFlag = flags[0];
        var action = MapAction(leadingFlag.Kind);
        var reason = string.Join(" ", flags.Select(flag => ExtractFirstSentence(flag.Reason)));
        var teamName = FindTeamName(developer.Login, teams);

        return new Recommendation(developer, teamName, action, reason);
    }

    /// <summary>Maps a leading flag kind onto its exact, design-specified supportive action text (FR-037).</summary>
    private static string MapAction(FlagKind kind) => kind switch
    {
        FlagKind.OverworkCommits => "Encourage real time off",
        FlagKind.OverworkPrActivity => "Nudge reviews back into the day",
        FlagKind.SpreadThin => "Rebalance project load",
        FlagKind.NegativeTone => "Check in on review climate",
        FlagKind.PossibleRushing => "Ease the pace pressure",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown flag kind."),
    };

    /// <summary>The name of the first team (in the given order) containing <paramref name="login"/>, else "No team".</summary>
    private static string FindTeamName(DeveloperLogin login, IReadOnlyList<Team> teams)
    {
        foreach (var team in teams)
        {
            if (team.Members.Contains(login))
            {
                return team.Name;
            }
        }

        return NoTeamName;
    }
}

/// <summary>
/// Builds the Overview's development trend from up to <see cref="WellnessOptions.TrendWeeks"/> weeks of
/// organisation-level commit counts (data-model.md DevelopmentTrend; FR-038; ui-design.md 4.1). Pure,
/// synchronous, and reference-free like every Domain signal calculator.
/// </summary>
/// <remarks>
/// The change statement compares the average of the trimmed window's first quarter against its last
/// quarter, rather than just its first and last week, so a single unusually quiet or busy week cannot
/// swing the whole statement — FR-038's "plain-language change statement" needs to be a stable reading,
/// not noise. The quarter size is always at least one week, even for very short windows, so the
/// comparison always has something on each side. The trend statement always ends with a fixed note that
/// the series is weekly relative commits (design contract 4.1: "the note that the series is weekly
/// relative commits"), so the UI stays a dumb renderer of this one string.
/// </remarks>
public static class TrendCalculator
{
    /// <summary>Absolute percent change at or below this magnitude reads as "steady" rather than up or down.</summary>
    private const int FlatThresholdPercent = 5;

    /// <summary>Percent rise above this magnitude adds the steep-ramp caution sentence (FR-038).</summary>
    private const int SteepRampThresholdPercent = 25;

    private const string NotEnoughHistoryStatement = "Not enough history for a trend yet.";

    private const string SteepRampCaution =
        " A ramp this steep is itself worth watching — sustained sprints often precede burnout.";

    private const string WeeklyRelativeNote = " Series shows weekly relative commits.";

    /// <summary>
    /// Builds the development trend from <paramref name="weeklyCommitCounts"/>, trimmed to the most recent
    /// <see cref="WellnessOptions.TrendWeeks"/> entries when longer. The series is assumed oldest first —
    /// index 0 is the oldest week, the last index is most recent — matching
    /// <see cref="ActivityDataset.WeeklyCommitCounts"/>'s documented order ("most recent last"). An empty
    /// or all-zero series produces "Not enough history for a trend yet." and returns
    /// <paramref name="weeklyCommitCounts"/> exactly as given, untrimmed, since there is no meaningful
    /// window to show.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="weeklyCommitCounts"/> or <paramref name="options"/> is null.</exception>
    public static DevelopmentTrend Calculate(IReadOnlyList<int> weeklyCommitCounts, WellnessOptions options)
    {
        ArgumentNullException.ThrowIfNull(weeklyCommitCounts);
        ArgumentNullException.ThrowIfNull(options);

        if (weeklyCommitCounts.Count == 0 || weeklyCommitCounts.All(count => count == 0))
        {
            return new DevelopmentTrend(weeklyCommitCounts, NotEnoughHistoryStatement);
        }

        var window = Trim(weeklyCommitCounts, options.TrendWeeks);
        var statement = BuildChangeStatement(window);

        return new DevelopmentTrend(window, statement);
    }

    /// <summary>Keeps only the most recent <paramref name="trendWeeks"/> entries of an oldest-first series.</summary>
    private static IReadOnlyList<int> Trim(IReadOnlyList<int> series, int trendWeeks) =>
        series.Count > trendWeeks ? series.Skip(series.Count - trendWeeks).ToList() : series;

    private static string BuildChangeStatement(IReadOnlyList<int> window)
    {
        var quarterSize = Math.Max(1, window.Count / 4);
        var firstQuarterAverage = window.Take(quarterSize).Average();
        var lastQuarterAverage = window.Skip(window.Count - quarterSize).Average();
        var percentChange = (int)Math.Round(
            ComputePercentChange(firstQuarterAverage, lastQuarterAverage), MidpointRounding.AwayFromZero);

        if (Math.Abs(percentChange) <= FlatThresholdPercent)
        {
            return $"Commit activity is steady across the window.{WeeklyRelativeNote}";
        }

        if (percentChange > FlatThresholdPercent)
        {
            var caution = percentChange > SteepRampThresholdPercent ? SteepRampCaution : string.Empty;
            return $"Commit activity is up ~{percentChange}% across the window.{caution}{WeeklyRelativeNote}";
        }

        return $"Commit activity is down ~{Math.Abs(percentChange)}% across the window.{WeeklyRelativeNote}";
    }

    /// <summary>
    /// The percent change from <paramref name="firstAverage"/> to <paramref name="lastAverage"/>. A
    /// zero-or-negative baseline cannot support a ratio, so it is treated as flat when the later average
    /// is also zero-or-negative, or as a full swing (100) when activity appeared from nothing — a
    /// defensive choice this domain rule needs but the spec does not otherwise define.
    /// </summary>
    private static double ComputePercentChange(double firstAverage, double lastAverage)
    {
        if (firstAverage <= 0)
        {
            return lastAverage <= 0 ? 0 : 100;
        }

        return (lastAverage - firstAverage) / firstAverage * 100;
    }
}

/// <summary>
/// Builds the Overview's Teams-section cards: one per organisation team plus a "No team" card when any
/// summarised developer belongs to none (data-model.md TeamCard, Team's no-team group; FR-036;
/// ui-design.md 4.1). Pure, synchronous, and reference-free like every Domain signal calculator.
/// </summary>
/// <remarks>
/// Team cards are ordered alphabetically by <see cref="Team.Name"/> (data-model.md); the "No team" card,
/// not being a real team, is appended after every named card rather than sorted among them. Commit events
/// are defensively deduplicated by SHA per card, mirroring every other Domain calculator that touches
/// <see cref="CommitEvent"/> directly (e.g. <see cref="OutOfHoursCommitCalculator"/>). Bot exclusion needs
/// no special handling here because <c>summaries</c> (built by <see cref="ActivityAggregator"/>) already
/// excludes bots entirely, and <see cref="DeveloperLogin.Unmatched"/> can never equal a real team member's
/// login, so unmatched-author events are naturally excluded once commits are filtered to team membership.
/// </remarks>
public static class TeamCardBuilder
{
    private const string NoTeamName = "No team";
    private const int MaxTopFlagged = 3;

    /// <summary>
    /// Builds one <see cref="TeamCard"/> per team in <paramref name="teams"/>, ordered by
    /// <see cref="Team.Name"/>, plus a trailing "No team" card when at least one developer in
    /// <paramref name="summaries"/> belongs to none of <paramref name="teams"/>.
    /// </summary>
    /// <param name="teams">The organisation's teams (FR-036).</param>
    /// <param name="summaries">Every summarised developer for the selected scope and period.</param>
    /// <param name="events">
    /// Every activity event for the same scope and period, used for the weekly commit sparkline and the
    /// after-hours share; non-commit events are only relevant through <see cref="ActivitySummary.ReviewCount"/>
    /// already folded into <paramref name="summaries"/>.
    /// </param>
    /// <param name="options">Wellness configuration: working hours and working days, used by the after-hours share.</param>
    /// <param name="period">
    /// The selected period. <see cref="Period.Days"/> drives the sparkline's bucket count
    /// (<c>ceil(Days / 7)</c>: 7&#8594;1, 14&#8594;2, 30&#8594;5 buckets) and <see cref="Period.End"/> anchors
    /// the most recent 7-day bucket.
    /// </param>
    /// <exception cref="ArgumentNullException">Any reference parameter is null.</exception>
    public static IReadOnlyList<TeamCard> Build(
        IReadOnlyList<Team> teams,
        IReadOnlyList<ActivitySummary> summaries,
        IReadOnlyList<ActivityEvent> events,
        WellnessOptions options,
        Period period)
    {
        ArgumentNullException.ThrowIfNull(teams);
        ArgumentNullException.ThrowIfNull(summaries);
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(options);

        var bucketCount = CeilingDivide(period.Days, 7);
        var summaryByLogin = summaries.ToDictionary(summary => summary.Developer.Login);
        var teamLogins = teams.SelectMany(team => team.Members).ToHashSet();

        var cards = teams
            .OrderBy(team => team.Name, StringComparer.OrdinalIgnoreCase)
            .Select(team => BuildCard(team.Name, team.Members.ToHashSet(), summaryByLogin, events, options, bucketCount, period.End))
            .ToList();

        var noTeamLogins = summaries
            .Select(summary => summary.Developer.Login)
            .Where(login => !teamLogins.Contains(login))
            .ToHashSet();

        if (noTeamLogins.Count > 0)
        {
            cards.Add(BuildCard(NoTeamName, noTeamLogins, summaryByLogin, events, options, bucketCount, period.End));
        }

        return cards;
    }

    private static TeamCard BuildCard(
        string name,
        IReadOnlySet<DeveloperLogin> memberLogins,
        IReadOnlyDictionary<DeveloperLogin, ActivitySummary> summaryByLogin,
        IReadOnlyList<ActivityEvent> events,
        WellnessOptions options,
        int bucketCount,
        DateTimeOffset periodEnd)
    {
        var memberSummaries = memberLogins
            .Select(login => summaryByLogin.GetValueOrDefault(login))
            .OfType<ActivitySummary>()
            .ToList();

        var teamCommits = DeduplicatedTeamCommits(events, memberLogins);
        var weeklySeries = BuildWeeklySeries(teamCommits, bucketCount, periodEnd);
        var afterHoursShare = ComputeAfterHoursShare(teamCommits, options);
        var avgProjectsInFlight = ComputeAverageProjectsInFlight(memberSummaries);
        var avgReviewsPerDev = memberSummaries.Count == 0
            ? null
            : (decimal?)memberSummaries.Average(summary => summary.ReviewCount);
        var topFlagged = memberSummaries
            .Where(summary => summary.Flags.Count >= 1)
            .OrderByDescending(summary => summary.Flags.Count)
            .ThenBy(summary => summary.Developer.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Take(MaxTopFlagged)
            .Select(summary => new TopFlaggedMember(summary.Developer, summary.Flags.Count))
            .ToList();

        return new TeamCard(
            name, memberSummaries.Count, weeklySeries, afterHoursShare, avgProjectsInFlight, avgReviewsPerDev, topFlagged);
    }

    /// <summary>Commit events authored by a team member, deduplicated by SHA (first occurrence wins).</summary>
    private static IReadOnlyList<CommitEvent> DeduplicatedTeamCommits(
        IReadOnlyList<ActivityEvent> events, IReadOnlySet<DeveloperLogin> memberLogins)
    {
        var seenShas = new HashSet<string>();
        var commits = new List<CommitEvent>();

        foreach (var activityEvent in events)
        {
            if (activityEvent is not CommitEvent commit || !memberLogins.Contains(commit.Author))
            {
                continue;
            }

            if (seenShas.Add(commit.Sha))
            {
                commits.Add(commit);
            }
        }

        return commits;
    }

    /// <summary>
    /// Buckets <paramref name="commits"/> into <paramref name="bucketCount"/> calendar weeks, oldest first,
    /// as consecutive 7-day windows ending at <paramref name="periodEnd"/> (design contract 4.1:
    /// "fine-grained enough for a sparkline"). Each window is start-inclusive, end-exclusive, matching every
    /// other boundary rule in this codebase.
    /// </summary>
    private static IReadOnlyList<int> BuildWeeklySeries(
        IReadOnlyList<CommitEvent> commits, int bucketCount, DateTimeOffset periodEnd)
    {
        var buckets = new int[bucketCount];

        foreach (var commit in commits)
        {
            var index = BucketIndex(commit.OccurredAt, bucketCount, periodEnd);
            if (index >= 0)
            {
                buckets[index]++;
            }
        }

        return buckets;
    }

    /// <summary>The oldest-first bucket index containing <paramref name="occurredAt"/>, or -1 when it falls outside every bucket.</summary>
    private static int BucketIndex(DateTimeOffset occurredAt, int bucketCount, DateTimeOffset periodEnd)
    {
        for (var mostRecentFirstIndex = 0; mostRecentFirstIndex < bucketCount; mostRecentFirstIndex++)
        {
            var bucketEnd = periodEnd.AddDays(-7 * mostRecentFirstIndex);
            var bucketStart = bucketEnd.AddDays(-7);

            if (occurredAt >= bucketStart && occurredAt < bucketEnd)
            {
                return bucketCount - 1 - mostRecentFirstIndex;
            }
        }

        return -1;
    }

    private static decimal? ComputeAfterHoursShare(IReadOnlyList<CommitEvent> teamCommits, WellnessOptions options)
    {
        if (teamCommits.Count == 0)
        {
            return null;
        }

        var outOfHoursCount = teamCommits.Count(commit => OutOfHoursCommitCalculator.IsOutOfHours(commit, options));
        return (decimal)outOfHoursCount / teamCommits.Count;
    }

    private static decimal? ComputeAverageProjectsInFlight(IReadOnlyList<ActivitySummary> memberSummaries)
    {
        var projectCounts = memberSummaries
            .Select(summary => summary.DistinctProjectCount)
            .Where(count => count.HasValue)
            .Select(count => count!.Value)
            .ToList();

        return projectCounts.Count == 0 ? null : (decimal)projectCounts.Average();
    }

    private static int CeilingDivide(int numerator, int denominator) => (numerator + denominator - 1) / denominator;
}
