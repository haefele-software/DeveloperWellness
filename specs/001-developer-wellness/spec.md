# Feature Specification: Developer Wellness Platform

**Feature Branch**: `001-developer-wellness`

**Created**: 2026-07-17 (consolidated from former specs 001-dev-wellness-dashboard, 002-ai-insights, and 003-checkin-signals)

**Status**: Draft

**Input**: Consolidated user descriptions: "A developer wellness application working with my organisational GitHub account, giving stats on our developers: commits, PR reviews, comments, time of commits, organisational level versus project level, tickets completed (future). The point is to see when developers and teams are overworked, spread thin, or context switching too much; we want to look after our developers and read when a developer is getting into a difficult spot. Hackathon scope, roughly 60 minutes of build, tasks split across multiple agents. Also add an AI service connection; the agent gives a small summary of a project or person and analyses PR comment tone. Additional stats: PRs after hours, projects in flight per user, amount of commits, code quality versus quantity, how many people need check-ins, and knowing when a check-in is needed."

## Purpose

Engineering leads need an early-warning view of developer wellbeing. GitHub activity already records how much, where, and when each developer works. This platform turns that activity into a wellness dashboard that highlights overwork (out-of-hours commits and PR activity), spread (activity across too many projects), load (commits, reviews, comments), review climate (comment tone), and rushing (volume outpacing review quality). An AI service adds short readable summaries of a project or a person. Everything converges on a check-in roster answering the question every caring lead actually asks: who needs a check-in right now, and why? A prominent in-app alert makes it impossible to miss when someone newly needs one. The intent is supportive throughout: helping leadership notice and reach out, never surveillance or performance scoring.

## Clarifications

### Session 2026-07-17

- Q: Which commits should count towards a developer's commit stats (including time-of-commit analysis)? → A: Commits on all branches, deduplicated so each unique commit counts once.
- Q: How should the hackathon demo cases (overwork and spread-thin flags) be produced? → A: Include a synthetic demo mode switch alongside live GitHub data; demo mode serves clearly fictitious data without contacting any external service.
- Q: At organisation scope, how many repositories should the dashboard cover per load? → A: The most recently active repositories up to a configurable cap (default 25), with the covered set stated in the UI.
- Q: In which timezone should working hours be evaluated when classifying a commit as out-of-hours? → A: The author's own local time as recorded on each commit; fall back to a configured organisation timezone when a commit carries no usable offset.
- Q: Which developers should appear on the dashboard for a given period? → A: Every organisation member appears; members with no recorded activity in the period are listed in a separate no-activity group with zero counts.
- Q: Which messaging channel should check-in notifications use for the MVP? → A: None. Knowing when a check-in is needed is the requirement; the MVP delivers a prominent in-app alert indicator only, and external mail or team chat delivery is deferred to future work.
- Q: When should the quality-versus-quantity view raise a possible-rushing flag for the roster? → A: Only when both conditions hold: output volume above the roster median AND changes-requested share above 40 percent, with the minimum sample of 3 PRs.
- Q: When the lead has a single project selected, what should the check-in roster and alert cover? → A: They follow the selected scope, recomputing against that project's activity only; the spread-thin signal applies only at organisation scope because it counts distinct projects.
- Q: How should a developer's "PR reviews performed" be counted? → A: Each submitted review counts separately (three review rounds on one PR count as three), because repeated review rounds are themselves a context-switching indicator.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - View per-developer activity summary for the organisation (Priority: P1)

An engineering lead opens the dashboard, which is connected to the organisation's GitHub account. They see the organisation's developers with each developer's commit count, pull request reviews performed, and comments written over the selected time period (default: last 14 days).

**Why this priority**: This is the core value; without a live per-developer activity summary nothing else exists. It is also the smallest demonstrable slice.

**Independent Test**: Configure the organisation connection, open the dashboard, and confirm every active developer appears with counts that match a manual spot check of GitHub for one developer.

**Acceptance Scenarios**:

