# Source references

Research date: 2026-10-11. Local AzerothCore revision: `5ddc706b7fdcbda01164862652a9b61d60e9a8e0`. Local paths below are relative to the named repository; they refer to inspected files, not hypothetical components. External URLs are mutable unless a revision is embedded. No third-party library was integrated or benchmarked.

## V1 Viewer configuration
Root: `X:\AzerothCore\ACWorldEdit\ac-world-edit`.
- `project.godot`: main scene, feature version 4.7, C#, GL Compatibility, Jolt configuration.
- `ACWorldEdit.csproj`: Godot.NET.Sdk 4.7.1, net8.0, SharpZipLib 1.4.2, test exclusion.
- `global.json`: SDK 8.0.425 with latestPatch roll-forward.
- `Main.tscn`: one Node3D with TerrainViewer script.
- `Launch-Viewer.ps1`: local build environment creation, builds, smoke launch. Not executed during research because it writes in the project.
- `README.md`: stated functionality, independently compared to code.

## V2 Archive and texture code
- `Scripts/Assets/AssetStore.cs`: constructor recursive archive discovery; Rank, Read, Exists, Source, Maps, Dispose.
- `Scripts/Assets/MpqArchive.cs`: constructor, Find, Read, Unpack, Inflate, Hash. Read-only FileStream; v0/v1; incomplete compression/patch feature coverage.
- `Scripts/Assets/BlpTexture.cs`: Decode and Color565. BLP2 only; selected mip, palette/raw/DXT decoding.
- `Scripts/Assets/TerrainData.cs`: Chunks.Read, WorldMap, TerrainTile constructor, DecodeAlpha, TerrainChunk.

## V3 Rendering and streaming
- `Scripts/TerrainRenderer.cs`: BeginTile, AddChunk, Release, SetGrid. Tile size/origin, mesh topology, normal transformation, reference-counted texture dictionary.
- `Scripts/TerrainViewer.cs`: BuildUi, LoadArchives, OpenMap, ResetTerrain, Prepare, CameraTile, UpdateWanted, _Process, RaiseAboveTerrain, Smoke, _ExitTree.
- `Scripts/TerrainMinimap.cs`: _Draw and _GuiInput.
- `Shaders/terrain.gdshader`: four layers, UV times eight, sequential/weighted blend, tint and shadow.
- `Tests/Program.cs`, `Tests/AssetChecks.csproj`: four-continent samples, finite heights, texture output sizes, synthetic alpha tests.
- Existing `artifacts/graphical.log`, `artifacts/runtime.log`: historical smoke evidence, not a new runtime benchmark.
- [Research asset test](asset-check-run.txt), [corrected junction test](corrected-workspace-asset-check-run.txt), [inspection hashes](inspection-hashes.json). Prebuilt test binary executed without rebuilding; source/binary identity is not guaranteed.

## A1 Server loading and entities
Root: `X:\AzerothCore\azerothcore-wotlk`.
- `src/server/game/Globals/ObjectMgr.cpp`: LoadCreatureTemplates (related loaders), LoadCreatureTemplateModels at line 712, LoadCreatures at 2320, LoadGameobjects at 2920, LoadQuests at 5081, LoadTrainers at 9954, LoadVendors at 10159, LoadGossipMenu at 10210, LoadGossipMenuItems at 10247. These are anchors at inspected revision, not promises about future line numbers.
- `src/server/game/Entities/Creature/Creature.cpp`: SaveToDB overloads at 1397/1412; runtime entity persistence differs from a general editor API.
- `src/server/game/Entities/GameObject/GameObjectData.h`: GameObjectTemplate type-dependent data union, including door, chest, questgiver, goober.
- `src/server/game/Quests/QuestDef.cpp`: Quest constructor, LoadQuestTemplateAddon, RequiredNpcOrGo and query serialization. Negative GO objective encoding is confirmed.
- `src/server/database/Database/Implementation/WorldDatabase.cpp`: prepared statements, including WORLD_SEL_SMART_SCRIPTS.
- `src/server/database/Database/Implementation/CharacterDatabase.cpp`, `LoginDatabase.cpp`: separate persistence areas.
- Schema definitions: `data/sql/base/db_world/<table>.sql`; historical baselines, not the sole description of the running schema.

