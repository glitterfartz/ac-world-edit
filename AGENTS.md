# AGENTS.md

ACWorldEdit is a Godot 4.7 (.NET / C#, `net8.0`) 3D world viewer and editor. It renders WoW client data (3.3.5 MPQ or retail CASC) and overlays world-database content: AzerothCore for the 3.3.5 client, TrinityCore master (`world` plus `hotfixes`) for retail. Edits stage in local drafts, sync to the `acworld` change log, and reach the world only when applied from Change History; checked rows also export as SQL. World reads are read-only, and applying needs write access.

Nested instructions: `Foundation/AGENTS.md`, `Scripts/AGENTS.md` and `Tests/AGENTS.md`. Read the one covering a folder before editing in it; this file stays the repo-wide set.

## Docs

Architecture lives in `docs/`, not here. Start at `docs/README.md`, or read the relevant page before working in an area:

- `docs/architecture/overview.md`: overview, layers and dependency rules, threading, repository layout, glossary.
- `docs/architecture/client-data.md`: `Scripts/Assets` (MPQ/CASC, formats, terrain, models, retail, listfile, navigation readers).
- `docs/architecture/viewer-runtime.md`: `Scripts/Viewer`, `Rendering`, `Overlays`, `Ui`, `Diagnostics`, smoke flags.
- `docs/architecture/rendering.md`: the retail shader port, material routing, per-material fidelity, inferred inputs.
- `docs/architecture/editing-and-export.md`: `Scripts/Data` and `Foundation` (drafts, spawn authoring, SQL export).
- `docs/architecture/testing-and-tooling.md`: test projects, test commands and settings, environment variables, tooling, CI, commit message rules (read before committing).
- `docs/reference/database-differences.md`: AzerothCore vs TrinityCore schema differences. Official schema docs:
  - AzerothCore world database: https://www.azerothcore.org/wiki/database-world
  - TrinityCore world database (master): https://trinitycore.info/database/master/world/home
- `docs/reference/shader-inspection-locations.md`: a map/tile/XYZ for inspecting each implemented liquid shader.
- `docs/status/completed-work.md`: what is built, per phase and area, with the tests and smokes that prove it.
- `docs/status/performance.md` and `docs/status/performance-history.md`: the `--perf-smoke`/`--perf-breakdown` benchmarks, the current frame budget, open work and older runs.
- `docs/status/needed-tests.md`: tests deferred until their feature works.
- `docs/plans/editor-plan.md`: all pending work (phase refinements, the design of editor phases 10 onward, cross-cutting gaps).
- `docs/plans/sniff-import-proposal.md`: research only; do not implement it unless asked.

## Commands

Shell is PowerShell on Windows and bash on Linux x64; both are supported (`native/win-x64`, `native/linux-x64`). SDK is pinned in `global.json` and the projects target `net8.0`; Godot is found via `--godot-path`, `GODOT_BIN` or `godot` on PATH.

```bash
git submodule update --init               # or clone with --recurse-submodules
python Tools/setup.py                     # sync submodules, build CascLib into native/<platform> (win-x64 or linux-x64); --with-tests adds StormLib
python Tools/setup.py --check             # report what is present, change nothing
dotnet build ACWorldEdit.csproj -p:NuGetAudit=false       # ~10 s; ACW001/ACW002 mean native/ or Data/WoWDBDefs is missing
python launch_viewer.py                   # build, then launch the viewer
python launch_viewer.py --smoke           # build, then run Godot headless with --terrain-smoke
dotnet test Tests/ACWorldEdit.Tests -p:NuGetAudit=false   # default suite, ~7 s, ~1440 tests
```

The test project needs a .NET 8 runtime: on a host that has only a newer runtime, prefix `DOTNET_ROLL_FORWARD=LatestMajor` or the run aborts with "You must install .NET 8". A command-line `--filter` is ANDed with `default.runsettings`, so `--filter "Category=Corpus"` matches nothing; pair it with `--settings corpus.runsettings` (or `all.runsettings`).

Corpus/KnownBug test settings, environment variables (`ACWORLD_*`) and smoke flags are in `testing-and-tooling.md` and `viewer-runtime.md`.

## Notes

- `ACWorldEdit.sln` exists for Godot's .NET export (without it the export produces no game assembly and only a warning); name `ACWorldEdit.csproj` explicitly in `dotnet` commands, as the scripts and CI do, so a build is not routed through the solution.
- Third-party code lives in pinned submodules (`ThirdParty/CascLib`, `ThirdParty/StormLib`, `Data/WoWDBDefs`); change a pin by checking out the new commit inside the submodule and `git add`-ing it, and keep `THIRD-PARTY.md` current. `Tools/package.py` makes a release build. CI and releases are in `.github/` (workflows and shared composite actions); a release is a pushed `vX.Y.Z` tag on `master`, which creates a draft GitHub Release; the steps are under "Continuous integration" in `docs/architecture/testing-and-tooling.md`.
- `artifacts/`, `native/`, `build/`, `.build-env/`, `wow-data/` and `bin/obj` are git-ignored; `artifacts/` holds throwaway probes and project copies, so ignore it when searching.
- Godot generates a `.cs.uid` beside every script; they show up untracked after new files are added. Never hand-edit a `.cs.uid` or anything under `.godot/`.
- The launcher redirects `APPDATA`/`LOCALAPPDATA` into `.build-env/`; use `-p:NuGetAudit=false` for dotnet commands.
- `--client-shot*` runs write PNGs into `artifacts/`. Read them back (the Read tool displays images) to inspect a rendering change; do not guess from logs alone. Prefer the same map, tile, hour and eye for before/after captures so they compare directly.
- Aim the shot camera deliberately: `--client-shot-sky` takes `HOUR[,YAW[,LIFT[,PITCH]]]`, and a pitch near 0 or one that grew from repeated edits can end up pointing at the sky. For liquid or ground inspections use a downward pitch (e.g. `-8` for a level view, `-45` to `-89` for terrain), pass an eye above the surface only as far as needed, and check the saved PNG actually shows the surface before drawing conclusions from it. Never compare two shots taken with different pitch, yaw or eye.
- Under Claude Code the `.claude/hooks` run `dotnet format whitespace` on each edited `.cs` and a build when a turn ends with uncommitted `.cs` changes. Agents without those hooks must do both themselves (`.editorconfig`: 4 spaces, LF, file-scoped namespaces).
- Do not run `Tools/refactor_gate.py` (or its structural/golden checks) unless the user explicitly asks; build and the relevant test project are enough for a normal change. The only time it is permissible to run the gate without the user asking is when running the `refactor-module` skill.

## Rules

- This file holds only commands, workflow notes and rules. Architecture goes in `docs/architecture/overview.md` or the matching `docs/architecture/*.md` page.
- Update the docs in the same change as the code: anything added, renamed, removed or replaced updates every doc page that describes it (including `docs/status/completed-work.md` and `docs/plans/editor-plan.md` when work lands or a gap is found, `docs/status/needed-tests.md` when a test is deferred). Fix wrong doc statements you come across; search the docs for names you touched before committing.
- `Foundation/`, `Scripts/Assets/` and `Scripts/Data/` must stay Godot-free: the xUnit project compiles them without a Godot reference.
- For world-database tables or columns, check the official schema docs, not only the live database or memory. Record AC/TC differences that affect the editors in `docs/reference/database-differences.md`.
  - AzerothCore: https://www.azerothcore.org/wiki/database-world (live database `acore_world`)
  - TrinityCore master: https://trinitycore.info/database/master/world/home (live database `world`)
- For client data (DBC/DB2 layouts, model attachment and animation IDs, FileDataIDs), check wowdev.wiki (`DB/<Table>` and `M2` pages) and wago.tools (the DB2 browser); the WoWDBDefs `manifest.json` maps each DB2 table to its FileDataID. Verify numeric IDs against the installed client (a temporary dump test) rather than trusting memory.
- When the MySQL MCP server is available, use it to research DB structure and real rows instead of guessing: query `acore_world` for AzerothCore and `world` for TrinityCore (plus the client DB2s for retail-only data). The MCP connection is read-only; do not attempt writes. Check `information_schema.COLUMNS` / `SHOW CREATE TABLE` for column names and defaults, and use the official schema docs above to confirm meaning.
- Use the context7 MCP for library/API documentation lookups (Godot's C# API in particular) instead of relying on memory: resolve the library first (`resolve-library-id`), then query one concept per call (`query-docs`). Do not guess method names or signatures; confirm them in the docs.
- `azerothcore-wotlk` and `wow-data` are read-only reference and must never be modified.
- Dialect-specific classes use the `Ac` / `Tc` prefix (file names too) and live in the layer's `AzerothCore/` / `TrinityCore/` folders (`Scripts/Ui/AzerothCore` beside `Scripts/Data/AzerothCore`, not one copy per feature). Shared interfaces, descriptors, context, renderers and the one factory or registry that names both dialects stay unprefixed. When working in a feature, rename and move legacy dialect-specific classes to match, moving `.cs.uid` companions with them.
- Write tests alongside each feature; test-first is fine. Skip trivial getters/setters. New UI requires automated interaction tests covering its flows (gdUnit4 window tests); new database-backed behavior requires live interaction tests proving a real connection works (`DbFact`-gated, one new file per feature, never appended to unrelated suites). Unit tests stay fast and deterministic; live-database and corpus tests are opt-in via environment variables, but those suites must actually run green against their dependency before the work is marked done.
- Inspect visual changes in Godot before calling them done.
- UI and status text says what happened ("Saved.", "Moved graveyard."). Don't say changes are a "local draft" or that the "database/source is unchanged"; mention drafts or the database only when it matters to a decision or an error.
- Status and feedback messages go to the status bar (`StatusBar`, fed by `StatusLog`; overlays use their `Report` callback), never into the Inspector panel.
- Commits: `type(scope): summary`, imperative, at most 72 characters, one logical change each. No AI attribution: never add a `Co-Authored-By:` trailer naming Claude or any AI model, and remove one when amending. Full rules in `testing-and-tooling.md`.