1. **Given** a valid organisation connection, **When** the lead opens the dashboard, **Then** each developer active in the period is listed with commit, review, and comment counts for that period.
2. **Given** the dashboard is displaying data, **When** the lead changes the time period (7, 14, or 30 days), **Then** all counts refresh to reflect the chosen period.
3. **Given** an organisation member had no activity in the period, **When** the dashboard loads, **Then** that member appears in a separate no-activity group with zero counts and is never silently dropped.

---

### User Story 2 - Spot overwork through time-of-commit patterns (Priority: P2)

The lead sees when each developer's commits happen across the day and week, evaluated in the author's own local time. Commits outside working hours (default 08:00 to 18:00, Monday to Friday) count as out-of-hours, and developers whose out-of-hours share exceeds the threshold are visually flagged.

**Why this priority**: Time-of-day patterns are the strongest overwork signal in the requested stats.

**Independent Test**: Using a developer with known late-night commits, confirm the dashboard shows their out-of-hours share and flags them when it exceeds the threshold.

**Acceptance Scenarios**:

1. **Given** commit data for a developer, **When** the lead views that developer, **Then** the distribution of commit times (working hours versus evenings and weekends) is visible.
2. **Given** a developer whose out-of-hours share exceeds the threshold (default 25 percent), **When** the summary is displayed, **Then** that developer carries a clearly visible wellbeing flag.
3. **Given** a developer with all commits inside working hours, **When** the summary is displayed, **Then** no out-of-hours flag is shown.

---

### User Story 3 - Switch between organisation level and project level (Priority: P3)

The lead switches the dashboard scope between the whole organisation (the most recently active repositories up to the configured cap) and a single project. At organisation level each developer also shows how many distinct projects they touched in the period; developers at or above the threshold are flagged as potentially spread thin.

**Why this priority**: Scope switching and the context-switching signal complete the core stats but build on data already fetched.

**Independent Test**: Select a specific project and confirm counts reduce to that project only; return to organisation scope and confirm distinct-project counts and spread-thin flags.

**Acceptance Scenarios**:

1. **Given** organisation scope, **When** the lead selects a single project, **Then** all displayed stats reflect only that project's activity for the period.
2. **Given** organisation scope, **When** the summary is displayed, **Then** each developer shows the number of distinct projects they were active in during the period.
3. **Given** a developer active in projects at or above the threshold (default 4), **When** the summary is displayed, **Then** that developer carries a spread-thin flag.

---

### User Story 4 - Check-in roster (Priority: P4)

The lead opens the check-in view and sees how many developers currently need a check-in and the list of those developers, each with plain-language reasons drawn from every active signal. Developers with the most concurrent signals appear first. The roster reflects the currently selected scope and period, recomputing when either changes.

**Why this priority**: This is the actionable heart of the whole application; every signal exists so this list can be trusted.

**Independent Test**: With data producing at least one flagged developer, open the check-in view and confirm the count, the ordered list, and at least one human-readable reason per flagged developer.

**Acceptance Scenarios**:

1. **Given** at least one developer has an active wellbeing flag in the period, **When** the lead opens the check-in view, **Then** the view shows the total needing check-ins and lists each flagged developer with every active flag stated as a plain-language reason.
2. **Given** several developers are flagged, **When** the roster is displayed, **Then** developers with more concurrent flags appear before those with fewer.
3. **Given** no developer is flagged in the period, **When** the lead opens the check-in view, **Then** the view states positively that nobody appears to need a check-in.

---

### User Story 5 - In-app check-in alert (Priority: P5)

Whenever one or more developers newly need a check-in, the dashboard shows a prominent alert indicator with the count, one click away from the roster. The indicator clears once the lead views the roster and reappears only when a further developer becomes flagged. Nothing is sent outside the application.

**Why this priority**: The lead's stated need is simply to know when a check-in is needed; an in-app alert delivers that awareness with no external infrastructure.

**Independent Test**: Cause a developer to become flagged, confirm the alert indicator appears with the correct count, open the roster, and confirm the indicator clears and stays clear until a different developer becomes flagged.

**Acceptance Scenarios**:

