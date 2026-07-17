# Design Brief: Developer Wellness Platform

Give this document to a design session (Claude or otherwise) together with your current design. The task it defines: **adjust the existing design so that every screen, state, and word below is satisfied**. The consolidated specification at `specs/001-developer-wellness/spec.md` is the source of truth; where this brief and the spec disagree, the spec wins, and where your current design disagrees with either, the design changes.

Requirement references (FR-xxx, SC-xxx) point at the spec so every design decision stays traceable.

---

## 1. Product in one paragraph

A dashboard for engineering leads that reads an organisation's GitHub activity and answers one caring question: **who needs a check-in right now, and why?** It shows per-developer activity (commits, reviews, comments), overwork signals (out-of-hours commits and PR activity), spread (projects in flight), review climate (comment tone), and rushing (volume outpacing review quality), with short AI summaries of any project or person. It is a care instrument, not a surveillance tool, and the design must feel that way.

## 2. Design principles (binding)

1. **Supportive, never accusatory.** Flags are conversation prompts, not verdicts. Microcopy says "might be worth a check-in", never "underperforming", "problem", or "offender". No leaderboards, no rankings, no composite person scores (FR-029).
2. **Every flag explains itself.** A flag is never an icon alone; it always carries or reveals a plain-language reason ("6 of 20 commits landed after 22:00 in their local time this fortnight") (FR-028, SC-010).
3. **Calm visual temperament.** Attention states use warm, measured emphasis, not alarm styling. Red-as-danger is reserved for system errors, never for people.
4. **Honest data.** Every view states its scope, period, and coverage ("14 days, 25 most recently active repositories") (FR-007). AI content is always labelled as AI-generated with its scope and period (FR-017, SC-006). Bounded analyses state their bounds ("analysed 200 of 340 comments") (FR-020).
5. **Never blocked, never blank.** Every data surface has designed loading, empty, partial, and error states (FR-011). AI being down never dims the rest of the dashboard (FR-014, SC-009).
6. **Accessible.** Flags are never conveyed by colour alone (pair icon plus text). Contrast meets WCAG AA. All interactive elements are keyboard reachable.

## 3. Global shell

Present on every screen:

- **App header** with product name and, when demo mode is on, a clearly visible "Demo data" badge; demo content uses obviously fictitious identities (FR-013).
- **Scope switcher**: organisation (default) or a single project. Organisation scope states its coverage: "Covering the 25 most recently active repositories" (FR-007).
- **Period selector**: 7, 14 (default), or 30 days (FR-009). Changing scope or period recalculates every view without any reconfiguration (SC-003).
- **Check-in alert indicator**: a prominent badge showing the count of developers newly needing a check-in in the selected scope and period; one click opens the check-in roster. It clears once the roster is viewed and reappears only when a further developer becomes flagged (FR-030, FR-031, SC-011). One indicator, one count; alerts never stack.
- **Data freshness line**: when live data cannot refresh, the last loaded data stays visible with its load time (FR-011).

## 4. Screens

### 4.1 Team overview (landing)

The per-developer activity table for the selected scope and period (FR-002 to FR-004, FR-008):

- Columns: developer, commits, PR reviews (each submitted review counts separately), comments, out-of-hours commit share, out-of-hours PR share, projects in flight (organisation scope only), flags.
- Wellbeing flags render as labelled chips: Overwork (commits), Overwork (PR activity), Spread thin, Tone, Possible rushing. Each chip reveals its reason on hover or tap.
- **No-activity group**: members with no recorded activity sit in a separate, visually quieter group with zero counts; they are never dropped (FR-012). An "unmatched activity" bucket collects events that cannot be attributed (edge case).
- Sorting affordances are fine, but the default order must not read as a performance ranking; default to alphabetical within flagged and unflagged groupings.

### 4.2 Developer detail

- Header: developer identity, period, scope, flags with reasons.
- **Time-of-commit distribution**: commits across hours and weekdays, evaluated in the author's local time (state this basis in a caption), out-of-hours share against the 25 percent threshold (FR-005, FR-006).
- PR after-hours share with its caption: evaluated in the organisation timezone because PR events carry no author-local offset (FR-024, FR-025).
- **AI summary panel**: on-request generation (never automatic on page load), at most roughly 120 words, supportive wording, marked "AI-generated · [scope] · [period]" (FR-016, FR-017). States: idle (button), loading (target under 10 seconds, SC-005), ready (with refresh affordance; results are session-cached per FR-021), unavailable (friendly message, rest of page unaffected), no-activity (plain statement, no speculation).

