# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

ACWorldEdit is a Godot 4.7 (.NET / C#, `net8.0`) 3D world viewer and editor for AzerothCore. It reads the WoW client's data (3.3.5 MPQ archives, or the retail CASC client) to render terrain, models and liquids, and overlays AzerothCore world-database content (creature/gameobject spawns, graveyards, area triggers, NPC interactions, quests, SmartAI). Edits are staged as drafts and exported as SQL; the database connection is read-only.

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

`default.runsettings` excludes `Corpus` and `KnownBug` categories; a command-line `--filter` overrides it. `Tests/ACWorldEdit.GdTests` is a separate gdUnit4 project that needs `GODOT_BIN` set in the environment.

Smoke workflows are opt-in CLI flags passed after `--` to Godot (`--terrain-smoke`, `--visual-smoke`, `--water-smoke`, `--reload-smoke`, `--creature-smoke`, `--client-smoke`, etc.); they are parsed in `Scripts/Diagnostics/TerrainViewer.Smoke.cs` and never run in normal startup.

### Environment variables used by tests/tools

- `ACWORLD_DB_INFO` = `host;port;user;password;database` — required by `Tests/.../Db/*` tests.
- `ACWORLD_DATA_DIR` (3.3.5 client data), `ACWORLD_RETAIL_DIR` (retail client), `ACWORLD_CLIENT_PROFILE` — client data for `ClientData`/`Corpus` tests.
- `ACWORLD_CASCLIB` (path to `CascLib.dll`; fetch with `Tools/get_casclib.py` into `native/win-x64/`), `ACWORLD_DBD_DIR` (WoWDBDefs; defaults to the `Data/WoWDBDefs` submodule (`git submodule update --init`)).

## Architecture

Three assemblies, split deliberately so logic is testable without Godot:

- **`Foundation/`** (`ACWorldEdit.Foundation`, plain `net8.0`, no Godot): the draft/edit model — `CreatureDraft`, `GraveyardDraft`, `InteractionDraft`, `ContentDrafts`, `PendingChanges`, `DraftHistory` (undo/redo), `SqlExport`, `SmartCatalog`/`SmartBehavior`, `WorldCoordinates`. Referenced by both the main project and the tests.
- **Main project** (`ACWorldEdit.csproj`, `Godot.NET.Sdk`): everything under `Scripts/`. It explicitly `Compile Remove`s `Tests/`, `Foundation/`, and `artifacts/` so those aren't built into the Godot assembly. `Scripts/` areas:
  - `Assets/` — Godot-free parsers: `Archives` (MPQ, `IClientFiles`/`AssetRef` abstraction, `ClientFolder`), `Formats` (BLP, DBC, chunked files), `Models` (M2/WMO), `World` (WDT/ADT terrain, heights, liquids, ground probe, area/light lookup), `Retail` (CASC file source, DB2 tables via DBCD, split-ADT retail terrain), `Client` (creature/gameobject/interaction catalogs).
  - `Data/` — read-only MySQL access (`WorldDatabase`, `DatabaseSettings`, `SettingsResolver`, `WorldConnection`). Reads go through `IDBSource` (opened with `DBSource.Open`, which picks the dialect and takes the client's `IClientLookup`): AzerothCore lives in `Data/AzerothCore/` (`Ac*Source`), TrinityCore in `Data/TrinityCore/` (`Tc*Source`; spawns, graveyards, teleports and area triggers so far, the editors' reads are not built yet).
  - `Rendering/`, `Viewer/` (`TerrainViewer` is a large partial class split across `TerrainViewer.*.cs`), `Overlays/` (spawn/graveyard/trigger overlays + `TransformGizmo`, each with `.Ui`/`.Editing` partials), `Ui/` (editor windows, client picker, settings), `Diagnostics/` (smoke workflows as more `TerrainViewer` partials).
- **Tests** (`Tests/ACWorldEdit.Tests`, xUnit): does *not* reference the Godot project; it compiles `Scripts/Assets/**` and `Scripts/Data/**/*.cs` directly via linked `Compile Include`, plus the Foundation project reference. So code in those two folders must stay Godot-free. Test categories/traits: unit (default), `Corpus` (needs client data), `Db` (needs `ACWORLD_DB_INFO`), `KnownBug`.

Multi-client support: parsers consume `IClientFiles` (`ClientKind.Wrath335` MPQ vs `Retail` CASC) and never branch on client kind; the viewer picks one implementation per loaded client (`ClientServices`). Retail terrain is split ADT (root + texture files addressed by FileDataID via `AssetRef`).

Game and DB data is read-only reference: `azerothcore-wotlk` and `wow-data` paths must never be modified (enforced by `.claude/hooks/block_readonly.py`). A PostToolUse hook runs `dotnet format whitespace` on each edited `.cs` file.

## Notes

- Detailed architecture docs (with diagrams): `docs/ARCHITECTURE.md`.
- AzerothCore vs TrinityCore world schema differences: `docs/DB-DIFFERENCES.md`.
- `artifacts/`, `native/`, `.build-env/`, `wow-data/` and `bin/obj` are git-ignored scratch/build output; `artifacts/` holds throwaway probes and copies of the project — ignore it when searching.
- `.cs.uid` files are generated by Godot alongside every script; they show up as untracked after new files are added.
- The launcher redirects `APPDATA`/`LOCALAPPDATA` into `.build-env/` to keep CLI caches in the project; use `-p:NuGetAudit=false` for dotnet commands.
- Retail terrain (split ADT) does not yet support height texturing (MHID/MTXP) or MAMP scale; only the first 4 of up to 8 layers are drawn.
- Retail creature displays use `CreatureDisplayInfo` model data and skins only (`RetailCreatureCatalog`). Displays with a `CreatureDisplayInfoExtra` (humanoids, roughly 30% of the NPCs around Goldshire) have no skin composed, so they draw with the pink unresolved-texture fallback.
- Retail liquids/sky (milestone 5): MH2O is read from the root ADT; its second `uint16` is a vertex format (<42, format 3 = heights+UV+depth) or a LiquidObject ID (ocean stores 42, which has no row and means depth-only). `RetailLiquids` (LiquidType/LiquidObject/LiquidMaterial) is registered per client in `ClientServices.OpenRetail`. Liquid textures are named by path in LiquidType, so the downloaded community listfile (see below) is installed as `CascFiles.PathResolver`; types whose texture is not listed (or when no listfile is downloaded) fall back to the base liquid of their kind. Retail lighting comes from Light/LightParams/LightData/LightSkybox DB2 (`ClientLighting.TryLoadRetail`, only the default weather slot) and skyboxes by FileDataID. `--client-shot-water` frames the largest liquid mesh.
- Retail placements: MDDF/MODF entries flagged as FileDataIDs become `fdid:N` references (WMO ones `fdid:N.wmo`, so the existing `.wmo` extension checks still tell buildings from doodads). Retail M2s (MD21 chunk + SFID/TXID) are drawn in the reference pose (no `.anim` files); retail WMO liquids load through `RetailLiquids`. Placements that name a model by path instead of FileDataID (some old-style WDTs) resolve only through the listfile (e.g. the "Garrison - …" interiors, whose global WMO is named by path).
- Community listfile: `ListfileStore` downloads wowdev/wow-listfile `community-listfile.csv` to `user://community-listfile.csv` (override `ACWORLD_LISTFILE`), only on user request: a one-time prompt on the first retail load, then File → Update listfile…. `PathListfile` loads it keyed by a 64-bit path hash (millions of entries); `ClientServices.OpenRetail` installs it as `CascFiles.PathResolver`. Nothing is bundled; corpus tests that need it (`RetailListfileFact`) skip unless it is present.
- `--client-shot` / `--client-shot-tile=X,Y` (windowed, not headless) saves `artifacts/client-shot.png` after the tile and its models load, framed on the largest nearby building with the UI hidden; use it to inspect rendering.

## Rules

- `NEEDED-TESTS.md` lists tests deferred while implementing retail loading; add them once the feature works and remove the entry when its test exists.
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
