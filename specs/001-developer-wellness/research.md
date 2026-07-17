# Research: Developer Wellness Platform

**Date**: 2026-07-17 | **Spec**: [spec.md](spec.md) | **Plan**: [plan.md](plan.md)

All technical unknowns from the Technical Context are resolved below. No NEEDS CLARIFICATION items remain.

## R1. Runtime, UI framework, and architecture

- **Decision**: .NET 10 with C# 14. Blazor Server (interactive server rendering) as the single deployable web application. Clean Architecture with four projects: Domain, Application, Infrastructure, Web.
- **Rationale**: Fixed by the user's stated choices. Blazor Server gives interactive UI with no separate API surface, which is the fastest route to the 60-minute demo. Clean Architecture keeps GitHub and the AI service behind ports so the demo-mode adapters (FR-013) swap in without touching the UI or domain logic.
- **Alternatives considered**: Blazor WebAssembly (requires a hosted API layer, slower to build); Razor Pages (less interactive for the alert indicator); Vertical Slice Architecture (faster for hackathons generally, but the user explicitly chose Clean Architecture).

## R2. GitHub data access

- **Decision**: Octokit (the official .NET GitHub client) against the GitHub REST API, authenticated with an organisation-scoped fine-grained personal access token supplied through configuration (user-secrets in development). Data retrieval per load, bounded by configuration:
  - Repositories: organisation repositories ordered by last push, top `RepoCap` (default 25, FR-007).
  - Commits on all branches (FR-002): list branches per repository (cap `BranchCap`, default 20, most recently updated first), then list commits per branch with `since` set to the period start; deduplicate by commit SHA across branches. Author identity from the commit author login where available, otherwise the "unmatched" bucket.
  - Time of commit (FR-005): the commit author date is a `DateTimeOffset` carrying the author's local offset; use it directly. When the offset is unusable, fall back to the configured organisation timezone.
  - Pull requests and reviews (FR-003, FR-024, FR-027): pull requests updated within the period; per PR, submitted reviews (each submission counts once) with state (approved, changes requested, commented) and `submitted_at`.
  - Comments (FR-004): issue comments and PR review comments with `since` at period start.
  - Members (FR-012): organisation members list; bot accounts filtered by login suffix `[bot]` and account type (FR-010).
- **Rationale**: Octokit removes REST plumbing and pagination boilerplate, which matters in a 60-minute build. REST with per-branch commit listing plus SHA deduplication is the simplest correct implementation of the all-branches clarification. The configured caps bound the worst case well inside the 5000 requests per hour PAT rate limit.
- **Alternatives considered**: GitHub GraphQL API (fewer round trips and better branch queries, but more bespoke query work and no strong typing out of the box); raw REST with HttpClient (more code for no gain); GitHub App installation tokens (better enterprise posture, slower to set up than a PAT; deferred beyond the hackathon).

## R3. AI service integration (Microsoft Foundry, GPT model)

- **Decision**: Consume the Foundry-deployed GPT model through the `IChatClient` abstraction from `Microsoft.Extensions.AI`. Packages: `Microsoft.Extensions.AI` (pipeline and DI helpers), `Microsoft.Extensions.AI.OpenAI` (adapter), `Azure.AI.OpenAI` (Foundry/Azure OpenAI endpoint client), and `Azure.Identity` available for Entra ID later; the MVP authenticates with an API key. Configuration: `Ai:Endpoint`, `Ai:ApiKey`, `Ai:DeploymentName`. Registered in DI as a keyed singleton behind the application port.
  - Summaries (FR-015, FR-016): one chat call per request; the prompt carries only the aggregated stats for the subject as compact JSON plus instructions for supportive wording and the roughly 120-word bound (FR-017, FR-022).
  - Tone classification (FR-018): batched calls (up to 25 comments per call) returning a strict JSON array of classifications (positive, neutral, negative, unanalysable); results map to tone assessments, with anything unparseable counted as unanalysed.
