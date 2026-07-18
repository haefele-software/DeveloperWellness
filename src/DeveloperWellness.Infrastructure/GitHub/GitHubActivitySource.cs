using DeveloperWellness.Application.Ports;
using DeveloperWellness.Domain.Model;
using DeveloperWellness.Domain.Options;
using Microsoft.Extensions.Options;

namespace DeveloperWellness.Infrastructure.GitHub;

/// <summary>
/// Live <see cref="IActivitySource"/> backed by the GitHub REST API via Octokit (research R2). Fetches the
/// organisation roster, teams, covered repositories, activity events, and weekly commit trend for a scope
/// and period, translating every Octokit and connectivity failure into a user-presentable
/// <see cref="ActivitySourceException"/> (FR-011) so no Octokit type ever crosses this port. Octokit types
/// are deliberately referenced with their full <c>Octokit.</c> prefix throughout this file rather than
/// via a blanket <c>using</c>, because <c>Octokit.Team</c> and <c>Octokit.Project</c> would otherwise
/// collide with the Domain model types of the same name.
/// </summary>
public sealed class GitHubActivitySource(IOptions<GitHubOptions> gitHubOptions, IOptions<WellnessOptions> wellnessOptions) : IActivitySource
{
    /// <summary>Page size used for every paged Octokit call; Octokit auto-follows pagination links up to this size per page.</summary>
    private const int PageSize = 100;

    /// <summary>
    /// Upper bound on repositories fetched concurrently. Reduced from 4 to 2 (rate-limit hardening): GitHub's
    /// secondary rate limiter penalises bursty concurrent REST traffic more heavily than raw request volume,
    /// so keeping fewer repository fetches in flight at once trades a modestly longer load time for a much
    /// lower chance of tripping it, on top of staying well under the 5000/hour PAT limit (research R2).
    /// </summary>
    private const int MaxConcurrentRepoFetches = 2;

    /// <summary>
    /// Hard page-count cap on the branch list itself (documented coverage bound, in the spirit of research
    /// R2's caps): one page of <see cref="PageSize"/> already returns 100 branches, comfortably more than
    /// the largest configured <c>BranchCap</c> (20 by default), so a second page could only ever contain
    /// branches beyond the cap this load would keep anyway.
    /// </summary>
    private const int BranchListPageCount = 1;

    /// <summary>
    /// Hard page-count cap on the commit list fetched per branch per load (documented coverage bound,
    /// research R2): <see cref="PageSize"/> times this is 300 commits, a generous per-branch ceiling for one
    /// wellness period that stops a single very active branch from unboundedly consuming the request budget.
    /// </summary>
    private const int CommitPageCountPerBranch = 3;

    /// <summary>
    /// Hard page-count cap on issue-comment and PR-review-comment lists fetched per repository per load
    /// (documented coverage bound, research R2): <see cref="PageSize"/> times this is 300 comments of each
    /// kind, per repository, per load.
    /// </summary>
    private const int CommentPageCount = 3;

    /// <summary>
    /// Hard page-count cap on the review list fetched per pull request (documented coverage bound): a single
    /// PR collecting more than <see cref="PageSize"/> reviews within one wellness period is not a realistic
    /// case worth paying for with unbounded pagination.
    /// </summary>
    private const int ReviewPageCountPerPr = 1;

    /// <summary>
    /// Conservative worst-case request budget checked before a load begins (preflight budget check): a full
    /// organisation load can issue several hundred requests at the caps above, so treat anything below this
    /// many remaining core requests as "not enough left to safely start a load" and fail fast with GitHub's
    /// own reset time instead of burning what little budget remains on a load that would fail partway
    /// through anyway.
    /// </summary>
    private const int MinRemainingRequestBudget = 300;

    private readonly object _clientLock = new();
    private Octokit.GitHubClient? _client;

