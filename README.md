# ACWorldEdit

A 3D world viewer and editor for [AzerothCore](https://www.azerothcore.org/) and [TrinityCore](https://www.trinitycore.org/), built with Godot 4.7 (.NET / C#, `net8.0`).

It reads the WoW client's data to render terrain, models and liquids. On top of that it overlays world-database content: creature and gameobject spawns, graveyards, area triggers, NPC interactions, vendor data, quests and SmartAI scripts.

A 3.3.5 client uses an AzerothCore world database. A retail (CASC) client uses a TrinityCore master `world` database plus optional `hotfixes`. TrinityCore support is in progress: spawns, graveyards, area triggers and read-only creature properties work; quest, SmartAI and waypoint editing are AzerothCore-only for now (see [docs/tc-status.md](docs/tc-status.md)).

The database connection is read-only, and the game files are never modified. Edits are staged as local drafts and leave the app only as SQL scripts for you to review and run.

## Architecture

See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for diagrams and an overview. Detail pages:

- [Client data and asset pipeline](docs/architecture/client-data.md)
- [Viewer runtime](docs/architecture/viewer-runtime.md)
- [Editing, data access and SQL export](docs/architecture/editing-and-export.md)
- [Testing and tooling](docs/architecture/testing-and-tooling.md)

## Prerequisites

- .NET SDK matching [`global.json`](global.json) (10.0.400, roll-forward to the latest feature band)
- Godot 4.7 .NET edition, found via `--godot-path`, the `GODOT_BIN` environment variable, or `godot` on PATH
- Python 3 (for `launch_viewer.py`)
- A WoW client (3.3.5 or retail) and, for database overlays, an AzerothCore (3.3.5) or TrinityCore master (retail) world database

## Build and run

```powershell
python launch_viewer.py                   # dotnet build ACWorldEdit.csproj, then launch the viewer
python launch_viewer.py --smoke           # build, then run Godot headless with --terrain-smoke
python launch_viewer.py --check-assets    # run the corpus test set (needs real client data)
```

The launcher redirects `APPDATA`/`LOCALAPPDATA` into `.build-env/` so CLI caches stay in the project. Pass `-p:NuGetAudit=false` to direct `dotnet` commands.

Other smoke workflows (`--visual-smoke`, `--water-smoke`, `--reload-smoke`, `--creature-smoke`, `--client-smoke`, ...) are opt-in flags passed to Godot after `--`.

## Test

```powershell
dotnet test Tests/ACWorldEdit.Tests -p:NuGetAudit=false                          # default fast suite
dotnet test Tests/ACWorldEdit.Tests --filter "FullyQualifiedName~SplitTerrainTests"   # one class
dotnet test Tests/ACWorldEdit.Tests --settings Tests/ACWorldEdit.Tests/corpus.runsettings     # Category=Corpus only
dotnet test Tests/ACWorldEdit.Tests --settings Tests/ACWorldEdit.Tests/knownbugs.runsettings  # Category=KnownBug only
dotnet test Tests/ACWorldEdit.Tests --settings Tests/ACWorldEdit.Tests/all.runsettings        # everything
```

The default settings exclude the `Corpus` (needs client data) and `KnownBug` categories, and a command-line `--filter` does not override that; combine it with `corpus.runsettings` or `all.runsettings`. `Db` tests need `ACWORLD_DB_INFO` (AzerothCore) or `ACWORLD_TC_DB_INFO` (TrinityCore) and skip without it. `Tests/ACWorldEdit.GdTests` is a separate gdUnit4 project that needs `GODOT_BIN` set.

## Environment variables

| Variable                 | Purpose                                                                                                    |
| ------------------------ | ---------------------------------------------------------------------------------------------------------- |
| `GODOT_BIN`              | Path to the Godot executable (alternative to `--godot-path`); also required by `Tests/ACWorldEdit.GdTests` |
| `ACWORLD_DB_INFO`        | `host;port;user;password;database`; AzerothCore connection, required by its `Db` tests                     |
| `ACWORLD_TC_DB_INFO`     | Same, plus optional `;hotfixes`; TrinityCore connection, required by its `Db` tests                        |
| `ACWORLD_DATA_DIR`       | 3.3.5 client data directory, for `ClientData`/`Corpus` tests                                               |
| `ACWORLD_RETAIL_DIR`     | Retail client directory, for `ClientData`/`Corpus` tests                                                   |
| `ACWORLD_CLIENT_PROFILE` | Client profile selection for `ClientData`/`Corpus` tests                                                   |
| `ACWORLD_CASCLIB`        | Path to `CascLib.dll`; fetch it with `python Tools/get_casclib.py` into `native/win-x64/`                  |
| `ACWORLD_DBD_DIR`        | WoWDBDefs directory; defaults to `Data/WoWDBDefs/definitions`                                                  |
| `ACWORLD_LISTFILE`       | Override for the community listfile location (default `user://community-listfile.csv`)                     |