## A2 Behavior and relationships
- `src/server/game/Movement/Waypoints/WaypointMgr.cpp`: WaypointMgr::Load, LoadWaypointAddons.
- `src/server/game/AI/SmartScripts/SmartScriptMgr.cpp`: SmartAIMgr::LoadSmartAIFromDB, IsTargetValid, IsEventValid; signed owner IDs and source restrictions.
- `src/server/game/AI/SmartScripts/SmartScriptMgr.h`: SMART_EVENT, SMART_ACTION, SMARTAI_TARGETS and holder layouts.
- `src/server/game/AI/SmartScripts/SmartScript.cpp`, `SmartAI.cpp`: execution, runtime AI state.
- `src/server/game/Conditions/ConditionMgr.cpp`: LoadConditions, IsObjectMeetToConditionList; AND inside ElseGroup, OR across groups, signed references.
- `src/server/game/Loot/LootMgr.cpp`: LootStore::LoadLootTable, CheckLootRefs and reference/group processing.
- `src/server/game/Events/GameEventMgr.cpp`: LoadFromDB, event-associated spawns including inverse event behavior.
- `src/server/scripts/Commands/cs_reload.cpp`: HandleReloadSmartScripts, HandleReloadCreatureTemplateCommand and individual reload entries. Reload is subsystem-specific.

## A3 DBC and spatial data
- `src/server/game/DataStores/DBCStores.cpp`: LoadDBC, LoadDBCStores, LOAD_DBC macro. Tables are explicitly paired to DBCs, including Spell.dbc -> spell_dbc.
- `src/server/shared/DataStores/DBCDatabaseLoader.cpp`: Load selects ordered SQL records and overrides/appends DBC entries.
- `src/server/shared/DataStores/DBCStructure.h`, `DBCfmt.h`: server-consumed structures/formats; not a universal client writer schema.
- `src/server/game/Grids/GridDefines.h`: Acore::ComputeGridCoord, ComputeGridCoordSimple, NormalizeMapCoord.
- `src/common/Collision/Maps/MapDefines.h`: MAX_NUMBER_OF_GRIDS=64, SIZE_OF_GRIDS=533.3333.
- `src/tools/map_extractor/adt.h`, `System.cpp`: map extraction and height data.
- `src/tools/vmap4_extractor/adtfile.h`: packed ADT::MDDF and ADT::MODF.
- `src/tools/vmap4_extractor/adtfile.cpp`: ADTFile::init reads MMDX/MWMO/MDDF/MODF.
- `src/tools/vmap4_extractor/wdtfile.cpp`: WDTFile::init reads global WMO.
- `src/tools/vmap4_extractor/wmo.h`: fixCoords; `model.cpp`, `wmo.cpp`: extraction transforms. Collision extractors are not complete visual renderers.

## A4 Updates and extension
- `src/server/database/Updater/DBUpdater.cpp`: UpdateFetcher construction, ApplyFile, update-table checks.
- `src/server/database/Updater/UpdateFetcher.cpp`: ReceiveIncludedDirectories, GetFileList, applied-file hashes/state and source-directory path expansion.
- `data/sql/updates/pending_db_world/create_sql.sh`: target convention for upstream SQL.
- `.agents/docs/sql-guidelines.md`: delete-before-insert, id+guid spawn predicates, complete SmartAI blocks, model probability conventions.
- `modules/how_to_make_a_module.md`, `modules/CMakeLists.txt`, `ModulesLoader.cpp.in.cmake`: module workflow and generated registration.
- `src/server/apps/worldserver/worldserver.conf.dist`: RA disabled, SOAP disabled, loopback SOAP default with port 7878.
- `AGENTS.md` and `LICENSE`: project instructions and GNU GPL context; source headers commonly specify GPL v2 or later. Check file-level notices before copying.

## D1 Read-only test database evidence
Additional inspected behavior sources:
- `src/server/game/Entities/Player/PlayerQuest.cpp`: Player::SatisfyQuestPreviousQuest, SatisfyQuestExclusiveGroup, SatisfyQuestBreadcrumb; actual signed prerequisite/group eligibility, including repeatable/seasonal qualifications.
- `src/server/game/Spells/SpellMgr.cpp`: LoadSpellProcs, LoadSpellRequired, LoadSpellGroups, LoadSpellTargetPositions; SQL reference and proc validation.
- `src/server/game/Achievements/AchievementMgr.cpp`: AchievementCriteriaData::IsValid; criterion-type, creature/spell/area and script checks.
- `src/tools/mmaps_generator/MapBuilder.cpp`, `mmaps-config.yaml`: navigation-generation implementation/configuration, inspected by inventory only; full build behavior was not tested.