    /// <inheritdoc />
    /// <remarks>
    /// Credentials are checked at call time rather than at start-up (data-model.md GitHubOptions is not
    /// start-up-validated): an unconfigured live source still lets the application boot, surfacing the
    /// shell's credentials-missing state on first load instead of crashing.
    /// </remarks>
    public async Task<ActivityDataset> GetActivityAsync(ScopeKey scope, Period period, CancellationToken cancellationToken)
    {
        var options = gitHubOptions.Value;

        if (string.IsNullOrWhiteSpace(options.Organisation) || string.IsNullOrWhiteSpace(options.Token))
        {
            throw new ActivitySourceException(
                "Pulse isn't connected to GitHub yet. Add GitHub:Organisation and GitHub:Token to configuration, then re-check.",
                innerException: null,
                ActivitySourceFailureKind.CredentialsMissing);
        }

        var client = GetOrCreateClient(options.Token);

        // Preflight budget check: fails fast on GitHub's own reset time before spending any of the
        // remaining budget on a load that would almost certainly run out of requests partway through.
        // Best effort by design (see TryGetExhaustedBudgetRetryAfterAsync's remarks): a failed preflight
        // call never blocks a load that might otherwise have succeeded.
        if (await TryGetExhaustedBudgetRetryAfterAsync(client, cancellationToken).ConfigureAwait(false) is { } exhaustedUntil)
        {
            throw new ActivitySourceException(
                "Pulse has hit GitHub's rate limit. It'll keep showing the data it already has and retry automatically shortly.",
                innerException: null,
                ActivitySourceFailureKind.RateLimited,
                exhaustedUntil);
        }

        try
        {
            return await FetchDatasetAsync(client, options.Organisation, scope, period, wellnessOptions.Value, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Octokit.RateLimitExceededException ex)
        {
            throw new ActivitySourceException(
                "Pulse has hit GitHub's rate limit. It'll keep showing the data it already has and retry automatically shortly.",
                ex,
                ActivitySourceFailureKind.RateLimited,
                ex.Reset);
        }
        catch (Octokit.SecondaryRateLimitExceededException ex)
        {
            throw new ActivitySourceException(
                "Pulse has hit GitHub's rate limit. It'll keep showing the data it already has and retry automatically shortly.",
                ex,
                ActivitySourceFailureKind.RateLimited,
                // Octokit 14's SecondaryRateLimitExceededException carries no reset or retry-after value.
                // GitHub's own secondary-limit guidance is to wait "at least a few minutes", so a fixed
                // conservative window stands in for a real reset time here.
                DateTimeOffset.UtcNow.AddMinutes(5));
        }
        catch (Octokit.AbuseException ex)
        {
            throw new ActivitySourceException(
                "Pulse has hit GitHub's rate limit. It'll keep showing the data it already has and retry automatically shortly.",
                ex,
                ActivitySourceFailureKind.RateLimited,
                ex.RetryAfterSeconds is { } retryAfterSeconds
                    ? DateTimeOffset.UtcNow.AddSeconds(retryAfterSeconds)
                    : DateTimeOffset.UtcNow.AddMinutes(5));
        }
        catch (Octokit.AuthorizationException ex)
        {
            throw new ActivitySourceException(
                "GitHub rejected Pulse's credentials. Check GitHub:Token in configuration, then re-check.",
                ex,
                ActivitySourceFailureKind.CredentialsMissing);
        }
        catch (Octokit.ForbiddenException ex)
        {
            throw new ActivitySourceException(
                "GitHub rejected Pulse's credentials, or the token is missing a required permission. Check GitHub:Token, then re-check.",
                ex,
                ActivitySourceFailureKind.CredentialsMissing);
        }
        catch (Octokit.ApiException ex)
        {
            throw new ActivitySourceException(
                "Pulse couldn't reach GitHub right now. Try again shortly.", ex, ActivitySourceFailureKind.Unavailable);
        }
        catch (HttpRequestException ex)
        {
            throw new ActivitySourceException(
                "Pulse couldn't reach GitHub right now. Try again shortly.", ex, ActivitySourceFailureKind.Unavailable);
        }
    }

    /// <summary>
    /// Preflight budget check: reads the core REST resource's remaining requests and reset time, returning
    /// that reset time when remaining is below <see cref="MinRemainingRequestBudget"/> (the load should not
    /// start), or null when there is enough budget, or when the preflight call itself fails — deliberately
    /// best effort, since this check exists only to save quota, not to gate a load that might otherwise have
    /// worked.
    /// </summary>
    private static async Task<DateTimeOffset?> TryGetExhaustedBudgetRetryAfterAsync(
        Octokit.GitHubClient client, CancellationToken cancellationToken)
    {
        Octokit.MiscellaneousRateLimit rateLimits;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            rateLimits = await client.RateLimit.GetRateLimits().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }

        var core = rateLimits.Resources.Core;
        return core.Remaining < MinRemainingRequestBudget ? core.Reset : null;
    }

