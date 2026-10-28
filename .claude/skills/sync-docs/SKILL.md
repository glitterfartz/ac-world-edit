---
name: sync-docs
description: Audit ACWorldEdit's documentation against the code and fix it - stale, missing, duplicated or wrong statements in CLAUDE.md, README and docs/ (renamed classes, removed flags, wrong counts, outdated status, broken links). Edits docs only.
argument-hint: "[page | area | since <commit> | last <N> commits]"
disable-model-invocation: true
---

# Sync docs

Keep the documentation true to the code. Edit documentation only: never change code, tests, scenes or settings to match a doc. If the code looks wrong and the doc looks right, report it.

Scope: `$ARGUMENTS`. If it is empty, audit everything. Say which scope you took.

Read `.claude/skills/_shared/working-rules.md` first; its Subagents, Boundaries and Report sections apply. The per-step build/test loop does not, since you change no code.

## Where things go

Read `CLAUDE.md` first; its Rules decide placement:

- `CLAUDE.md` holds only commands, workflow notes and rules. Architecture (folder structure, class responsibilities, data flow, formats, feature internals) belongs in `docs/ARCHITECTURE.md` or the matching `docs/architecture/*.md` page. Move misplaced architecture out of `CLAUDE.md`; don't copy it.
- `docs/ARCHITECTURE.md`: overview, layers, dependency rules, threading, repository layout, glossary.
- `docs/architecture/client-data.md`: `Scripts/Assets`. `viewer-runtime.md`: `Scripts/Viewer`, `Rendering`, `Overlays`, `Ui`, `Diagnostics`, shaders, smoke flags. `editing-and-export.md`: `Scripts/Data`, `Foundation`. `testing-and-tooling.md`: test projects, categories, tooling.
- `docs/DB-DIFFERENCES.md`: AC vs TC schema differences that affect the editors.
- `docs/tc-status.md`: the one living status, plan and TODO doc. Fold any stray plan or TODO doc into it, and remove the stray content only after moving it.
- `docs/NEEDED-TESTS.md`: tests deferred until their feature works. `docs/performance.md`: benchmark results and open performance work. `docs/SKILLS.md`: the index of `.claude/skills`.
- Any other `*.md` (README, proposals, tool notes): check them too, but skip `artifacts/`, `native/`, `.build-env/`, `wow-data/`, `azerothcore-wotlk/`, `Data/WoWDBDefs/`, `bin/`, `obj/` and `addons/`. Never edit those.

## How to audit

1. **Inventory.** Glob every `.md` outside the skipped folders. For each, note the last-touched commit (`git log -1 --format="%h %cs" -- <file>`) and the commits since then that touched the code it describes (`git log --oneline <hash>..HEAD -- <code paths>`). Stale pages first.
2. **Verify each checkable claim** against the code with Grep/Glob/Read, citing `file:line`:
   - **Names:** every class, file, method, field, flag, setting, env var, table, column, shader, scene, folder and command still exists with that spelling and location, including current `Ac`/`Tc` names.
   - **Numbers:** limits, budgets, counts, thresholds, versions, test and phase counts, tile radius, cache sizes. Look them up (`DbLimits`, `*Limit` constants, upload budgets in `TerrainViewer` and `ModelLibrary`, `global.json`, the csproj).
   - **Behavior:** described flows (per-frame order, threading, streaming, draft and export steps) still match. Read the code, not a neighboring doc.
   - **Lists and tables:** overlays, panels, smoke flags (against `ConfigureSmokeChecks` and the flag parsing in `Scripts/Diagnostics/`), sources, descriptors, test projects and categories, env vars (against `SettingsResolver` and the tests), skills (against `.claude/skills/`).
   - **Status:** `docs/tc-status.md` and the status blurb in `CLAUDE.md` match the code and tests, not the commit messages.
   - **Links:** relative links, anchors and the official schema URLs resolve; `docs/` pages named in `CLAUDE.md` exist.
   - **Commands:** every command in `CLAUDE.md`, `testing-and-tooling.md` and the README is valid. Cheap read-only checks (`--help`, `dotnet test --list-tests`) are fine; don't run long suites unless the claim is about their result.
   - **Schema facts:** table and column names agree with the sources (`Ac*Source`, `Tc*Source`, descriptors) and, when in doubt, the official schema docs (AzerothCore https://www.azerothcore.org/wiki/database-world, TrinityCore https://trinitycore.info/database/master/world/home). A live table may be checked read-only; the official docs win on meaning.
3. **Reverse check.** Public pieces with no docs: classes and files under `Scripts/`, `Foundation/`, `Tools/`, `Tests/`; smoke flags; env vars; settings keys; scenes; shaders; skills. Give a new piece a row or paragraph on the page that owns its area, at the page's existing level of detail. Don't document trivia or private helpers.
4. **Duplication and contradiction.** A fact on two pages lives on one and is linked from the other. Pages that disagree are fixed to the code's truth. Remove dead sections (finished plan text, "delete when X lands" notes whose condition is met) after confirming.
5. **Voice.** Short, factual, present tense, no marketing, no history unless the page is a status or history page. Keep tables, heading depth and mermaid diagrams consistent and valid.

## Rules

- Don't invent. A claim you can't verify from code, tests or an official source stays as is and goes in the report as "unverified" with what you checked.
- Delete or rewrite only what you have shown to be wrong, duplicated or finished; don't reword correct pages.
- Quoted UI text follows the UI-text rules (says what happened; never "local draft" or "database unchanged").
- Keep each file's line endings (the repo mixes CRLF and LF). Use Edit for targeted changes; for scripted rewrites, preserve `\r\n`.
- If a doc and the code disagree and you can't tell which is intended, report it. If you find a bug, report it; don't fix it.

## Commits

Docs only, grouped by page or topic, one logical change per commit: `docs(scope): summary`, scope naming the area (`docs(viewer): ...`, `docs(data): ...`) or none.

## Subagent slices

For a full audit, these four slices split the work well (ask first, per the shared rules): `client-data.md` vs `Scripts/Assets`; `viewer-runtime.md` vs `Scripts/Viewer`, `Rendering`, `Overlays`, `Ui`, `Diagnostics`; `editing-and-export.md` and `DB-DIFFERENCES.md` vs `Scripts/Data` and `Foundation`; `testing-and-tooling.md`, `tc-status.md`, `SKILLS.md` and `CLAUDE.md` vs `Tests/`, `Tools/`, `.claude/skills/` and the csproj files. Ask each for wrong, stale or missing statements with the doc `file:line`, the contradicting code `file:line` and the correct text.

## Report

The scope audited; pages changed with a one-line summary each; commits; statements left unverified or needing a decision (doc `file:line`, what you checked); code issues noticed but not touched; code you chose not to document and why. List meaning changes, not typos.
