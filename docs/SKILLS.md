# Project skills

Claude Code skills checked into `.claude/skills/<name>/SKILL.md`. Each `SKILL.md` holds the full instructions; this is only the index. Invoke one with `/<name>`; skills without `disable-model-invocation` also trigger automatically when a request matches their description.

| Skill | Invocation | Purpose |
|---|---|---|
| `audit` | `/audit` only | Read-only codebase audit with parallel explorer agents, written to `docs/audit.md` |
| `check-layering` | `/check-layering` or automatic | Grep the Godot-free folders for Godot references, then run the default test suite |
| `commit` | `/commit` only | Commit the working tree using the "Commit messages" rules in `CLAUDE.md` |
| `run-ac-world-edit` | `/run-ac-world-edit` or automatic | Build and run the viewer through `driver.py`: smoke workflows and screenshots |

To add a skill, create `.claude/skills/<name>/SKILL.md` with `name` and `description` frontmatter (add `disable-model-invocation: true` for request-only skills), keep helper scripts beside it, and add a row here.