### 4.3 Project detail

- Project activity summary for the period, contributors, flags present within this scope.
- **AI project summary panel**: identical mechanics to 4.2 (FR-015).

### 4.4 Check-in roster

The heart of the product (FR-026 to FR-028, SC-010):

- Headline: "N people might need a check-in" for the selected scope and period.
- Ordered list, most concurrent flags first; each entry shows the developer, every active flag as a plain-language reason, and a link to their detail page.
- At project scope, the roster covers that project's activity only, and the spread-thin signal is absent (not shown as zero); a short caption explains this.
- When tone signals are unavailable (AI down or unconfigured), the roster states that tone signals are absent (SC-009).
- **Positive empty state**: "Nobody appears to need a check-in right now" with a warm, affirming design; this state is a success, not an absence.

### 4.5 Tone view

- Aggregate tone distributions (positive, neutral, negative) per project and per comment author (FR-018).
- Tone flags where negative share exceeds 20 percent (FR-019).
- Analysed-versus-total statement whenever the 200-comment bound applies, and a partial-failure statement ("analysed 120 of 200 before the service became unavailable") (FR-020).
- **Hard rule**: no individual comment is ever shown with a tone verdict, and no comment text is quoted with a classification (FR-018). Unanalysable comments count as "unanalysed", never as negative.

### 4.6 Quality versus quantity view

- Per developer with at least 3 PRs: volume (commits, PRs opened) side by side with rework proxies (changes-requested share, average review rounds per PR) (FR-027).
- Developers below the sample: "Not enough review data to say anything useful", with no judgement styling.
- The possible-rushing flag appears only when both conditions hold (above-median volume AND changes-requested share above 40 percent) and reads supportively: "High output with rising rework; possibly rushing".
- **No composite score, index, or rank anywhere on this screen** (FR-029).

## 5. States to design for every data surface

| State | Requirement |
|-------|-------------|
| Loading | Skeleton or progress, never a blank page |
| Empty period | Plain statement that there was no activity (edge case) |
| Credential missing or invalid | Clear, actionable setup message (edge case) |
| Rate limited or service down | Problem stated; last loaded data kept visible with timestamp (FR-011) |
| AI unavailable | Friendly message local to AI panels; everything else fully alive (FR-014, SC-009) |
| Demo mode | Badge always visible; all views work identically (FR-013) |

## 6. Voice and microcopy

- Address the lead as a colleague; refer to developers respectfully by name.
- Reasons follow the pattern: observation, context, gentle suggestion. Example: "8 of Developer A's 30 commits this fortnight landed after hours in their local time. It might be worth a quiet check-in."
- Never: scores, grades, comparisons between named people, exclamation-mark urgency, or automated conclusions stated as fact ("is burnt out"). Always: "appears", "might", "worth a conversation".
- AI labels use exactly: "AI-generated · [scope] · [period]".
- Use fictitious identities (Developer A, Developer B) in all design mock-ups; never real names.

## 7. Explicitly out of scope (do not design)

- Sign-in, user management, or roles (trusted internal tool).
- External notifications of any kind: email, chat, push (in-app alert only, FR-032).
- Exports, sharing, or copy-as-report of AI content (FR-023).
- Per-comment tone display, tone excerpts, or tone timelines.
- Historical trends beyond the selected period, check-in follow-up workflow (acknowledge, snooze), tickets completed (FR-034).

## 8. Design acceptance checklist

The adjusted design is complete when:

- [ ] Every screen in section 4 exists with all states in section 5.
- [ ] A lead can answer "how many people need a check-in and why" within 30 seconds of landing (SC-010) and spot the highest out-of-hours developers within 30 seconds (SC-002).
- [ ] The alert indicator's appear, clear, and reappear lifecycle is designed (SC-011).
- [ ] Every flag everywhere carries a plain-language reason (SC-010).
- [ ] All AI content is labelled with the exact AI label pattern (SC-006).
- [ ] Scope, period, and coverage statements are visible on every data view (FR-007).
- [ ] No ranking, score, or per-comment tone verdict appears anywhere (FR-029, FR-018).
- [ ] The demo-mode badge and fictitious identities are used in every mock-up (FR-013).
- [ ] Flags are readable without colour perception and the design meets WCAG AA contrast.
