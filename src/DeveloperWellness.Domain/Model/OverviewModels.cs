namespace DeveloperWellness.Domain.Model;

/// <summary>
/// A supportive suggestion for a manager, derived from one flagged developer's leading wellbeing signal
/// (data-model.md Recommendation; FR-037). Built by <see cref="Signals.RecommendationMapper"/>. Never an
/// instruction, never a score (design contract principle 1): there is no severity or ranking field here,
/// only the developer, their team, the suggested action, and a short reason.
/// </summary>
public sealed record Recommendation(Developer Developer, string TeamName, string Action, string Reason);

/// <summary>
/// Weekly commit activity over recent weeks with a plain-language change statement (data-model.md
/// DevelopmentTrend; FR-038). Built by <see cref="Signals.TrendCalculator"/>. Organisation scope only; a
/// statement, never a target.
/// </summary>
public sealed record DevelopmentTrend(IReadOnlyList<int> WeeklyCommits, string ChangeStatement);

/// <summary>
/// One of a <see cref="TeamCard"/>'s up to three most-flagged members (ui-design.md 4.1; FR-036).
/// </summary>
/// <remarks>
/// <see cref="FlagCount"/> exists only to order and label this chip ("{n} signals") within its own card;
/// it is never surfaced as a score or used to rank people against each other across cards or teams
/// (FR-029, design contract principle 1).
/// </remarks>
public sealed record TopFlaggedMember(Developer Developer, int FlagCount);

/// <summary>
/// One Overview Teams-section card: a team's size, activity sparkline, and headline measures
/// (data-model.md TeamCard; FR-036; ui-design.md 4.1). Built by <see cref="Signals.TeamCardBuilder"/>,
/// which documents exactly when each nullable measure is null.
/// </summary>
public sealed record TeamCard(
    string Name,
    int Size,
    IReadOnlyList<int> WeeklySeries,
    decimal? AfterHoursShare,
    decimal? AvgProjectsInFlight,
    decimal? AvgReviewsPerDev,
    IReadOnlyList<TopFlaggedMember> TopFlagged);
