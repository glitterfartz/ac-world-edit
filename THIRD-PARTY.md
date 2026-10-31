# Third-party components

ACWorldEdit itself is MIT licensed (see `LICENSE`). It uses the components below. Each keeps its own licence; the full texts are in the submodule folders, in the NuGet packages, and next to the shipped files in a build.

## Pinned git submodules

`python Tools/setup.py` syncs these and builds the native libraries. The pinned commit of each is recorded in this repository (`git submodule status`).

| Component | Path | Licence | Used for |
|---|---|---|---|
| [CascLib](https://github.com/ladislav-zezula/CascLib) | `ThirdParty/CascLib` | MIT | Native CASC reader, built to `native/win-x64/CascLib.dll`; shipped with builds. Retail profile only |
| [StormLib](https://github.com/ladislav-zezula/StormLib) | `ThirdParty/StormLib` | MIT | MPQ reference implementation for the differential test, built to `native/win-x64/StormLib.dll`. Tests only; never shipped |
| [WoWDBDefs](https://github.com/wowdev/WoWDBDefs) | `Data/WoWDBDefs` | Data (`definitions/`): CC BY-SA 4.0. Code: BSD-3-Clause | Retail table layouts; the `.dbd` files are copied into builds as `definitions/` together with the upstream `LICENSE.md` |

## NuGet packages

These are not submodules. They are pinned by version in `ACWorldEdit.csproj` and restored by `dotnet`.

| Package | Licence |
|---|---|
| SharpZipLib | MIT |
| MySqlConnector | MIT |
| DBCD | MIT |
| Godot.NET.Sdk / Godot Engine | MIT |

## Reference data

`Tools/generate_condition_catalog.py` and `Tools/generate_smart_catalog.py` generate C# catalogs from the AzerothCore and TrinityCore headers. Those projects are GPL-2.0 licensed; their sources are not included in this repository or in builds, but the generated catalogs (`Foundation/AzerothCore/Ac*Types.cs`, `Foundation/TrinityCore/Tc*Types.cs`) are. Their licensing status has not been reviewed.