1. **Given** a developer newly needs a check-in this session, **When** the dashboard is displayed after the data load, **Then** a prominent alert indicator shows the count of developers needing check-ins and links to the roster.
2. **Given** the lead has viewed the roster after an alert, **When** the roster is recomputed with no further newly flagged developers, **Then** the indicator stays cleared.
3. **Given** the lead has viewed the roster, **When** an additional developer becomes flagged, **Then** the alert indicator reappears.

---

### User Story 6 - Pull request activity after hours (Priority: P6)

The lead sees, per developer, how much pull request activity (PRs opened and reviews submitted) happens outside working hours, alongside the commit-based signal. A developer whose out-of-hours PR share exceeds the threshold gains an overwork flag that feeds the check-in roster.

**Why this priority**: Reviews and PR management are invisible to commit-based signals; an on-time committer who reviews at midnight is still overworked.

**Independent Test**: For a developer with known late-evening review activity, confirm the out-of-hours PR share is displayed and the flag appears when the threshold is exceeded.

**Acceptance Scenarios**:

1. **Given** PR events exist for a developer in the period, **When** the lead views that developer, **Then** the share of PR activity outside working hours is visible.
2. **Given** a developer whose out-of-hours PR share exceeds the threshold (default 25 percent), **When** the roster is computed, **Then** that developer carries an overwork flag citing after-hours PR activity.

---

### User Story 7 - AI summary of a project (Priority: P7)

The lead requests a summary for a project and receives a short narrative (at most roughly 120 words) describing recent activity: how busy it is, main contributors, and any wellbeing signals, grounded in the same period and data the dashboard displays.

**Why this priority**: The most demonstrable AI capability, reusing data the dashboard already holds.

**Independent Test**: Select a project with known activity, request a summary, and confirm a concise narrative appears that matches the on-screen stats and states its period.

**Acceptance Scenarios**:

1. **Given** a project with activity in the selected period, **When** the lead requests a project summary, **Then** a narrative of at most roughly 120 words appears, consistent with the displayed stats, stating the covered period, and visibly marked as AI-generated.
2. **Given** a project with no activity in the period, **When** a summary is requested, **Then** the summary plainly states there was no recorded activity rather than inventing content.
3. **Given** the AI service is unreachable, **When** a summary is requested, **Then** a friendly message explains the summary is unavailable and every other dashboard function continues to work.

---

### User Story 8 - AI summary of a developer (Priority: P8)

The lead requests a summary for an individual developer and receives a short, supportively worded narrative of their recent activity, patterns, and wellbeing flags, grounded in the displayed data.

**Why this priority**: Directly serves the mission of noticing when someone is heading into a difficult spot, using the same mechanism as the project summary.

**Independent Test**: Select a developer with a known overwork flag, request a summary, and confirm the narrative reflects the flag and the on-screen numbers in supportive language.

**Acceptance Scenarios**:

1. **Given** a developer with wellbeing flags in the period, **When** the lead requests a developer summary, **Then** the narrative mentions the flagged signals, uses supportive and non-judgemental wording, and is marked as AI-generated.
2. **Given** a developer in the no-activity group, **When** a summary is requested, **Then** the summary states the absence of recorded activity without speculation about causes.

---

### User Story 9 - PR comment tone analysis (Priority: P9)

The lead opens a tone view. The system classifies the tone of pull request comments in the selected scope and period (positive, neutral, negative) and shows aggregate distributions per project and per comment author, with a flag when the negative share exceeds the threshold. Individual comments are never displayed with tone verdicts.

**Why this priority**: Tone is a distinct wellbeing signal, but it processes comment text through the AI service at volume, so it lands after the summary mechanism is proven.

**Independent Test**: For a scope and period with mixed-tone comments, confirm the distribution is displayed at project level and per author, and that a scope above the negative threshold carries a visible flag.

**Acceptance Scenarios**:

1. **Given** pull request comments exist in the selected scope and period, **When** the lead opens the tone view, **Then** a tone distribution (positive, neutral, negative) is shown for the scope and per comment author.
2. **Given** a project or developer whose negative share exceeds the threshold (default 20 percent), **When** the tone view is displayed, **Then** that project or developer carries a clearly visible tone flag.
3. **Given** no comments exist in the scope and period, **When** the tone view is opened, **Then** the view plainly states there is nothing to analyse.
4. **Given** the AI service fails part-way through analysing a set of comments, **When** results are shown, **Then** the view states how many comments were analysed out of the total rather than presenting partial results as complete.