    /// <summary>Returns the cached client, creating it under a lock on first use (thread-safe reuse across calls).</summary>
    private Octokit.GitHubClient GetOrCreateClient(string token)
    {
        if (_client is { } existing)
        {
            return existing;
        }

        lock (_clientLock)
        {
            return _client ??= new Octokit.GitHubClient(new Octokit.ProductHeaderValue("DeveloperWellness-Pulse"))
            {
                Credentials = new Octokit.Credentials(token),
            };
        }
    }

    /// <summary>Assembles the full dataset for one scope and period (research R2, data-model.md ActivityDataset).</summary>
    private static async Task<ActivityDataset> FetchDatasetAsync(
        Octokit.GitHubClient client,
        string organisation,
        ScopeKey scope,
        Period period,
        WellnessOptions options,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var members = await client.Organization.Member.GetAll(organisation, new Octokit.ApiOptions { PageSize = PageSize })
            .ConfigureAwait(false);
        var roster = members.Select(MapDeveloper).ToList();

        cancellationToken.ThrowIfCancellationRequested();
        var teams = await FetchTeamsAsync(client, organisation, cancellationToken).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();
        var repositories = await FetchCoveredRepositoriesAsync(client, organisation, scope, options.RepoCap, cancellationToken)
            .ConfigureAwait(false);
        var projects = repositories.Select(MapProject).ToList();
        var coveredProjectNames = projects.Select(p => p.Name).ToList();

        cancellationToken.ThrowIfCancellationRequested();
        var repoResults = await FetchAllRepoActivityAsync(client, organisation, repositories, period, options, cancellationToken)
            .ConfigureAwait(false);

        var events = DeduplicateCommits(repoResults.SelectMany(r => r.Events));
        var weeklyCommitCounts = SumWeeklyParticipation(repoResults.Select(r => r.WeeklyParticipation), options.TrendWeeks);

        return new ActivityDataset(
            roster: roster,
            projects: projects,
            teams: teams,
            events: events,
            weeklyCommitCounts: weeklyCommitCounts,
            coveredProjectNames: coveredProjectNames,
            loadedAt: DateTimeOffset.UtcNow,
            isDemoData: false);
    }

    // ---------------------------------------------------------------------------------------------
    // Teams (FR-036): raw fetch (I/O) is kept separate from the first-alphabetical assignment rule
    // (pure), so the assignment rule is unit-testable in isolation from the network.
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Fetches every organisation team and its raw member list, then applies the first-team-alphabetical
    /// assignment rule. A <see cref="Octokit.ForbiddenException"/> (missing <c>read:org</c>) degrades
    /// quietly to zero teams rather than failing the whole load; a rate-limit failure still propagates,
    /// since silently returning zero teams on a throttled request would be misleading.
    /// </summary>
    private static async Task<IReadOnlyList<Team>> FetchTeamsAsync(
        Octokit.GitHubClient client, string organisation, CancellationToken cancellationToken)
    {
        IReadOnlyList<Octokit.Team> rawTeams;
        try
        {
            rawTeams = await client.Organization.Team.GetAll(organisation, new Octokit.ApiOptions { PageSize = PageSize })
                .ConfigureAwait(false);
        }
        catch (Octokit.RateLimitExceededException)
        {
            throw;
        }
        catch (Octokit.SecondaryRateLimitExceededException)
        {
            throw;
        }
        catch (Octokit.AbuseException)
        {
            throw;
        }
        catch (Octokit.ForbiddenException)
        {
            return [];
        }

        var rawTeamMembers = new List<(string Name, IReadOnlyList<string> MemberLogins)>(rawTeams.Count);

        foreach (var rawTeam in rawTeams)
        {
            cancellationToken.ThrowIfCancellationRequested();

            IReadOnlyList<Octokit.User> members;
            try
            {
                members = await client.Organization.Team.GetAllMembers(rawTeam.Id, new Octokit.ApiOptions { PageSize = PageSize })
                    .ConfigureAwait(false);
            }
            catch (Octokit.RateLimitExceededException)
            {
                throw;
            }
            catch (Octokit.SecondaryRateLimitExceededException)
            {
                throw;
            }
            catch (Octokit.AbuseException)
            {
                throw;
            }
            catch (Octokit.ForbiddenException)
            {
                // This specific team's membership isn't visible; skip it rather than failing the whole load.
                continue;
            }

            rawTeamMembers.Add((rawTeam.Name, members.Select(m => m.Login).ToList()));
        }

        return AssignFirstTeamAlphabetically(rawTeamMembers);
    }

