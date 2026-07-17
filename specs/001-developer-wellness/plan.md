# Implementation Plan: Developer Wellness Platform

**Branch**: `001-developer-wellness` | **Date**: 2026-07-17 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/001-developer-wellness/spec.md`

## Summary

A Clean Architecture Blazor Server application (.NET 10, C# 14) that reads one GitHub organisation's activity read-only through an Octokit-based adapter, computes wellbeing signals (out-of-hours commits in author-local time, after-hours PR activity, spread-thin project counts, tone flags, possible rushing) as pure Domain functions driven by configurable thresholds, surfaces them on six Blazor routes centred on a check-in roster with a session-scoped in-app alert, and adds AI summaries and PR comment tone classification through a Microsoft Foundry GPT deployment behind the `IChatClient` abstraction of Microsoft.Extensions.AI. Two ports (`IActivitySource`, `IAiInsightService`) isolate all external services; deterministic demo-mode adapters (default on) make the whole product runnable and demonstrable with zero credentials. No persistence: session-scoped memory only. Build is time-boxed for a hackathon and structured so implementation tasks parallelise across multiple Sonnet 5 subagents along port and page seams.

## Technical Context

**Language/Version**: C# 14 on .NET 10

**Primary Dependencies**: ASP.NET Core Blazor Server; Octokit (GitHub REST); Microsoft.Extensions.AI + Microsoft.Extensions.AI.OpenAI + Azure.AI.OpenAI (Foundry GPT via `IChatClient`); Microsoft.Extensions.Caching.Memory; xUnit + Microsoft.AspNetCore.Mvc.Testing (tests)

**Storage**: None. In-memory session-scoped caching only (`IMemoryCache` keyed by scope and period; circuit-scoped alert state). EF Core deliberately absent (research R4); scaffold EF checklist items are N/A with this justification.

**Testing**: xUnit unit tests for Domain signal calculators and options validation; `WebApplicationFactory` smoke suite with demo mode forced on. No Testcontainers (no database, Docker not assumed).

**Target Platform**: Cross-platform ASP.NET Core web server, developed on Windows 11, single instance, trusted internal use, no end-user sign-in (spec assumption).

**Project Type**: Web application (single deployable, Clean Architecture, four projects).

**Performance Goals**: First organisation summary within 2 minutes of setup (SC-001); AI summary within 10 seconds or friendly unavailable state (SC-005); check-in answer within 30 seconds of opening (SC-010); scope or period switch recomputes from cache without reconfiguration (SC-003).

**Constraints**: Roughly 60-minute hackathon build; read-only GitHub access; session-only retention; bounded fetches (25 repositories, 20 branches per repository, 200 tone comments); demo mode must run with zero credentials and zero network access; no external messaging; no ranking or composite scores of developers.

**Scale/Scope**: One organisation, tens of developers, 25 covered repositories per load, periods of 7/14/30 days, six UI routes, two external integrations. GitHub PAT rate limit 5000 requests per hour bounds worst-case fetch comfortably given the caps.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

`.specify/memory/constitution.md` is an unratified template with placeholder principles; no project constitution gates exist. Gate evaluation: **PASS (vacuous)**, pre-Phase-0 and post-Phase-1. In its place, the repository-level binding constraints were applied: the model policy in `CLAUDE.md` (planning on Fable 5, implementation delegated to Sonnet 5 `scaffold-implementer` subagents) and the scaffold completeness checklist of `/speckit-implement-scaffold`, with the EF configuration item pre-justified N/A per the no-persistence decision. Recommend running `/speckit-constitution` after the hackathon if this project outlives it.

## Project Structure

### Documentation (this feature)

```text
specs/001-developer-wellness/
├── plan.md              # This file
├── research.md          # Phase 0 output (R1..R9, all unknowns resolved)
├── data-model.md        # Phase 1 output (entities, derived models, options)
├── quickstart.md        # Phase 1 output (run + validation scenarios)
├── checklists/
│   └── requirements.md  # Spec quality checklist (16/16)
├── contracts/
│   ├── application-ports.md  # IActivitySource, IAiInsightService, services, routes
│   └── ui-design.md          # Binding UI design brief
└── tasks.md             # Phase 2 output (/speckit-tasks - NOT created by /speckit-plan)
```

### Source Code (repository root)

```text
DeveloperWellness.slnx
src/
├── DeveloperWellness.Domain/            # Pure signal logic, no dependencies
│   ├── Model/                           # Developer, Project, ActivityEvent hierarchy, ActivityDataset
│   ├── Signals/                         # OutOfHours, SpreadThin, Rushing, ToneAggregation, CheckInComposition calculators
│   └── Options/                         # WellnessOptions + validation
├── DeveloperWellness.Application/       # Ports + thin orchestration services
│   ├── Ports/                           # IActivitySource, IAiInsightService (+ exceptions)
│   └── Services/                        # DashboardQuery, CheckIn, ToneAnalysis, QualityQuantity, CheckInAlert
├── DeveloperWellness.Infrastructure/    # Adapters only
│   ├── GitHub/                          # GitHubActivitySource (Octokit), GitHubOptions
│   ├── Ai/                              # FoundryAiInsightService (IChatClient), AiOptions, prompts
│   └── Demo/                            # DemoActivitySource, DemoAiInsightService (fixed seed)
└── DeveloperWellness.Web/               # Blazor Server
    ├── Components/Layout/               # Shell: scope switcher, period selector, demo badge, alert indicator, freshness line
    ├── Components/Pages/                # Overview, DeveloperDetail, ProjectDetail, CheckIns, Tone, Quality
    ├── Components/Shared/               # Flag chips, distribution bars, AI panel, state placeholders
    └── Program.cs                       # DI: demo/live adapter selection, options binding + validation

tests/
├── DeveloperWellness.UnitTests/         # Domain calculators, options validation, alert state transitions
└── DeveloperWellness.IntegrationTests/  # WebApplicationFactory smoke suite (demo mode on)
```

**Structure Decision**: Clean Architecture with four source projects and two test projects, exactly as the user specified. Domain has zero references; Application references Domain; Infrastructure references Application (implements its ports); Web references Application and Infrastructure for DI composition only. The Demo adapters live in Infrastructure so demo mode is a registration decision, not a code path inside the UI. Seams for the multi-agent split (research R9): Domain calculators, GitHub adapter, AI adapter, demo adapters, and each Web page are disjoint file sets behind the contracts.

## Complexity Tracking

No constitution gates exist, and no gate violations require justification. The four-project split is the user-mandated Clean Architecture baseline, not added complexity; everything optional beyond the spec (persistence, external messaging, auth) has been excluded rather than justified.
