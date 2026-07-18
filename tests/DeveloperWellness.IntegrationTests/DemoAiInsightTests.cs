using DeveloperWellness.Application.Ports;
using DeveloperWellness.Domain.Model;
using DeveloperWellness.Infrastructure.Ai;
using DeveloperWellness.Infrastructure.Demo;
using Microsoft.Extensions.Options;

namespace DeveloperWellness.IntegrationTests;

/// <summary>
/// Verifies <see cref="DemoAiInsightService"/> (T034): always available, deterministic, within the
/// roughly 120-word bound for every seeded case and its fallbacks, and honest about no-activity subjects.
/// Also verifies <see cref="FoundryAiInsightService"/> (T033) degrades to unavailable when unconfigured,
/// without ever attempting a network call.
/// </summary>
public class DemoAiInsightTests
{
    private static readonly DeveloperLogin NovaLogin = new("nova-stardust-demo");
    private static readonly DeveloperLogin RemyLogin = new("remy-afterglow-demo");
    private static readonly DeveloperLogin JuniperLogin = new("juniper-dataforge-demo");
    private static readonly DeveloperLogin MarloweLogin = new("marlowe-critique-demo");
    private static readonly DeveloperLogin RiverLogin = new("river-hurrybrook-demo");
    private static readonly DeveloperLogin DexLogin = new("dex-quietstorm-demo");
    private static readonly DeveloperLogin GenericLogin = new("flynn-circuitry-demo");

    private const string PulseApiProject = "pulse-api-demo";
    private const string GenericProject = "aurora-mobile-demo";

    private static int CountWords(string text) =>
        text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

    private static SummaryGrounding DeveloperGrounding(
        string subjectDescriptor,
        bool hasActivity = true,
        int commitCount = 6,
        int reviewCount = 2,
        int commentCount = 1,
        int prsOpenedCount = 1,
        decimal? outOfHoursCommitShare = 0.10m,
        decimal? outOfHoursPrShare = 0.10m,
        int? distinctProjectCount = 2) =>
        new(
            subjectDescriptor: subjectDescriptor,
            scopeLabel: "organisation",
            periodDays: 14,
            commitCount: hasActivity ? commitCount : 0,
            reviewCount: hasActivity ? reviewCount : 0,
            commentCount: hasActivity ? commentCount : 0,
            prsOpenedCount: hasActivity ? prsOpenedCount : 0,
            outOfHoursCommitShare: hasActivity ? outOfHoursCommitShare : null,
            outOfHoursPrShare: hasActivity ? outOfHoursPrShare : null,
            distinctProjectCount: hasActivity ? distinctProjectCount : null,
            flags: [],
            teamName: "Platform",
            hasActivity: hasActivity);

    private static SummaryGrounding ProjectGrounding(string projectName) =>
        new(
            subjectDescriptor: projectName,
            scopeLabel: projectName,
            periodDays: 14,
            commitCount: 40,
            reviewCount: 15,
            commentCount: 8,
            prsOpenedCount: 6,
            outOfHoursCommitShare: 0.30m,
            outOfHoursPrShare: 0.15m,
            distinctProjectCount: null,
            flags: [],
            teamName: null,
            hasActivity: true);

    public static TheoryData<AiSubject, SummaryGrounding> SeededAndFallbackSubjects()
    {
        var data = new TheoryData<AiSubject, SummaryGrounding>
        {
            { AiSubject.Developer(NovaLogin), DeveloperGrounding("Nova Stardust") },
            { AiSubject.Developer(RemyLogin), DeveloperGrounding("Remy Afterglow") },
            { AiSubject.Developer(JuniperLogin), DeveloperGrounding("Juniper Dataforge", distinctProjectCount: 5) },
            { AiSubject.Developer(MarloweLogin), DeveloperGrounding("Marlowe Critique", commentCount: 13) },
            { AiSubject.Developer(RiverLogin), DeveloperGrounding("River Hurrybrook", prsOpenedCount: 5, commitCount: 8) },
            { AiSubject.Developer(DexLogin), DeveloperGrounding("Dex Quietstorm", hasActivity: false) },
            { AiSubject.Developer(GenericLogin), DeveloperGrounding("Flynn Circuitry") },
            { AiSubject.Project(PulseApiProject), ProjectGrounding(PulseApiProject) },
            { AiSubject.Project(GenericProject), ProjectGrounding(GenericProject) },
        };

        return data;
    }

