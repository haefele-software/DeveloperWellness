# Quickstart: Developer Wellness Platform

**Date**: 2026-07-17 | **Spec**: [spec.md](spec.md) | **Contracts**: [contracts/](contracts/)

## Prerequisites

- .NET 10 SDK
- Nothing else for demo mode. Live mode additionally needs a GitHub fine-grained personal access token (read-only: organisation members, repository contents metadata, pull requests) and a Microsoft Foundry (Azure OpenAI) deployment of a GPT model.

## Run in demo mode (zero configuration)

Demo mode is the default (`Wellness:DemoMode = true`, research R5). From the repository root:

```powershell
dotnet run --project src/DeveloperWellness.Web
```

Open the printed local address. Expected: the "Demo data" badge is visible, the team overview shows the fictitious roster, and no network calls leave the machine.

## Configure live mode

Store secrets with user-secrets (never in appsettings.json):

```powershell
cd src/DeveloperWellness.Web
dotnet user-secrets set "GitHub:Organisation" "<your-org>"
dotnet user-secrets set "GitHub:Token" "<fine-grained-pat>"
dotnet user-secrets set "Ai:Endpoint" "https://<resource>.openai.azure.com/"
dotnet user-secrets set "Ai:ApiKey" "<key>"
dotnet user-secrets set "Ai:DeploymentName" "<gpt-deployment>"
dotnet user-secrets set "Wellness:DemoMode" "false"
dotnet user-secrets set "Wellness:OrganisationTimeZone" "South Africa Standard Time"
```

Restart the app. AI settings are optional: leaving them unset runs the dashboard with AI panels in their friendly unavailable state (FR-014).

## Validation scenarios

Run each after the corresponding story lands. Demo mode makes all of them executable without credentials (SC-013).

| # | Scenario | Steps | Expected outcome |
|---|----------|-------|------------------|
| 1 | Activity summary (P1, SC-001) | Open `/`, switch period 7/14/30 | Every roster member listed; counts change with period; no-activity group present; bots absent |
| 2 | Overwork flags (P2) | Open the seeded overwork developer's detail | Time-of-commit distribution visible with author-local caption; overwork chip with plain-language reason |
| 3 | Scope switch (P3, SC-003) | Select a single project, then organisation | Stats recompute per scope; coverage statement lists covered repositories; spread-thin flag on the seeded case at organisation scope only |
| 4 | Check-in roster (P4, SC-010) | Open `/checkins` | Count headline; ordered list, most flags first; every entry has readable reasons; positive empty state when nothing is flagged |
| 5 | Alert lifecycle (P5, SC-011) | Load with a seeded flagged developer; view roster; recompute | Indicator appears with count; clears after viewing; reappears only for a further newly flagged developer |
| 6 | PR after-hours (P6) | Open the seeded late-reviewer's detail | Out-of-hours PR share visible with organisation-timezone caption; flag cites after-hours PR activity |
| 7 | AI summaries (P7, P8, SC-005, SC-006) | Request project and developer summaries | Under 10 seconds or friendly unavailable message; text at most roughly 120 words; labelled "AI-generated · [scope] · [period]"; repeat request served from session cache |
| 8 | Tone view (P9, SC-008) | Open `/tone` | Aggregate distributions per project and author; seeded negative project flagged; analysed-versus-total stated when the 200 cap applies; no per-comment verdicts anywhere |
| 9 | Quality versus quantity (P10) | Open `/quality` | Volume beside rework proxies; below-sample developers marked insufficient data; possible-rushing flag only on the seeded both-conditions case; no composite score |
| 10 | Degradation (SC-009) | Unset AI settings in live mode, reload | All non-AI functions unchanged; roster states tone signals unavailable |

## Tests

```powershell
dotnet build
dotnet test
```

Expected: Domain calculator unit tests and the `WebApplicationFactory` smoke suite (demo mode forced on) all green. There is no database and no Testcontainers dependency (research R4, R8).