    /// <summary>
    /// Applies the first-team-alphabetical assignment rule (FR-036, data-model.md Team): teams are
    /// ordered by name, and each member counts under the first team (in that order) that claims their
    /// login; later teams no longer list that member. Developers on no team are simply absent from every
    /// resulting <see cref="Team.Members"/> list, derived downstream by comparing against the roster.
    /// </summary>
    internal static IReadOnlyList<Team> AssignFirstTeamAlphabetically(
        IReadOnlyList<(string Name, IReadOnlyList<string> MemberLogins)> rawTeams)
    {
        var sortedTeams = rawTeams.OrderBy(t => t.Name, StringComparer.Ordinal).ToList();
        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<Team>(sortedTeams.Count);

        foreach (var (name, memberLogins) in sortedTeams)
        {
            var firstClaim = new List<DeveloperLogin>();

            foreach (var login in memberLogins)
            {
                if (claimed.Add(login))
                {
                    firstClaim.Add(new DeveloperLogin(login));
                }
            }

            result.Add(new Team(name, firstClaim));
        }

        return result;
    }

    // ---------------------------------------------------------------------------------------------
    // Repositories (FR-007)
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// At project scope, fetches exactly the named repository, throwing a friendly
    /// <see cref="ActivitySourceException"/> when it does not exist in the organisation. At organisation
    /// scope, fetches every org repository and takes the top <paramref name="repoCap"/> by push recency.
    /// </summary>
    private static async Task<IReadOnlyList<Octokit.Repository>> FetchCoveredRepositoriesAsync(
        Octokit.GitHubClient client, string organisation, ScopeKey scope, int repoCap, CancellationToken cancellationToken)
    {
        if (scope.Kind == ScopeKind.Project)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var repository = await client.Repository.Get(organisation, scope.ProjectName!).ConfigureAwait(false);
                return [repository];
            }
            catch (Octokit.NotFoundException ex)
            {
                throw new ActivitySourceException(
                    $"Pulse can't find a repository named '{scope.ProjectName}' in the '{organisation}' organisation on GitHub.",
                    ex,
                    ActivitySourceFailureKind.Unavailable);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        var repositories = await client.Repository.GetAllForOrg(organisation, new Octokit.ApiOptions { PageSize = PageSize })
            .ConfigureAwait(false);

        return SelectMostRecentlyPushed(repositories, repoCap);
    }

    /// <summary>Orders repositories by push recency, most recent first, and takes the top <paramref name="repoCap"/> (FR-007).</summary>
    internal static IReadOnlyList<Octokit.Repository> SelectMostRecentlyPushed(
        IReadOnlyList<Octokit.Repository> repositories, int repoCap) =>
        repositories
            .OrderByDescending(r => r.PushedAt ?? DateTimeOffset.MinValue)
            .Take(repoCap)
            .ToList();

    /// <summary>Maps a fetched repository to the Domain <see cref="Project"/> shape.</summary>
    internal static Project MapProject(Octokit.Repository repository) =>
        new(repository.Name, repository.PushedAt ?? DateTimeOffset.UtcNow);

    // ---------------------------------------------------------------------------------------------
    // Per-repository activity: commits, PRs/reviews, comments, weekly participation.
    // ---------------------------------------------------------------------------------------------

