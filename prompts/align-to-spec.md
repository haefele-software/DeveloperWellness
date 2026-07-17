# Prompt: Align the DeveloperWellness design and implementation to the specification

Copy this entire file into Claude Code (or reference it with `@prompts/align-to-spec.md`) whenever the design or code needs to be brought in line with the specification.

---

You are working in the DeveloperWellness repository. Your task is to adjust the current design and implementation so that they follow the single consolidated specification, correcting any drift and filling any gaps. Do not invent scope beyond the specification.

## Source of truth

1. **Specification**: `specs/001-developer-wellness/spec.md` is the single, consolidated spec (FR-001 to FR-034, SC-001 to SC-013, 7 recorded clarifications). If the design or code disagrees with it, the specification wins.
2. **Design artefacts**: `specs/001-developer-wellness/plan.md`, `data-model.md`, `contracts/`, and `tasks.md` where they exist. If they are missing or stale, regenerate them with the Spec Kit workflow rather than improvising.
3. **Repository policy**: `CLAUDE.md` and `.claude/settings.json` define the model policy. Respect them exactly.

## What the product is

A developer wellness platform for engineering leadership: a Clean Architecture Blazor Server application that connects read-only to one GitHub organisation and surfaces per-developer and per-project activity stats (commits on all branches deduplicated, PR reviews, comments, time-of-commit patterns in the author's local time), wellbeing flags (overwork, spread thin, after-hours PR activity, negative review tone, possible rushing), AI-generated summaries of a project or person, aggregate PR comment tone analysis, a check-in roster ordered by concurrent flags, and an in-app alert when someone newly needs a check-in. A configuration-driven demo mode serves clearly fictitious synthetic data with no external calls. The purpose is supportive care for developers, never surveillance, scoring, or ranking.

## Fixed technology decisions

- Clean Architecture with a Blazor Server presentation layer on .NET.
- GitHub organisation access through a configuration-supplied, organisation-scoped credential, strictly read-only.
- AI capabilities through a Microsoft Foundry (Azure AI Foundry) connection using a GPT-class deployed model, consumed via the Microsoft.Extensions.AI abstractions so the concrete provider stays swappable.
- No persistence layer in the MVP: session-scoped, in-memory data and caches only.
- Configuration carries all secrets; never hard-code credentials, endpoints, or personal data, and never commit real developer names in tests or fixtures.

## Model policy (binding)

- All analysis, planning, and design adjustment in this task runs in the main loop on Fable 5 at maximum reasoning effort.
- All code writing is delegated to Sonnet 5 subagents (prefer the `scaffold-implementer` agent; otherwise `general-purpose` pinned to `claude-sonnet-5`). The main loop reviews, verifies, and integrates; it does not write feature code itself.
- Split independent implementation work across multiple parallel subagents (divide and conquer) whenever their file sets do not overlap.

## Procedure

1. **Read the specification end to end**, including the Clarifications section; the seven recorded decisions there are binding (all-branches commit counting, demo mode, 25-repository coverage cap, author-local timezone with organisation fallback, all-members roster, in-app alert only with no external messaging, and the two-condition possible-rushing rule).
2. **Inventory the current state**: solution layout, projects, layers, endpoints or pages, services, and tests.
3. **Produce a gap matrix** covering every functional requirement FR-001 to FR-034 with one status each: Met, Partial, Missing, or Contradicts. Cite the file or component behind each status.
4. **Resolve the gaps**:
   - If `plan.md` or `tasks.md` are missing or stale, run `/speckit-plan` and then `/speckit-tasks` first.
   - Implement corrections and missing slices with `/speckit-implement-scaffold` (preferred) so every feature-shaped change satisfies the scaffold completeness checklist: entry point, sealed handler or service, meaningful validation, consumer-shaped DTOs, EF configuration where entities exist, tests, API or page metadata, CancellationToken propagation, Result-pattern error handling, and supporting infrastructure.
   - Where the codebase contradicts the specification, change the code, not the specification. If you believe the specification itself is wrong, stop and raise it; do not silently diverge.
5. **Honour the guardrails** in every change: aggregate-only tone display, no composite scores or rankings of developers, supportive non-judgemental wording, AI outputs marked as AI-generated with scope and period, minimal data sent to the AI service, graceful degradation when GitHub or the AI service is unavailable, and full functionality in demo mode without external calls.
6. **Verify**: `dotnet build` and `dotnet test` must pass; walk the acceptance scenarios of the priority stories P1 to P5 against the running application, and spot-check success criteria SC-001, SC-005, SC-010, and SC-011.
7. **Report**: the final gap matrix (before and after), files changed, build and test results, and any specification concerns raised rather than silently resolved.

## Constraints

- Hackathon pace: prefer the smallest change that satisfies the requirement; priorities P1 to P5 are the demo backbone and take precedence when time is short.
- Never weaken the read-only GitHub stance, introduce end-user sign-in, add persistence, or add external messaging; all are explicitly out of scope for the MVP.
- Keep the demo path working at all times: with `DemoMode` enabled the application must run with no credentials and no network access.