    [Fact]
    public void IsAvailable_AlwaysReturnsTrue()
    {
        var service = new DemoAiInsightService();

        Assert.True(service.IsAvailable);
    }

    [Fact]
    public async Task SummariseAsync_CalledTwiceWithSameSubjectAndGrounding_ProducesIdenticalText()
    {
        var service = new DemoAiInsightService();
        var subject = AiSubject.Developer(NovaLogin);
        var grounding = DeveloperGrounding("Nova Stardust");

        var first = await service.SummariseAsync(subject, grounding, CancellationToken.None);
        var second = await service.SummariseAsync(subject, grounding, CancellationToken.None);

        Assert.Equal(first.Text, second.Text);
    }

    [Fact]
    public async Task SummariseAsync_AnySubject_ReturnsIsDemoTrue()
    {
        var service = new DemoAiInsightService();

        var summary = await service.SummariseAsync(
            AiSubject.Developer(NovaLogin), DeveloperGrounding("Nova Stardust"), CancellationToken.None);

        Assert.True(summary.IsDemo);
    }

    [Theory]
    [MemberData(nameof(SeededAndFallbackSubjects))]
    public async Task SummariseAsync_ForEverySeededSubjectAndFallback_TextIsAtMost120Words(
        AiSubject subject, SummaryGrounding grounding)
    {
        var service = new DemoAiInsightService();

        var summary = await service.SummariseAsync(subject, grounding, CancellationToken.None);
        var wordCount = CountWords(summary.Text);

        Assert.True(wordCount <= 120, $"Expected at most 120 words but got {wordCount}. Text: {summary.Text}");
        Assert.False(string.IsNullOrWhiteSpace(summary.Text));
    }

    [Fact]
    public async Task SummariseAsync_NoActivityDeveloper_MentionsAbsenceOfActivityWithoutSpeculating()
    {
        var service = new DemoAiInsightService();
        var grounding = DeveloperGrounding("Dex Quietstorm", hasActivity: false);

        var summary = await service.SummariseAsync(AiSubject.Developer(DexLogin), grounding, CancellationToken.None);
        var text = summary.Text;

        Assert.Contains("no recorded activity", text, StringComparison.OrdinalIgnoreCase);

        string[] speculativePhrases = ["vacation", "sick", "quit", "fired", "burnout", "leave", "resign", "on holiday"];
        Assert.All(speculativePhrases, phrase => Assert.DoesNotContain(phrase, text, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Smoke check only: <see cref="DemoAiInsightService.ClassifyToneAsync"/> now classifies rather than
    /// throwing (T038 completes the stub this test used to guard). Full parser and heuristic coverage
    /// lives in <c>ToneClassificationTests</c>.
    /// </summary>
    [Fact]
    public async Task ClassifyToneAsync_NeutralComment_ReturnsOneResultWithoutThrowing()
    {
        var service = new DemoAiInsightService();

        var result = await service.ClassifyToneAsync(
            ["Can you add a short comment explaining the retry logic?"], CancellationToken.None);

        Assert.Single(result);
        Assert.Equal(ToneClass.Neutral, result[0]);
    }

    [Fact]
    public void FoundryAiInsightService_WithEmptyOptions_IsAvailableReturnsFalse()
    {
        var service = new FoundryAiInsightService(Options.Create(new AiOptions()));

        Assert.False(service.IsAvailable);
    }

    [Fact]
    public async Task FoundryAiInsightService_SummariseAsync_WithEmptyOptions_ThrowsAiInsightExceptionWithoutNetworkCall()
    {
        var service = new FoundryAiInsightService(Options.Create(new AiOptions()));
        var grounding = DeveloperGrounding("Nova Stardust");

        var exception = await Assert.ThrowsAsync<AiInsightException>(
            () => service.SummariseAsync(AiSubject.Developer(NovaLogin), grounding, CancellationToken.None));

        Assert.Equal("The summary service isn't configured.", exception.Message);
    }
}
