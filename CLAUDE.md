# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

ACWorldEdit is a Godot 4.7 (.NET / C#, `net8.0`) 3D world viewer and editor for AzerothCore. It reads the WoW client's data (3.3.5 MPQ archives, or the retail CASC client) to render terrain, models and liquids, and overlays AzerothCore world-database content (creature/gameobject spawns, graveyards, area triggers, NPC interactions, quests, SmartAI). Edits are staged as drafts and exported as SQL; the database connection is read-only.

## Commands

Shell is PowerShell on Windows. SDK is pinned in `global.json`; Godot is located via the `Godot 4.7.lnk` shortcut, `X:\Godot-4.7\...`, or `godot` on PATH.

```powershell
./Launch-Viewer.ps1                 # dotnet build ACWorldEdit.csproj, then launch the viewer
./Launch-Viewer.ps1 -Smoke          # build, then run Godot headless with --terrain-smoke
./Launch-Viewer.ps1 -CheckAssets    # run the corpus test set (needs real client data)

dotnet test Tests/ACWorldEdit.Tests -p:NuGetAudit=false                          # default fast suite
dotnet test Tests/ACWorldEdit.Tests --filter "FullyQualifiedName~SplitTerrainTests"   # one class
dotnet test Tests/ACWorldEdit.Tests --settings Tests/ACWorldEdit.Tests/corpus.runsettings     # Category=Corpus only
dotnet test Tests/ACWorldEdit.Tests --settings Tests/ACWorldEdit.Tests/knownbugs.runsettings  # Category=KnownBug only
dotnet test Tests/ACWorldEdit.Tests --settings Tests/ACWorldEdit.Tests/all.runsettings        # everything
```

`default.runsettings` excludes `Corpus` and `KnownBug` categories; a command-line `--filter` overrides it. `Tests/ACWorldEdit.GdTests` is a separate gdUnit4 project that needs `GODOT_BIN` set in the environment.

Smoke workflows are opt-in CLI flags passed after `--` to Godot (`--terrain-smoke`, `--visual-smoke`, `--water-smoke`, `--reload-smoke`, `--creature-smoke`, `--client-smoke`, etc.); they are parsed in `Scripts/Diagnostics/TerrainViewer.Smoke.cs` and never run in normal startup.

### Environment variables used by tests/tools

- `ACWORLD_DB_INFO` = `host;port;user;password;database` — required by `Tests/.../Db/*` tests.
- `ACWORLD_DATA_DIR` (3.3.5 client data), `ACWORLD_RETAIL_DIR` (retail client), `ACWORLD_CLIENT_PROFILE` — client data for `ClientData`/`Corpus` tests.
- `ACWORLD_CASCLIB` (path to `CascLib.dll`; fetch with `Tools/Get-CascLib.ps1` into `native/win-x64/`), `ACWORLD_DBD_DIR` (WoWDBDefs; defaults to `Data/definitions`).

## Architecture

Three assemblies, split deliberately so logic is testable without Godot:

- **`Foundation/`** (`ACWorldEdit.Foundation`, plain `net8.0`, no Godot): the draft/edit model — `CreatureDraft`, `GraveyardDraft`, `InteractionDraft`, `ContentDrafts`, `PendingChanges`, `DraftHistory` (undo/redo), `SqlExport`, `SmartCatalog`/`SmartBehavior`, `WorldCoordinates`. Referenced by both the main project and the tests.
- **Main project** (`ACWorldEdit.csproj`, `Godot.NET.Sdk`): everything under `Scripts/`. It explicitly `Compile Remove`s `Tests/`, `Foundation/`, and `artifacts/` so those aren't built into the Godot assembly. `Scripts/` areas:
  - `Assets/` — Godot-free parsers: `Archives` (MPQ, `IClientFiles`/`AssetRef` abstraction, `ClientFolder`), `Formats` (BLP, DBC, chunked files), `Models` (M2/WMO), `World` (WDT/ADT terrain, heights, liquids, ground probe, area/light lookup), `Retail` (CASC file source, DB2 tables via DBCD, split-ADT retail terrain), `Client` (creature/gameobject/interaction catalogs).
  - `Data/` — read-only MySQL access (`WorldDatabase`, `*Source` classes, `DatabaseSettings`, `SettingsResolver`).
  - `Rendering/`, `Viewer/` (`TerrainViewer` is a large partial class split across `TerrainViewer.*.cs`), `Overlays/` (spawn/graveyard/trigger overlays + `TransformGizmo`, each with `.Ui`/`.Editing` partials), `Ui/` (editor windows, client picker, settings), `Diagnostics/` (smoke workflows as more `TerrainViewer` partials).
- **Tests** (`Tests/ACWorldEdit.Tests`, xUnit): does *not* reference the Godot project; it compiles `Scripts/Assets/**` and `Scripts/Data/*.cs` directly via linked `Compile Include`, plus the Foundation project reference. So code in those two folders must stay Godot-free. Test categories/traits: unit (default), `Corpus` (needs client data), `Db` (needs `ACWORLD_DB_INFO`), `KnownBug`. `Tests/PARITY.md` maps the older `Tests/AssetChecks` console checks to the xUnit tests.

Multi-client support: parsers consume `IClientFiles` (`ClientKind.Wrath335` MPQ vs `Retail` CASC) and never branch on client kind; the viewer picks one implementation per loaded client (`ClientServices`). Retail terrain is split ADT (root + texture files addressed by FileDataID via `AssetRef`).

Game and DB data is read-only reference: `azerothcore-wotlk` and `wow-data` paths must never be modified (enforced by `.claude/hooks/block_readonly.ps1`). A PostToolUse hook runs `dotnet format whitespace` on each edited `.cs` file.

## Notes

- `artifacts/`, `native/`, `.build-env/`, `wow-data/` and `bin/obj` are git-ignored scratch/build output; `artifacts/` holds throwaway probes and copies of the project — ignore it when searching.
- `.cs.uid` files are generated by Godot alongside every script; they show up as untracked after new files are added.
- The launcher redirects `APPDATA`/`LOCALAPPDATA` into `.build-env/` to keep CLI caches in the project; use `-p:NuGetAudit=false` for dotnet commands.
- Retail terrain (split ADT) does not yet support height texturing (MHID/MTXP) or MAMP scale; only the first 4 of up to 8 layers are drawn. Liquids and sky are milestone 5.
- Retail placements: MDDF/MODF entries flagged as FileDataIDs become `fdid:N` references (WMO ones `fdid:N.wmo`, so the existing `.wmo` extension checks still tell buildings from doodads). Retail M2s (MD21 chunk + SFID/TXID) are drawn in the reference pose (no `.anim` files); retail WMO liquids are skipped. Placements that name a model by path instead of FileDataID (some old-style WDTs) cannot be resolved without a listfile.
- `--client-shot` / `--client-shot-tile=X,Y` (windowed, not headless) saves `artifacts/client-shot.png` after the tile and its models load, framed on the largest nearby building with the UI hidden; use it to inspect rendering.

## Rules

- Feature implementations should be followed up by tests, but don't write tests for trivial getters/setters or Godot UI wiring. Tests should be fast and deterministic; slow corpus tests are opt-in via `--settings corpus.runsettings`.
- For large features, don't implement tests ahead of the feature; implement the feature first, then add tests. Tests should be written to verify the feature works as intended, not to drive the implementation.
- If a feature requires a visual change you must inspect in Godot to verify them.