    /// <summary>The events and weekly participation series fetched for a single repository.</summary>
    private readonly record struct RepoActivityResult(IReadOnlyList<ActivityEvent> Events, IReadOnlyList<int> WeeklyParticipation);

    /// <summary>Fetches every covered repository's activity concurrently, bounded by <see cref="MaxConcurrentRepoFetches"/>.</summary>
    private static async Task<IReadOnlyList<RepoActivityResult>> FetchAllRepoActivityAsync(
        Octokit.GitHubClient client,
        string organisation,
        IReadOnlyList<Octokit.Repository> repositories,
        Period period,
        WellnessOptions options,
        CancellationToken cancellationToken)
    {
        using var gate = new SemaphoreSlim(MaxConcurrentRepoFetches);

        async Task<RepoActivityResult> FetchOneAsync(Octokit.Repository repository)
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await FetchRepoActivityAsync(
                        client, organisation, repository.Name, repository.DefaultBranch, period, options, cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                gate.Release();
            }
        }

        var tasks = repositories.Select(FetchOneAsync).ToList();
        return await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    /// <summary>Fetches one repository's commits, PR/review/PR-opened events, comments, and weekly participation.</summary>
    private static async Task<RepoActivityResult> FetchRepoActivityAsync(
        Octokit.GitHubClient client,
        string organisation,
        string repositoryName,
        string? defaultBranchName,
        Period period,
        WellnessOptions options,
        CancellationToken cancellationToken)
    {
        var events = new List<ActivityEvent>();

        events.AddRange(
            await FetchCommitEventsAsync(client, organisation, repositoryName, defaultBranchName, period, options.BranchCap, cancellationToken)
                .ConfigureAwait(false));

        var pullRequests = await FetchPullRequestsUpdatedInPeriodAsync(client, organisation, repositoryName, period, cancellationToken)
            .ConfigureAwait(false);
        events.AddRange(
            await FetchPullRequestEventsAsync(client, organisation, repositoryName, pullRequests, period, cancellationToken)
                .ConfigureAwait(false));

        events.AddRange(await FetchCommentEventsAsync(client, organisation, repositoryName, period, cancellationToken).ConfigureAwait(false));

        var weeklyParticipation = await FetchWeeklyParticipationAsync(client, organisation, repositoryName, cancellationToken)
            .ConfigureAwait(false);

        return new RepoActivityResult(events, weeklyParticipation);
    }

    /// <summary>
    /// Lists up to <paramref name="branchCap"/> branches and every commit on each since
    /// <paramref name="period"/>'s start, deduplicated by SHA within the repository (FR-002). The
    /// repository's default branch, when known, is moved to the front of the branch list before the cap is
    /// applied via <see cref="OrderDefaultBranchFirst"/> (rate-limit hardening): it carries the shared
    /// mainline history every other branch forks from, so fetching it first lets the branch-head skip below
    /// apply to the largest possible number of feature branches. Beyond the default branch, Octokit's branch
    /// list carries no update-recency ordering, so the remaining branches, in the order GitHub returns them,
    /// stand in for "most recently updated first" — a documented approximation (research R2), not a precise
    /// recency sort.
    /// </summary>
    private static async Task<IReadOnlyList<ActivityEvent>> FetchCommitEventsAsync(
        Octokit.GitHubClient client,
        string organisation,
        string repositoryName,
        string? defaultBranchName,
        Period period,
        int branchCap,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var branches = await client.Repository.Branch.GetAll(
                organisation, repositoryName, new Octokit.ApiOptions { PageSize = PageSize, PageCount = BranchListPageCount })
            .ConfigureAwait(false);

        var orderedBranches = OrderDefaultBranchFirst(branches, defaultBranchName).Take(branchCap);

        var events = new List<ActivityEvent>();
        var seenShas = new HashSet<string>(StringComparer.Ordinal);

        foreach (var branch in orderedBranches)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Branch-head skip (rate-limit hardening, documented invariant): a branch's commit history is
            // an immutable DAG walked backwards from its head. If this head SHA was already collected while
            // walking an earlier branch (typically the default branch, fetched first per the ordering
            // above), then everything reachable from this head inside the period window is necessarily a
            // subset of what that earlier branch's listing already returned — so the per-branch commit-list
            // call for this branch is skipped outright rather than re-downloading shared ancestry.
            if (seenShas.Contains(branch.Commit.Sha))
            {
                continue;
            }

            var commitRequest = new Octokit.CommitRequest { Since = period.Start, Sha = branch.Name };
            var commits = await client.Repository.Commit.GetAll(
                    organisation, repositoryName, commitRequest, new Octokit.ApiOptions { PageSize = PageSize, PageCount = CommitPageCountPerBranch })
                .ConfigureAwait(false);

            foreach (var commit in commits)
            {
                if (!seenShas.Add(commit.Sha))
                {
                    continue;
                }

                var occurredAt = commit.Commit.Author.Date;
                if (occurredAt < period.Start || occurredAt > period.End)
                {
                    continue;
                }

                events.Add(new CommitEvent(
                    AuthorFrom(commit.Author?.Login),
                    repositoryName,
                    occurredAt,
                    commit.Sha,
                    // GitHub's commit author date always carries a genuine author-local UTC offset in the
                    // REST response (FR-005); there is no "offset absent" case to fall back from here.
                    hasUsableOffset: true));
            }
        }

