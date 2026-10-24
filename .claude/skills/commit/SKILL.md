---
name: commit
description: Commit the current changes using this repo's commit convention (type(scope): summary), splitting unrelated work into separate commits.
disable-model-invocation: true
---

# Commit

Follow the "Commit messages" rules in `CLAUDE.md`:

1. Run `git status` and `git diff` (staged and unstaged). If the changes mix unrelated work, group them and make one commit per logical change, staging only that group's files.
2. Subject: `type(scope): summary`. Types: feat, fix, perf, refactor, test, docs, build, chore, style. Scopes: assets, archives, formats, models, world, retail, client, data, foundation, viewer, rendering, overlays, ui, diagnostics, tests, docs. Imperative, lowercase after the colon, no trailing period, 72 characters or fewer.
3. Add a wrapped (72 col) body only when the change needs explaining: what and why, not a file list.
4. Skip untracked `.cs.uid` files unless their `.cs` file is part of the commit.
5. End the message with the attribution trailer the session provides. Never use placeholder subjects (WIP, misc, updates, fixes).
6. Run `git log --oneline -5` afterwards to confirm.
