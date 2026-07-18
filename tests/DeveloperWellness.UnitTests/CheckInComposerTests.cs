using DeveloperWellness.Domain.Model;
using DeveloperWellness.Domain.Signals;

namespace DeveloperWellness.UnitTests;

/// <summary>
/// Unit tests for <see cref="CheckInComposer"/> (tasks.md T023): roster count correctness, the
/// most-flags-first ordering with alphabetical tie-breaks (FR-028), inclusion of unflagged and
/// no-activity developers, and the <c>additionalFlags</c> merge semantics (append, exact-pair dedupe,
/// distinct-reason same-kind retention, and developers present only via <c>additionalFlags</c>).
/// </summary>
public class CheckInComposerTests
{
    private static Developer Dev(string login, string? displayName = null, bool isBot = false) =>
        new(new DeveloperLogin(login), displayName, isBot);

    private static WellbeingFlag Flag(FlagKind kind, string reason) => new(kind, reason);

    private static ActivitySummary Summary(Developer developer, IReadOnlyList<WellbeingFlag>? flags = null, bool hasActivity = true) =>
        new(
            developer: developer,
            commitCount: 0,
            reviewCount: 0,
            commentCount: 0,
            prsOpenedCount: 0,
            outOfHoursCommitShare: null,
            outOfHoursPrShare: null,
            distinctProjectCount: null,
            flags: flags ?? [],
            hasActivity: hasActivity);

    [Fact]
    public void Compose_WithNoFlaggedDevelopers_ReturnsZeroNeedsCheckInCount()
    {
        var summaries = new List<ActivitySummary> { Summary(Dev("alice")), Summary(Dev("bob")) };

        var result = CheckInComposer.Compose(summaries);

        Assert.Equal(0, result.NeedsCheckInCount);
        Assert.Equal(2, result.Roster.Count);
        Assert.All(result.Roster, status => Assert.False(status.NeedsCheckIn));
    }

    [Fact]
    public void Compose_WithDevelopersHavingDifferentFlagCounts_OrdersMostFlagsFirst()
    {
        var alice = Dev("alice"); // one flag
        var bob = Dev("bob"); // two flags
        var carol = Dev("carol"); // no flags

        var summaries = new List<ActivitySummary>
        {
            Summary(alice, [Flag(FlagKind.OverworkCommits, "reason-a")]),
            Summary(bob, [Flag(FlagKind.OverworkCommits, "reason-b1"), Flag(FlagKind.SpreadThin, "reason-b2")]),
            Summary(carol),
        };

        var result = CheckInComposer.Compose(summaries);

        Assert.Equal(["bob", "alice", "carol"], result.Roster.Select(status => status.Developer.Login.Value));
    }

    [Fact]
    public void Compose_WithEqualFlagCountsAmongFlagged_OrdersAlphabeticallyByDisplayName()
    {
        var zed = Dev("zed", displayName: "Zed Zeeman");
        var amy = Dev("amy", displayName: "Amy Anderson");

        var summaries = new List<ActivitySummary>
        {
            Summary(zed, [Flag(FlagKind.OverworkCommits, "reason-z")]),
            Summary(amy, [Flag(FlagKind.SpreadThin, "reason-a")]),
        };

        var result = CheckInComposer.Compose(summaries);

        Assert.Equal(["Amy Anderson", "Zed Zeeman"], result.Roster.Select(status => status.Developer.DisplayName));
    }

    [Fact]
    public void Compose_WithUnflaggedDevelopers_OrdersAlphabeticallyByDisplayName()
    {
        var zed = Dev("zed", displayName: "Zed Zeeman");
        var amy = Dev("amy", displayName: "Amy Anderson");

        var summaries = new List<ActivitySummary> { Summary(zed), Summary(amy) };

        var result = CheckInComposer.Compose(summaries);

        Assert.Equal(["Amy Anderson", "Zed Zeeman"], result.Roster.Select(status => status.Developer.DisplayName));
    }

