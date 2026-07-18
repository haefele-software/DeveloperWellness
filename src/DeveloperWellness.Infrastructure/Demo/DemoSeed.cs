using DeveloperWellness.Domain.Model;

namespace DeveloperWellness.Infrastructure.Demo;

/// <summary>
/// Hand-authored, fixed-seed dataset builder backing <see cref="DemoActivitySource"/> (FR-013, research
/// R5). Every roster member, team, project, and event below is fictitious (no real person's name, login,
/// or repository appears anywhere in this file). Timestamps are generated relative to
/// <see cref="Period.End"/> — never anchored to a fixed calendar date — so the same seeded wellbeing
/// cases hold no matter when the demo is run or which of the 7/14/30 day periods is selected.
/// </summary>
/// <remarks>
/// Every event's <see cref="DateTimeOffset"/> carries a fixed +02:00 offset (<see cref="OrgOffset"/>),
/// used both as the organisation reference timezone (South Africa Standard Time) and as every seeded
/// developer's author-local offset. Collapsing the two bases onto one constant offset is a deliberate
/// simplification for a synthetic dataset: it lets every hour-of-day and day-of-week check read directly
/// off <see cref="ActivityEvent.OccurredAt"/> without a timezone conversion step, in this class and in
/// tests that verify it.
/// </remarks>
internal static class DemoSeed
{
    /// <summary>
    /// The fixed UTC offset used for every demo timestamp (South Africa Standard Time, UTC+02:00).
    /// </summary>
    private static readonly TimeSpan OrgOffset = TimeSpan.FromHours(2);

    // ---------------------------------------------------------------------------------------------
    // Roster logins. Every login is a clearly invented handle; every case comment below names the
    // exact seeded rule the developer exists to trip (task T008 requirements 1-10).
    // ---------------------------------------------------------------------------------------------

    private static readonly DeveloperLogin Nova = new("nova-stardust-demo");       // seeded: overwork (commits)
    private static readonly DeveloperLogin Flynn = new("flynn-circuitry-demo");    // ordinary
    private static readonly DeveloperLogin Wren = new("wren-ironforge-demo");      // ordinary
    private static readonly DeveloperLogin Dex = new("dex-quietstorm-demo");       // seeded: no activity

    private static readonly DeveloperLogin Remy = new("remy-afterglow-demo");      // seeded: overwork (PR activity)
    private static readonly DeveloperLogin Blair = new("blair-pixelrun-demo");     // ordinary
    private static readonly DeveloperLogin Sasha = new("sasha-lumen-demo");        // ordinary
    private static readonly DeveloperLogin Koda = new("koda-driftwood-demo");      // seeded: no activity

    private static readonly DeveloperLogin Juniper = new("juniper-dataforge-demo"); // seeded: spread thin
    private static readonly DeveloperLogin Marlowe = new("marlowe-critique-demo");  // seeded: frustrated commenter
    private static readonly DeveloperLogin Sable = new("sable-querywise-demo");     // ordinary; seeded: steady quality vs quantity (sufficient PR sample, not flagged)
    private static readonly DeveloperLogin Finch = new("finch-parallax-demo");      // ordinary
    private static readonly DeveloperLogin Opal = new("opal-vectorsong-demo");      // ordinary

    private static readonly DeveloperLogin River = new("river-hurrybrook-demo");   // seeded: possible rushing
    private static readonly DeveloperLogin Teagan = new("teagan-testcraft-demo");  // ordinary (reviews River's PRs)
    private static readonly DeveloperLogin Holt = new("holt-checksum-demo");       // ordinary (reviews River's PRs)
    private static readonly DeveloperLogin Brynn = new("brynn-edgecase-demo");     // seeded: no activity
    private static readonly DeveloperLogin Avery = new("avery-stackframe-demo");   // ordinary (reviews River's PRs)

    private static readonly DeveloperLogin PulseBot = new("pulsebot-ci-demo");     // seeded: bot (IsBot = true)
    private static readonly DeveloperLogin Lark = new("lark-tooling-demo");        // ordinary
    private static readonly DeveloperLogin Shay = new("shay-buildpipe-demo");      // ordinary
    private static readonly DeveloperLogin Indigo = new("indigo-scriptwell-demo"); // ordinary

    private static readonly DeveloperLogin Zephyr = new("zephyr-freelance-demo");  // ordinary, seeded: no team