---

### User Story 10 - Quality versus quantity view (Priority: P10)

The lead opens a view comparing each developer's output volume (commits and PRs opened) against rework proxies (share of their PRs with changes requested, average review rounds per PR), spotting when high volume comes with rising rework. Developers with fewer than the minimum PR sample are marked insufficient data rather than judged.

**Why this priority**: Valuable context for check-in conversations, but it refines rather than creates the roster.

**Independent Test**: For a period with known PR review history, confirm both dimensions are displayed per developer and that a developer with fewer than 3 PRs is marked insufficient data.

**Acceptance Scenarios**:

1. **Given** developers with at least the minimum PR sample, **When** the lead opens the view, **Then** volume measures and rework proxies are shown side by side for each developer.
2. **Given** a developer with fewer PRs than the minimum sample, **When** the view is displayed, **Then** that developer is explicitly marked as insufficient data with no rework judgement shown.
3. **Given** a developer above the roster median volume whose changes-requested share exceeds 40 percent, **When** the roster is computed, **Then** a possible-rushing signal is listed among that developer's check-in reasons.

---

### Edge Cases

- The organisation credential is missing, invalid, or expired: the dashboard shows a clear, actionable message rather than an empty or broken page.
- GitHub imposes service limits (rate limiting) or is temporarily unavailable: the dashboard reports the problem and, where data was already loaded, keeps showing the last loaded data with its timestamp.
- Automated accounts (bots) produce commits, reviews, or comments: these are excluded from developer stats.
- A commit's author cannot be matched to an organisation member: the activity is grouped under an "unmatched" bucket rather than misattributed.
- A period contains no activity at all: the dashboard states this plainly instead of rendering empty charts.
- Very large organisations or periods: organisation scope is bounded to the most recently active repositories (configurable cap, default 25); the covered set is always stated rather than implying full coverage.
- PR events carry no author-local time offset (unlike commits), so after-hours classification for PR activity uses the configured organisation timezone; the view states this basis.
- The AI service is unreachable, misconfigured, or rate-limited: AI features show a clear, friendly message; all other dashboard functions remain fully usable.
- A summary request races a data refresh: the summary states the period and scope it was generated for.
- Very large comment volumes: tone analysis is bounded to the most recent comments up to a configurable cap (default 200 per scope and period), and the view states the bound when it applies.
- Non-English comments: tone classification is best effort; unclassifiable comments are counted as unanalysed rather than defaulting to negative.
- Repeated requests for the same AI result in the same session: results are reused rather than regenerated, until the scope, period, or data changes or the user explicitly refreshes.
- Many developers become flagged at once: the alert indicator shows one count; it never stacks multiple overlapping alerts.
- Switching scope or period changes who is flagged: the roster and alert always reflect the currently selected scope and period, and the spread-thin signal is simply absent at project scope rather than shown as zero.
- A developer is flagged by several signals simultaneously: reasons are listed together under one roster entry, not duplicated.
- Tiny PR samples: rework proxies are suppressed below the minimum sample to avoid unfair judgement.
- Tone signals unavailable (AI service absent): the roster computes without tone-based reasons and states that tone signals are unavailable.
- Demo mode is active: every view, including AI summaries, tone results, and the alert indicator, works with clearly fictitious synthetic data and nothing contacts any external service.

## Requirements *(mandatory)*

### Functional Requirements

**GitHub connection and core stats**

