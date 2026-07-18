using DeveloperWellness.Application.Ports;
using DeveloperWellness.Domain.Model;

namespace DeveloperWellness.Infrastructure.Demo;

/// <summary>
/// Deterministic, network-free demo implementation of <see cref="IAiInsightService"/> (FR-013, research
/// R5). Serves every capability the live service does (<see cref="IsAvailable"/> is always true, FR-013),
/// returning hand-authored, supportive summaries built from the caller's <see cref="SummaryGrounding"/>
/// figures rather than a live model call. The five seeded wellbeing cases from <c>DemoSeed</c> — logins
/// duplicated here as string literals rather than referenced from that internal type, the same
/// cross-assembly-boundary reasoning <c>DemoDatasetTests</c> documents — each get a narrative coherent
/// with their specific pattern; every other subject falls back to a generic, steady-signals template.
/// </summary>
public sealed class DemoAiInsightService : IAiInsightService
{
    private const string NovaLogin = "nova-stardust-demo";
    private const string RemyLogin = "remy-afterglow-demo";
    private const string JuniperLogin = "juniper-dataforge-demo";
    private const string MarloweLogin = "marlowe-critique-demo";
    private const string RiverLogin = "river-hurrybrook-demo";

    private const string PulseApiProject = "pulse-api-demo";

    private const int SimulatedDelayMilliseconds = 300;
    private const int MaxWords = 120;

    /// <inheritdoc />
    /// <remarks>Demo mode serves every capability with canned data (FR-013); there is no unconfigured state.</remarks>
    public bool IsAvailable => true;

