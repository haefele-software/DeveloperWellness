# Design Contract: Pulse

**Source of truth**: the approved Pulse design export (`Pulse Dashboard.html`, a Claude design artefact; decoded structure summarised here). This contract binds implementation; where a screen detail is not stated here, follow the design export. Requirement references point at `../spec.md`.

## 1. Product in one paragraph

Pulse ("Pulse — Developer wellness") reads an organisation's GitHub activity and answers one caring question: **who needs a check-in right now, and why?** It opens on an at-a-glance Overview (KPI tiles, projects table, team cards, manager recommendations, development trend, review sentiment), with a Team overview table, a check-in roster, quality-versus-quantity, and per-person and per-project detail one click away. It is a care instrument, not a surveillance tool: the application exists to look after developers, never to control them.

## 2. Design principles (binding)

1. **Supportive, never accusatory.** Flags are conversation prompts, not verdicts; the roster says people "might need" a check-in. No leaderboards, no composite person scores, no ranking of people (FR-029). Projects may be ordered by activity; people may not.
2. **Every flag explains itself** with the design's reason pattern: observation, context, gentle suggestion (for example: "13 of 34 commits (38%) landed out of hours in their local time over the last 14 days. It might be worth a quiet check-in about workload.").
3. **Calm visual temperament.** Fluent-like light UI, accent blue `#0f6cbd`, attention in warm amber (`#8a3707` text, `#c48733` bars), never alarm red for people.
4. **Honest data.** The persistent coverage line states scope, repository bound, period, data load time, and demo marker (FR-007). AI outputs carry the exact label `AI-generated · {scope} · last {N} days` (FR-017). Bounded analyses state their sample.
5. **Never blocked, never blank.** Credentials missing → full-page connect state; rate limited → banner, data kept, automatic retry (FR-011). AI down → quiet local unavailable states (FR-014). Loading → skeleton rows.
6. **Accessible.** Flags pair icon plus label plus hover reason; no colour-only signals; WCAG AA contrast; keyboard reachable.

## 3. Global shell (every page)

- **Brand**: "Pulse — Developer wellness". Nav: Overview, Team overview, Check-in roster (with live count badge), Quality vs quantity, Project detail. Developer detail is a drill-in with a back link, not a nav item.
- **Status chips**: connection dot and text ("SignalR connected" green; "GitHub paused (rate limit)" amber), freshness text ("GitHub sync 2 min ago" / "Data as of today 09:42"), runtime badge ("Blazor Server · .NET {actual runtime version}"), lead persona chip.
- **Demo banner** when demo mode is on: "Demo data — fictitious identities" (FR-013).
- **Alert pill** (FR-030, FR-031): "{N} people newly need a check-in", click opens the roster and marks seen; seen-state is keyed per scope and period within the session.
- **Scope selector** (Organisation plus each covered repository) and **period buttons** (7, 14, 30 days). Every change recomputes with a brief skeleton state.
- **Coverage line**: "{Scope} · covering the 10 most recently active repositories · last {N} days · data loaded {time} · demo data".

## 4. Screens

### 4.1 Overview (landing, `/`) — FR-035..FR-039

Headline "{Scope} at a glance" with "Open the check-in roster →".

- **KPI tiles**: Might need a check-in (count, note "{n} new since roster last viewed"); After-hours commits (weighted share, note "threshold 25% · author-local time"); After-hours PR activity (note "organisation time"); Projects per developer (note "spread-thin from 4 concurrent") or Contributors when project-scoped; Review sentiment ("{neg}% negative", note "{pos}% positive · tone flag from 20%") or an em-dash tile noting "tone service unavailable".
- **Projects table** ("overall stats per project · click one to open its detail"): Project, People, Commits, PRs, Reviews, Comments, After-hours (bar, amber above 25 percent), Signals ("{n} people carry a signal here" in amber, or "quiet"); ordered by commits; row click switches scope to that project and opens project detail.
- **Teams** ("click a card for the full table · flags open the person"): one card per team: name, "{n} devs", activity sparkline, bars for After-hours, Projects in flight, Reviews per dev (amber above their attention levels), and up to three most-flagged member chips linking to the person (FR-036).
- **Recommendations for managers** ("drawn from active signals — suggestions, not instructions"): up to six flagged people, avatar, name, team, action from leading signal (Encourage real time off / Nudge reviews back into the day / Rebalance project load / Check in on review climate / Ease the pace pressure), short reason, "Open →". Empty state: "No recommendations this period — signals are quiet across the board." (FR-037).
- **Development trend**: 12-week sparkline plus "{Commit activity is up ~X% across the window}" with the steep-ramp caution when above 25 percent, and the note that the series is weekly relative commits (FR-038).
- **Review sentiment**: distribution bar (positive, neutral, negative) plus "{pos}% positive · {neu}% neutral · {neg}% negative across analysed comments. Tone concerns surface on the check-in roster, never per comment." Unavailable note when AI is off (FR-039).

### 4.2 Team overview (`/team`) — FR-002..FR-012