- **FR-001**: The system MUST connect to a single GitHub organisation using an organisation-scoped credential supplied through configuration, with read-only access. The system MUST NOT write to GitHub.
- **FR-002**: The system MUST display, per developer, the number of commits made within the selected period, counting commits on all branches and counting each unique commit exactly once even when it is reachable from several branches.
- **FR-003**: The system MUST display, per developer, the number of pull request reviews performed within the selected period, counting each submitted review separately (multiple review rounds on the same PR each count).
- **FR-004**: The system MUST display, per developer, the number of comments written (pull request and issue comments) within the selected period.
- **FR-005**: The system MUST show the time-of-day distribution of each developer's commits and compute the share outside configured working hours (default 08:00 to 18:00, Monday to Friday), evaluating each commit in the author's own local time as recorded on the commit, falling back to a configured organisation timezone when no usable offset is present.
- **FR-006**: The system MUST flag a developer as a wellbeing concern when their out-of-hours commit share exceeds a configurable threshold (default 25 percent).
- **FR-007**: The system MUST support switching the dashboard scope between organisation level and a single project, with all stats recalculated for the chosen scope. Organisation level covers the most recently active repositories up to a configurable cap (default 25), and the dashboard MUST state which repositories are covered.
- **FR-008**: The system MUST show, per developer at organisation scope, the number of distinct projects with activity in the period, and MUST flag developers at or above a configurable threshold (default 4) as potentially spread thin.
- **FR-009**: The system MUST let the user select the reporting period from at least: last 7, 14, and 30 days (default 14).
- **FR-010**: The system MUST exclude automated (bot) accounts from all developer stats.
- **FR-011**: The system MUST present clear, user-friendly messages for connection failures, service limits, and empty results, and MUST keep previously loaded data visible with its load time where available.
- **FR-012**: The dashboard roster MUST include every organisation member (excluding bots per FR-010); members with no recorded activity in the selected period MUST appear in a separate no-activity group with zero counts.
- **FR-013**: The system MUST provide a demo mode, enabled through configuration, that serves synthetic data for every capability in this specification (core stats, AI summaries, tone results, roster, and alert) without contacting any external service. Synthetic data MUST use clearly fictitious developer identities and MUST include at least one overwork case and one spread-thin case. When demo mode is off, all stats MUST derive from live data.

**AI insights**

- **FR-014**: The system MUST connect to a single organisation-configured AI text analysis and generation service, with connection details and credential supplied through configuration. The dashboard MUST function fully when the service is not configured or unavailable; AI features degrade to a friendly unavailable state.
- **FR-015**: The system MUST generate, on request, a concise summary (at most roughly 120 words) of a selected project's activity for the selected period, grounded exclusively in the activity data the dashboard holds for that scope and period.
- **FR-016**: The system MUST generate, on request, a concise summary of a selected developer's activity and wellbeing signals for the selected period, worded supportively, grounded exclusively in the dashboard's data for that developer.
- **FR-017**: Every AI-generated output MUST be visibly marked as AI-generated and MUST state the scope and period it covers. Summaries MUST NOT present information that is not derivable from the data provided to the service.
- **FR-018**: The system MUST classify the tone of pull request comments in the selected scope and period into positive, neutral, or negative, and display aggregate distributions per project and per comment author. Individual comments MUST NOT be displayed with tone verdicts.
- **FR-019**: The system MUST flag a project or developer when the negative share of analysed comments exceeds a configurable threshold (default 20 percent).
- **FR-020**: Tone analysis MUST be bounded to a configurable maximum number of comments per scope and period (default 200, most recent first), and the display MUST state when the bound was applied and how many comments were analysed out of the total.
- **FR-021**: The system MUST reuse AI results within a session for an unchanged scope, period, and data set, regenerating only on change or explicit refresh.
- **FR-022**: The system MUST send the AI service only the minimum data needed for each request (aggregated stats for summaries; comment text and minimal context for tone analysis) and MUST NOT send credentials or repository content beyond that.
- **FR-023**: AI insights are presented only within the dashboard for engineering leadership use; there is no export, alerting, or sharing of AI-generated content.

**Check-in signals and alerts**

