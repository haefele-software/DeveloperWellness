# DeveloperWellness

## Model policy (binding)

This repository separates model duties. Follow this in every session:

- **Specification and planning run on Fable 5 at maximum reasoning.** All Spec Kit planning skills (`/speckit-constitution`, `/speckit-specify`, `/speckit-clarify`, `/speckit-plan`, `/speckit-tasks`, `/speckit-checklist`, `/speckit-analyze`, `/speckit-converge`, `/speckit-taskstoissues`) are pinned to `claude-fable-5` in their frontmatter. The project default model is Fable 5 with effort `xhigh` (`.claude/settings.json`). Ad hoc design, architecture, and planning discussion also stays in the main loop on Fable 5; apply maximum reasoning (ultrathink).
- **Implementation runs on Sonnet 5.** `/speckit-implement` is pinned to `claude-sonnet-5`. Any code-writing outside that skill must be delegated to a subagent running `claude-sonnet-5`: prefer the `scaffold-implementer` agent when available, otherwise `general-purpose` with `model: "claude-sonnet-5"`. The main loop reviews and verifies the returned work; it does not write feature code itself.
- **Preferred implementation entry point**: `/speckit-implement-scaffold` (global skill). It keeps planning and verification in the main loop on Fable 5 and delegates all implementation to the Sonnet `scaffold-implementer` subagent, enforcing the scaffold completeness checklist per task.

## Workflow

GitHub Spec Kit is installed (`.specify/` plus `.claude/skills/speckit-*`, PowerShell scripts). Standard feature flow:

1. `/speckit-constitution` (once, project principles)
2. `/speckit-specify` then optionally `/speckit-clarify`
3. `/speckit-plan` then `/speckit-tasks` (optionally `/speckit-checklist`, `/speckit-analyze`)
4. `/speckit-implement-scaffold` (preferred) or `/speckit-implement`
