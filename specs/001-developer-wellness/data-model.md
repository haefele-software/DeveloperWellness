# Data Model: Developer Wellness Platform

**Date**: 2026-07-17 | **Spec**: [spec.md](spec.md) | **Research**: [research.md](research.md)

No persistence layer exists (research R4); every type below is an in-memory domain model, most of them immutable C# records. Session-scoped state is explicitly marked. Names are indicative; implementers keep them unless a learned convention dictates otherwise.

## Value types and enums

| Type | Values / shape | Notes |
|------|----------------|-------|
| `DeveloperLogin` | string, non-empty | Canonical identity, the GitHub login; case-insensitive comparisons |
| `ScopeKey` | `Organisation` or `Project(name)` | Selected scope (FR-007) |
| `Period` | 7, 14, or 30 days; `Start`, `End` timestamps | Default 14 (FR-009) |
| `FlagKind` | `OverworkCommits`, `OverworkPrActivity`, `SpreadThin`, `NegativeTone`, `PossibleRushing` | FR-026 |
| `ToneClass` | `Positive`, `Neutral`, `Negative`, `Unanalysed` | Unparseable results become `Unanalysed`, never `Negative` (FR-018, edge case) |
| `ReviewState` | `Approved`, `ChangesRequested`, `Commented` | Feeds FR-027 rework proxies |
| `AiSubject` | `Project(name)` or `Developer(login)` | FR-015, FR-016 |

## Core entities

### Developer
- `Login: DeveloperLogin` (unique within the organisation)
- `DisplayName: string` (falls back to login)
- `IsBot: bool` (excluded everywhere when true, FR-010)
- Validation: bots never appear in summaries, rosters, or tone aggregates.

### Project
- `Name: string` (unique within the organisation)
- `LastPushedAt: DateTimeOffset` (drives the recently-active ordering for the coverage cap, FR-007)

### ActivityEvent (closed hierarchy)
Common: `Author: DeveloperLogin or Unmatched`, `ProjectName: string`, `OccurredAt: DateTimeOffset`.

| Subtype | Extra fields | Source rules |
|---------|--------------|--------------|
| `CommitEvent` | `Sha: string` (dedup key), `HasUsableOffset: bool` | All branches, deduplicated by SHA (FR-002); `OccurredAt` preserves the author-local offset (FR-005) |
| `ReviewEvent` | `PrNumber: int`, `State: ReviewState` | Each submitted review is one event (FR-003) |
| `CommentEvent` | `CommentId: long`, `BodyText: string` | Body held transiently for tone classification only (FR-022); never rendered with a verdict |
| `PrOpenedEvent` | `PrNumber: int` | FR-024 |

Validation: `CommitEvent.Sha` unique per dataset; events outside the period are rejected at ingestion; unmatched authors group under a reserved `Unmatched` marker (edge case).

### ActivityDataset
The fetch result for one scope and period: `Roster: IReadOnlyList<Developer>`, `Projects: IReadOnlyList<Project>`, `Events: IReadOnlyList<ActivityEvent>`, `CoveredProjectNames: IReadOnlyList<string>`, `LoadedAt: DateTimeOffset`, `IsDemoData: bool`. Cached by `(ScopeKey, Period)` (FR-021, R4); `LoadedAt` feeds the stale-data statement (FR-011).

## Derived (computed) models

All computed by pure Domain functions from an `ActivityDataset` plus `WellnessOptions` (R6).

### ActivitySummary (per developer, scope, period)
- `CommitCount, ReviewCount, CommentCount, PrsOpenedCount: int`
- `OutOfHoursCommitShare: decimal?` (author-local basis; null when no commits)
- `OutOfHoursPrShare: decimal?` (organisation-timezone basis, FR-024)
- `DistinctProjectCount: int?` (organisation scope only; null at project scope)
- `Flags: IReadOnlyList<WellbeingFlag>`
- `HasActivity: bool` (false places the developer in the no-activity group, FR-012)

### WellbeingFlag
- `Kind: FlagKind`, `Reason: string` (plain language, mandatory, SC-010)
- Rules (FR-026): OverworkCommits when out-of-hours commit share > 25 percent; OverworkPrActivity when out-of-hours PR share > 25 percent; SpreadThin when distinct projects >= 4 (organisation scope only); NegativeTone when the developer's authored-comment negative share > 20 percent (only when tone available); PossibleRushing per QualityQuantitySnapshot rule.

### ToneAggregate (per scope key: project or comment author)
- `Positive, Neutral, Negative, Unanalysed: int`
- `AnalysedCount, TotalCount: int` (bound statement when `TotalCount > AnalysedCount`, FR-020)
- `NegativeShare: decimal` (over analysed only)
- `Flagged: bool` (NegativeShare > 20 percent, FR-019)

### QualityQuantitySnapshot (per developer)
- `Commits, PrsOpened: int`
- `ChangesRequestedShare: decimal?`, `AvgReviewRounds: decimal?`
- `SufficientSample: bool` (>= 3 PRs, FR-027)
- `PossibleRushing: bool` (volume above roster median AND ChangesRequestedShare > 40 percent AND SufficientSample)

### CheckInStatus (per developer)
- `Developer`, `Flags: IReadOnlyList<WellbeingFlag>`, `NeedsCheckIn: bool` (any flag)
- Roster ordering: flag count descending, then alphabetical (FR-028; design brief 4.1)

### AiSummary
- `Subject: AiSubject`, `Scope: ScopeKey`, `Period`, `Text: string` (roughly 120 words max), `GeneratedAt: DateTimeOffset`, `IsDemo: bool`
- Invariant: rendered only with the label "AI-generated · [scope] · [period]" (FR-017)

## Session-scoped state (per Blazor circuit)

### CheckInAlertState
- `SeenFlagged: HashSet<(ScopeKey, DeveloperLogin)>` per period
- `UnseenCount(scope, period, currentRoster): int` drives the indicator (FR-030)
- Transitions (FR-031): flagged developer not in `SeenFlagged` → counts as new → indicator visible; roster viewed → all current flagged added to `SeenFlagged` → indicator clears; further developer flagged → indicator reappears. State dies with the circuit (assumption: session-scoped).

## Configuration options (bound, validated at start-up)

| Options record | Keys and defaults |
|----------------|-------------------|
| `GitHubOptions` | `Organisation` (required in live mode), `Token` (secret, required in live mode) |
| `AiOptions` | `Endpoint`, `ApiKey` (secret), `DeploymentName`; all optional, absence = AI unavailable state (FR-014) |
| `WellnessOptions` | `DemoMode = true` (R5); `WorkingHoursStart = 08:00`, `WorkingHoursEnd = 18:00`, working days Monday to Friday; `OrganisationTimeZone` (IANA or Windows id, required); `OutOfHoursThreshold = 0.25`; `SpreadThinThreshold = 4`; `NegativeToneThreshold = 0.20`; `ChangesRequestedThreshold = 0.40`; `MinPrSample = 3`; `RepoCap = 25`; `BranchCap = 20`; `ToneCommentCap = 200`; `PeriodDaysDefault = 14` |

Validation rules: thresholds in (0, 1]; caps >= 1; working hours start before end; unknown timezone id fails start-up with a clear message (FR-033, FR-011).
