# Contracts: Application Ports and UI Surface

**Date**: 2026-07-17 | **Spec**: [spec.md](../spec.md) | **Data model**: [data-model.md](../data-model.md)

This application exposes no external API; its outward contract is the Blazor UI (governed by [ui-design.md](ui-design.md)). The binding internal contracts are the two ports below, which decouple the Domain and UI from GitHub and the AI service and make demo mode (FR-013) a pure adapter swap. Implementers MUST NOT let Octokit, Azure, or HTTP types leak through these interfaces.

## Port 1: IActivitySource (Application layer)

```csharp
public interface IActivitySource
{
    Task<ActivityDataset> GetActivityAsync(
        ScopeKey scope,
        Period period,
        CancellationToken cancellationToken);
}
```

- Returns the full dataset (roster, projects, events, covered project names, load time) for the scope and period per data-model.md.
- MUST throw `ActivitySourceException` with a user-presentable message for credential, rate-limit, and connectivity failures (FR-011); callers keep the previous dataset visible.
- MUST apply: repo cap and recently-active ordering (FR-007), branch cap with SHA deduplication (FR-002), bot exclusion (FR-010), full member roster (FR-012), unmatched-author bucketing (edge case).
- Implementations: `GitHubActivitySource` (Octokit, research R2), `DemoActivitySource` (deterministic synthetic data, research R5). Selection by `WellnessOptions.DemoMode` at DI registration.

## Port 2: IAiInsightService (Application layer)

```csharp
public interface IAiInsightService
{
    bool IsAvailable { get; }

    Task<AiSummary> SummariseAsync(
        AiSubject subject,
        SummaryGrounding grounding,   // aggregated stats only, never raw repository content (FR-022)
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ToneClass>> ClassifyToneAsync(
        IReadOnlyList<string> commentBodies,   // batched, caller enforces the 200-comment cap (FR-020)
        CancellationToken cancellationToken);
}
```

- `IsAvailable` is false when unconfigured; every UI surface degrades to the friendly unavailable state and the roster notes absent tone signals (FR-014, SC-009).
- `SummariseAsync` MUST honour the roughly 120-word bound and supportive wording (FR-015 to FR-017); failures throw `AiInsightException` with a user-presentable message.
- `ClassifyToneAsync` returns one `ToneClass` per input in order; unparseable results map to `Unanalysed` (never `Negative`). Partial batch failure returns the classified prefix so the UI can state analysed-versus-total (FR-020).
- Implementations: `FoundryAiInsightService` (`IChatClient` from Microsoft.Extensions.AI over Azure.AI.OpenAI, research R3), `DemoAiInsightService` (canned outputs, research R5).

## Application services (orchestration, thin)

| Service | Responsibility | Key FRs |
|---------|----------------|---------|
| `DashboardQueryService` | Fetch-or-cache dataset, compute `ActivitySummary` list via Domain calculators | FR-002..FR-012, FR-021 |
| `CheckInService` | Compose `CheckInStatus` roster and ordering from summaries plus tone | FR-026..FR-028 |
| `ToneAnalysisService` | Select comments (cap, most recent first), call port 2, build `ToneAggregate`s | FR-018..FR-020 |
| `QualityQuantityService` | Build `QualityQuantitySnapshot`s | FR-027, FR-029 |
| `CheckInAlertService` (circuit-scoped) | Alert lifecycle per data-model.md transitions | FR-030, FR-031 |

Every async method takes and propagates a `CancellationToken`. Failures surface as Result-style outcomes or typed exceptions mapped to the design brief's error states; exceptions are never used for ordinary control flow.

## UI surface (routes)

| Route | Screen (design brief section) | Primary FRs |
|-------|-------------------------------|-------------|
| `/` | Team overview (4.1) | FR-002..FR-012 |
| `/developer/{login}` | Developer detail (4.2) | FR-005, FR-006, FR-016, FR-024, FR-025 |
| `/project/{name}` | Project detail (4.3) | FR-015 |
| `/checkins` | Check-in roster (4.4) | FR-026..FR-028 |
| `/tone` | Tone view (4.5) | FR-018..FR-020 |
| `/quality` | Quality versus quantity (4.6) | FR-027, FR-029 |

Global shell elements (scope switcher, period selector, demo badge, alert indicator, freshness line) per design brief section 3 on every route. Visual and state requirements are contractual via [ui-design.md](ui-design.md).
