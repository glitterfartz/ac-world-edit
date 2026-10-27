# CLAUDE.md

ACWorldEdit is a Godot 4.7 (.NET / C#, `net8.0`) 3D world viewer and editor. It renders WoW client data (3.3.5 MPQ or retail CASC) and overlays world-database content: AzerothCore for the 3.3.5 client, TrinityCore master (`world` plus `hotfixes`) for retail. Edits are staged as drafts and exported as SQL; the database connection is read-only.

> **Overhaul in progress: TrinityCore backend** (branch `trinity-master`; remaining plan in `docs/TC-DB-Update-Plan.md`, status in `docs/tc-status.md`). Phases 1–6 are done (Phase 6, creature properties, edits the template, stats, vendor, gossip, trainer and quest-link pages for TrinityCore; refinements are in the status page); shared Template, vendor, gossip, trainer and quest-link descriptors exist for both dialects. Phases 7–9 (quests, SmartAI, waypoints) are not started and their editors are off for TrinityCore.
> - New database code goes through one interface: reads through `DBSource.Open(...)` → `IDBSource`, schema knowledge through `IDBSource.Schema` (`IWorldSchema`; Foundation code uses `WorldSchemas.For(dialect)`). Don't construct `Ac*Source`/`Tc*Source` outside `Scripts/Data` or add uses of the AzerothCore-only statics (`InteractionTables`, `SmartCatalog`, `QuestModel`, `NpcFlags`); add missing descriptors to `IWorldSchema`.
> - Legacy forms still in the code are listed under "Coexisting forms" in `docs/tc-status.md`; don't build on them.

## Docs

Architecture lives in `docs/`, not here. Read the relevant page before working in an area:

- `docs/ARCHITECTURE.md`: overview, layers and dependency rules, threading, repository layout, glossary.
- `docs/architecture/client-data.md`: `Scripts/Assets` (MPQ/CASC, formats, terrain, models, retail, listfile).
- `docs/architecture/viewer-runtime.md`: `Scripts/Viewer`, `Rendering`, `Overlays`, `Ui`, `Diagnostics`, shaders, smoke flags.
- `docs/architecture/editing-and-export.md`: `Scripts/Data` and `Foundation` (drafts, SQL export).
- `docs/architecture/testing-and-tooling.md`: test projects, categories, tooling.
- `docs/DB-DIFFERENCES.md`: AzerothCore vs TrinityCore schema differences. Official schema docs: AzerothCore https://www.azerothcore.org/wiki/database-world, TrinityCore https://trinitycore.info/database/master/world/home.
- `docs/tc-status.md`: status of the TrinityCore work and the project's other open work and known gaps.
- `docs/NEEDED-TESTS.md`: tests deferred until their feature works.

## Commands

Shell is PowerShell on Windows. SDK is pinned in `global.json`; Godot is found via `--godot-path`, `GODOT_BIN` or `godot` on PATH.

```powershell
python launch_viewer.py                   # build, then launch the viewer
python launch_viewer.py --smoke           # build, then run Godot headless with --terrain-smoke
python launch_viewer.py --check-assets    # corpus tests (needs real client data)

dotnet test Tests/ACWorldEdit.Tests -p:NuGetAudit=false                                       # default suite
dotnet test Tests/ACWorldEdit.Tests --filter "FullyQualifiedName~SplitTerrainTests"            # one class
dotnet test Tests/ACWorldEdit.Tests --settings Tests/ACWorldEdit.Tests/corpus.runsettings     # Category=Corpus
dotnet test Tests/ACWorldEdit.Tests --settings Tests/ACWorldEdit.Tests/knownbugs.runsettings  # Category=KnownBug
dotnet test Tests/ACWorldEdit.Tests --settings Tests/ACWorldEdit.Tests/all.runsettings        # everything
```

`default.runsettings` excludes `Corpus` and `KnownBug`, and `--filter` does not override that: filter those tests together with `--settings corpus.runsettings` (or `all.runsettings`). `Tests/ACWorldEdit.GdTests` (gdUnit4) needs `GODOT_BIN`.

Smoke workflows are opt-in flags passed after `--` to Godot (`--terrain-smoke`, `--creature-smoke`, `--client-smoke`, ...; full list in `viewer-runtime.md`). `--client-shot` / `--client-shot-tile=X,Y` (windowed) saves `artifacts/client-shot.png` framed on the largest nearby building with the UI hidden; `--client-shot-water` frames the largest liquid. Use them to inspect rendering.

Environment variables for tests and tools:

- `ACWORLD_DB_INFO` = `host;port;user;password;database` (AzerothCore Db tests); `ACWORLD_TC_DB_INFO` = the same plus optional `;hotfixes` (TrinityCore Db tests, `TcDbFact`).
- `ACWORLD_DATA_DIR` (3.3.5 client), `ACWORLD_RETAIL_DIR` (retail client), `ACWORLD_CLIENT_PROFILE`: `ClientData`/`Corpus` tests.
- `ACWORLD_CASCLIB` (`CascLib.dll`; `Tools/get_casclib.py` copies it into `native/win-x64/`), `ACWORLD_DBD_DIR` (defaults to the `Data/WoWDBDefs` submodule; `git submodule update --init`).
- `ACWORLD_LISTFILE` overrides the community listfile path; `RetailListfileFact` tests skip without it.

## Notes

- `artifacts/`, `native/`, `.build-env/`, `wow-data/` and `bin/obj` are git-ignored; `artifacts/` holds throwaway probes and project copies, so ignore it when searching.
- Godot generates a `.cs.uid` beside every script; they show up untracked after new files are added.
- The launcher redirects `APPDATA`/`LOCALAPPDATA` into `.build-env/`; use `-p:NuGetAudit=false` for dotnet commands.
- A PostToolUse hook runs `dotnet format whitespace` on each edited `.cs` file.
- Test retail with pre-Cataclysm areas such as Goldshire/Elwynn Forest: the database's spawns come from 3.3.5, so later zones often have no creatures.

## Rules

- This file holds only commands, workflow notes and rules. Architecture (folder structure, class responsibilities, data flow, formats, feature internals) goes in `docs/ARCHITECTURE.md` or the matching `docs/architecture/*.md` page.
- Update the docs in the same change as the code. Anything added, renamed, removed or replaced (class, file, table, command, flag, environment variable, pattern) updates every doc page that describes it, including `docs/tc-status.md` when work lands or a gap is found and `docs/NEEDED-TESTS.md` when a deferred test is added. Fix any wrong doc statement you come across. Before committing, search the docs for the names you touched and commit the doc edits with the code.
- `Foundation/`, `Scripts/Assets/` and `Scripts/Data/` must stay Godot-free: the xUnit project compiles them without a Godot reference.
- For world-database tables or columns, check the official schema docs (linked above), not only the live database or memory. Record AC/TC differences that affect the editors in `docs/DB-DIFFERENCES.md`.
- `azerothcore-wotlk` and `wow-data` are read-only reference and must never be modified (enforced by `.claude/hooks/block_readonly.py`).
- Dialect-specific classes must use the `Ac` prefix for AzerothCore and `Tc` for TrinityCore, including file names. Keep them in corresponding `AzerothCore/` and `TrinityCore/` directories under their feature or layer. Shared interfaces, descriptors, context and renderers stay unprefixed in the common directory. When working in a feature, rename and move legacy dialect-specific classes to follow this convention; update references and move Godot `.cs.uid` companions with their scripts.
- Follow features with tests, written after the feature works (not test-first for large features). Skip trivial getters/setters and Godot UI wiring. Keep tests fast and deterministic; slow corpus tests are opt-in.
- Inspect visual changes in Godot before calling them done.
- UI and status text says what happened ("Saved.", "Moved graveyard."). Don't say changes are a "local draft" or that the "database/source is unchanged"; mention drafts or the database only when it matters to a decision or an error.
- Status and feedback messages go to the status bar (`StatusBar`, fed by `StatusLog`; overlays use their `Report` callback), never into Scene Explorer windows or overlay inspectors.

## Commit messages

- Subject: `type(scope): summary`, scope optional, imperative, lowercase after the colon, no trailing period, at most 72 characters. No placeholders ("WIP", "misc", "updates", "fixes").
- Types: `feat` (new user-visible capability), `fix`, `perf`, `refactor` (no behavior change), `test`, `docs`, `build` (csproj, global.json, NuGet, launcher), `chore` (repo hygiene, moving or deleting files), `style` (formatting only).
- Scopes: `assets`, `archives`, `formats`, `models`, `world`, `retail`, `client`, `data`, `foundation`, `viewer`, `rendering`, `overlays`, `ui`, `diagnostics`, `tests`, `docs`.
- Body only when needed: blank line, prose wrapped at 72 columns, what changed and why. No file lists.
- One logical change per commit; split unrelated work.
- No AI attribution: never add a `Co-Authored-By:` trailer naming Claude or any AI model, and remove one when amending.
