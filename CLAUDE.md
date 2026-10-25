# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

ACWorldEdit is a Godot 4.7 (.NET / C#, `net8.0`) 3D world viewer and editor for AzerothCore and TrinityCore. It reads the WoW client's data (3.3.5 MPQ archives, or the retail CASC client) to render terrain, models and liquids, and overlays world-database content: AzerothCore for the 3.3.5 client, TrinityCore master (`world` plus `hotfixes`) for retail. Edits are staged as drafts and exported as SQL; the database connection is read-only.

> **Overhaul in progress: TrinityCore backend** (branch `trinity-master`, phases in `docs/TC-DB-Update-Plan.md`, live status in `docs/tc-status.md`). Phases 1–5 are done, Phase 6 (creature properties) is read-only so far, Phases 7–9 (quests, SmartAI, waypoints) are not started and their editors are switched off for TrinityCore.
> - New database code goes through one interface: reads through `DBSource.Open(...)` → `IDBSource`, schema knowledge through `IDBSource.Schema` (`IWorldSchema`: `Tables`, `Sql`; Foundation code gets it from `WorldSchemas.For(dialect)`). Don't construct `Ac*Source`/`Tc*Source` outside `Scripts/Data`, and don't add new uses of the AzerothCore-only statics (`InteractionTables`, `SmartCatalog`, `QuestModel`, `NpcFlags`); when a dialect needs a descriptor `IWorldSchema` lacks, add it there.
> - Legacy forms still in the code are listed under "Coexisting forms" in `docs/tc-status.md`; they are being removed, so don't build on them.

## Architecture

Architecture lives in `docs/`, not here. Read the relevant page before working in an area:

- `docs/ARCHITECTURE.md`: overview, layers and dependency rules, threading, repository layout, glossary.
- `docs/architecture/client-data.md`: `Scripts/Assets` (MPQ/CASC, formats, terrain, models, retail specifics, listfile).
- `docs/architecture/viewer-runtime.md`: `Scripts/Viewer`, `Rendering`, `Overlays`, `Ui`, `Diagnostics`, shaders.
- `docs/architecture/editing-and-export.md`: `Scripts/Data` (AzerothCore/TrinityCore sources) and `Foundation` (drafts, SQL export).
- `docs/architecture/testing-and-tooling.md`: test projects, categories, tooling.
- `docs/DB-DIFFERENCES.md`: AzerothCore vs TrinityCore world schema differences.

## Commands

Shell is PowerShell on Windows. SDK is pinned in `global.json`; Godot is located via `--godot-path`, the `GODOT_BIN` environment variable, or `godot` on PATH (no machine-specific paths are hardcoded).

```powershell
python launch_viewer.py                   # dotnet build ACWorldEdit.csproj, then launch the viewer
python launch_viewer.py --smoke           # build, then run Godot headless with --terrain-smoke
python launch_viewer.py --check-assets    # run the corpus test set (needs real client data)

dotnet test Tests/ACWorldEdit.Tests -p:NuGetAudit=false                          # default fast suite
dotnet test Tests/ACWorldEdit.Tests --filter "FullyQualifiedName~SplitTerrainTests"   # one class
dotnet test Tests/ACWorldEdit.Tests --settings Tests/ACWorldEdit.Tests/corpus.runsettings     # Category=Corpus only
dotnet test Tests/ACWorldEdit.Tests --settings Tests/ACWorldEdit.Tests/knownbugs.runsettings  # Category=KnownBug only
dotnet test Tests/ACWorldEdit.Tests --settings Tests/ACWorldEdit.Tests/all.runsettings        # everything
```

`default.runsettings` excludes `Corpus` and `KnownBug` categories, and a command-line `--filter` does not override that: filter a Corpus or KnownBug test together with `--settings corpus.runsettings` (or `all.runsettings`). `Tests/ACWorldEdit.GdTests` is a separate gdUnit4 project that needs `GODOT_BIN` set in the environment.

Smoke workflows are opt-in CLI flags passed after `--` to Godot (`--terrain-smoke`, `--visual-smoke`, `--water-smoke`, `--reload-smoke`, `--creature-smoke`, `--client-smoke`, etc.); they are parsed in `Scripts/Diagnostics/TerrainViewer.Smoke.cs` and never run in normal startup. `--client-shot` / `--client-shot-tile=X,Y` (windowed, not headless) saves `artifacts/client-shot.png` after the tile and its models load, framed on the largest nearby building with the UI hidden; use it to inspect rendering. `--client-shot-water` frames the largest liquid mesh.

### Environment variables used by tests/tools

- `ACWORLD_DB_INFO` = `host;port;user;password;database` — required by the AzerothCore `Tests/.../Db/*` tests.
- `ACWORLD_TC_DB_INFO` = `host;port;user;password;database[;hotfixes]` — required by the TrinityCore Db tests (`TcDbFact`).
- `ACWORLD_DATA_DIR` (3.3.5 client data), `ACWORLD_RETAIL_DIR` (retail client), `ACWORLD_CLIENT_PROFILE` — client data for `ClientData`/`Corpus` tests.
- `ACWORLD_CASCLIB` (path to `CascLib.dll`; fetch with `Tools/get_casclib.py` into `native/win-x64/`), `ACWORLD_DBD_DIR` (WoWDBDefs; defaults to the `Data/WoWDBDefs` submodule (`git submodule update --init`)).
- `ACWORLD_LISTFILE` overrides the downloaded community listfile path; corpus tests that need it (`RetailListfileFact`) skip unless it is present.

## Notes

- `artifacts/`, `native/`, `.build-env/`, `wow-data/` and `bin/obj` are git-ignored scratch/build output; `artifacts/` holds throwaway probes and copies of the project — ignore it when searching.
- `.cs.uid` files are generated by Godot alongside every script; they show up as untracked after new files are added.
- The launcher redirects `APPDATA`/`LOCALAPPDATA` into `.build-env/` to keep CLI caches in the project; use `-p:NuGetAudit=false` for dotnet commands.
- A PostToolUse hook runs `dotnet format whitespace` on each edited `.cs` file.
- When testing retail rendering (tests, smokes, shots), use pre-Cataclysm areas such as Goldshire/Elwynn Forest. The world database's spawns come from 3.3.5, so areas added or reworked after WotLK often have no creatures.

## Rules

- Don't put architecture descriptions (assembly/folder structure, class responsibilities, data flow, file-format details, feature internals) in this file. Add or update them in `docs/ARCHITECTURE.md` or the matching `docs/architecture/*.md` page instead, and keep those docs current when the code changes. This file holds only commands, workflow notes and rules.
- `Foundation/`, `Scripts/Assets/` and `Scripts/Data/` must stay Godot-free: the xUnit project compiles them directly without a Godot reference.
- When working with world-database tables or columns, check the official schema docs, not only the live database or memory: AzerothCore at https://www.azerothcore.org/wiki/database-world and TrinityCore (master) at https://trinitycore.info/database/master/world/home. Record any AC/TC difference that affects the editors in `docs/DB-DIFFERENCES.md`.
- Game and DB data is read-only reference: `azerothcore-wotlk` and `wow-data` paths must never be modified (enforced by `.claude/hooks/block_readonly.py`).
- `docs/NEEDED-TESTS.md` lists tests deferred while implementing retail loading; add them once the feature works and remove the entry when its test exists.
- `docs/tc-status.md` tracks the TrinityCore database work (plan status, gaps, work in progress) and the project's other unfinished features and known gaps; update it when work lands or a new gap is found.
- Feature implementations should be followed up by tests, but don't write tests for trivial getters/setters or Godot UI wiring. Tests should be fast and deterministic; slow corpus tests are opt-in via `--settings corpus.runsettings`.
- For large features, don't implement tests ahead of the feature; implement the feature first, then add tests. Tests should be written to verify the feature works as intended, not to drive the implementation.
- If a feature requires a visual change you must inspect in Godot to verify them.

- Don't add UI text, tooltips or status messages saying that changes are a "local draft" or that the "database is unchanged" / "source is unchanged". That is the app's basic premise; users know it. Write status messages about what happened ("Saved.", "Moved graveyard.") and only mention drafts or the database when the distinction matters to a decision or an error.

- Don't put status or feedback messages in Scene Explorer windows or the overlay inspectors (selection details, query-status labels). Post them to the status bar along the bottom of the screen (`StatusBar`, fed by `StatusLog`; overlays report through their `Report` callback). The newest message shows on the right of the bar and clicking it opens the message history.

## Commit messages

- Subject: `type(scope): summary`, scope optional. Types: `feat` (new user-visible capability), `fix` (wrong behavior corrected), `perf`, `refactor` (no behavior change), `test`, `docs`, `build` (csproj, global.json, NuGet, launcher), `chore` (repo hygiene, .gitignore, moving or deleting files), `style` (formatting only).
- Scopes follow the code layout: `assets`, `archives`, `formats`, `models`, `world`, `retail`, `client`, `data`, `foundation`, `viewer`, `rendering`, `overlays`, `ui`, `diagnostics`, `tests`, `docs`.
- Summary: imperative mood ("add", "fix", "remove"), lowercase after the colon, no trailing period, 72 characters or fewer.
- Body (when the change needs explaining): blank line, then wrapped prose at 72 columns. Say what changed and why, and any constraint a reader needs. Do not list file names the diff already shows.
- One logical change per commit. If a commit mixes unrelated work, split it.
- Never use placeholders such as "WIP", "misc", "updates", "fixes", or stray symbols.
- No AI attribution in commits: never add a `Co-Authored-By:` trailer naming Claude (or any AI model), and remove one if it appears when amending. The commit author is the only attribution.
