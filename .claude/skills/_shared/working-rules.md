# Working rules for the editing skills

Shared by `refactor`, `consolidate-tests`, `optimize` and `sync-docs` (and the read-only parts by `audit`). Each skill's `SKILL.md` says what it changes and how it proves the change is safe; this file holds what they have in common. `CLAUDE.md` still wins where the two disagree.

## Scope and plan

- Work on one area per run. Take the scope from the skill's arguments. If none was given, pick one by the skill's rule and say which you chose and why.
- Before the first edit, read `CLAUDE.md`, the docs page for the area (`docs/ARCHITECTURE.md`, then the matching `docs/architecture/*.md`) and `docs/tc-status.md` (known gaps, "Coexisting forms", "Open work").
- Record a baseline (the skill says which), then show the user a short plan (at most ~10 items, lowest risk first, each with how it will be checked) and wait for a go-ahead before editing.
- If `artifacts/audit.md` exists and is recent, use its findings for the area as a starting point, but re-verify each against the code.

## Subagents

The user's global instructions require permission before any subagent starts. Skills run in the main session, so:

1. Map the area directly when it is small. Only propose subagents when the area is too large to read without flooding the context.
2. When you do, ask first: say why (parallel read-only mapping keeps raw file contents out of this session), which model this session is running on, the slices (disjoint, with file counts) and the model recommended for each. Recommend **Sonnet** for read-only `Explore` mapping: the work is reading and citing, and Haiku is more likely to miss subtle duplication or report guesses as facts.
3. Wait for a yes. If the user says no, map inline.
4. Subagents are read-only `Explore` agents used only for mapping. Ask each for `file:line` citations and tell it not to edit, build or run anything. Re-read whatever you will act on. All edits, builds, tests, smokes, measurements and commits happen in this session.

## Per step

1. Make one logical change.
2. Build and run the default suite: `dotnet test Tests/ACWorldEdit.Tests -p:NuGetAudit=false`. Compare pass/skip counts with the baseline; a difference needs a reason (the skill says which are acceptable). On a failure, fix or revert.
3. If the step touches UI code, a descriptor that feeds the UI, or anything a smoke covers, run the matching smoke with the `run-ac-world-edit` skill and look at the screenshots it saves. Smokes take minutes: run them in the background with output redirected to the scratchpad. Run both dialects' smokes when shared code changed. Smokes known to fail headless are listed in `docs/tc-status.md`; run those windowed.
4. After editing `Foundation/`, `Scripts/Assets/` or `Scripts/Data/`, run the `check-layering` skill.
5. Update the docs in the same change: grep `docs/` and `CLAUDE.md` for every name you renamed, moved, merged, removed or added, and update `docs/tc-status.md` when work lands or a gap is found. Fix wrong doc statements you come across.
6. Commit with the skill's commit type, following the "Commit messages" rules in `CLAUDE.md` (the `commit` skill has the checklist). Include doc edits and `.cs.uid` companions in the same commit. One logical change per commit. No `Co-Authored-By` or other AI attribution.

## Build and process hygiene

- Use `-p:NuGetAudit=false` for dotnet. The format hook runs on each edited `.cs` file by itself.
- If a stale compiler server breaks the Godot build (CS8785 or "Cannot instantiate C# script"), run `dotnet build-server shutdown` and rebuild with `--no-incremental`.
- Never kill Godot by image name; stop only processes you started.
- Never print `ACWORLD_DB_INFO`, `ACWORLD_TC_DB_INFO` or any connection string, and don't search the transcript for `ACWORLD_*` values.
- The database is read-only: through `mcp__mcp_server_mysql__mysql_query` run only `SELECT`, `EXPLAIN`, `SHOW` or `DESCRIBE` (default schema `acore_world`; TrinityCore data is `world.` and `hotfixes.`).

## Boundaries

- `azerothcore-wotlk` and `wow-data` are read-only; never touch them or name the `wow-data` path in a shell command (`.claude/hooks/block_readonly.py` blocks it).
- Never run `git stash`, `git reset --hard`, `git checkout -- <path>` on files you didn't change, or force-push, and don't push. `stash@{0}` belongs to the user. If the working tree has someone else's uncommitted changes, stage only your own files by path.
- If a step fails and the fix isn't obvious after two attempts, revert only your own uncommitted change and report it.
- If you can't tell whether a change is safe (behavior, coverage or a doc's intent), leave it and list it for the user.
- Don't create standalone plan, TODO or notes files. Open work goes in `docs/tc-status.md`; deferred tests go in `docs/NEEDED-TESTS.md`.

## Report

End with the commits made (hash and subject), the before/after numbers the skill names, the smokes run with their result lines, what you left in place and why, and anything that needs the user's decision. Describe only what matters to someone reading the code.
