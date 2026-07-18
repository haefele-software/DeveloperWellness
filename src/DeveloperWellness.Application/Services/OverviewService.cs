using DeveloperWellness.Domain.Model;
using DeveloperWellness.Domain.Options;
using DeveloperWellness.Domain.Signals;
using Microsoft.Extensions.Options;

namespace DeveloperWellness.Application.Services;

/// <summary>
/// Composes the Pulse Overview landing snapshot for a scope and period (tasks.md T029; data-model.md
/// OrganisationOverview; FR-035..FR-039), bridging <see cref="CheckInService"/>'s already-fetched-and-
/// enriched dashboard snapshot and roster composition through the Overview's own KPI, project-row, and
/// scope-conditional Teams/trend/recommendations rules.
/// </summary>
/// <remarks>
/// Fetches through <see cref="CheckInService"/> alone — one fetch path, backed by
/// <see cref="DashboardQueryService"/>'s own scope-and-period cache — rather than also injecting
/// <see cref="DashboardQueryService"/> directly; a second consumer of that cache costs nothing once the
/// snapshot is warm. Registered scoped, matching <see cref="CheckInService"/> and
/// <see cref="DashboardQueryService"/>'s circuit lifetime.
/// </remarks>
/// <param name="checkInService">Fetches the dashboard snapshot and composes the check-in roster it is built from.</param>
/// <param name="alertService">Session-scoped seen-state behind the might-need-check-in KPI's new-since-viewed note (FR-030, FR-031). Never marked seen here — only the roster view does that.</param>
/// <param name="toneAnalysisService">Supplies the organisation-level sentiment reading (FR-039); consulted at organisation scope only.</param>
/// <param name="wellnessOptions">Wellness configuration: working hours, thresholds, and the trend window.</param>
public sealed class OverviewService(
    CheckInService checkInService,
    CheckInAlertService alertService,
    ToneAnalysisService toneAnalysisService,
    IOptions<WellnessOptions> wellnessOptions)
{
    private readonly CheckInService _checkInService = checkInService ?? throw new ArgumentNullException(nameof(checkInService));
    private readonly CheckInAlertService _alertService = alertService ?? throw new ArgumentNullException(nameof(alertService));
    private readonly ToneAnalysisService _toneAnalysisService = toneAnalysisService ?? throw new ArgumentNullException(nameof(toneAnalysisService));
    private readonly WellnessOptions _wellnessOptions = wellnessOptions is null
        ? throw new ArgumentNullException(nameof(wellnessOptions))
        : wellnessOptions.Value;

    /// <summary>
    /// Fetches the dashboard snapshot for <paramref name="scope"/> and <paramref name="periodDays"/> via
    /// <see cref="CheckInService"/> and composes the Overview snapshot from it. <see cref="OverviewResult.Overview"/>
    /// is null exactly when <see cref="OverviewResult.Source"/> carries no snapshot, mirroring
    /// <see cref="CheckInRosterResult.Composition"/>.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="scope"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="periodDays"/> is not 7, 14, or 30.</exception>
    public async Task<OverviewResult> GetAsync(ScopeKey scope, int periodDays, CancellationToken cancellationToken)
    {
        var rosterResult = await _checkInService.GetRosterAsync(scope, periodDays, cancellationToken).ConfigureAwait(false);

        if (rosterResult.Source.Snapshot is not { } snapshot || rosterResult.Composition is not { } composition)
        {
            return new OverviewResult(null, rosterResult.Source);
        }

        var overview = await BuildOverviewAsync(scope, periodDays, snapshot, composition, cancellationToken).ConfigureAwait(false);
        return new OverviewResult(overview, rosterResult.Source);
    }

    private async Task<OrganisationOverview> BuildOverviewAsync(
        ScopeKey scope, int periodDays, DashboardSnapshot snapshot, CheckInComposition composition, CancellationToken cancellationToken)
    {
        var dataset = snapshot.Dataset;
        var summaries = snapshot.Summaries;
        var isOrganisationScope = scope.Kind == ScopeKind.Organisation;
        var botLogins = dataset.Roster.Where(developer => developer.IsBot).Select(developer => developer.Login).ToHashSet();

        var flaggedRoster = composition.Roster.Where(status => status.NeedsCheckIn).ToList();
        var flaggedLogins = flaggedRoster.Select(status => status.Developer.Login).ToList();
        var newSinceViewed = _alertService.UnseenCount(scope, periodDays, flaggedLogins);

        var kpis = new OverviewKpis(
            MightNeedCheckIn: composition.NeedsCheckInCount,
            NewSinceViewed: newSinceViewed,
            AfterHoursCommitShare: ComputeAfterHoursCommitShare(dataset.Events, periodDays, botLogins),
            AfterHoursPrShare: ComputeAfterHoursPrShare(dataset.Events, periodDays, botLogins),
            ProjectsPerDeveloper: isOrganisationScope ? ComputeProjectsPerDeveloper(summaries) : null,
            Contributors: isOrganisationScope ? null : summaries.Count(summary => summary.HasActivity));

        var projectRows = BuildProjectRows(dataset, summaries, botLogins);

        var teams = isOrganisationScope
            ? TeamCardBuilder.Build(dataset.Teams, summaries, dataset.Events, _wellnessOptions, new Period(periodDays, snapshot.LoadedAt))
            : [];

        // The composed roster (not the raw summaries) is the mapping source: tone and possible-rushing
        // flags are layered in only by CheckInComposer, so they never reach ActivitySummary.Flags itself
        // (RecommendationMapper's remarks) — using the raw summaries here would silently drop anyone
        // flagged only by tone or only by possible rushing from "Recommendations for managers" (FR-037).
        var recommendations = RecommendationMapper.Map(flaggedRoster, dataset.Teams);

        var trend = isOrganisationScope
            ? TrendCalculator.Calculate(dataset.WeeklyCommitCounts, _wellnessOptions)
            : null;

        var sentiment = isOrganisationScope
            ? await GetOrganisationSentimentAsync(scope, periodDays, cancellationToken).ConfigureAwait(false)
            : new SentimentReading(0, 0, 0, Available: false);

        return new OrganisationOverview(kpis, projectRows, teams, recommendations, trend, sentiment);
    }

    /// <summary>
    /// The organisation-level review sentiment reading (FR-039): <see cref="ToneAggregation.Sentiment"/>
    /// from <see cref="ToneAnalysisService"/>'s aggregation, or the unavailable reading when tone signals
    /// could not be produced this call (AI unconfigured, unreachable, or nothing to aggregate).
    /// </summary>
    private async Task<SentimentReading> GetOrganisationSentimentAsync(ScopeKey scope, int periodDays, CancellationToken cancellationToken)
    {
        var tones = await _toneAnalysisService.GetTonesAsync(scope, periodDays, cancellationToken).ConfigureAwait(false);
        return tones.Aggregation?.Sentiment ?? new SentimentReading(0, 0, 0, Available: false);
    }

    /// <summary>Org-wide weighted out-of-hours commit share: total out-of-hours commits over total commits, bots excluded (FR-010); null when nobody committed.</summary>
    private decimal? ComputeAfterHoursCommitShare(
        IReadOnlyList<ActivityEvent> events, int periodDays, IReadOnlySet<DeveloperLogin> botLogins)
    {
        var results = OutOfHoursCommitCalculator.Calculate(events, _wellnessOptions, periodDays)
            .Where(pair => !botLogins.Contains(pair.Key))
            .Select(pair => pair.Value)
            .ToList();

        var totalCommits = results.Sum(result => result.TotalCommits);
        return totalCommits == 0 ? null : (decimal)results.Sum(result => result.OutOfHoursCommits) / totalCommits;
    }

    /// <summary>Org-wide weighted out-of-hours PR-activity share, same weighting as <see cref="ComputeAfterHoursCommitShare"/>; null when nobody had PR events.</summary>
    private decimal? ComputeAfterHoursPrShare(
        IReadOnlyList<ActivityEvent> events, int periodDays, IReadOnlySet<DeveloperLogin> botLogins)
    {
        var results = PrAfterHoursCalculator.Calculate(events, _wellnessOptions, periodDays)
            .Where(pair => !botLogins.Contains(pair.Key))
            .Select(pair => pair.Value)
            .ToList();

        var totalPrEvents = results.Sum(result => result.TotalPrEvents);
        return totalPrEvents == 0 ? null : (decimal)results.Sum(result => result.OutOfHoursPrEvents) / totalPrEvents;
    }

    /// <summary>The mean <see cref="ActivitySummary.DistinctProjectCount"/> across active summaries; null when none had activity or a distinct-project count (organisation scope only).</summary>
    private static decimal? ComputeProjectsPerDeveloper(IReadOnlyList<ActivitySummary> summaries)
    {
        var counts = summaries
            .Where(summary => summary.HasActivity && summary.DistinctProjectCount.HasValue)
            .Select(summary => summary.DistinctProjectCount!.Value)
            .ToList();

        return counts.Count == 0 ? null : (decimal)counts.Average();
    }

    /// <summary>
    /// One <see cref="ProjectRow"/> per covered project (data-model.md ProjectRow), including covered
    /// projects with zero activity as all-zero rows, ordered by <see cref="ProjectRow.Commits"/> descending
    /// (projects may be ranked; people never, design contract principle 1).
    /// </summary>
    private ProjectRow[] BuildProjectRows(
        ActivityDataset dataset, IReadOnlyList<ActivitySummary> summaries, IReadOnlySet<DeveloperLogin> botLogins)
    {
        var flaggedLogins = summaries.Where(summary => summary.Flags.Count > 0).Select(summary => summary.Developer.Login).ToHashSet();

        return dataset.CoveredProjectNames
            .Select(name => BuildProjectRow(name, dataset.Events, botLogins, flaggedLogins))
            .OrderByDescending(row => row.Commits)
            .ToArray();
    }

    private ProjectRow BuildProjectRow(
        string projectName,
        IReadOnlyList<ActivityEvent> events,
        IReadOnlySet<DeveloperLogin> botLogins,
        IReadOnlySet<DeveloperLogin> flaggedLogins)
    {
        var projectEvents = events
            .Where(activityEvent => string.Equals(activityEvent.ProjectName, projectName, StringComparison.Ordinal)
                && !botLogins.Contains(activityEvent.Author))
            .ToList();

        var commits = DeduplicatedCommits(projectEvents);
        var prsOpened = projectEvents.Count(activityEvent => activityEvent is PrOpenedEvent);
        var reviews = projectEvents.Count(activityEvent => activityEvent is ReviewEvent);
        var comments = projectEvents.Count(activityEvent => activityEvent is CommentEvent);

        var authors = projectEvents
            .Select(activityEvent => activityEvent.Author)
            .Where(author => !author.IsUnmatched)
            .ToHashSet();

        var flaggedHere = authors.Count(flaggedLogins.Contains);
        var signalNote = flaggedHere switch
        {
            0 => "quiet",
            1 => "1 person carries a signal here",
            _ => $"{flaggedHere} people carry a signal here",
        };

        return new ProjectRow(
            projectName, authors.Count, commits.Count, prsOpened, reviews, comments, ComputeCommitAfterHoursShare(commits), signalNote);
    }

    /// <summary>Commit events deduplicated by SHA within the given group (first occurrence wins), mirroring every other Domain calculator's dedup.</summary>
    private static List<CommitEvent> DeduplicatedCommits(IReadOnlyList<ActivityEvent> events)
    {
        var seenShas = new HashSet<string>();
        var commits = new List<CommitEvent>();

        foreach (var activityEvent in events)
        {
            if (activityEvent is CommitEvent commit && seenShas.Add(commit.Sha))
            {
                commits.Add(commit);
            }
        }

        return commits;
    }

    private decimal? ComputeCommitAfterHoursShare(IReadOnlyList<CommitEvent> commits)
    {
        if (commits.Count == 0)
        {
            return null;
        }

        var outOfHours = commits.Count(commit => OutOfHoursCommitCalculator.IsOutOfHours(commit, _wellnessOptions));
        return (decimal)outOfHours / commits.Count;
    }
}

/// <summary>
/// The outcome of an <see cref="OverviewService"/> fetch: the composed Overview snapshot (null when
/// <see cref="Source"/> carries no snapshot), and the underlying dashboard result so the Overview page can
/// bridge shell state and render stale/error surfaces exactly like every other page.
/// </summary>
public sealed record OverviewResult(OrganisationOverview? Overview, DashboardResult Source);