- Headline mirrors the roster headline ("{N} people might need a check-in" / positive empty), with "View the roster →" and the caption "Default order: flagged first, then alphabetical — never a ranking".
- Sortable columns: Developer, Commits, PR reviews, Comments, OOH commits, OOH PRs, Projects (organisation scope only), Flags. After-hours cells go amber and bold above 25 percent.
- **No-activity group**: "No recorded activity this period" rows with "No activity — still part of the team".
- **Unmatched line**: "Unmatched activity: {explanation, counted here, never against anyone}".
- Footer captions: author-local versus organisation-timezone basis; "Flags are conversation prompts, not verdicts — hover any flag for its reason."

### 4.3 Check-in roster (`/checkins`) — FR-026..FR-028, FR-018..FR-020

- Headline "{N} people might need a check-in"; sub "{Scope} · last {N} days · ordered by number of concurrent signals" plus, at project scope, "project scope: spread-thin isn't assessed here".
- When AI is off: "Tone signals are currently unavailable, so this roster reflects activity signals only."
- **Frustration paragraph** when tone-flagged people exist: "There's some frustration showing in review comments: {names}' comments read more negative than usual this period. It's climate, not character — review pressure is usually the cause."
- Entries: avatar, name, team, every flag with its full reason, "View {name}'s detail →".
- Positive empty state: "Nobody appears to need a check-in right now. Across {coverage}, every signal sits in its normal range. That's the outcome this dashboard is for — enjoy it."

### 4.4 Developer detail (drill-in) — FR-005, FR-006, FR-016, FR-024, FR-025

- Back link; header: name, "{team} · {scope} scope · last {N} days · active in {n} projects"; flags or "Signals steady".
- Stat tiles: Commits ("{n} out of hours"), PR reviews given ("each submitted review counts"), Comments ("PRs and reviews"), PRs opened ("{cr}% saw changes requested" or "below review-data sample").
- **Time of commits heatmap**: hours by weekday in the author's local time, working-hours cells in accent blue, out-of-hours in amber, legend "Within working hours (09–18, Mon–Fri) / Out of hours".
- Out-of-hours commit and PR share bars with the 25 percent threshold marker and their timezone captions.
- **AI summary panel** (FR-015..FR-017, FR-021): idle ("Generate a short, supportive summary… Nothing runs until you ask."), loading ("Summarising… usually under 10 seconds."), ready (text, exact AI label, Refresh), down ("The summary service isn't reachable right now. Everything else on this page is live…"), no-activity ("{name} recorded no activity in this period, so there's nothing to summarise.").

### 4.5 Project detail (`/project/{name}`)

- Header with contributor count; stat tiles (Commits, PRs opened, Reviews, Comments); Contributors list, flagged first, with activity line and flags; note "{n} of {m} carry a flag in this scope"; caption "Spread-thin isn't assessed at project scope — it only makes sense across the whole organisation."
- AI project summary panel, identical mechanics to 4.4.

### 4.6 Quality vs quantity (`/quality`) — FR-027, FR-029

- Sub "Volume beside rework proxies — no score, no index, no ranking. Shown only for people with at least 3 PRs this period."
- Columns: Developer, Commits, PRs opened, Changes-requested share (amber above 40 percent), Avg review rounds, Signal ("Possible rushing" or "Volume and rework look in step").
- Below-sample note: "Not enough review data to say anything useful: {names} — fewer than 3 PRs this period, which is perfectly normal."
- Closing caption explaining the rushing rule reads as pace pressure, not carelessness.

## 5. Signal rules (as designed)

| Flag | Rule | Guard |
|------|------|-------|
| Overwork (commits) | out-of-hours commit share > 25% (author-local) | — |
| Overwork (PR activity) | out-of-hours PR share > 25% (organisation time) | >= 3 PR events (FR-025) |
| Spread thin | >= 4 projects in flight | organisation scope only |
| Tone | negative share > 20% of analysed authored comments | >= 10 analysed comments (FR-019); AI available |
| Possible rushing | volume above median AND changes-requested share > 40% | >= 3 PRs opened |

## 6. Explicitly out of scope (do not design or build)

Sign-in and roles; external notifications; exports or sharing of AI content; any tone surface beyond the roster mention and the organisation-level sentiment reading (no tone page, no per-project tone, no per-comment verdicts); check-in follow-up workflow; historical trend targets.

## 7. Design acceptance checklist

- [ ] All six screens in section 4 exist with credential-missing, rate-limited, loading, empty, AI-unavailable, and demo states.
- [ ] The Overview renders KPI tiles, projects table, team cards, recommendations, trend, and sentiment at zero navigation clicks (SC-014).
- [ ] Every flag everywhere carries the design's observation-context-suggestion reason (SC-010).
- [ ] The AI label pattern is exactly `AI-generated · {scope} · last {N} days` on every AI output (SC-006).
- [ ] The alert pill lifecycle (appear, clear on roster view, reappear; keyed per scope and period) works (SC-011).
- [ ] No ranking or score of a person, no per-comment tone verdict, and no per-project tone appears anywhere (FR-029, FR-018).
- [ ] Fictitious identities and the demo banner appear in demo mode (FR-013); flags are readable without colour perception; WCAG AA contrast.
