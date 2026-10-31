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

| Requirement | Needed for | Notes |
| --- | --- | --- |
| Windows x64 | everything | The native build and `native/win-x64` layout are Windows only for now |
| Git | cloning, submodules | Clone with `--recurse-submodules`, or run the setup script |
| .NET SDK matching [`global.json`](global.json) (10.0.400, roll-forward to the latest feature band) | build, tests | Projects target `net8.0` |
| Godot 4.7.1 **.NET (mono) edition** | running, packaging | Found via `--godot-path`, the `GODOT_BIN` environment variable, or `godot` on PATH |
| Python 3 | setup and launch scripts | Standard library only |
| CMake and Visual Studio Build Tools ("Desktop development with C++") | `Tools/setup.py` | Builds CascLib (Retail profile) and StormLib (tests) |
| A WoW client: 3.3.5 (MPQ) or retail (CASC) | viewing | Never modified |
| An AzerothCore (3.3.5) or TrinityCore master (retail) `world` database, plus optional `hotfixes` | database overlays and editing | Read-only connection; a SELECT-only account is enough |

Third-party code comes from pinned git submodules: `ThirdParty/CascLib` and `ThirdParty/StormLib` (both MIT) and `Data/WoWDBDefs` (retail table layouts, CC BY-SA 4.0). NuGet packages (SharpZipLib, MySqlConnector, DBCD) are pinned by version in `ACWorldEdit.csproj` and restored by `dotnet`, so they are not submodules. See [THIRD-PARTY.md](THIRD-PARTY.md).

The Wrath profile needs none of the native libraries; only the Retail profile needs CascLib and the WoWDBDefs definitions.

## Setup

```powershell
git clone --recurse-submodules https://github.com/glitterfartz/ac-world-edit.git
cd ac-world-edit
python Tools/setup.py                     # sync submodules, build CascLib into native/win-x64/
python Tools/setup.py --with-tests        # also build StormLib, used only by the StormLib differential test
python Tools/setup.py --check             # report what is present and change nothing
```

`Tools/setup.py` syncs the pinned submodules, builds out of tree under `.build-env/cmake/` (the submodule folders stay clean), copies the DLLs and their licences into `native/win-x64/`, and skips a rebuild when the pinned commit and options are unchanged (`--force` rebuilds). It prints what is still missing, with the fix. Set `ACWORLD_CMAKE_GENERATOR` if CMake cannot pick a Visual Studio generator. When the checkout path is long (over about 80 characters for `.build-env\cmake`), the build moves to a short folder under the temp directory, because MSVC fails past Windows' 260-character path limit; `ACWORLD_CMAKE_BUILD_DIR` chooses the folder.

## Build and run

```powershell
python launch_viewer.py                   # dotnet build ACWorldEdit.csproj, then launch the viewer
python launch_viewer.py --smoke           # build, then run Godot headless with --terrain-smoke
python launch_viewer.py --check-assets    # run the corpus test set (needs real client data)
```

The launcher redirects `APPDATA`/`LOCALAPPDATA` into `.build-env/` so CLI caches stay in the project. Pass `-p:NuGetAudit=false` to direct `dotnet` commands. A build warns (`ACW001`, `ACW002`) when `CascLib.dll` or the WoWDBDefs definitions are missing, and the viewer says the same in its status bar when the Retail profile is selected.

Other smoke workflows (`--visual-smoke`, `--water-smoke`, `--reload-smoke`, `--creature-smoke`, `--client-smoke`, ...) are opt-in flags passed to Godot after `--`.

## First run

1. Start the viewer (`python launch_viewer.py`).
2. **File → Load client…**: pick **Wrath** (a 3.3.5 folder containing `common.MPQ`) or **Retail** (the install folder containing `.build.info`). The folder is remembered per profile. A retail client offers to download the community listfile once; **File → Update listfile…** repeats it later.
3. **File → Database connection…**: enter host, port, user, password and the world database (for example `acore_world`; for retail also an optional hotfixes database). The connection is tested before it is saved. Skip this step to browse the client data without database overlays.
4. Pick a map and fly around. Edits are staged as drafts; **File → Pending changes…** lists them and exports reviewable SQL. Settings, drafts and exports live in Godot's user folder: `%APPDATA%\Godot\app_userdata\ACWorldEdit` when you start the project from the Godot editor or an exported build, and `.build-env\roaming\Godot\app_userdata\ACWorldEdit` when you start it with `launch_viewer.py`, which redirects `APPDATA`.

## Package a build

```powershell
python Tools/package.py                   # build/win-x64/ACWorldEdit.exe plus its data folder
python Tools/package.py --pack-only       # just ACWorldEdit.pck; needs no export templates
```

This uses the `Windows Desktop` preset in `export_presets.cfg`. It needs the Godot 4.7.1 .NET export templates (Editor → Manage Export Templates), and `Tools/setup.py` must have run first. The csproj publishes `CascLib.dll`, the retail `definitions/` and the licences into the build's `data_ACWorldEdit_windows_x86_64` folder, where the loader looks for them, and the script fails if the export is missing them or the game assembly. Godot's .NET export needs `ACWorldEdit.sln` at the project root (it exports an exe with no game assembly, and only a warning, without it), so keep that file and name the project in `dotnet` commands (`dotnet build ACWorldEdit.csproj`); a bare `dotnet build` in the repo root is ambiguous. Output goes to the git-ignored `build/`. A CI job would run `python Tools/setup.py` and then `python Tools/package.py` on a Windows runner with Godot and its templates installed; no CI is configured yet. Do not run the script from a shell with `APPDATA` redirected: Godot would look for the export templates under the redirected folder.

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
| `ACWORLD_CASCLIB`        | Path to `CascLib.dll` (default: the one `python Tools/setup.py` builds into `native/win-x64/`)             |
| `ACWORLD_DBD_DIR`        | WoWDBDefs directory; defaults to `Data/WoWDBDefs/definitions` (the submodule)                              |
| `ACWORLD_CMAKE_GENERATOR` | CMake generator for `Tools/setup.py` (default: CMake's Visual Studio generator)                           |
| `ACWORLD_CMAKE_BUILD_DIR` | Folder for `Tools/setup.py`'s CMake builds (default `.build-env/cmake`, or a short temp folder for long checkout paths) |
| `ACWORLD_LISTFILE`       | Override for the community listfile location (default `user://community-listfile.csv`)                     |

## License

MIT, see [LICENSE](LICENSE). Third-party components keep their own licences: [THIRD-PARTY.md](THIRD-PARTY.md).