MCP server was explicitly presented as read-only, used only for SELECT. Observed databases: auth 22 tables, characters 108, world 314. World version: core_revision `5ddc706b7fdc`, db_version `ACDB 335.17-dev`, cache_id 17. This is a test copy, not proof of live-server settings.
- [Table engines and approximate counts](database-query-0.txt).
- [Detailed core entity/behavior columns](database-query-1.txt).
- [Quest/relationship/loot/update columns](database-query-2.txt).
- [Declared foreign keys](database-query-3.txt).
- [Creature orphan audit](database-extra-0.txt).
- [Creature indexes](database-extra-1.txt).
- [Configured update paths](database-extra-2.txt).
- [Empty SmartAI sample lookup](database-extra-3.txt).
Exact SQL and limitations are in [research notes](research-notes.md).

## E1 Official server documentation
- [World database index](https://www.azerothcore.org/wiki/database-world): documentation navigation; use individual table docs plus pinned source to settle behavior.
- [AzerothCore repository](https://github.com/azerothcore/azerothcore-wotlk).
- [TrinityCore repository](https://github.com/TrinityCore/TrinityCore): distinct 3.3.5 branch exists; schema interchangeability not established.

## E2 Godot
- [Editor plugins](https://docs.godotengine.org/en/stable/tutorials/plugins/editor/index.html).
- [EditorPlugin API](https://docs.godotengine.org/en/stable/classes/class_editorplugin.html): docks, gizmo/inspector/import plugins and undo manager integration.
- [Thread-safe API guidance, 4.6](https://docs.godotengine.org/en/4.6/tutorials/performance/thread_safe_apis.html): active scene-tree restrictions. Verify exact API names against installed 4.7.1 before implementation.

## E3 Asset tooling
- [StormLib](https://github.com/ladislav-zezula/StormLib), [MIT license](https://raw.githubusercontent.com/ladislav-zezula/StormLib/master/LICENSE), [archive creation implementation](https://github.com/ladislav-zezula/StormLib/blob/master/src/SFileCreateArchive.cpp).
- [warcraft-rs](https://github.com/wowemulation-dev/warcraft-rs), [workspace manifest](https://raw.githubusercontent.com/wowemulation-dev/warcraft-rs/main/Cargo.toml): separate archive/world/graphics/DBC crates; manifest declares MIT OR Apache-2.0. Format-version/round-trip quality requires corpus testing.
- [Noggit3](https://github.com/wowdev/noggit3), [GPL3 license](https://raw.githubusercontent.com/wowdev/noggit3/master/COPYING), [MapIndex source](https://raw.githubusercontent.com/wowdev/noggit3/master/src/noggit/map_index.cpp): tile/global WMO indexing and save workflow.
- [Noggit Red fork](https://github.com/Marlamin/noggit-red): evaluate a pinned build, not an assumption that every fork has identical behavior.
- [WoW Spell Editor](https://github.com/stoneharry/WoW-Spell-Editor): specialist tool candidate; no integration or license audit performed.
- [DBC-to-SQL dataset/tool](https://github.com/Kaev/AzerothcoreDBCToSQL): comparison candidate, not a substitute for installed client/schema provenance.
- wowdev ADT/v18, M2, WMO and WDT wiki pages could not be retrieved in this session. They are discovery leads, not verified evidence here.

## E4 Scripting and IDE
- [ALE](https://github.com/azerothcore/mod-ale): former mod-eluna URL redirects here; README explicitly distinguishes ALE from original Eluna and links a compatibility fork.
- [Original Eluna API](https://elunaluaengine.github.io/).
- [ElunaAzerothcore fork](https://github.com/ElunaLuaEngine/ElunaAzerothcore): repository exists; compatibility/build viability against local revision not tested.
- [LuaLS](https://github.com/LuaLS/lua-language-server).
- [clangd](https://clangd.llvm.org/).
- [Language Server Protocol](https://microsoft.github.io/language-server-protocol/).
- [Debug Adapter Protocol](https://microsoft.github.io/debug-adapter-protocol/).
- [MySqlConnector](https://mysqlconnector.net/): asynchronous .NET driver candidate. Pin package/version/license in implementation dependency review.