- **FR-024**: The system MUST classify each pull request event (PR opened, review submitted) in the selected period as inside or outside working hours, evaluated in the configured organisation timezone, and MUST display per developer the count and share of out-of-hours PR activity.
- **FR-025**: The system MUST add an overwork flag citing after-hours PR activity when a developer's out-of-hours PR share exceeds a configurable threshold (default 25 percent).
- **FR-026**: The system MUST compute a needs-check-in status per developer per period within the selected scope, active when the developer has one or more active wellbeing flags computable in that scope: out-of-hours commits (FR-006), spread thin (FR-008, organisation scope only, as it counts distinct projects), negative comment tone as author (FR-019, when available), after-hours PR activity (FR-025), or possible rushing (FR-027).
- **FR-027**: The system MUST display, per developer with at least a configurable minimum PR sample (default 3), output volume (commits, PRs opened) alongside rework proxies (share of PRs with changes requested, average review rounds per PR), and MUST raise a possible-rushing signal only when both conditions hold: output volume above the roster median AND changes-requested share above a configurable threshold (default 40 percent). Developers below the minimum sample MUST be marked insufficient data with no judgement shown.
- **FR-028**: The system MUST display a check-in roster showing the total count of developers needing check-ins and the ordered list (most concurrent flags first), with every active flag expressed as a plain-language reason. When nobody is flagged, the roster MUST state this positively.
- **FR-029**: The system MUST NOT combine signals into a single published score or ranking of developers; signals are always shown as separate named reasons.
- **FR-030**: The system MUST display a prominent in-app alert indicator whenever one or more developers newly need a check-in in the selected scope and period, showing the count and linking to the check-in roster.
- **FR-031**: "Newly" MUST be evaluated within the running session: a flagged developer counts as new until the lead views the roster after that developer was flagged. The indicator MUST clear once the roster is viewed and MUST reappear only when a further developer becomes flagged.
- **FR-032**: The system MUST NOT send messages outside the application; external delivery (email, team chat, or otherwise) is explicitly future work. All alert and roster wording MUST remain supportive and non-judgemental, and nothing is ever addressed to the flagged developer.
- **FR-033**: All thresholds, caps, and the minimum PR sample in this specification MUST be configurable with the stated defaults.
- **FR-034**: Tickets or work items completed is explicitly OUT of scope and recorded as a future enhancement.

### Key Entities

- **Developer**: A member of the organisation identified by their GitHub account; carries display name or handle and activity aggregates. No data beyond what GitHub already exposes is collected.
- **Project**: A repository belonging to the organisation; the unit of project-level scope.
- **Activity Event**: A single commit, pull request review, comment, or PR opened event, with author, project, type, and timestamp (author-local where available).
- **Activity Summary**: Aggregated counts per developer for a scope and period: commits, reviews, comments, out-of-hours shares (commits and PR activity), and distinct active projects.
- **Wellbeing Signal**: A flag attached to a developer's summary (overwork, spread thin, negative tone, possible rushing) derived from configurable thresholds, always carrying a plain-language reason.
- **AI Service Connection**: The configured link to the organisation's AI text service; carries endpoint identity and credential by reference, never displayed.
- **AI Summary**: A generated narrative about one project or one developer for a scope and period; carries text, subject, period, generation time, and AI-generated marking.
- **Tone Assessment**: The classification of one pull request comment (positive, neutral, negative, or unanalysed); held only to build aggregates, never displayed individually.
- **Tone Aggregate**: The tone distribution for a scope (project or comment author) and period, including counts, negative share, analysed-versus-total figures, and threshold flag.
- **Check-in Status**: Per developer and period: the set of active wellbeing flags with reasons and the resulting needs-check-in state.
- **Quality-Quantity Snapshot**: Per developer and period: volume measures and rework proxies, plus a sufficiency marker.
- **Check-in Alert**: The session-scoped state behind the in-app indicator: the set of flagged developers not yet seen by the lead, the resulting count, and the viewed marker that clears it.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A lead sees the per-developer activity summary for the whole organisation within 2 minutes of completing the one-time connection setup.
- **SC-002**: A lead can identify the developers with the highest out-of-hours activity for a chosen period in under 30 seconds from opening the dashboard.
- **SC-003**: Switching between organisation scope and a project scope, or changing the period, updates all stats without requiring the user to reconfigure anything.
- **SC-004**: Outside demo mode, 100 percent of displayed stats derive from the organisation's live GitHub data; no manual data entry is required anywhere.
- **SC-005**: A requested AI summary appears within 10 seconds, or a clear unavailable message appears instead; the dashboard never blocks on AI features.
- **SC-006**: 100 percent of AI-generated outputs are visibly marked as AI-generated and state their scope and period.
- **SC-007**: In a review of 10 generated summaries against the on-screen data, no summary contains a factual claim that contradicts or exceeds the displayed stats.
- **SC-008**: A lead can see the tone distribution and any tone flags for a project in under 30 seconds from opening the tone view.
- **SC-009**: With the AI service deliberately unreachable, 100 percent of non-AI functions continue to work unchanged, and the roster states that tone signals are unavailable.
- **SC-010**: A lead can answer "how many people need a check-in and why" within 30 seconds of opening the check-in view, and 100 percent of flagged developers show at least one plain-language reason.
- **SC-011**: 100 percent of newly flagged developers produce a visible alert indicator on the next dashboard display, and the indicator clears once the roster is viewed, reappearing only for further newly flagged developers.
- **SC-012**: The quality-versus-quantity view shows both dimensions for every developer with sufficient data, and 100 percent of insufficient-data cases are explicitly labelled rather than judged.
- **SC-013**: In a demo (live or demo mode), the following are all visible without manual editing: at least one overwork case, one spread-thin case, two check-in roster entries with distinct reason sets, the alert indicator, one project summary, one developer summary, and one tone flag.

