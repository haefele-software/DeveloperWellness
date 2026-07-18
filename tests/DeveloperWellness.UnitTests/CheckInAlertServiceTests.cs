using DeveloperWellness.Application.Services;
using DeveloperWellness.Domain.Model;

namespace DeveloperWellness.UnitTests;

/// <summary>
/// Unit tests for <see cref="CheckInAlertService"/> (tasks.md T026): the full FR-031 seen/unseen
/// lifecycle (initial unseen count, clearing on <see cref="CheckInAlertService.MarkSeen"/>, staying clear
/// for an unchanged roster, reappearing only for a newly flagged login), independent seen-state per scope
/// and per period, the empty-roster edge case, and the <see cref="CheckInAlertService.Changed"/>
/// notification.
/// </summary>
public class CheckInAlertServiceTests
{
    private static readonly ScopeKey OrgScope = ScopeKey.Organisation;
    private static readonly ScopeKey ProjectScope = ScopeKey.Project("pulse-api-demo");

    private static DeveloperLogin Login(string login) => new(login);

    [Fact]
    public void UnseenCount_BeforeAnyMarkSeen_ReturnsEveryCurrentlyFlaggedLogin()
    {
        var service = new CheckInAlertService();
        var flagged = new[] { Login("alice"), Login("bob") };

        var unseen = service.UnseenCount(OrgScope, 14, flagged);

        Assert.Equal(2, unseen);
    }

    [Fact]
    public void UnseenCount_WithEmptyFlaggedCollection_ReturnsZero()
    {
        var service = new CheckInAlertService();

        var unseen = service.UnseenCount(OrgScope, 14, []);

        Assert.Equal(0, unseen);
    }

    [Fact]
    public void MarkSeen_ThenUnseenCountForTheSameRoster_ReturnsZero()
    {
        var service = new CheckInAlertService();
        var flagged = new[] { Login("alice"), Login("bob") };

        service.MarkSeen(OrgScope, 14, flagged);
        var unseen = service.UnseenCount(OrgScope, 14, flagged);

        Assert.Equal(0, unseen);
    }

    [Fact]
    public void MarkSeen_CalledAgainWithTheSameRoster_StaysClear()
    {
        var service = new CheckInAlertService();
        var flagged = new[] { Login("alice"), Login("bob") };

        service.MarkSeen(OrgScope, 14, flagged);
        service.MarkSeen(OrgScope, 14, flagged);
        var unseen = service.UnseenCount(OrgScope, 14, flagged);

        Assert.Equal(0, unseen);
    }

    [Fact]
    public void UnseenCount_AfterMarkSeenWithOneFurtherFlaggedLogin_ReturnsOneForTheNewLoginOnly()
    {
        var service = new CheckInAlertService();
        var original = new[] { Login("alice"), Login("bob") };
        service.MarkSeen(OrgScope, 14, original);

        var withNewlyFlagged = new[] { Login("alice"), Login("bob"), Login("carol") };
        var unseen = service.UnseenCount(OrgScope, 14, withNewlyFlagged);

        Assert.Equal(1, unseen);
    }

    [Fact]
    public void MarkSeen_ForOneScope_DoesNotClearUnseenCountForADifferentScope()
    {
        var service = new CheckInAlertService();
        var flagged = new[] { Login("alice") };

        service.MarkSeen(OrgScope, 14, flagged);
        var unseenForProjectScope = service.UnseenCount(ProjectScope, 14, flagged);

        Assert.Equal(1, unseenForProjectScope);
    }

    [Fact]
    public void MarkSeen_ForOnePeriod_DoesNotClearUnseenCountForADifferentPeriod()
    {
        var service = new CheckInAlertService();
        var flagged = new[] { Login("alice") };

        service.MarkSeen(OrgScope, 14, flagged);
        var unseenForDifferentPeriod = service.UnseenCount(OrgScope, 30, flagged);

        Assert.Equal(1, unseenForDifferentPeriod);
    }

    [Fact]
    public void MarkSeen_Always_RaisesChanged()
    {
        var service = new CheckInAlertService();
        var raised = false;
        service.Changed += () => raised = true;

        service.MarkSeen(OrgScope, 14, [Login("alice")]);

        Assert.True(raised);
    }

    [Fact]
    public void MarkSeen_WithEmptyFlaggedCollection_StillRaisesChanged()
    {
        var service = new CheckInAlertService();
        var raised = false;
        service.Changed += () => raised = true;

        service.MarkSeen(OrgScope, 14, []);

        Assert.True(raised);
    }

    [Fact]
    public void UnseenCount_WithNullScope_ThrowsArgumentNullException()
    {
        var service = new CheckInAlertService();

        Assert.Throws<ArgumentNullException>(() => service.UnseenCount(null!, 14, []));
    }

    [Fact]
    public void UnseenCount_WithNullCurrentFlagged_ThrowsArgumentNullException()
    {
        var service = new CheckInAlertService();

        Assert.Throws<ArgumentNullException>(() => service.UnseenCount(OrgScope, 14, null!));
    }

    [Fact]
    public void MarkSeen_WithNullScope_ThrowsArgumentNullException()
    {
        var service = new CheckInAlertService();

        Assert.Throws<ArgumentNullException>(() => service.MarkSeen(null!, 14, []));
    }

    [Fact]
    public void MarkSeen_WithNullCurrentFlagged_ThrowsArgumentNullException()
    {
        var service = new CheckInAlertService();

        Assert.Throws<ArgumentNullException>(() => service.MarkSeen(OrgScope, 14, null!));
    }
}
