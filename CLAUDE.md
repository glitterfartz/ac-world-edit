# CLAUDE.md

ACWorldEdit is a Godot 4.7 (.NET / C#, `net8.0`) 3D world viewer and editor. It renders WoW client data (3.3.5 MPQ or retail CASC) and overlays world-database content: AzerothCore for the 3.3.5 client, TrinityCore master (`world` plus `hotfixes`) for retail. Edits are staged as drafts and exported as SQL; the database connection is read-only.

> **TrinityCore backend** (merged into `master`; plan, phase status and open work in `docs/tc-status.md`).
> - New database code goes through one interface: reads through `DBSource.Open(...)` → `IDBSource`, schema knowledge through `IDBSource.Schema` (`IWorldSchema`; Foundation code uses `WorldSchemas.For(dialect)`). Don't construct `Ac*Source`/`Tc*Source` outside `Scripts/Data` or use the AzerothCore statics (`AcTables`, `AcQuestModel`, `AcNpcFlags`) from shared or TrinityCore code; add missing descriptors to `IWorldSchema`.

## Docs

Architecture lives in `docs/`, not here. Read the relevant page before working in an area:

- `docs/ARCHITECTURE.md`: overview, layers and dependency rules, threading, repository layout, glossary.
- `docs/architecture/client-data.md`: `Scripts/Assets` (MPQ/CASC, formats, terrain, models, retail, listfile).
- `docs/architecture/viewer-runtime.md`: `Scripts/Viewer`, `Rendering`, `Overlays`, `Ui`, `Diagnostics`, shaders, smoke flags.
- `docs/architecture/editing-and-export.md`: `Scripts/Data` and `Foundation` (drafts, SQL export).
- `docs/architecture/testing-and-tooling.md`: test projects, test commands and settings, environment variables, tooling, commit message rules (read before committing).
- `docs/DB-DIFFERENCES.md`: AzerothCore vs TrinityCore schema differences. Official schema docs: AzerothCore https://www.azerothcore.org/wiki/database-world, TrinityCore https://trinitycore.info/database/master/world/home.
- `docs/tc-status.md`: status of the TrinityCore work and the project's other open work and known gaps.
- `docs/NEEDED-TESTS.md`: tests deferred until their feature works.
- `docs/performance.md`: the `--perf-smoke` benchmark, measured costs and open performance work.

## Commands

Shell is PowerShell on Windows. SDK is pinned in `global.json`; Godot is found via `--godot-path`, `GODOT_BIN` or `godot` on PATH.

```powershell
git submodule update --init               # or clone with --recurse-submodules
python Tools/setup.py                     # sync submodules, build CascLib into native/win-x64 (--with-tests adds StormLib)
python launch_viewer.py                   # build, then launch the viewer
python launch_viewer.py --smoke           # build, then run Godot headless with --terrain-smoke
dotnet test Tests/ACWorldEdit.Tests -p:NuGetAudit=false   # default suite
```

Corpus/KnownBug test settings, environment variables (`ACWORLD_*`) and smoke flags are in `testing-and-tooling.md` and `viewer-runtime.md`.

## Notes

- `ACWorldEdit.sln` exists for Godot's .NET export; pass `ACWorldEdit.csproj` explicitly to `dotnet` commands, since a bare `dotnet build` in the root sees two candidates.
- Third-party code lives in pinned submodules (`ThirdParty/CascLib`, `ThirdParty/StormLib`, `Data/WoWDBDefs`); change a pin by checking out the new commit inside the submodule and `git add`-ing it, and keep `THIRD-PARTY.md` current. `Tools/package.py` makes a release build. CI and releases are in `.github/` (workflows and shared composite actions); a release is a pushed `vX.Y.Z` tag on `master`, which creates a draft GitHub Release; the steps are under "Continuous integration" in `docs/architecture/testing-and-tooling.md`.
- `artifacts/`, `native/`, `build/`, `.build-env/`, `wow-data/` and `bin/obj` are git-ignored; `artifacts/` holds throwaway probes and project copies, so ignore it when searching.
- Godot generates a `.cs.uid` beside every script; they show up untracked after new files are added.
- The launcher redirects `APPDATA`/`LOCALAPPDATA` into `.build-env/`; use `-p:NuGetAudit=false` for dotnet commands.
- A PostToolUse hook runs `dotnet format whitespace` on each edited `.cs` file.

## Rules

- This file holds only commands, workflow notes and rules. Architecture goes in `docs/ARCHITECTURE.md` or the matching `docs/architecture/*.md` page.
- Update the docs in the same change as the code: anything added, renamed, removed or replaced updates every doc page that describes it (including `docs/tc-status.md` when work lands or a gap is found, `docs/NEEDED-TESTS.md` when a test is deferred). Fix wrong doc statements you come across; search the docs for names you touched before committing.
- `Foundation/`, `Scripts/Assets/` and `Scripts/Data/` must stay Godot-free: the xUnit project compiles them without a Godot reference.
- For world-database tables or columns, check the official schema docs, not only the live database or memory. Record AC/TC differences that affect the editors in `docs/DB-DIFFERENCES.md`.
- `azerothcore-wotlk` and `wow-data` are read-only reference and must never be modified.
- Dialect-specific classes use the `Ac` / `Tc` prefix (file names too) and live in `AzerothCore/` / `TrinityCore/` directories under their feature or layer; shared interfaces, descriptors, context and renderers stay unprefixed. When working in a feature, rename and move legacy dialect-specific classes to match, moving `.cs.uid` companions with them.
- Write tests after a feature works (not test-first for large features). Skip trivial getters/setters and Godot UI wiring. Keep tests fast and deterministic; slow corpus tests are opt-in.
- Inspect visual changes in Godot before calling them done.
- UI and status text says what happened ("Saved.", "Moved graveyard."). Don't say changes are a "local draft" or that the "database/source is unchanged"; mention drafts or the database only when it matters to a decision or an error.
- Status and feedback messages go to the status bar (`StatusBar`, fed by `StatusLog`; overlays use their `Report` callback), never into Scene Explorer windows or overlay inspectors.
- Commits: `type(scope): summary`, imperative, at most 72 characters, one logical change each. No AI attribution: never add a `Co-Authored-By:` trailer naming Claude or any AI model, and remove one when amending. Full rules in `testing-and-tooling.md`.
