using DeveloperWellness.Application.Ports;
using DeveloperWellness.Domain.Model;
using DeveloperWellness.Domain.Options;
using DeveloperWellness.Domain.Signals;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace DeveloperWellness.Application.Services;

/// <summary>
/// Discriminates why an <see cref="AiSummaryResult"/> does or does not carry a summary (contracts/
/// application-ports.md AiSummaryService; FR-014, FR-015..FR-017). The <c>AiSummaryPanel</c> component
/// renders one of its five surfaces directly from this value rather than catching an exception.
/// </summary>
public enum AiSummaryState
{
    /// <summary><see cref="AiSummaryResult.Summary"/> carries a freshly generated or session-cached summary.</summary>
    Ready,

    /// <summary>The AI service is unconfigured; no request was attempted (FR-014).</summary>
    Unavailable,

    /// <summary>The subject has no recorded activity in this period; no request was attempted rather than speculating.</summary>
    NoActivity,

    /// <summary>The dashboard data needed to ground a request, or the AI request itself, failed or timed out (SC-005).</summary>
    Failed,
}

/// <summary>
/// The outcome of an <see cref="AiSummaryService.GetSummaryAsync"/> call: exactly one of a ready summary,
/// or <see cref="State"/> explaining why there is not one, plus an optional diagnostic message for
/// <see cref="AiSummaryState.Failed"/> (the panel itself renders its own fixed, design-contract copy for
/// every state rather than this message, per ui-design.md 4.4/4.5).
/// </summary>
public sealed record AiSummaryResult(AiSummary? Summary, AiSummaryState State, string? Error);