    // ---------------------------------------------------------------------------------------------
    // Project names.
    // ---------------------------------------------------------------------------------------------

    private const string PulseApi = "pulse-api-demo";
    private const string PulseWeb = "pulse-web-demo";
    private const string AuroraMobile = "aurora-mobile-demo";
    private const string BeaconDataPipeline = "beacon-data-pipeline-demo";
    private const string SentryQaSuite = "sentry-qa-suite-demo";
    private const string ToolingForge = "tooling-forge-demo";
    private const string NightwatchService = "nightwatch-service-demo";

    /// <summary>
    /// Hand-authored 12-week weekly commit series feeding the development trend (FR-013, FR-038). Fixed
    /// regardless of scope or period: it is presented as an organisation-wide trend input, not recomputed
    /// per fetch. Ascending with an overall rise of (55-38)/38 ≈ 44.7%, comfortably above the 25% steep-ramp
    /// caution threshold so that caution path is exercisable.
    /// </summary>
    private static readonly IReadOnlyList<int> WeeklyCommitCountsSeries =
    [
        38, 40, 42, 43, 45, 46, 48, 49, 51, 52, 54, 55,
    ];

    /// <summary>
    /// Ordinary roster members: (login, home project, commit count). Kept deliberately modest — a single
    /// project each and a handful of in-hours commits — so none of them trips a seeded rule by accident
    /// (task requirement 10). Sable is the sole exception: <see cref="BuildSableSteadyPrs"/> layers a
    /// separate, deliberately steady PR case on top of her ordinary commits here.
    /// </summary>
    private static readonly (DeveloperLogin Login, string Project, int CommitCount)[] OrdinaryMembers =
    [
        (Flynn, PulseApi, 4),
        (Wren, PulseApi, 3),
        (Blair, AuroraMobile, 5),
        (Sasha, AuroraMobile, 4),
        (Sable, BeaconDataPipeline, 3),
        (Finch, BeaconDataPipeline, 4),
        (Opal, BeaconDataPipeline, 5),
        (Teagan, SentryQaSuite, 3),
        (Holt, SentryQaSuite, 4),
        (Avery, SentryQaSuite, 3),
        (Lark, ToolingForge, 4),
        (Shay, ToolingForge, 3),
        (Indigo, ToolingForge, 4),
        (Zephyr, PulseWeb, 4),
    ];

    /// <summary>
    /// Marlowe's authored review comments: fictional, respectful, no profanity. The first five read as
    /// terse/frustrated (task requirement 4, ~38% of the sample); the rest read neutral to positive, so
    /// the later canned tone classifier's output looks coherent against the phrasing.
    /// </summary>
    private static readonly string[] MarloweCommentBodies =
    [
        "This is the third time this null check has come back broken.",
        "Why was this merged without tests again?",
        "We flagged this exact issue two sprints ago and nothing changed.",
        "Please stop resubmitting the same untested diff.",
        "This keeps breaking staging and nobody seems to notice before merge.",
        "Looks fine, small nit: consider renaming this variable for clarity.",
        "Nice cleanup here, this reads much better than before.",
        "Can you add a short comment explaining the retry logic?",
        "LGTM once the CI pipeline goes green.",
        "Good catch on the edge case, thanks for adding a test.",
        "Minor style nit, otherwise this looks solid.",
        "Approving — the refactor is a clear improvement.",
        "Happy with this, just double-check the changelog entry.",
    ];

    /// <summary>Builds the full deterministic dataset for the given scope and period.</summary>
    public static ActivityDataset BuildDataset(ScopeKey scope, Period period)
    {
        var roster = BuildRoster();
        var teams = BuildTeams();
        var projects = BuildProjects(period.End);
        var events = BuildEvents(period);

        var (scopedProjects, coveredNames, scopedEvents) = ApplyScope(scope, projects, events);

        return new ActivityDataset(
            roster: roster,
            projects: scopedProjects,
            teams: teams,
            events: scopedEvents,
            weeklyCommitCounts: WeeklyCommitCountsSeries,
            coveredProjectNames: coveredNames,
            loadedAt: DateTimeOffset.UtcNow,
            isDemoData: true);
    }

