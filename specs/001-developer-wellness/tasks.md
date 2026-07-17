# Tasks: Pulse, the Developer Wellness Platform

**Input**: Design documents from `/specs/001-developer-wellness/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/application-ports.md, contracts/ui-design.md (the binding Pulse design contract), quickstart.md

**Tests**: Included per research R8: unit tests for Domain signal calculators and a `WebApplicationFactory` smoke suite in demo mode. No database, no Testcontainers.

**Organization**: Tasks are grouped by user story (US1..US11 map to spec priorities; US11 builds inside the demo backbone per its build-order note). File seams follow contracts/application-ports.md so `[P]` tasks are safe for parallel subagents (research R9). Regenerated 2026-07-17 after the Pulse design alignment; supersedes the previous list, no tasks were complete.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: US1..US11, mapping to spec.md user stories
- Every task names its exact file path(s)

## Phase 1: Setup (Shared Infrastructure)

- [ ] T001 Create `DeveloperWellness.slnx` with projects `src/DeveloperWellness.Domain`, `src/DeveloperWellness.Application`, `src/DeveloperWellness.Infrastructure`, `src/DeveloperWellness.Web` (Blazor Server), `tests/DeveloperWellness.UnitTests` (xUnit), `tests/DeveloperWellness.IntegrationTests` (xUnit); references: Application→Domain, Infrastructure→Application, Web→Application+Infrastructure, tests→respective sources per plan.md
- [ ] T002 Add NuGet packages: `Octokit`, `Microsoft.Extensions.AI`, `Microsoft.Extensions.AI.OpenAI`, `Azure.AI.OpenAI` to `src/DeveloperWellness.Infrastructure/DeveloperWellness.Infrastructure.csproj`; `Microsoft.AspNetCore.Mvc.Testing` to `tests/DeveloperWellness.IntegrationTests/DeveloperWellness.IntegrationTests.csproj`
- [ ] T003 [P] Create repo-root `.gitignore` (.NET patterns) and `src/DeveloperWellness.Web/appsettings.json` with `Wellness` defaults from data-model.md (DemoMode true, working hours 09:00-18:00 Mon-Fri, OrganisationTimeZone, OutOfHoursThreshold 0.25, MinPrEvents 3, SpreadThinThreshold 4, NegativeToneThreshold 0.20, MinAnalysedComments 10, ChangesRequestedThreshold 0.40, MinPrSample 3, RepoCap 10, BranchCap 20, ToneCommentCap 200, TrendWeeks 12, PeriodDaysDefault 14)

---

## Phase 2: Foundational (Blocking Prerequisites)

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [ ] T004 [P] Domain primitives (`DeveloperLogin`, `ScopeKey`, `Period`, `FlagKind`, `ToneClass`, `ReviewState`, `AiSubject`, `WellbeingFlag`) per data-model.md in `src/DeveloperWellness.Domain/Model/Primitives.cs`
- [ ] T005 [P] Domain entities (`Developer`, `Project`, `Team`, `ActivityEvent` hierarchy: `CommitEvent`/`ReviewEvent`/`CommentEvent`/`PrOpenedEvent`, `ActivityDataset` including teams and weekly commit counts) per data-model.md in `src/DeveloperWellness.Domain/Model/Entities.cs`
- [ ] T006 [P] `WellnessOptions` record with start-up validation (threshold ranges, caps and guards >= 1, hours ordering, timezone id resolution) in `src/DeveloperWellness.Domain/Options/WellnessOptions.cs`
- [ ] T007 Application ports per contracts/application-ports.md: `IActivitySource`, `IAiInsightService`, `SummaryGrounding`, `ActivitySourceException`, `AiInsightException` in `src/DeveloperWellness.Application/Ports/` (depends on T004, T005)
- [ ] T008 `DemoActivitySource` implementing `IActivitySource` with a fixed-seed deterministic dataset per the Pulse design: fictitious identities across five teams, seeded overwork (commits and PR-activity) cases, spread-thin case, frustrated-commenter case (>= 10 comments), possible-rushing case, no-activity members, an unmatched-author event, and a 12-week weekly commit series (FR-013, research R5) in `src/DeveloperWellness.Infrastructure/Demo/DemoActivitySource.cs`
- [ ] T009 Web shell per ui-design.md sections 2-3: `src/DeveloperWellness.Web/Program.cs` (options binding + validation, `IMemoryCache`, one-time wiring of the two DI extension methods), DI extension files `src/DeveloperWellness.Application/DependencyInjection.cs` and `src/DeveloperWellness.Infrastructure/DependencyInjection.cs` (demo/live adapter selection by `WellnessOptions.DemoMode`; later service and adapter tasks extend these files, never Program.cs), and `src/DeveloperWellness.Web/Components/Layout/MainLayout.razor` (Pulse branding and nav with roster count badge, hidden until US4 provides its source, scope selector, period buttons 7/14/30, demo banner, connection status and freshness chips, runtime badge, coverage line, credentials-missing "Connect Pulse to GitHub" full-page state with re-check, rate-limit banner; alert pill slot added in US5)

**Checkpoint**: Foundation ready; user stories can start, several in parallel

---

## Phase 3: User Story 1 - Per-developer activity summary (Priority: P1) 🎯 MVP

**Goal**: Live per-developer commits, reviews, and comments with period switching, on the Team overview table

**Independent Test**: Quickstart scenario 1: open `/team` in demo mode; every roster member listed with sortable columns; period switch refreshes counts; no-activity group present; live-mode spot check matches GitHub

- [ ] T010 [P] [US1] `ActivityAggregator` (per-developer commit/review/comment counts, review = each submission, no-activity grouping, unmatched bucket, bot exclusion) in `src/DeveloperWellness.Domain/Signals/ActivityAggregator.cs`
- [ ] T011 [P] [US1] Unit tests for `ActivityAggregator` (counts, SHA-dedup input, zero-activity grouping, bot exclusion) in `tests/DeveloperWellness.UnitTests/ActivityAggregatorTests.cs`
- [ ] T012 [US1] `DashboardQueryService` (fetch-or-cache dataset by scope+period per FR-021, keep-last-data with load time and automatic periodic retry after rate-limit failures per FR-011, the shell banner binds to this state) in `src/DeveloperWellness.Application/Services/DashboardQueryService.cs`, registered in `src/DeveloperWellness.Application/DependencyInjection.cs` (depends on T007, T008)
- [ ] T013 [P] [US1] `GitHubActivitySource` via Octokit per research R2: org members and teams (`read:org`, first-team-alphabetically assignment, no-team fallback), repos ordered by push with RepoCap (10), commits on all branches with BranchCap and SHA dedup preserving author-local offsets, PRs updated in period with reviews (state + submitted_at), issue and PR review comments since period start, PR opened events, weekly commit counts via the participation statistics endpoint (TrendWeeks), bot filtering; user-presentable `ActivitySourceException` for credential and rate-limit failures; plus `GitHubOptions` in `src/DeveloperWellness.Infrastructure/GitHub/GitHubActivitySource.cs` and `src/DeveloperWellness.Infrastructure/GitHub/GitHubOptions.cs`
- [ ] T014 [US1] Team overview page (sortable columns, flagged-first default ordering caption "never a ranking", no-activity group "still part of the team", unmatched-activity line, timezone and conversation-prompt captions, loading/empty/error states, period wiring) per ui-design.md 4.2 in `src/DeveloperWellness.Web/Components/Pages/TeamOverview.razor` (serves as the root page until T030 lands the Pulse Overview at `/`, then moves to `/team`)
- [ ] T015 [US1] Integration smoke test: `WebApplicationFactory` with demo mode on renders the Team overview with the seeded roster and no-activity group in `tests/DeveloperWellness.IntegrationTests/OverviewSmokeTests.cs`

**Checkpoint**: MVP demonstrable

---

## Phase 4: User Story 2 - Time-of-commit overwork signal (Priority: P2)

**Goal**: Out-of-hours commit share in author-local time with overwork flags and the commit heatmap

**Independent Test**: Quickstart scenario 2: seeded overwork developer shows heatmap, share above 25 percent, and a reasoned flag

- [ ] T016 [P] [US2] `OutOfHoursCommitCalculator` (author-local offset, organisation-timezone fallback, weekend handling, threshold flag with the design's observation-context-suggestion reason) in `src/DeveloperWellness.Domain/Signals/OutOfHoursCommitCalculator.cs`
- [ ] T017 [P] [US2] Unit tests (inside/outside 09:00-18:00, weekend, offset fallback, boundary at exactly 25 percent) in `tests/DeveloperWellness.UnitTests/OutOfHoursCommitCalculatorTests.cs`
- [ ] T018 [US2] Wire `OutOfHoursCommitCalculator` into summary building in `src/DeveloperWellness.Application/Services/DashboardQueryService.cs`; `FlagChip` shared component (icon + label chip revealing the reason on hover) in `src/DeveloperWellness.Web/Components/Shared/FlagChip.razor` and OOH-commits column (amber above threshold) + chips on `src/DeveloperWellness.Web/Components/Pages/TeamOverview.razor`
- [ ] T019 [US2] Developer detail page `/developer/{login}` (back link, header with team and scope, stat tiles, hours-by-weekday commit heatmap in author-local time with legend, out-of-hours share bar with 25 percent marker and caption) per ui-design.md 4.4 in `src/DeveloperWellness.Web/Components/Pages/DeveloperDetail.razor`

---

## Phase 5: User Story 3 - Organisation versus project scope (Priority: P3)

**Goal**: Scope switching with the coverage line, projects-in-flight counts, spread-thin flags, and project detail

**Independent Test**: Quickstart scenario 3: project scope reduces stats to that project; organisation scope shows Projects column and the seeded spread-thin flag

- [ ] T020 [P] [US3] `SpreadThinCalculator` (distinct projects with activity, threshold >= 4, organisation scope only) with unit tests in `src/DeveloperWellness.Domain/Signals/SpreadThinCalculator.cs` and `tests/DeveloperWellness.UnitTests/SpreadThinCalculatorTests.cs`
- [ ] T021 [US3] Project-scope recomputation and `SpreadThinCalculator` wiring in `src/DeveloperWellness.Application/Services/DashboardQueryService.cs`; scope selector wiring, coverage line, and Projects column (organisation scope only) in `src/DeveloperWellness.Web/Components/Layout/MainLayout.razor` and `src/DeveloperWellness.Web/Components/Pages/TeamOverview.razor`
- [ ] T022 [US3] Project detail page `/project/{name}` (stat tiles, contributors flagged-first with activity lines and flags, "n of m carry a flag" note, spread-thin caption) per ui-design.md 4.5 in `src/DeveloperWellness.Web/Components/Pages/ProjectDetail.razor`

**Checkpoint**: Core dashboard (P1-P3) complete

---

## Phase 6: User Story 4 - Check-in roster (Priority: P4)

**Goal**: The consolidated "who needs a check-in and why" view

**Independent Test**: Quickstart scenario 4: count headline, ordering by concurrent signals, readable reasons, positive empty state

- [ ] T023 [P] [US4] `CheckInComposer` (flags → `CheckInStatus`, scope-aware per FR-026, ordering most-flags-first then alphabetical) with unit tests in `src/DeveloperWellness.Domain/Signals/CheckInComposer.cs` and `tests/DeveloperWellness.UnitTests/CheckInComposerTests.cs`
- [ ] T024 [US4] `CheckInService` composing statuses from summaries (tone input optional, absence noted per SC-009) in `src/DeveloperWellness.Application/Services/CheckInService.cs`
- [ ] T025 [US4] Check-in roster page `/checkins` (count headline, sub with scope and ordering note plus project-scope spread-thin caption, entries with team and all reasons and "View {name}'s detail →", tone-unavailable note, positive empty state per the design's wording) per ui-design.md 4.3 in `src/DeveloperWellness.Web/Components/Pages/CheckIns.razor`

---

## Phase 7: User Story 5 - In-app check-in alert (Priority: P5)

**Goal**: The alert pill: "{N} people newly need a check-in", clears on roster view, keyed per scope and period

**Independent Test**: Quickstart scenario 5 lifecycle (appear, clear, stay clear, reappear)

- [ ] T026 [P] [US5] `CheckInAlertService` (circuit-scoped `SeenFlagged` state machine keyed by scope and period per data-model.md) with unit tests in `src/DeveloperWellness.Application/Services/CheckInAlertService.cs` and `tests/DeveloperWellness.UnitTests/CheckInAlertServiceTests.cs`
- [ ] T027 [US5] Alert pill in the shell (count text linking `/checkins`, mark-seen on roster view) in `src/DeveloperWellness.Web/Components/Layout/MainLayout.razor`

---

## Phase 8: User Story 11 - Pulse Overview landing page (build order: here, after US4/US5)

**Goal**: The landing Overview per ui-design.md 4.1: KPI tiles, projects table, Teams section, Recommendations for managers, development trend, sentiment reading

**Independent Test**: Quickstart scenario 11: all six Overview elements visible at `/` with zero navigation; a projects-table row switches scope and opens project detail

- [ ] T028 [P] [US11] Overview domain builders with unit tests: `RecommendationMapper` (leading-flag action mapping per FR-037), `TrendCalculator` (weekly series, change statement, steep-ramp caution per FR-038), team-card aggregation (after-hours, projects in flight, reviews per dev, top-flagged per FR-036) in `src/DeveloperWellness.Domain/Signals/OverviewBuilders.cs` and `tests/DeveloperWellness.UnitTests/OverviewBuildersTests.cs`
- [ ] T029 [US11] `OverviewService` composing the `OrganisationOverview` snapshot (KPI tiles including might-need-check-in with new-since-viewed note, project rows ordered by commits with signal notes, team cards, recommendations, trend; sentiment placeholder until US9; the after-hours-PR KPI consumes `PrAfterHoursCalculator` when T031 has landed, otherwise shows its placeholder until T032) in `src/DeveloperWellness.Application/Services/OverviewService.cs`, registered in `src/DeveloperWellness.Application/DependencyInjection.cs`
- [ ] T030 [US11] Pulse Overview landing page `/` per ui-design.md 4.1 (headline "{Scope} at a glance", roster call-to-action, KPI tiles, projects table with scope-switching rows, Teams cards, Recommendations with empty state, trend sparkline, sentiment reading with unavailable state; scope-conditional per spec: Teams and trend render at organisation scope only, and the projects-per-developer KPI swaps to Contributors at project scope) in `src/DeveloperWellness.Web/Components/Pages/Overview.razor`; nav updated in `src/DeveloperWellness.Web/Components/Layout/MainLayout.razor`; `/` route moved off `src/DeveloperWellness.Web/Components/Pages/TeamOverview.razor` to `/team`

**Checkpoint**: Demo backbone complete (US1-US5 plus the Overview)

---

## Phase 9: User Story 6 - PR activity after hours (Priority: P6)

**Goal**: Out-of-hours share of PR opens and review submissions, with the 3-event guard, feeding the roster

**Independent Test**: Quickstart scenario 6: seeded late reviewer shows the share and an after-hours PR flag; nobody with fewer than 3 PR events is flagged

- [ ] T031 [P] [US6] `PrAfterHoursCalculator` (organisation timezone basis, threshold flag with reason, minimum 3 PR events guard per FR-025) with unit tests in `src/DeveloperWellness.Domain/Signals/PrAfterHoursCalculator.cs` and `tests/DeveloperWellness.UnitTests/PrAfterHoursCalculatorTests.cs`
- [ ] T032 [US6] Wire `PrAfterHoursCalculator` into summary building in `src/DeveloperWellness.Application/Services/DashboardQueryService.cs`; OOH-PRs column on `src/DeveloperWellness.Web/Components/Pages/TeamOverview.razor` and PR-share bar with organisation-timezone caption on `src/DeveloperWellness.Web/Components/Pages/DeveloperDetail.razor`

---

## Phase 10: User Story 7 - AI project summary (Priority: P7)

**Goal**: On-request grounded project summary via the Foundry GPT deployment behind `IChatClient`

**Independent Test**: Quickstart scenario 7 (project half): under 10 seconds or unavailable state; roughly 120 words; exact AI label; session cache on repeat

- [ ] T033 [P] [US7] `FoundryAiInsightService` implementing `SummariseAsync` and `IsAvailable` via `IChatClient` (Microsoft.Extensions.AI over Azure.AI.OpenAI per research R3; supportive prompt, roughly 120-word bound) plus `AiOptions` and DI registration in `src/DeveloperWellness.Infrastructure/Ai/FoundryAiInsightService.cs`, `src/DeveloperWellness.Infrastructure/Ai/AiOptions.cs`
- [ ] T034 [P] [US7] `DemoAiInsightService` (canned deterministic summaries and tone results for the seeded dataset; no network) in `src/DeveloperWellness.Infrastructure/Demo/DemoAiInsightService.cs`
- [ ] T035 [US7] `AiSummaryService` (builds `SummaryGrounding` from aggregates only per FR-022, session caching per FR-021) in `src/DeveloperWellness.Application/Services/AiSummaryService.cs`
- [ ] T036 [US7] `AiSummaryPanel` shared component (idle "Nothing runs until you ask", loading "usually under 10 seconds", ready with label `AI-generated · {scope} · last {N} days` and Refresh, down, no-activity states) per ui-design.md 4.4/4.5 in `src/DeveloperWellness.Web/Components/Shared/AiSummaryPanel.razor`, integrated into `src/DeveloperWellness.Web/Components/Pages/ProjectDetail.razor`

---

## Phase 11: User Story 8 - AI developer summary (Priority: P8)

**Goal**: Supportive on-request summary of a developer

**Independent Test**: Quickstart scenario 7 (developer half): flagged signals mentioned supportively; no-activity handled without speculation

- [ ] T037 [US8] Developer grounding in `src/DeveloperWellness.Application/Services/AiSummaryService.cs` and `AiSummaryPanel` integration into `src/DeveloperWellness.Web/Components/Pages/DeveloperDetail.razor`

---

## Phase 12: User Story 9 - Frustration signal and sentiment reading (Priority: P9)

**Goal**: Per-author tone whose surfaces are the roster frustration mention and the Overview sentiment reading; no tone page, no per-project tone

**Independent Test**: Quickstart scenario 8: seeded frustrated developer's roster entry carries the frustration reason with analysed-sample note; Overview sentiment reading shows the distribution; no per-comment verdicts anywhere

- [ ] T038 [US9] Tone classification: batched `ClassifyToneAsync` (25 per call, strict JSON, unparseable → `Unanalysed`, partial-prefix return per FR-020) in `src/DeveloperWellness.Infrastructure/Ai/FoundryAiInsightService.cs` and canned tone results in `src/DeveloperWellness.Infrastructure/Demo/DemoAiInsightService.cs` (same files as T033/T034: run after Phase 10)
- [ ] T039 [P] [US9] `ToneAggregator` (per-author distributions with the 10-analysed-comments guard per FR-019, 20 percent threshold, analysed-versus-total figures, plus the organisation-level `SentimentReading` per FR-039) with unit tests in `src/DeveloperWellness.Domain/Signals/ToneAggregator.cs` and `tests/DeveloperWellness.UnitTests/ToneAggregatorTests.cs`
- [ ] T040 [US9] `ToneAnalysisService` (most-recent-first selection with ToneCommentCap, aggregate building, aggregates cached per scope and period per FR-021) in `src/DeveloperWellness.Application/Services/ToneAnalysisService.cs`
- [ ] T041 [US9] Tone surfacing: frustration mentions into check-in reasons (design wording, analysed-sample note) in `src/DeveloperWellness.Application/Services/CheckInService.cs` and the roster frustration paragraph in `src/DeveloperWellness.Web/Components/Pages/CheckIns.razor`; sentiment reading into `src/DeveloperWellness.Application/Services/OverviewService.cs` and the Overview sentiment tile and panel in `src/DeveloperWellness.Web/Components/Pages/Overview.razor`; no tone page or route (FR-018)

---

## Phase 13: User Story 10 - Quality versus quantity (Priority: P10)

**Goal**: Volume beside rework proxies with the two-condition possible-rushing flag; no score, no index, no ranking

**Independent Test**: Quickstart scenario 9: side-by-side table, below-sample note, rushing flag only on the seeded both-conditions case

- [ ] T042 [P] [US10] `RushingCalculator` producing `QualityQuantitySnapshot` (changes-requested share, average review rounds, minimum sample 3, above-median AND above 40 percent rule) with unit tests in `src/DeveloperWellness.Domain/Signals/RushingCalculator.cs` and `tests/DeveloperWellness.UnitTests/RushingCalculatorTests.cs`
- [ ] T043 [US10] `QualityQuantityService` in `src/DeveloperWellness.Application/Services/QualityQuantityService.cs`
- [ ] T044 [US10] Quality page `/quality` (columns per the design, "Volume and rework look in step" steady state, below-sample note, closing pace-pressure caption) per ui-design.md 4.6 in `src/DeveloperWellness.Web/Components/Pages/Quality.razor`, and rushing-flag input into `src/DeveloperWellness.Application/Services/CheckInService.cs` (after T041)

---

## Phase 14: Polish & Cross-Cutting Concerns

- [ ] T045 [P] Integration smoke tests for `/`, `/checkins`, `/quality`, and the AI panels with the seeded demo cases (SC-013 assertions: Overview with KPI tiles, projects table, team cards, at least one recommendation, trend, sentiment reading; overwork, spread-thin, two roster entries, alert pill, one frustration mention, one project summary and one developer summary) in `tests/DeveloperWellness.IntegrationTests/PagesSmokeTests.cs`
- [ ] T046 Run all eleven quickstart.md validation scenarios in demo mode; fix any gaps found (touches whichever files fail)
- [ ] T047 Design-contract compliance pass against contracts/ui-design.md section 7 (AI label pattern, captions, coverage line, state completeness, no person ranking, no per-comment or per-project tone, no colour-only flags, WCAG AA contrast) across `src/DeveloperWellness.Web/Components/`

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: T001 → T002; T003 parallel with T002
- **Foundational (Phase 2)**: after Setup. T004, T005, T006 in parallel; T007 after T004+T005; T008 after T007; T009 after T006+T007+T008. BLOCKS all stories
- **US1 (Phase 3)**: after Foundational; everything else consumes its dataset path
- **US2, US3 (Phases 4-5)**: after US1 (both extend TeamOverview.razor and DashboardQueryService)
- **US4 (Phase 6)**: after US2+US3 (composes their flags). **US5 (Phase 7)**: after US4
- **US11 (Phase 8)**: after US4+US5 (needs check-in counts and seen-state); T028 can run parallel to US5
- **US6 (Phase 9)**: T031 (Domain-only) may run parallel with Phase 8; T032 waits for US11 (the TeamOverview.razor route move lands in T030)
- **US7 (Phase 10)**: T033/T034 can start any time after T007; T035/T036 after US3 (ProjectDetail exists)
- **US8 (Phase 11)**: after US7 and US2. **US9 (Phase 12)**: after US7 (AI service files), US4 (CheckInService), and US11 (OverviewService)
- **US10 (Phase 13)**: after US9 (CheckInService order). **Polish (Phase 14)**: after all desired stories

### Sequential-touch files (never assign to concurrent subagents)

- `src/DeveloperWellness.Application/DependencyInjection.cs`: T009 → T012 → T024 → T026 → T029 → T035 → T040 → T043 (each service task appends its registration)
- `src/DeveloperWellness.Infrastructure/DependencyInjection.cs`: T009 → T013 → T033 → T034 (each adapter task appends its registration)
- `src/DeveloperWellness.Web/Program.cs`: T009 only; later tasks never edit it
- `src/DeveloperWellness.Application/Services/DashboardQueryService.cs`: T012 → T018 → T021 → T032
- `src/DeveloperWellness.Application/Services/CheckInService.cs`: T024 → T041 → T044
- `src/DeveloperWellness.Application/Services/OverviewService.cs`: T029 → T041
- `src/DeveloperWellness.Web/Components/Pages/Overview.razor`: T030 → T041
- `src/DeveloperWellness.Web/Components/Pages/TeamOverview.razor`: T014 → T018 → T021 → T030 (route move) → T032
- `src/DeveloperWellness.Web/Components/Layout/MainLayout.razor`: T009 → T021 → T027 → T030
- `src/DeveloperWellness.Web/Components/Pages/CheckIns.razor`: T025 → T041
- `src/DeveloperWellness.Infrastructure/Ai/FoundryAiInsightService.cs` and `src/DeveloperWellness.Infrastructure/Demo/DemoAiInsightService.cs`: T033/T034 → T038

### Cross-story parallel opportunities (multi-agent)

After Foundational completes, these run concurrently on separate subagents (disjoint files):

- **Lane A (core UI)**: US1 → US2 → US3 → US4 → US5 → US11
- **Lane B (GitHub adapter)**: T013 alongside Lane A's T010-T012
- **Lane C (AI adapters)**: T033 + T034 any time after T007; T035/T036 once ProjectDetail exists
- Within any story, all `[P]` tasks launch together

## Parallel Example: after Foundational

```text
Subagent 1: T010, T011, T012 then T014   (US1 domain + service + page)
Subagent 2: T013                          (GitHub adapter, independent files)
Subagent 3: T033, T034                    (AI adapters, independent files)
```

## Implementation Strategy

- **MVP first**: Phases 1-3 (T001-T015) deliver the demonstrable P1 slice. Stop and validate quickstart scenario 1.
- **Demo backbone**: Phases 4-8 (US2-US5 plus the US11 Overview) complete the story the demo hinges on. This is the 60-minute target; within Phase 8, the Teams, Recommendations, trend, and sentiment panels are the first candidates to stub if the clock bites, since the KPI tiles and projects table alone satisfy a minimal FR-035.
- **Time-permitting**: Phases 9-13 in order; each story is independently demonstrable. US6 and US7 are the best value-per-minute next picks.
- **Every task** is implemented by a Sonnet 5 `scaffold-implementer` subagent per the repository model policy, against the scaffold completeness checklist (EF items N/A per plan; entry point = routed Blazor page or shell component; validation = options validation and input guards; Result-pattern via typed exceptions mapped to the design contract's states).