/// <summary>
/// Builds a <see cref="SummaryGrounding"/> from already-computed aggregates only — commit, review,
/// comment, and PR-open counts, out-of-hours shares, distinct-project counts, and flag reasons; never
/// comment bodies or any other raw repository content (FR-022) — and requests an AI-generated summary for
/// a project or developer subject, session-caching successful results so an unchanged scope, period, and
/// subject are served from cache rather than regenerated (FR-021, contracts/application-ports.md
/// AiSummaryService).
/// </summary>
/// <remarks>
/// Registered scoped, matching <see cref="DashboardQueryService"/>'s circuit lifetime; reads that
/// service's own cached snapshot rather than fetching a second copy of the same data.
/// </remarks>
/// <param name="queryService">Fetches (cache permitting) the enriched dashboard snapshot grounding is built from.</param>
/// <param name="aiInsightService">Generates the summary text itself once grounding is built; also the source of <see cref="IsAvailable"/>.</param>
/// <param name="cache">Backs the per-subject, scope, and period session cache (FR-021).</param>
/// <param name="wellnessOptions">Working-hours and working-day configuration used to compute project-level out-of-hours shares.</param>
public sealed class AiSummaryService(
    DashboardQueryService queryService,
    IAiInsightService aiInsightService,
    IMemoryCache cache,
    IOptions<WellnessOptions> wellnessOptions)
{
    private static readonly TimeSpan CacheSlidingExpiration = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan SummaryTimeout = TimeSpan.FromSeconds(10);

    private const string DashboardUnavailableFallbackMessage = "The dashboard data needed for a summary isn't available right now.";
    private const string TimeoutMessage = "The summary request took longer than 10 seconds and was cancelled.";
    private const string SubjectNotFoundMessage = "This developer isn't on the roster for this scope.";

    private readonly DashboardQueryService _queryService = queryService ?? throw new ArgumentNullException(nameof(queryService));
    private readonly IAiInsightService _aiInsightService = aiInsightService ?? throw new ArgumentNullException(nameof(aiInsightService));
    private readonly IMemoryCache _cache = cache ?? throw new ArgumentNullException(nameof(cache));
    private readonly WellnessOptions _wellnessOptions = wellnessOptions is null
        ? throw new ArgumentNullException(nameof(wellnessOptions))
        : wellnessOptions.Value;

    /// <summary>False when the underlying AI service is unconfigured (FR-014); no request is ever attempted while false.</summary>
    public bool IsAvailable => _aiInsightService.IsAvailable;

    /// <summary>
    /// Returns the session-cached summary for this exact subject, scope, and period when one exists and
    /// <paramref name="refresh"/> is false; otherwise builds grounding from the current dashboard snapshot
    /// and requests a fresh summary, honouring a 10-second timeout (SC-005) and caching the outcome only
    /// when it comes back <see cref="AiSummaryState.Ready"/> (FR-021). <paramref name="refresh"/> evicts
    /// any cached entry first but never forces a fresh dashboard fetch — a "Refresh" click asks for a new
    /// AI reading of the currently displayed data, not a new GitHub fetch.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="subject"/> or <paramref name="scope"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="periodDays"/> is not 7, 14, or 30.</exception>
    public async Task<AiSummaryResult> GetSummaryAsync(
        AiSubject subject, ScopeKey scope, int periodDays, bool refresh, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(scope);

        if (!IsAvailable)
        {
            return new AiSummaryResult(null, AiSummaryState.Unavailable, null);
        }

        var cacheKey = BuildCacheKey(subject, scope, periodDays);

        if (refresh)
        {
            _cache.Remove(cacheKey);
        }
        else if (_cache.TryGetValue(cacheKey, out AiSummaryResult? cached) && cached is not null)
        {
            return cached;
        }

        var dashboardResult = await _queryService.GetAsync(scope, periodDays, cancellationToken).ConfigureAwait(false);
        if (dashboardResult.Snapshot is not { } snapshot)
        {
            return new AiSummaryResult(null, AiSummaryState.Failed, dashboardResult.ErrorMessage ?? DashboardUnavailableFallbackMessage);
        }

        var grounding = BuildGrounding(subject, scope, periodDays, snapshot);
        if (grounding is null)
        {
            return new AiSummaryResult(null, AiSummaryState.Failed, SubjectNotFoundMessage);
        }

        if (!grounding.HasActivity)
        {
            return new AiSummaryResult(null, AiSummaryState.NoActivity, null);
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(SummaryTimeout);

        AiSummary summary;
        try
        {
            summary = await _aiInsightService.SummariseAsync(subject, grounding, timeoutCts.Token).ConfigureAwait(false);
        }
        catch (AiInsightException ex)
        {
            return new AiSummaryResult(null, AiSummaryState.Failed, ex.Message);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The linked token's own timeout fired, not the caller's cancellation (FoundryAiInsightService
            // mirrors this exact guard for the same reason: distinguish "we gave up" from "the caller did").
            return new AiSummaryResult(null, AiSummaryState.Failed, TimeoutMessage);
        }

        var result = new AiSummaryResult(summary, AiSummaryState.Ready, null);
        _cache.Set(cacheKey, result, new MemoryCacheEntryOptions { SlidingExpiration = CacheSlidingExpiration });
        return result;
    }

    /// <summary>Builds grounding for either subject shape; null only when a developer subject is not on this scope's roster.</summary>
    private SummaryGrounding? BuildGrounding(AiSubject subject, ScopeKey scope, int periodDays, DashboardSnapshot snapshot) =>
        subject.Kind == AiSubjectKind.Project
            ? BuildProjectGrounding(subject, scope, periodDays, snapshot)
            : BuildDeveloperGrounding(subject, scope, periodDays, snapshot);

    /// <summary>
    /// Project grounding: event totals and out-of-hours shares recomputed directly from the (already
    /// project-scoped) dataset events, bots excluded (FR-010); flags are the union of every active
    /// contributor's flags, one reason per distinct <see cref="FlagKind"/> (data-model.md WellbeingFlag is
    /// per-developer; a project has no flags of its own). <see cref="SummaryGrounding.DistinctProjectCount"/>
    /// stays null — the record carries no dedicated contributor-count field, and distinct-project-count is
    /// a per-developer, organisation-scope-only measure that does not apply to a project subject.
    /// </summary>
    private SummaryGrounding BuildProjectGrounding(AiSubject subject, ScopeKey scope, int periodDays, DashboardSnapshot snapshot)
    {
        var projectName = subject.ProjectName!;
        var botLogins = snapshot.Dataset.Roster.Where(developer => developer.IsBot).Select(developer => developer.Login).ToHashSet();

        var projectEvents = snapshot.Dataset.Events
            .Where(activityEvent =>
                string.Equals(activityEvent.ProjectName, projectName, StringComparison.Ordinal) &&
                !botLogins.Contains(activityEvent.Author))
            .ToList();

        var commits = DeduplicatedCommits(projectEvents);
        var prEvents = projectEvents.Where(activityEvent => activityEvent is PrOpenedEvent or ReviewEvent).ToList();

        var flags = snapshot.Summaries
            .Where(summary => summary.HasActivity)
            .SelectMany(summary => summary.Flags)
            .GroupBy(flag => flag.Kind)
            .Select(group => new GroundingFlag(group.Key, group.First().Reason))
            .ToList();

        return new SummaryGrounding(
            subjectDescriptor: projectName,
            scopeLabel: ScopeLabel(scope),
            periodDays: periodDays,
            commitCount: commits.Count,
            reviewCount: projectEvents.Count(activityEvent => activityEvent is ReviewEvent),
            commentCount: projectEvents.Count(activityEvent => activityEvent is CommentEvent),
            prsOpenedCount: projectEvents.Count(activityEvent => activityEvent is PrOpenedEvent),
            outOfHoursCommitShare: commits.Count == 0
                ? null
                : (decimal)commits.Count(commit => OutOfHoursCommitCalculator.IsOutOfHours(commit, _wellnessOptions)) / commits.Count,
            outOfHoursPrShare: prEvents.Count == 0
                ? null
                : (decimal)prEvents.Count(prEvent => PrAfterHoursCalculator.IsOutOfHours(prEvent, _wellnessOptions)) / prEvents.Count,
            distinctProjectCount: null,
            flags: flags,
            teamName: null,
            hasActivity: projectEvents.Count > 0);
    }

    /// <summary>
    /// Developer grounding: every field copied straight from that developer's already-enriched
    /// <see cref="ActivitySummary"/> (FR-022 — the same aggregates the page itself renders, nothing more).
    /// Returns null when <paramref name="subject"/>'s login is not on this scope's roster at all (defensive;
    /// the page itself already guards against this before ever rendering the panel).
    /// </summary>
    private static SummaryGrounding? BuildDeveloperGrounding(AiSubject subject, ScopeKey scope, int periodDays, DashboardSnapshot snapshot)
    {
        var login = subject.Login!.Value;
        var summary = snapshot.Summaries.FirstOrDefault(candidate => candidate.Developer.Login == login);
        if (summary is null)
        {
            return null;
        }

        var teamName = snapshot.Dataset.Teams.FirstOrDefault(team => team.Members.Contains(summary.Developer.Login))?.Name;
        var flags = summary.Flags.Select(flag => new GroundingFlag(flag.Kind, flag.Reason)).ToList();

        return new SummaryGrounding(
            subjectDescriptor: $"{summary.Developer.DisplayName} ({summary.Developer.Login.Value})",
            scopeLabel: ScopeLabel(scope),
            periodDays: periodDays,
            commitCount: summary.CommitCount,
            reviewCount: summary.ReviewCount,
            commentCount: summary.CommentCount,
            prsOpenedCount: summary.PrsOpenedCount,
            outOfHoursCommitShare: summary.OutOfHoursCommitShare,
            outOfHoursPrShare: summary.OutOfHoursPrShare,
            distinctProjectCount: summary.DistinctProjectCount,
            flags: flags,
            teamName: teamName,
            hasActivity: summary.HasActivity);
    }

    /// <summary>Commit events deduplicated by SHA (first occurrence wins), mirroring every other Domain calculator's dedup.</summary>
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

    /// <summary>"organisation", or the project name verbatim — the exact convention <c>FoundryAiInsightService</c> and <c>DemoAiInsightService</c> both reconstruct a <see cref="ScopeKey"/> from.</summary>
    private static string ScopeLabel(ScopeKey scope) => scope.Kind == ScopeKind.Organisation ? "organisation" : scope.ProjectName!;

    private static object BuildCacheKey(AiSubject subject, ScopeKey scope, int periodDays) => ("aisummary", subject, scope, periodDays);
}
