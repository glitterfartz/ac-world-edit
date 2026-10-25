# Project skills

Claude Code skills checked into this repo under `.claude/skills/`. Each skill is a `SKILL.md` (frontmatter `name` + `description`, then instructions) plus any helper scripts. Invoke one by name with `/<name>`; skills without `disable-model-invocation` can also be picked up automatically when a request matches their description.

| Skill | Invocation | Purpose |
|---|---|---|
| [`audit`](#audit) | `/audit` only | Read-only codebase audit written to `docs/audit.md` |
| [`check-layering`](#check-layering) | `/check-layering` or automatic | Check the Godot-free folders for Godot references, then run the fast test suite |
| [`commit`](#commit) | `/commit` only | Commit the working tree using the repo's commit convention |
| [`run-ac-world-edit`](#run-ac-world-edit) | `/run-ac-world-edit` or automatic | Build and run the viewer, run smoke workflows, take screenshots |

## audit

`.claude/skills/audit/SKILL.md`

A read-only audit of the whole repo, written to `docs/audit.md`. It changes no other file. It sets `disable-model-invocation: true` because it starts several subagents, so it runs only when you type `/audit`.

**Steps**

1. Ask before starting subagents (the user's global rule): explain why, name the session's model, show the split with file and line counts, and recommend a model for each agent (Sonnet for the explorers).
2. Start 8 read-only explorer agents in parallel, one per area: `Foundation/`, `Scripts/Assets/`, `Scripts/Data/`, `Scripts/Viewer/` + `Scripts/Rendering/`, `Scripts/Overlays/`, `Scripts/Ui/`, Diagnostics and tools, and `Tests/`. Build output, `artifacts/`, `Experiments/`, the WoWDBDefs submodule and the read-only game/DB data paths are skipped.
3. Check the explorers' duplication, dead-code and cross-area claims against the code (repo-wide searches, including `.tscn` files and string-based uses), and find the duplication and inconsistencies that only show up across areas.
4. Write `docs/audit.md` with six sections: module map, duplication, inconsistencies, dead code and half-finished abstractions, test coverage, and simplification opportunities ranked by risk (low/med/high), each with the tests it needs first. Every claim cites `path:line`; anything not confirmed is marked *unverified*.

## check-layering

`.claude/skills/check-layering/SKILL.md`

`Foundation/`, `Scripts/Assets/**` and `Scripts/Data/**` are compiled directly into `Tests/ACWorldEdit.Tests`, which does not reference Godot, so any Godot reference there breaks the test build.

**Use** after editing those folders, or before committing.

**Steps**

1. Grep `*.cs` under `Foundation`, `Scripts/Assets` and `Scripts/Data` for
   `using Godot|\bGodot\.|\bGD\.|\bNode3D\b|\bVector3\b|ProjectSettings|res://|user://`.
   Matches in plain prose comments are ignored; anything else is reported as `file:line`.
2. Run `dotnet test Tests/ACWorldEdit.Tests -p:NuGetAudit=false`.
3. Report the violations and the pass/fail/skip counts. Violations are not fixed unless asked.

## commit

`.claude/skills/commit/SKILL.md`

Commits the current changes following the "Commit messages" rules in `CLAUDE.md`. It sets `disable-model-invocation: true`, so it runs only when you type `/commit`.

**Steps**

1. Read `git status` and `git diff` (staged and unstaged). Unrelated work is grouped and committed separately, staging only each group's files.
2. Subject `type(scope): summary`: imperative, lowercase after the colon, no trailing period, 72 characters or fewer.
   - Types: `feat`, `fix`, `perf`, `refactor`, `test`, `docs`, `build`, `chore`, `style`.
   - Scopes: `assets`, `archives`, `formats`, `models`, `world`, `retail`, `client`, `data`, `foundation`, `viewer`, `rendering`, `overlays`, `ui`, `diagnostics`, `tests`, `docs`.
3. A body wrapped at 72 columns only when the change needs explaining (what and why, no file list).
4. Untracked `.cs.uid` files are skipped unless their `.cs` file is in the commit.
5. No AI `Co-Authored-By:` trailers, even if the session provides one, and no placeholder subjects (WIP, misc, updates, fixes).
6. `git log --oneline -5` afterwards to confirm.

## run-ac-world-edit

`.claude/skills/run-ac-world-edit/SKILL.md`, helper `driver.py`

Builds the project and runs one of the app's own `--*-smoke` workflows, printing only the result lines (lines containing `SMOKE`, `CLIENT_SHOT`, `ERROR`, `Unhandled` or `Exception`). Verified on Windows 11.

**Use** when asked to run the viewer, take a screenshot, or confirm a rendering or loading change works in the real app.

**Environment**

- `GODOT_BIN`: the Godot .NET exe. The driver switches to its `_console.exe` sibling so output is captured.
- `ACWORLD_DATA_DIR`: the 3.3.5 client `Data` folder. `ACWORLD_CLIENT_PROFILE` defaults to `wrath`.
- `ACWORLD_DB_INFO`: optional for these flows.

**Commands**

```bash
python .claude/skills/run-ac-world-edit/driver.py client          # headless: client opens, first tile streams
python .claude/skills/run-ac-world-edit/driver.py shot            # windowed: saves artifacts/client-shot.png
python .claude/skills/run-ac-world-edit/driver.py shot 33,25      # same, at ADT tile 33,25
python .claude/skills/run-ac-world-edit/driver.py shot --water    # frame the largest liquid instead of a building
python .claude/skills/run-ac-world-edit/driver.py terrain         # any --<name>-smoke: terrain, water, creature, ...
```

With no argument the driver runs `client`. Every mode runs `dotnet build ACWorldEdit.csproj` first; the Godot run times out after 300 s.

**Results**

- Success is a `*_SMOKE_OK` or `TERRAIN_SMOKE_PASSED` line and exit code 0.
- `shot` opens a real window briefly (the image is read from the viewport), hides the UI, frames the largest nearby building, and prints `screenshot: <path>`. Inspect the PNG for non-black terrain and textured models.

**Gotchas**

- `launch_viewer.py` redirects `APPDATA`/`LOCALAPPDATA` into `.build-env/`, so Godot's saved settings are not found there; the driver does not redirect, and the environment variables above override saved settings.
- The loaded map can vary between runs; the `map=...` log line says which.
- `Multitexture M2 batch` and `WMO shader N uses base-texture preview` warnings are normal and filtered.
- Any shell command mentioning the `wow-data` path is blocked by `.claude/hooks/block_readonly.py`, even read-only ones.
- `CLIENT_SMOKE_NO_CLIENT` (after about 5 s): no client folder resolved; export `ACWORLD_DATA_DIR`.
- `CLIENT_SMOKE_TIMEOUT` (180 s): the tile or model load stalled; the message shows `loaded=N/M modelsBusy=...`.

## Adding a skill

Create `.claude/skills/<name>/SKILL.md` with `name` and `description` frontmatter (the description decides when it triggers automatically; add `disable-model-invocation: true` for skills that should run only on request), keep helper scripts beside it, and add a section here.