    /// <inheritdoc />
    /// <remarks>
    /// Deterministic: identical <paramref name="subject"/> and <paramref name="grounding"/> values always
    /// produce identical <see cref="AiSummary.Text"/>; only <see cref="AiSummary.GeneratedAt"/> varies
    /// between calls, by design. The small delay makes the UI's loading state visible in demos without
    /// meaningfully slowing them down.
    /// </remarks>
    public async Task<AiSummary> SummariseAsync(AiSubject subject, SummaryGrounding grounding, CancellationToken cancellationToken)
    {
        await Task.Delay(SimulatedDelayMilliseconds, cancellationToken).ConfigureAwait(false);

        var text = EnforceWordLimit(ComposeSummary(subject, grounding));

        return new AiSummary(
            subject,
            ScopeFromGrounding(grounding),
            PeriodFromGrounding(grounding),
            text,
            generatedAt: DateTimeOffset.UtcNow,
            isDemo: true);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Deterministic, network-free keyword-heuristic classification — no chat call, unlike the live
    /// adapter. Each body is checked against a small set of frustration markers drawn directly from the
    /// seeded frustrated commenter's actual comment bodies (<c>DemoSeed.MarloweCommentBodies</c>, entries
    /// 1-5), then a small set of appreciative markers (entries 7, 9, 10, 12, 13); anything matching
    /// neither reads as neutral (entries 6, 8, 11). An empty or whitespace body maps to
    /// <see cref="ToneClass.Unanalysed"/> — there is nothing to read — which is the only way this adapter
    /// ever produces that value, since every seeded comment body is non-empty. Order is preserved:
    /// result[i] is the classification of commentBodies[i].
    /// </remarks>
    public async Task<IReadOnlyList<ToneClass>> ClassifyToneAsync(IReadOnlyList<string> commentBodies, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(commentBodies);
        cancellationToken.ThrowIfCancellationRequested();

        if (commentBodies.Count == 0)
        {
            return [];
        }

        await Task.Delay(SimulatedDelayMilliseconds, cancellationToken).ConfigureAwait(false);

        return commentBodies.Select(ClassifyBody).ToList();
    }

    /// <summary>
    /// Frustration markers drawn directly from the seeded frustrated commenter's actual comment bodies
    /// (<c>DemoSeed.MarloweCommentBodies</c> entries 1-5), specific multi-word phrases chosen so they
    /// never false-positive on the appreciative or neutral comments in that same seeded set.
    /// </summary>
    private static readonly string[] NegativeMarkers =
    [
        "third time",
        "broken",
        "why was",
        "without tests",
        "nothing changed",
        "stop resubmitting",
        "untested",
        "keeps breaking",
        "nobody seems to notice",
        "still failing",
    ];

    /// <summary>Appreciative markers; checked only when no negative marker matched.</summary>
    private static readonly string[] PositiveMarkers =
    [
        "thanks",
        "nice",
        "great",
        "good catch",
        "love",
        "lgtm",
        "happy",
        "approving",
    ];

    /// <summary>
    /// Classifies one comment body: empty or whitespace is unanalysed (nothing to read); otherwise a
    /// negative marker wins over a positive one (a terse complaint that also happens to say "thanks for
    /// looking" should still read as negative); anything matching neither reads as neutral.
    /// </summary>
    private static ToneClass ClassifyBody(string body)
    {
        var text = body ?? string.Empty;

        if (string.IsNullOrWhiteSpace(text))
        {
            return ToneClass.Unanalysed;
        }

        if (NegativeMarkers.Any(marker => text.Contains(marker, StringComparison.OrdinalIgnoreCase)))
        {
            return ToneClass.Negative;
        }

        if (PositiveMarkers.Any(marker => text.Contains(marker, StringComparison.OrdinalIgnoreCase)))
        {
            return ToneClass.Positive;
        }

        return ToneClass.Neutral;
    }

    /// <summary>
    /// Selects the narrative for the given subject: the no-activity template first (covers every
    /// no-activity developer uniformly, not just the seeded one, since <see cref="SummaryGrounding.HasActivity"/>
    /// is exactly the signal data-model.md defines for that group), then the five seeded per-login
    /// narratives, then a generic template per subject kind.
    /// </summary>
    private static string ComposeSummary(AiSubject subject, SummaryGrounding grounding)
    {
        if (!grounding.HasActivity)
        {
            return NoActivityNarrative(grounding);
        }

        if (subject.Kind == AiSubjectKind.Developer)
        {
            return (subject.Login?.Value ?? string.Empty) switch
            {
                NovaLogin => OverworkCommitsNarrative(grounding),
                RemyLogin => OverworkPrActivityNarrative(grounding),
                JuniperLogin => SpreadThinNarrative(grounding),
                MarloweLogin => FrustratedCommenterNarrative(grounding),
                RiverLogin => PossibleRushingNarrative(grounding),
                _ => GenericDeveloperNarrative(grounding),
            };
        }

        return subject.ProjectName switch
        {
            PulseApiProject => PulseApiProjectNarrative(grounding),
            _ => GenericProjectNarrative(grounding),
        };
    }

    /// <summary>Seeded case: overwork (commits). Mentions the out-of-hours commit share gently; no judgement.</summary>
    private static string OverworkCommitsNarrative(SummaryGrounding grounding) =>
        $"{grounding.SubjectDescriptor} has been active this period, with {grounding.CommitCount} commits over the last " +
        $"{grounding.PeriodDays} days. A notable share of that work, about {FormatShare(grounding.OutOfHoursCommitShare)}, " +
        "is landing outside the usual working hours. That kind of pattern often shows up when someone is squeezing in " +
        "early mornings, evenings, or weekend time to keep pace with the workload. Nothing here says anything is wrong, " +
        "but it might be worth a quiet, no-pressure check-in about how the pace feels right now, and whether some of " +
        "that after-hours work could ease off before it becomes a habit.";

    /// <summary>Seeded case: overwork (PR activity). Mentions the out-of-hours PR share gently; no judgement.</summary>
    private static string OverworkPrActivityNarrative(SummaryGrounding grounding) =>
        $"{grounding.SubjectDescriptor} has been carrying a fair amount of pull-request activity this period — " +
        $"{grounding.ReviewCount} reviews and {grounding.PrsOpenedCount} pull requests opened — and a meaningful share " +
        $"of that work, about {FormatShare(grounding.OutOfHoursPrShare)}, is happening outside normal working hours. " +
        "Review activity late in the evening or over a weekend often means someone is trying to unblock teammates or " +
        "catch up before the next day starts, which is thoughtful but can add up. This isn't a comment on the work " +
        "itself — it's simply a pattern worth a quiet check-in about, so reviews don't quietly become an after-hours habit.";

    /// <summary>Seeded case: spread thin. Mentions the distinct project count gently; no judgement.</summary>
    private static string SpreadThinNarrative(SummaryGrounding grounding) =>
        $"{grounding.SubjectDescriptor} is currently active across {FormatCount(grounding.DistinctProjectCount)} different " +
        "projects this period, a wider spread than most of the team. Splitting attention across that many codebases at " +
        "once can make it harder to go deep on any one of them, and the context-switching itself carries a cost that " +
        "doesn't always show up in commit counts. There's no sign here of anything being handled badly — the work " +
        $"itself looks fine — but it might be worth checking in about whether {grounding.SubjectDescriptor} feels " +
        "stretched thin, and whether some of that project load could be rebalanced across the team.";

    /// <summary>Seeded case: frustrated commenter. Frames the tone as climate, not character; suggests a gentle check-in.</summary>
    private static string FrustratedCommenterNarrative(SummaryGrounding grounding) =>
        $"{grounding.SubjectDescriptor} has authored {grounding.CommentCount} review comments this period, and several " +
        "read a bit terser or more frustrated than usual. That kind of tone in comments is far more often about review " +
        "pressure — repeated issues, tight deadlines, or a process friction point — than about anything personal. It's " +
        "worth a gentle check-in framed around the work and the process rather than the wording itself: asking what's " +
        "making reviews feel harder right now can surface something fixable. This is climate, not character, and a " +
        "quiet conversation usually goes a long way.";

    /// <summary>Seeded case: possible rushing. Frames high output plus rework as pace pressure, not carelessness.</summary>
    private static string PossibleRushingNarrative(SummaryGrounding grounding) =>
        $"{grounding.SubjectDescriptor} has opened {grounding.PrsOpenedCount} pull requests this period, a higher volume " +
        "than most of the team, alongside a good share of reviews coming back with changes requested rather than a " +
        "straight approval. Taken together, that combination — high output alongside more rework than usual — often " +
        "points to pace pressure rather than carelessness: moving fast enough that there isn't quite enough time to " +
        "double-check before submitting. A supportive check-in about workload and deadlines, rather than about code " +
        "quality, is likely to land better here and help ease that pressure.";

    /// <summary>Generic template for any developer not matching a seeded case; steady signals, nothing to flag.</summary>
    private static string GenericDeveloperNarrative(SummaryGrounding grounding) =>
        $"{grounding.SubjectDescriptor} had a steady {grounding.PeriodDays} days: {grounding.CommitCount} commits, " +
        $"{grounding.ReviewCount} reviews given, and {grounding.CommentCount} comments, with activity landing mostly " +
        "within normal working hours. Nothing here stands out as a signal worth worrying about — this looks like an " +
        "ordinary, sustainable pace. As always, this summary reflects only the aggregated activity figures shown " +
        "elsewhere on this page, not any judgement about the person.";

    /// <summary>
    /// States the absence of activity plainly and does not speculate about the reason (FR/US8 acceptance):
    /// covers every no-activity developer, seeded or otherwise, since <see cref="SummaryGrounding.HasActivity"/>
    /// is false for all of them alike.
    /// </summary>
    private static string NoActivityNarrative(SummaryGrounding grounding) =>
        $"{grounding.SubjectDescriptor} has no recorded activity in this period. There's nothing in the data to " +
        "explain why, so this summary won't guess or speculate about the reason — it simply notes that no activity " +
        "was recorded. If it's useful, a quick, low-pressure check-in can confirm everything's fine, without reading " +
        "anything into the silence itself.";

    /// <summary>Narrative for the seeded, more-active project; describes activity level only, no wellbeing verdicts.</summary>
    private static string PulseApiProjectNarrative(SummaryGrounding grounding) =>
        $"{grounding.SubjectDescriptor} has been one of the busier projects this period, with {grounding.CommitCount} " +
        $"commits, {grounding.ReviewCount} reviews, and {grounding.PrsOpenedCount} pull requests opened across the " +
        "team. Activity here includes some out-of-hours commits, worth keeping in mind for whoever is carrying that " +
        "load. The project itself looks healthy and active overall — any wellbeing notes surface at the person level " +
        "rather than here, so the check-in roster is the right place to look for anyone who might appreciate a quiet word.";

    /// <summary>Generic template for any project not matching the seeded case; steady activity, nothing to flag.</summary>
    private static string GenericProjectNarrative(SummaryGrounding grounding) =>
        $"{grounding.SubjectDescriptor} saw {grounding.CommitCount} commits, {grounding.ReviewCount} reviews, and " +
        $"{grounding.PrsOpenedCount} pull requests opened over the last {grounding.PeriodDays} days. Activity levels " +
        "here look steady and in line with what you'd expect for a project of this size. There's nothing unusual to " +
        "flag at the project level this period — any wellbeing signals for individual contributors show up on the " +
        "check-in roster rather than here.";

    /// <summary>Formats a share as a whole-number percentage, or a neutral phrase when unavailable.</summary>
    private static string FormatShare(decimal? share) => share is { } value ? $"{value:P0}" : "an unavailable share";

    /// <summary>Formats a count, or a neutral phrase when unavailable (e.g. at project scope).</summary>
    private static string FormatCount(int? count) => count?.ToString() ?? "several";

    /// <summary>Trims to at most <see cref="MaxWords"/> whitespace-separated words, as a safety net over the hand-authored templates above.</summary>
    private static string EnforceWordLimit(string text)
    {
        var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return words.Length <= MaxWords ? text : string.Join(' ', words.Take(MaxWords));
    }

    /// <summary>
    /// Reconstructs the scope this summary was generated for from <see cref="SummaryGrounding.ScopeLabel"/>,
    /// mirroring <c>FoundryAiInsightService</c>'s identical reconstruction (the port's signature carries no
    /// other scope information down to either adapter).
    /// </summary>
    private static ScopeKey ScopeFromGrounding(SummaryGrounding grounding) =>
        string.Equals(grounding.ScopeLabel, "organisation", StringComparison.OrdinalIgnoreCase)
            ? ScopeKey.Organisation
            : ScopeKey.Project(grounding.ScopeLabel);

    /// <summary>
    /// Reconstructs the period this summary was generated for from <see cref="SummaryGrounding.PeriodDays"/>,
    /// anchored to "now" (mirroring <c>FoundryAiInsightService</c>'s identical reconstruction).
    /// </summary>
    private static Period PeriodFromGrounding(SummaryGrounding grounding) =>
        new(grounding.PeriodDays, DateTimeOffset.UtcNow);
}