- **Rationale**: `Microsoft.Extensions.AI` is the documented, provider-agnostic path (the user's "Microsoft.AI.Extensions" intent) and keeps the concrete provider swappable, which is exactly what the demo-mode AI adapter needs. Verified against current Microsoft Learn guidance for package selection.
- **Alternatives considered**: Semantic Kernel (heavier than needed for two prompt shapes); `Azure.AI.Projects` agent framework (agent administration is unnecessary here); calling the REST endpoint directly (loses the abstraction and the demo swap).

## R4. Persistence and caching

- **Decision**: No database and no EF Core. An `IMemoryCache` holds the fetched activity dataset and AI results keyed by scope plus period (FR-021), with a session-length TTL and explicit refresh. The check-in alert's viewed state lives in a circuit-scoped service (per Blazor Server circuit, which is per browser session), matching the spec's session-scoped semantics (FR-031).
- **Rationale**: The spec mandates session-only retention. Removing the database removes migrations, containers, and test infrastructure from the critical path. The scaffold checklist's EF configuration item is N/A for every slice in this feature, justified by this decision.
- **Alternatives considered**: SQLite for history (out of scope per FR-034 future work); distributed cache (single-instance internal tool does not need it).

## R5. Demo mode

- **Decision**: `Wellness:DemoMode` boolean, **default true**, so a fresh clone runs with zero credentials and no network access. Demo implementations of both ports (`DemoActivitySource`, `DemoAiInsightService`) generate a deterministic synthetic dataset from a fixed seed: clearly fictitious identities, at least one overwork case, one spread-thin case, one negative-tone project, one possible-rushing case, and pre-written summary texts (FR-013, SC-013).
- **Rationale**: Default-on protects the demo from credential or connectivity failure and makes the first run instant. Determinism makes the acceptance scenarios repeatable.
- **Alternatives considered**: Default off (risks a broken first demo); recorded live fixtures (real identities would leak into the repository, unacceptable).

## R6. Signal computation

- **Decision**: All signal logic (out-of-hours shares, spread-thin count, tone aggregation, possible-rushing rule, needs-check-in composition, alert state transitions) lives in the Domain project as pure, synchronous, allocation-light functions over the fetched dataset, driven by a strongly typed `WellnessOptions` (working hours, thresholds, caps, organisation timezone). Values per the spec: out-of-hours 25 percent, spread thin 4 projects, negative tone 20 percent, rushing above-median volume AND changes-requested share above 40 percent with minimum 3 PRs, repo cap 25, tone cap 200.
- **Rationale**: Pure domain functions are trivially unit-testable, which is where the limited test budget buys the most confidence, and they honour FR-033's configurability requirement in one place.
- **Alternatives considered**: Computing in the UI layer (untestable, violates Clean Architecture); computing during fetch in Infrastructure (couples signals to the data source and breaks demo-mode parity).

## R7. Charts and UI rendering

- **Decision**: Render the time-of-day distribution and tone bars with plain Blazor markup and CSS (proportional widths), no JavaScript chart library.
- **Rationale**: Zero interop and zero external dependencies inside the time box; the visual contract in `contracts/ui-design.md` requires only simple distributions.
- **Alternatives considered**: A JS chart library via interop (setup and styling cost exceeds its value for two simple charts).

## R8. Testing approach

- **Decision**: xUnit. Unit tests target the Domain signal calculators and options validation (the highest-value logic). One integration smoke suite boots the Web project via `WebApplicationFactory` with demo mode forced on and asserts the overview, check-in, and tone pages render with the seeded demo cases. No Testcontainers (no database; Docker not assumed available).
- **Rationale**: Matches the plan's no-persistence decision and the hackathon budget while still proving the P1 to P5 acceptance scenarios end to end through the demo dataset.
- **Alternatives considered**: bUnit component tests (valuable later, cut for time); Playwright end-to-end (infrastructure cost too high for the window).

## R9. Multi-agent build strategy

- **Decision**: The task breakdown (Phase 2, `/speckit-tasks`) will maximise `[P]` parallel markers by keeping file sets disjoint along Clean Architecture seams: Domain calculators, GitHub adapter, AI adapter, demo adapters, and each UI page are independently buildable slices behind the ports defined in `contracts/application-ports.md`. Implementation runs on Sonnet 5 `scaffold-implementer` subagents per the repository model policy, several in parallel where files do not overlap.
- **Rationale**: The user explicitly requested divide-and-conquer implementation across multiple agents; ports-first design is what makes that safe.