    [Fact]
    public void Compose_WithFlaggedAndUnflaggedDevelopers_PlacesAllFlaggedBeforeAllUnflagged()
    {
        var alice = Dev("alice");
        var bob = Dev("bob");

        var summaries = new List<ActivitySummary>
        {
            Summary(alice),
            Summary(bob, [Flag(FlagKind.OverworkCommits, "reason-b")]),
        };

        var result = CheckInComposer.Compose(summaries);

        Assert.Equal(["bob", "alice"], result.Roster.Select(status => status.Developer.Login.Value));
        Assert.True(result.Roster[0].NeedsCheckIn);
        Assert.False(result.Roster[1].NeedsCheckIn);
        Assert.Equal(1, result.NeedsCheckInCount);
    }

    [Fact]
    public void Compose_WithNoActivityDeveloper_IncludesThemUnflagged()
    {
        var summaries = new List<ActivitySummary> { Summary(Dev("alice"), flags: [], hasActivity: false) };

        var result = CheckInComposer.Compose(summaries);

        Assert.Single(result.Roster);
        Assert.False(result.Roster[0].NeedsCheckIn);
        Assert.Empty(result.Roster[0].Flags);
        Assert.Equal(0, result.NeedsCheckInCount);
    }

    [Fact]
    public void Compose_WithAdditionalFlags_AppendsThemAfterTheSummarysOwnFlags()
    {
        var alice = Dev("alice");
        var ownFlag = Flag(FlagKind.OverworkCommits, "own-reason");
        var toneFlag = Flag(FlagKind.NegativeTone, "tone-reason");

        var summaries = new List<ActivitySummary> { Summary(alice, [ownFlag]) };
        var additionalFlags = new Dictionary<DeveloperLogin, IReadOnlyList<WellbeingFlag>>
        {
            [alice.Login] = [toneFlag],
        };

        var result = CheckInComposer.Compose(summaries, additionalFlags);

        Assert.Equal([ownFlag, toneFlag], result.Roster[0].Flags);
    }

    [Fact]
    public void Compose_WithAdditionalFlagsDuplicatingAnExistingFlagExactly_DedupesToOneEntry()
    {
        var alice = Dev("alice");

        var summaries = new List<ActivitySummary> { Summary(alice, [Flag(FlagKind.NegativeTone, "same-reason")]) };
        var additionalFlags = new Dictionary<DeveloperLogin, IReadOnlyList<WellbeingFlag>>
        {
            [alice.Login] = [Flag(FlagKind.NegativeTone, "same-reason")],
        };

        var result = CheckInComposer.Compose(summaries, additionalFlags);

        Assert.Single(result.Roster[0].Flags);
    }

    [Fact]
    public void Compose_WithAdditionalFlagsSameKindButDifferentReason_KeepsBothReasons()
    {
        var alice = Dev("alice");

        var summaries = new List<ActivitySummary> { Summary(alice, [Flag(FlagKind.NegativeTone, "reason-1")]) };
        var additionalFlags = new Dictionary<DeveloperLogin, IReadOnlyList<WellbeingFlag>>
        {
            [alice.Login] = [Flag(FlagKind.NegativeTone, "reason-2")],
        };

        var result = CheckInComposer.Compose(summaries, additionalFlags);

        Assert.Equal(2, result.Roster[0].Flags.Count);
        Assert.Contains(result.Roster[0].Flags, flag => flag.Reason == "reason-1");
        Assert.Contains(result.Roster[0].Flags, flag => flag.Reason == "reason-2");
    }

    [Fact]
    public void Compose_WithDeveloperOnlyInAdditionalFlags_StillProducesAStatus()
    {
        var ghostLogin = new DeveloperLogin("ghost");
        var additionalFlags = new Dictionary<DeveloperLogin, IReadOnlyList<WellbeingFlag>>
        {
            [ghostLogin] = [Flag(FlagKind.NegativeTone, "tone-only-reason")],
        };

        var result = CheckInComposer.Compose([], additionalFlags);

        Assert.Single(result.Roster);
        Assert.Equal(ghostLogin, result.Roster[0].Developer.Login);
        Assert.True(result.Roster[0].NeedsCheckIn);
        Assert.Equal(1, result.NeedsCheckInCount);
    }

    [Fact]
    public void Compose_WithEmptySummariesAndNoAdditionalFlags_ReturnsEmptyRosterAndZeroCount()
    {
        var result = CheckInComposer.Compose([]);

        Assert.Empty(result.Roster);
        Assert.Equal(0, result.NeedsCheckInCount);
    }
}