    // ---------------------------------------------------------------------------------------------
    // Roster, teams, projects
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The full organisation roster (task requirement: ~18-24 members across five teams plus a no-team
    /// member), including the developers with zero events in every period (FR-012).
    /// </summary>
    private static IReadOnlyList<Developer> BuildRoster() =>
    [
        new Developer(Nova, "Nova Stardust", isBot: false),
        new Developer(Flynn, "Flynn Circuitry", isBot: false),
        new Developer(Wren, "Wren Ironforge", isBot: false),
        new Developer(Dex, "Dex Quietstorm", isBot: false),

        new Developer(Remy, "Remy Afterglow", isBot: false),
        new Developer(Blair, "Blair Pixelrun", isBot: false),
        new Developer(Sasha, "Sasha Lumen", isBot: false),
        new Developer(Koda, "Koda Driftwood", isBot: false),

        new Developer(Juniper, "Juniper Dataforge", isBot: false),
        new Developer(Marlowe, "Marlowe Critique", isBot: false),
        new Developer(Sable, "Sable Querywise", isBot: false),
        new Developer(Finch, "Finch Parallax", isBot: false),
        new Developer(Opal, "Opal Vectorsong", isBot: false),

        new Developer(River, "River Hurrybrook", isBot: false),
        new Developer(Teagan, "Teagan Testcraft", isBot: false),
        new Developer(Holt, "Holt Checksum", isBot: false),
        new Developer(Brynn, "Brynn Edgecase", isBot: false),
        new Developer(Avery, "Avery Stackframe", isBot: false),

        new Developer(PulseBot, "Pulsebot CI", isBot: true),
        new Developer(Lark, "Lark Tooling", isBot: false),
        new Developer(Shay, "Shay Buildpipe", isBot: false),
        new Developer(Indigo, "Indigo Scriptwell", isBot: false),

        new Developer(Zephyr, "Zephyr Freelance", isBot: false),
    ];

    /// <summary>
    /// Five organisation teams (FR-036). <see cref="Zephyr"/> deliberately belongs to none of them, so
    /// callers building the no-team group (a caller-level concern per <see cref="Team"/>'s remarks) find
    /// exactly one roster member outside every team.
    /// </summary>
    private static IReadOnlyList<Team> BuildTeams() =>
    [
        new Team("Platform", [Nova, Flynn, Wren, Dex]),
        new Team("Mobile", [Remy, Blair, Sasha, Koda]),
        new Team("Data", [Juniper, Marlowe, Sable, Finch, Opal]),
        new Team("QA", [River, Teagan, Holt, Brynn, Avery]),
        new Team("DevEx", [PulseBot, Lark, Shay, Indigo]),
    ];

    /// <summary>Seven fictitious repositories with descending <see cref="Project.LastPushedAt"/> values.</summary>
    private static IReadOnlyList<Project> BuildProjects(DateTimeOffset end) =>
    [
        new Project(PulseApi, end - TimeSpan.FromHours(3)),
        new Project(PulseWeb, end - TimeSpan.FromHours(10)),
        new Project(AuroraMobile, end - TimeSpan.FromHours(18)),
        new Project(BeaconDataPipeline, end - TimeSpan.FromHours(30)),
        new Project(SentryQaSuite, end - TimeSpan.FromHours(48)),
        new Project(ToolingForge, end - TimeSpan.FromHours(60)),
        new Project(NightwatchService, end - TimeSpan.FromHours(90)),
    ];

    // ---------------------------------------------------------------------------------------------
    // Time helpers. All arithmetic works backwards from period.End in calendar days, classifying each
    // candidate day as a weekday or weekend day by inspecting its actual DayOfWeek — never by assuming a
    // fixed calendar date — so the seeded cases hold no matter what date the demo happens to run on.
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Returns the instant <paramref name="daysAgo"/> full calendar days before <paramref name="end"/>'s
    /// local date, at the given local hour and minute, expressed in <see cref="OrgOffset"/>.
    /// </summary>
    private static DateTimeOffset At(DateTimeOffset end, int daysAgo, int hour, int minute = 0)
    {
        var endLocal = end.ToOffset(OrgOffset);
        var date = endLocal.Date.AddDays(-daysAgo);
        return new DateTimeOffset(date.Year, date.Month, date.Day, hour, minute, 0, OrgOffset);
    }

    /// <summary>True when the calendar date <paramref name="daysAgo"/> days before <paramref name="end"/> is a Saturday or Sunday.</summary>
    private static bool IsWeekendDate(DateTimeOffset end, int daysAgo)
    {
        var endLocal = end.ToOffset(OrgOffset);
        var date = endLocal.Date.AddDays(-daysAgo);
        return date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
    }

    /// <summary>The inclusive range [<paramref name="fromInclusive"/>, <paramref name="toInclusive"/>] of "days ago" values.</summary>
    private static List<int> DaysAgoRange(int fromInclusive, int toInclusive) =>
        Enumerable.Range(fromInclusive, toInclusive - fromInclusive + 1).ToList();

    /// <summary>Splits <paramref name="daysAgoValues"/> into weekday and weekend "days ago" buckets, nearest-to-<paramref name="end"/> first.</summary>
    private static (List<int> Weekdays, List<int> Weekends) SplitByWeekday(DateTimeOffset end, IEnumerable<int> daysAgoValues)
    {
        var weekdays = new List<int>();
        var weekends = new List<int>();

        foreach (var daysAgo in daysAgoValues)
        {
            (IsWeekendDate(end, daysAgo) ? weekends : weekdays).Add(daysAgo);
        }

        return (weekdays, weekends);
    }

    /// <summary>Indexes into a non-empty "days ago" bucket with wraparound, so a fixed number of seeded events never index out of range regardless of how many weekday/weekend slots a given week actually has.</summary>
    private static int Idx(IReadOnlyList<int> daysAgoValues, int i) => daysAgoValues[i % daysAgoValues.Count];

    // ---------------------------------------------------------------------------------------------
    // Event assembly
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Builds every event for the given period. The seeded cases (requirements 1-9) live entirely inside
    /// the last 6 calendar days before <see cref="Period.End"/> — a window proven to sit safely inside any
    /// 7/14/30 day period regardless of the exact time of day <see cref="Period.End"/> falls on — so they
    /// hold for every period length. Additional filler activity for ordinary members is added, gated on
    /// <see cref="Period.Days"/>, into the older 8-13 and 15-29 day windows so 14 and 30 day periods carry
    /// more history without disturbing any seeded case's ratio. A final bounds filter is a safety net, not
    /// a substitute for correct generation.
    /// </summary>
    private static List<ActivityEvent> BuildEvents(Period period)
    {
        var end = period.End;
        var events = new List<ActivityEvent>();

        var shaCounter = 0;
        string NextSha() => $"demo-sha-{++shaCounter:D6}";

        var commentIdCounter = 900_000L;
        long NextCommentId() => ++commentIdCounter;

        var (coreWeekdays, coreWeekends) = SplitByWeekday(end, DaysAgoRange(1, 6));

        events.AddRange(BuildNovaOverworkCommits(end, coreWeekdays, coreWeekends, NextSha));
        events.AddRange(BuildRemyOverworkPr(end, coreWeekdays, coreWeekends));
        events.AddRange(BuildJuniperSpreadThin(end, coreWeekdays, NextSha));
        events.AddRange(BuildMarloweComments(end, coreWeekdays, NextCommentId));
        events.AddRange(BuildRiverRushing(end, coreWeekdays));
        events.AddRange(BuildRiverRushingCommits(end, coreWeekdays, NextSha));
        events.AddRange(BuildSableSteadyPrs(end, coreWeekdays));
        events.AddRange(BuildBotEvents(end, coreWeekdays, NextSha));
        events.AddRange(BuildUnmatchedAuthorEvent(end, coreWeekdays, NextSha));
        events.AddRange(BuildOrdinaryMembersActivity(end, coreWeekdays, NextSha));

        if (period.Days >= 14)
        {
            var midWeekdays = SplitByWeekday(end, DaysAgoRange(8, 13)).Weekdays;
            events.AddRange(BuildFillerActivity(end, midWeekdays, NextSha));
        }

        if (period.Days == 30)
        {
            var outerWeekdays = SplitByWeekday(end, DaysAgoRange(15, 29)).Weekdays;
            events.AddRange(BuildFillerActivity(end, outerWeekdays, NextSha));
        }

        // Safety net: no seeded generation above should ever produce an out-of-bounds event, but a
        // demo source must never surface one regardless.
        return events.Where(e => e.OccurredAt >= period.Start && e.OccurredAt <= period.End).ToList();
    }

    /// <summary>
    /// Seeded case: overwork (commits). Nova's out-of-hours commit share, evaluated in author-local time
    /// (every commit carries a genuine +02:00 offset, <see cref="CommitEvent.HasUsableOffset"/> true), is
    /// (|weekdays| + 2·|weekends|) / (3·|weekdays| + 2·|weekends|) — 41% to 50% depending on the week's
    /// exact weekday/weekend split, always comfortably above the 25% threshold.
    /// </summary>
    private static IEnumerable<ActivityEvent> BuildNovaOverworkCommits(
        DateTimeOffset end, List<int> weekdays, List<int> weekends, Func<string> nextSha)
    {
        var events = new List<ActivityEvent>();

        foreach (var d in weekdays)
        {
            events.Add(new CommitEvent(Nova, PulseApi, At(end, d, 9, 45), nextSha(), hasUsableOffset: true));
            events.Add(new CommitEvent(Nova, PulseApi, At(end, d, 14, 30), nextSha(), hasUsableOffset: true));
            events.Add(new CommitEvent(Nova, PulseApi, At(end, d, 21, 30), nextSha(), hasUsableOffset: true)); // out of hours
        }

        foreach (var d in weekends)
        {
            events.Add(new CommitEvent(Nova, PulseWeb, At(end, d, 11, 0), nextSha(), hasUsableOffset: true)); // out of hours (weekend)
            events.Add(new CommitEvent(Nova, PulseWeb, At(end, d, 16, 0), nextSha(), hasUsableOffset: true)); // out of hours (weekend)
        }

        return events;
    }

    /// <summary>
    /// Seeded case: overwork (PR activity). Remy's seven PR events (two opens, five reviews — org-timezone
    /// basis, no personal offset) put three of them out of hours, a 42.9% share, above the 25% threshold
    /// with the >= 3 event guard satisfied. PR #4150 receives two separate review submissions from Remy,
    /// also covering the "multiple review rounds on one PR" requirement (FR-003).
    /// </summary>
    private static IEnumerable<ActivityEvent> BuildRemyOverworkPr(DateTimeOffset end, List<int> weekdays, List<int> weekends) =>
    [
        new PrOpenedEvent(Remy, AuroraMobile, At(end, Idx(weekdays, 0), 10, 0), prNumber: 4201),                                   // in hours
        new ReviewEvent(Remy, AuroraMobile, At(end, Idx(weekdays, 1), 14, 0), prNumber: 4150, ReviewState.Approved),               // in hours
        new ReviewEvent(Remy, AuroraMobile, At(end, Idx(weekdays, 2), 16, 0), prNumber: 4150, ReviewState.Commented),              // in hours, second round on PR #4150
        new ReviewEvent(Remy, AuroraMobile, At(end, Idx(weekdays, 3), 11, 30), prNumber: 4153, ReviewState.Commented),             // in hours
        new PrOpenedEvent(Remy, AuroraMobile, At(end, Idx(weekends, 0), 11, 0), prNumber: 4202),                                   // out of hours (weekend)
        new ReviewEvent(Remy, AuroraMobile, At(end, Idx(weekdays, 0), 22, 0), prNumber: 4151, ReviewState.ChangesRequested),       // out of hours (evening)
        new ReviewEvent(Remy, AuroraMobile, At(end, Idx(weekends, 1), 13, 0), prNumber: 4152, ReviewState.Approved),               // out of hours (weekend)
    ];

    /// <summary>Seeded case: spread thin. Juniper has in-hours commit activity across five distinct projects (organisation scope).</summary>
    private static IEnumerable<ActivityEvent> BuildJuniperSpreadThin(DateTimeOffset end, List<int> weekdays, Func<string> nextSha)
    {
        string[] projects = [PulseApi, PulseWeb, AuroraMobile, BeaconDataPipeline, SentryQaSuite];
        var events = new List<ActivityEvent>();

        for (var i = 0; i < projects.Length; i++)
        {
            events.Add(new CommitEvent(Juniper, projects[i], At(end, Idx(weekdays, i), 10 + i, 0), nextSha(), hasUsableOffset: true));
        }

        return events;
    }

    /// <summary>Seeded case: frustrated commenter. Marlowe authors 13 review comments, ~38% terse/frustrated in phrasing.</summary>
    private static IEnumerable<ActivityEvent> BuildMarloweComments(DateTimeOffset end, List<int> weekdays, Func<long> nextCommentId)
    {
        var events = new List<ActivityEvent>();

        for (var i = 0; i < MarloweCommentBodies.Length; i++)
        {
            var hour = 10 + (i % 6); // always within 09:00-18:00
            events.Add(new CommentEvent(Marlowe, BeaconDataPipeline, At(end, Idx(weekdays, i), hour, 0), nextCommentId(), MarloweCommentBodies[i]));
        }

        return events;
    }

    /// <summary>
    /// Seeded case: possible rushing (PR side). River opens five PRs; three receive a changes-requested
    /// review from a different developer and two receive an approval — a 60% changes-requested share,
    /// above the 40% threshold, with the >= 3 PR sample satisfied. Reviewer logins are ordinary members,
    /// deliberately kept at one PR event each so they never approach the PR-overwork guard themselves.
    /// </summary>
    private static IEnumerable<ActivityEvent> BuildRiverRushing(DateTimeOffset end, List<int> weekdays) =>
    [
        new PrOpenedEvent(River, SentryQaSuite, At(end, Idx(weekdays, 0), 9, 30), prNumber: 5301),
        new PrOpenedEvent(River, SentryQaSuite, At(end, Idx(weekdays, 1), 10, 0), prNumber: 5302),
        new PrOpenedEvent(River, SentryQaSuite, At(end, Idx(weekdays, 2), 10, 30), prNumber: 5303),
        new PrOpenedEvent(River, SentryQaSuite, At(end, Idx(weekdays, 3), 11, 0), prNumber: 5304),
        new PrOpenedEvent(River, SentryQaSuite, At(end, Idx(weekdays, 4), 11, 30), prNumber: 5305),

        new ReviewEvent(Teagan, SentryQaSuite, At(end, Idx(weekdays, 0), 15, 0), prNumber: 5301, ReviewState.ChangesRequested),
        new ReviewEvent(Holt, SentryQaSuite, At(end, Idx(weekdays, 1), 15, 0), prNumber: 5302, ReviewState.ChangesRequested),
        new ReviewEvent(Avery, SentryQaSuite, At(end, Idx(weekdays, 2), 15, 0), prNumber: 5303, ReviewState.ChangesRequested),
        new ReviewEvent(Teagan, SentryQaSuite, At(end, Idx(weekdays, 3), 15, 0), prNumber: 5304, ReviewState.Approved),
        new ReviewEvent(Holt, SentryQaSuite, At(end, Idx(weekdays, 4), 15, 0), prNumber: 5305, ReviewState.Approved),
    ];

    /// <summary>
    /// Seeded case: possible rushing (volume side). Eight in-hours commits push River's raw volume
    /// (commits + PRs opened = 13) comfortably above the roster median, alongside the five PR opens from
    /// <see cref="BuildRiverRushing"/>.
    /// </summary>
    private static IEnumerable<ActivityEvent> BuildRiverRushingCommits(DateTimeOffset end, List<int> weekdays, Func<string> nextSha)
    {
        var events = new List<ActivityEvent>();

        for (var i = 0; i < 8; i++)
        {
            var hour = 9 + (i % 8); // always within 09:00-17:00
            events.Add(new CommitEvent(River, SentryQaSuite, At(end, Idx(weekdays, i), hour, 0), nextSha(), hasUsableOffset: true));
        }

        return events;
    }

    /// <summary>
    /// Seeded case: steady quality vs quantity. Sable — an ordinary Data-team member who already carries a
    /// modest in-hours commit history in her home project — opens exactly three pull requests there, all
    /// in hours, reviewed by teammates already active in that same project (Finch, Opal, Juniper), so none
    /// of them gains a new distinct project or approaches their own PR-event guard from a single review.
    /// Only one of the three PRs receives a changes-requested review — a 33% share, below the 40%
    /// rushing threshold — so <c>/quality</c> finally has a sufficient-sample developer (&gt;= 3 PRs,
    /// FR-027's minimum) whose volume and rework read "in step" (ui-design.md 4.6). Before this case,
    /// River Hurrybrook was the only sufficient-sample developer at any scope or period, and she is always
    /// flagged, so the steady row could never render.
    /// </summary>
    private static IEnumerable<ActivityEvent> BuildSableSteadyPrs(DateTimeOffset end, List<int> weekdays) =>
    [
        new PrOpenedEvent(Sable, BeaconDataPipeline, At(end, Idx(weekdays, 0), 10, 0), prNumber: 6701),
        new ReviewEvent(Finch, BeaconDataPipeline, At(end, Idx(weekdays, 0), 15, 0), prNumber: 6701, ReviewState.Approved),

        new PrOpenedEvent(Sable, BeaconDataPipeline, At(end, Idx(weekdays, 1), 10, 30), prNumber: 6702),
        new ReviewEvent(Opal, BeaconDataPipeline, At(end, Idx(weekdays, 1), 15, 0), prNumber: 6702, ReviewState.Commented),

        new PrOpenedEvent(Sable, BeaconDataPipeline, At(end, Idx(weekdays, 2), 11, 0), prNumber: 6703),
        new ReviewEvent(Juniper, BeaconDataPipeline, At(end, Idx(weekdays, 2), 15, 0), prNumber: 6703, ReviewState.ChangesRequested),
    ];

    /// <summary>Seeded case: bot exclusion. Pulsebot-ci-demo (IsBot = true) authors two in-hours commits that later aggregation must exclude.</summary>
    private static IEnumerable<ActivityEvent> BuildBotEvents(DateTimeOffset end, List<int> weekdays, Func<string> nextSha) =>
    [
        new CommitEvent(PulseBot, ToolingForge, At(end, Idx(weekdays, 0), 9, 30), nextSha(), hasUsableOffset: true),
        new CommitEvent(PulseBot, ToolingForge, At(end, Idx(weekdays, 1), 14, 0), nextSha(), hasUsableOffset: true),
    ];

    /// <summary>Seeded case: unmatched author. One commit could not be matched to any roster login (edge case).</summary>
    private static IEnumerable<ActivityEvent> BuildUnmatchedAuthorEvent(DateTimeOffset end, List<int> weekdays, Func<string> nextSha) =>
    [
        new CommitEvent(DeveloperLogin.Unmatched, NightwatchService, At(end, Idx(weekdays, 0), 10, 15), nextSha(), hasUsableOffset: false),
    ];

    /// <summary>
    /// Ordinary members: modest, in-hours-only commit activity in a single home project each, so exactly
    /// the seeded cases above trip a rule (task requirement 10).
    /// </summary>
    private static IEnumerable<ActivityEvent> BuildOrdinaryMembersActivity(DateTimeOffset end, List<int> weekdays, Func<string> nextSha)
    {
        var events = new List<ActivityEvent>();

        foreach (var (login, project, count) in OrdinaryMembers)
        {
            for (var i = 0; i < count; i++)
            {
                var hour = 9 + ((i * 2) % 8); // always within 09:00-17:00
                events.Add(new CommitEvent(login, project, At(end, Idx(weekdays, i), hour, 0), nextSha(), hasUsableOffset: true));
            }
        }

        return events;
    }

    /// <summary>
    /// Extra in-hours filler commits for ordinary members only, placed in the older 8-13 / 15-29 day
    /// windows so 14 and 30 day periods carry more history. Never touches a case-carrying developer, so no
    /// seeded ratio is disturbed by adding it.
    /// </summary>
    private static IEnumerable<ActivityEvent> BuildFillerActivity(DateTimeOffset end, List<int> weekdayOffsets, Func<string> nextSha)
    {
        var events = new List<ActivityEvent>();

        if (weekdayOffsets.Count == 0)
        {
            return events;
        }

        var i = 0;
        foreach (var (login, project, _) in OrdinaryMembers)
        {
            events.Add(new CommitEvent(login, project, At(end, Idx(weekdayOffsets, i), 11, 0), nextSha(), hasUsableOffset: true));
            i++;
        }

        return events;
    }

    // ---------------------------------------------------------------------------------------------
    // Scope filtering
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Narrows projects, covered project names, and events to a single project when
    /// <paramref name="scope"/> is a project scope; the roster and teams are never narrowed (FR-012,
    /// FR-036 no-activity and no-team grouping recompute naturally from the full roster).
    /// </summary>
    private static (IReadOnlyList<Project> Projects, IReadOnlyList<string> CoveredNames, IReadOnlyList<ActivityEvent> Events) ApplyScope(
        ScopeKey scope, IReadOnlyList<Project> projects, IReadOnlyList<ActivityEvent> events)
    {
        if (scope.Kind == ScopeKind.Organisation)
        {
            return (projects, projects.Select(p => p.Name).ToList(), events);
        }

        var name = scope.ProjectName!;
        var scopedProjects = projects.Where(p => string.Equals(p.Name, name, StringComparison.Ordinal)).ToList();
        var scopedEvents = events.Where(e => string.Equals(e.ProjectName, name, StringComparison.Ordinal)).ToList();

        return (scopedProjects, [name], scopedEvents);
    }
}
