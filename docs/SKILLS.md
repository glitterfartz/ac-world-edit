# Project skills

Claude Code skills checked into `.claude/skills/<name>/SKILL.md`. Each `SKILL.md` holds the full instructions; this is only the index. Invoke one with `/<name> [arguments]`; skills without `disable-model-invocation` also trigger automatically when a request matches their description.

| Skill | Invocation | Purpose |
|---|---|---|
| `audit` | `/audit [area ...]` only | Read-only survey with parallel explorer agents, written to `artifacts/audit.md`; ranks findings and names the skill that fixes each |
| `refactor` | `/refactor <area> \| recent` only | Behavior-preserving cleanup of production code in one area: AC/TC sharing, interfaces, legacy forms, naming, dead code |
| `consolidate-tests` | `/consolidate-tests <area>` only | Merge and trim xUnit, gdUnit and smoke checks in one area while proving coverage did not drop |
| `sync-docs` | `/sync-docs [scope]` only | Check `CLAUDE.md`, the README and `docs/` against the code and fix them; edits docs only |
| `optimize` | `/optimize <area>` only | Measure (`--perf-smoke`, monitors, probes), make local proven performance wins, report overhauls |
| `check-layering` | `/check-layering` or automatic | Grep the Godot-free folders for Godot references, then run the default test suite |
| `commit` | `/commit` only | Commit the working tree using the "Commit messages" rules in `CLAUDE.md` |
| `run-ac-world-edit` | `/run-ac-world-edit` or automatic | Build and run the viewer through `driver.py`: smoke workflows and screenshots |

`refactor`, `consolidate-tests`, `sync-docs` and `optimize` share `.claude/skills/_shared/working-rules.md` (scope and plan, subagent permission, per-step build/test/smoke/commit loop, git and database boundaries, report). Each skill's own file adds what it changes and the rule that proves a step safe: same behavior and suite counts (`refactor`), no coverage drop (`consolidate-tests`), a verified claim (`sync-docs`), a repeatable before/after number (`optimize`). `audit` finds the work and these four do it, one area per run.

To add a skill, create `.claude/skills/<name>/SKILL.md` with `name` and `description` frontmatter (add `disable-model-invocation: true` for request-only skills, `argument-hint` when it takes arguments), keep helper scripts beside it, and add a row here.