## Assumptions

- **Hackathon scope**: The build is intended to fit a roughly 60-minute hackathon window with tasks split across multiple parallel agents; defaults favour the simplest workable option. Priorities P1 to P5 are the demo backbone; P6 to P10 follow as time allows.
- **Single organisation**: The platform serves one GitHub organisation per deployment, with the credential provided through configuration at start-up. There is no end-user sign-in for the MVP; it runs as a trusted internal tool for engineering leadership.
- **Working hours and timezone**: Working hours default to 08:00 to 18:00, Monday to Friday. Commits are evaluated in the author's own local time as recorded on each commit; a configured organisation timezone serves as the fallback and as the basis for PR events, which carry no author-local offset.
- **Thresholds are illustrative defaults**: Out-of-hours share 25 percent (commits and PR activity), spread thin at 4 or more distinct projects, negative tone share 20 percent, rushing at above-median volume with changes-requested share above 40 percent (minimum 3 PRs), organisation coverage cap 25 repositories, tone analysis cap 200 comments. All configurable and expected to be tuned with real usage.
- **Provisioned AI service**: The organisation already has an AI text service with a suitable general-purpose language model provisioned; the platform consumes it through configuration. The specific service, model, and connection technology named in the user input are planning decisions recorded for `/speckit-plan`.
- **Aggregate-only tone display**: Tone is shown only as aggregates per project and per comment author. Per-comment verdicts and quoted excerpts are deliberately excluded to keep the feature supportive rather than disciplinary.
- **Language**: Comments are predominantly English; tone classification for other languages is best effort in the MVP.
- **Data handling**: Activity data, AI-generated content, and tone assessments are fetched or computed on demand and held only for the running session (no long-term storage in the MVP). Comment text is sent to the AI service solely for tone classification. The platform presents individual activity for supportive wellbeing purposes, for engineering leadership use only; it is not a performance-measurement tool.
- **Alerting is in-app only**: The MVP surfaces the need for a check-in exclusively inside the dashboard. Automatic external delivery (email or team chat) was considered and deliberately deferred to future work.
- **Alert state is session-scoped**: "Newly flagged" and the alert's viewed state are evaluated within the running session. Restarting the application may re-raise an alert already seen; accepted for the hackathon.
- **Bot exclusion**: Accounts identifiable as automated (for example names ending in "[bot]") are excluded.
- **Sensitivity**: The roster and alerts exist to prompt supportive human conversations by leadership. Wording avoids blame, signals are always explained, and no automated judgement is presented as fact.
- **Future work**: Tickets or work items completed, external alert delivery, check-in follow-up workflow (acknowledge, snooze, history), historical trends, per-developer timezones for PR events, and exports are all out of scope for the MVP.