        return events;
    }

    /// <summary>
    /// Moves the repository's default branch to the front of <paramref name="branches"/>, if present,
    /// leaving every other branch in GitHub's original order (rate-limit hardening): the shared mainline is
    /// walked first so its commits populate the caller's seen-SHA set before any feature branch is
    /// considered, maximising how often a feature branch's already-covered head SHA lets its own listing be
    /// skipped outright. A no-op when <paramref name="defaultBranchName"/> is null/blank or matches no
    /// fetched branch.
    /// </summary>
    internal static IReadOnlyList<Octokit.Branch> OrderDefaultBranchFirst(
        IReadOnlyList<Octokit.Branch> branches, string? defaultBranchName)
    {
        if (string.IsNullOrWhiteSpace(defaultBranchName))
        {
            return branches;
        }

        var defaultBranch = branches.FirstOrDefault(b => string.Equals(b.Name, defaultBranchName, StringComparison.Ordinal));
        if (defaultBranch is null)
        {
            return branches;
        }

        return [defaultBranch, .. branches.Where(b => !ReferenceEquals(b, defaultBranch))];
    }

    /// <summary>
    /// Pages pull requests newest-updated-first, stopping as soon as a page contains an item whose
    /// <c>updated_at</c> falls before <paramref name="period"/>'s start, so repositories with long PR
    /// history are not walked in full (research R2).
    /// </summary>
    private static async Task<IReadOnlyList<Octokit.PullRequest>> FetchPullRequestsUpdatedInPeriodAsync(
        Octokit.GitHubClient client, string organisation, string repositoryName, Period period, CancellationToken cancellationToken)
    {
        var request = new Octokit.PullRequestRequest
        {
            State = Octokit.ItemStateFilter.All,
            SortProperty = Octokit.PullRequestSort.Updated,
            SortDirection = Octokit.SortDirection.Descending,
        };

        var result = new List<Octokit.PullRequest>();
        var page = 1;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var pageOptions = new Octokit.ApiOptions { PageSize = PageSize, StartPage = page, PageCount = 1 };
            var batch = await client.PullRequest.GetAllForRepository(organisation, repositoryName, request, pageOptions)
                .ConfigureAwait(false);

            if (batch.Count == 0)
            {
                break;
            }

            var reachedStale = false;
            foreach (var pullRequest in batch)
            {
                if (pullRequest.UpdatedAt < period.Start)
                {
                    reachedStale = true;
                    break;
                }

                result.Add(pullRequest);
            }

            if (reachedStale || batch.Count < PageSize)
            {
                break;
            }

            page++;
        }

        return result;
    }

    /// <summary>
    /// Builds <see cref="PrOpenedEvent"/>s for PRs opened within the period (FR-024) and
    /// <see cref="ReviewEvent"/>s for every submitted review within the period (FR-003, FR-027).
    /// </summary>
    private static async Task<IReadOnlyList<ActivityEvent>> FetchPullRequestEventsAsync(
        Octokit.GitHubClient client,
        string organisation,
        string repositoryName,
        IReadOnlyList<Octokit.PullRequest> pullRequests,
        Period period,
        CancellationToken cancellationToken)
    {
        var events = new List<ActivityEvent>();

        foreach (var pullRequest in pullRequests)
        {
            if (pullRequest.CreatedAt >= period.Start && pullRequest.CreatedAt <= period.End)
            {
                events.Add(new PrOpenedEvent(AuthorFrom(pullRequest.User?.Login), repositoryName, pullRequest.CreatedAt, pullRequest.Number));
            }

            cancellationToken.ThrowIfCancellationRequested();
            var reviews = await client.PullRequest.Review.GetAll(
                    organisation, repositoryName, pullRequest.Number, new Octokit.ApiOptions { PageSize = PageSize, PageCount = ReviewPageCountPerPr })
                .ConfigureAwait(false);

            foreach (var review in reviews)
            {
                if (review.SubmittedAt < period.Start || review.SubmittedAt > period.End)
                {
                    continue;
                }

                if (!review.State.TryParse(out Octokit.PullRequestReviewState rawState))
                {
                    continue; // an unrecognised state string; skip rather than guess.
                }

                var state = MapReviewState(rawState);
                if (state is null)
                {
                    continue;
                }

                events.Add(new ReviewEvent(AuthorFrom(review.User?.Login), repositoryName, review.SubmittedAt, pullRequest.Number, state.Value));
            }
        }

        return events;
    }

    /// <summary>
    /// Maps a submitted review's raw state to <see cref="ReviewState"/>. A dismissed review still
    /// represented real submitted feedback at the time, so it folds into <see cref="ReviewState.Commented"/>
    /// rather than being dropped; a pending review was never actually submitted (no meaningful
    /// <c>SubmittedAt</c>), so it maps to <c>null</c> and the caller skips it entirely.
    /// </summary>
    internal static ReviewState? MapReviewState(Octokit.PullRequestReviewState rawState) => rawState switch
    {
        Octokit.PullRequestReviewState.Approved => ReviewState.Approved,
        Octokit.PullRequestReviewState.ChangesRequested => ReviewState.ChangesRequested,
        Octokit.PullRequestReviewState.Commented => ReviewState.Commented,
        Octokit.PullRequestReviewState.Dismissed => ReviewState.Commented,
        Octokit.PullRequestReviewState.Pending => null,
        _ => ReviewState.Commented,
    };

    /// <summary>
    /// Fetches issue comments and PR review comments updated since the period start (FR-004), filtering
    /// to <see cref="Octokit.IssueComment.CreatedAt"/> / <see cref="Octokit.PullRequestReviewComment.CreatedAt"/>
    /// within the period explicitly, because GitHub's <c>since</c> parameter matches on <c>updated_at</c>,
    /// not <c>created_at</c> — an old comment edited recently would otherwise be miscounted as new activity.
    /// </summary>
    private static async Task<IReadOnlyList<ActivityEvent>> FetchCommentEventsAsync(
        Octokit.GitHubClient client, string organisation, string repositoryName, Period period, CancellationToken cancellationToken)
    {
        var events = new List<ActivityEvent>();

        cancellationToken.ThrowIfCancellationRequested();
        var issueComments = await client.Issue.Comment.GetAllForRepository(
                organisation,
                repositoryName,
                new Octokit.IssueCommentRequest { Since = period.Start },
                new Octokit.ApiOptions { PageSize = PageSize, PageCount = CommentPageCount })
            .ConfigureAwait(false);

        foreach (var comment in issueComments)
        {
            if (comment.CreatedAt < period.Start || comment.CreatedAt > period.End)
            {
                continue;
            }

            events.Add(new CommentEvent(AuthorFrom(comment.User?.Login), repositoryName, comment.CreatedAt, comment.Id, comment.Body));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var reviewComments = await client.PullRequest.ReviewComment.GetAllForRepository(
                organisation,
                repositoryName,
                new Octokit.PullRequestReviewCommentRequest { Since = period.Start },
                new Octokit.ApiOptions { PageSize = PageSize, PageCount = CommentPageCount })
            .ConfigureAwait(false);

        foreach (var comment in reviewComments)
        {
            if (comment.CreatedAt < period.Start || comment.CreatedAt > period.End)
            {
                continue;
            }

            events.Add(new CommentEvent(AuthorFrom(comment.User?.Login), repositoryName, comment.CreatedAt, comment.Id, comment.Body));
        }

        return events;
    }

    /// <summary>
    /// Fetches the repository's 52-week participation series (FR-038). The statistics endpoint can
    /// return a still-computing or unavailable result for a repository; that repository's contribution
    /// is skipped rather than failing the whole load. Rate-limit failures still propagate.
    /// </summary>
    private static async Task<IReadOnlyList<int>> FetchWeeklyParticipationAsync(
        Octokit.GitHubClient client, string organisation, string repositoryName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var participation = await client.Repository.Statistics.GetParticipation(organisation, repositoryName, cancellationToken)
                .ConfigureAwait(false);
            return participation?.All ?? [];
        }
        catch (Octokit.RateLimitExceededException)
        {
            throw;
        }
        catch (Octokit.SecondaryRateLimitExceededException)
        {
            throw;
        }
        catch (Octokit.AbuseException)
        {
            throw;
        }
        catch (Octokit.ApiException)
        {
            return [];
        }
    }

    /// <summary>
    /// Sums each covered repository's weekly participation series element-wise, aligned at the most
    /// recent week, and returns the last <paramref name="trendWeeks"/> entries (FR-038). A repository
    /// with fewer weeks of history than <paramref name="trendWeeks"/> contributes zero to the missing
    /// leading weeks rather than misaligning the sum.
    /// </summary>
    internal static IReadOnlyList<int> SumWeeklyParticipation(IEnumerable<IReadOnlyList<int>> perRepositorySeries, int trendWeeks)
    {
        var totals = new int[trendWeeks];

        foreach (var series in perRepositorySeries)
        {
            var offset = series.Count - trendWeeks; // aligns this series' most recent week with totals[^1]
            for (var i = 0; i < trendWeeks; i++)
            {
                var sourceIndex = offset + i;
                if (sourceIndex >= 0 && sourceIndex < series.Count)
                {
                    totals[i] += series[sourceIndex];
                }
            }
        }

        return totals;
    }

    // ---------------------------------------------------------------------------------------------
    // Cross-cutting mapping helpers.
    // ---------------------------------------------------------------------------------------------

    /// <summary>Maps a fetched organisation member to the Domain <see cref="Developer"/> shape (FR-012).</summary>
    internal static Developer MapDeveloper(Octokit.User user) =>
        new(new DeveloperLogin(user.Login), user.Name, IsBotAccount(user));

    /// <summary>True when the account type is <see cref="Octokit.AccountType.Bot"/> or the login carries GitHub's <c>[bot]</c> suffix (FR-010).</summary>
    internal static bool IsBotAccount(Octokit.User user) =>
        user.Type is Octokit.AccountType.Bot || user.Login.EndsWith("[bot]", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Resolves an event author's login, or <see cref="DeveloperLogin.Unmatched"/> when GitHub could not
    /// associate any account with the event (e.g. a commit whose email matches no GitHub user).
    /// </summary>
    internal static DeveloperLogin AuthorFrom(string? login) =>
        string.IsNullOrWhiteSpace(login) ? DeveloperLogin.Unmatched : new DeveloperLogin(login);

    /// <summary>
    /// Keeps the first occurrence of each <see cref="CommitEvent.Sha"/> across the whole dataset
    /// (data-model.md ActivityDataset validation: "CommitEvent.Sha unique per dataset"). Per-repository
    /// dedup already happens in <see cref="FetchCommitEventsAsync"/> because the same commit can appear
    /// on multiple branches; this final pass covers the rare case of the same SHA across two repositories.
    /// </summary>
    internal static IReadOnlyList<ActivityEvent> DeduplicateCommits(IEnumerable<ActivityEvent> events)
    {
        var seenShas = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<ActivityEvent>();

        foreach (var activityEvent in events)
        {
            if (activityEvent is CommitEvent commit && !seenShas.Add(commit.Sha))
            {
                continue;
            }

            result.Add(activityEvent);
        }

        return result;
    }
}
