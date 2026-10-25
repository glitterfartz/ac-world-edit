# TrinityCore master world DB as the retail editing backend

## Context
Retail mode (CASC client) currently has the database switched off (`ClientProfile.Retail.SupportsDatabase=false`, `ClientServices.OpenRetail` sets `DatabaseEnabled=false`). Retail editing should use the TrinityCore master world DB (local `world` schema, TDB 1210.26091, plus `hotfixes`). All schema knowledge today is hardcoded AzerothCore 3.3.5. It lives in inline SQL in `Scripts/Data/*Source.cs`, in table whitelists in `Foundation/InteractionRows.cs`, and in enums in `QuestModel`, `SmartCatalog`, `NpcInteraction` and the creature sections.

Goal: an `IDBSource` abstraction with an AzerothCore and a TrinityCore implementation. The quest, creature and behavior editors switch their field sets and catalogs per dialect. Every DB difference is documented in `docs/DB-DIFFERENCES.md`. The work is delivered in phases.

**Decisions (from the user)**
- The dialect follows the client kind: Wrath335 uses AzerothCore and Retail uses TrinityCore. There is no autodetect, so a schema mismatch fails with a dialect-specific error.
- Hotfixes are read from a second database name on the same connection (default `hotfixes`), falling back to client DB2.
- Each phase delivers full read and edit/export parity for its area.
- The differences doc goes in `docs/DB-DIFFERENCES.md`, linked from CLAUDE.md.

**Phase 1 scope decisions (recorded after the codebase survey)**
- Waypoints: `WaypointSource`/`WaypointDraft`/`SqlExport.Waypoint`/`WaypointOverlay` were added after this plan was written. Phase 1 puts waypoint reads and export behind `IDBSource`/`ISqlDialect` with an AC implementation only. TC waypoints get their own phase (Phase 9, below). Until then the waypoint layer is disabled in TC mode.
  - Accepted tradeoff: TC mode has no waypoint layer until Phase 9.
- Statics: `InteractionTables`, `SmartCatalog`, `QuestModel` and `NpcFlags` become thin static facades over AC schema instances (`AcTables`, `AcSchema`). Only the places that must choose a dialect take the schema object: `RowSet.Apply`, `InteractionDraft`, `SqlExport.Row`, and the new `IDBSource` call sites. The ~480 other references are left alone.
  - Accepted tradeoff: the statics linger as AC-only scaffolding, so some code still reaches the AC schema directly. They are removed or converted as the TC phases touch each area. In exchange, Phase 1 is a small, reviewable, no-behavior-change diff.
- `WorldDatabase.SourceKey`: the dialect is hashed in for TrinityCore only. AC keys are unchanged.
  - Accepted tradeoff: the rule is not uniform (AC hash has no dialect ingredient). In exchange, existing AC drafts in `interactions.json` stay valid and TC keys can't collide with AC ones.
- Guid widening: every guid-typed value becomes `ulong`, not only the list under Design. That adds `GameObjectSpawn`, `WaypointUser`, the `NpcInteraction.Build`/`ContentSource`/`InteractionSource` parameters, the `FindSpawnsAsync` tuples, `WaypointOverlay`'s focused guid, `SmartBehavior.CreatureSpawnExists`, and the five `GetUInt32` readers (`CreatureSource`, `GameObjectSource`, `GraveyardSource`, `WaypointSource`, `ContentSource.FindSpawnsAsync`).
  - Accepted tradeoff: a wider diff in Phase 1 that touches overlays and tests. In exchange, no spot can overflow when TC bigint guids arrive.
- Other survey findings that Phase 1 must handle:
  - Statics used outside the sources need a non-DB home: `CreatureSource.SafeError` (~14 uses), `TeleportSource.Continents`/`Pick`, `ContentSource.FillTexts`, `InteractionSource.MaxRows`/`MaxSearch`, `GraveyardSource.HealerRadius`.
  - `Tests/.../SpawnPreviewTests.cs:196` calls static `WorldReadSession.Spatial` directly; keep its signature and SQL until Spatial goes per-dialect.
  - `SqlExport` has no gameobject move statement today, so "GO move" export is new work (Phase 4), not a port.
  - `ColumnKind` has no 64-bit kind; add one before TC `npcflag`/bigint guid columns (Phase 6).
  - `SqlExport.Build` output must stay byte-for-byte identical for AC (`SqlExportScriptTests`).
  - The connection is a lazy `Func<>` reading a UI control; keep the snapshot-before-`Task.Run` pattern when it becomes `Func<WorldConnection>`.

## Design

**Translation model: dialect-native rows, shared records only for overlays.**
- Read-only overlay results stay shared and are translated by each dialect: `CreatureSpawn`, `GameObject*` records, `GraveyardRecord`/`GraveyardSite`, `AreaTriggerRecord`, `TeleportLocation`.
- Editing stays on the existing generic path, `RowEdit(Table, Key, Before, After)` → `SqlExport`. Each dialect supplies its own table catalog, so structural differences round-trip without a lossy neutral model. Examples: `quest_objectives` 1:N rows, `creature_template_difficulty`, `creature_template_gossip`.
- Editors switch descriptors (field groups, enums, catalogs), not data models.

**Types (Godot-free, so tests compile them)**
- `Foundation/WorldDialect.cs`: `enum WorldDialect { AzerothCore, TrinityCore }`.
- `Scripts/Data/WorldConnection.cs`: `record WorldConnection(DatabaseSettings World, string? Hotfixes, WorldDialect Dialect)`.
  - It replaces `Func<DatabaseSettings> Connection` everywhere: overlays, `ContentEditorWindow`, `CreatureContext`, the sections and `TerrainViewer.Teleports/Locate`.
  - `WorldDatabase.SourceKey` hashes the dialect too, so drafts in `interactions.json` can't cross over.
- `Scripts/Data/IDBSource.cs`: one interface, with members grouped by area that mirror today's public APIs:
  - Spawns: `ReadCreaturesAsync`, `ReadGameObjectsAsync`, `FindSpawnsAsync`.
  - World points: `ReadGraveyardsAsync`, `ReadAreaTriggersAsync`, `ReadTeleportsAsync`.
  - Content: `ReadQuestAsync`, `ReadBehaviorAsync`, `ReadAreaTriggerBehaviorAsync`, `SearchAsync(SmartParamKind…)`, `SearchBroadcastAsync`, `UsedActionListsAsync`.
  - Interaction: `ReadInteractionAsync`, `MenuExistsAsync`, `TrainerExistsAsync`, `SearchItemDetailsAsync`, `SearchQuestsAsync`.
  - Schema: `IWorldSchema Schema` (see below) and `string SourceKey`.
  - The factory is `DBSource.Open(WorldConnection, IClientLookup?)`. `IClientLookup` is a Godot-free wrapper over `Db2Tables`, null for Wrath, that serves AreaTrigger, BroadcastText, ItemSparse and SpellName.
- `IWorldSchema`, implemented per dialect:
  - `ITableCatalog Tables` replaces the static `InteractionTables`.
  - `QuestSchema`, `SmartCatalog Smart`, `NpcSchema` (npc flags, gossip option kinds) and `CreatureSchema` (ranks/classification, flag tables).
  - `ISqlDialect Sql` gives `SqlExport` its dialect-specific statements: creature/GO move, graveyard edit, and hotfix rows that need `hotfix_data`.
- Folders: `Scripts/Data/AzerothCore/` holds today's sources, moved and renamed `Ac*Source`. `Scripts/Data/TrinityCore/` holds `Tc*Source`. In `Tests/ACWorldEdit.Tests/ACWorldEdit.Tests.csproj:12`, change `Scripts/Data/*.cs` to `Scripts/Data/**/*.cs` with `%(RecursiveDir)`.
- Shared helpers stay in `WorldDatabase.cs` (`InList`, `AddRegion`, `AddSearch`, `Distance`). `Spatial` and `Restrictions` become per-dialect, because masks become `spawnDifficulties`/`PhaseId` and pools become `pool_members`.
- Guid widening: TC guids are `bigint` (the max today is 11.8M). Widen `CreatureSpawn.Guid`, `CreatureMove.Guid`, `SpiritHealer.Guid` and the snapshot guid to `ulong`.

**UI switching**
- `ClientProfile` gains `WorldDialect Dialect`, and Retail gets `SupportsDatabase=true`. `ClientServices.OpenRetail` sets `DatabaseEnabled=true`.
- `TerrainViewer` builds the `WorldConnection` from the profile and the per-profile settings.
- Editors read `connection.Dialect` only through `IDBSource.Schema`:
  - `CreatureSectionRegistry` filters sections by schema support.
  - `TemplateSection`, `StatsSection`, `GossipSection`, `TrainerSection` and `VendorSection` take their field groups and enums from the schema.
  - `QuestPanel.Pages` builds pages from `QuestSchema`. TC adds an Objectives page and drops `RequiredNpcOrGo`/`RequiredItem`.
  - `BehaviorPanel.Rules` uses `Schema.Smart`.

## Phases (each phase = its own commit(s), tests, and a visual check where the UI changes)

**Phase 1: docs and interface extraction (AzerothCore only, no behavior change)**
- Write `docs/DB-DIFFERENCES.md` from the appendix below, and link it from CLAUDE.md Notes.
- Add `WorldDialect`, `WorldConnection`, `IDBSource`, `IWorldSchema` and `ITableCatalog`. Move the sources into `Scripts/Data/AzerothCore/` behind `AcDBSource`. `InteractionTables` becomes `AcTables : ITableCatalog`, and `SmartCatalog`/`QuestModel` data moves behind `AcSchema`.
- Replace every direct `new XSource(...)` with `DBSource.Open(connection)`. That covers the ~20 call sites listed by the survey: the overlays, `QuestPanel`, `BehaviorPanel*`, the `CreatureProperties/*` sections and `TerrainViewer.Locate/Teleports`.
- Widen guids to `ulong`. Run `check-layering`, the full default suite and the Db suite against AC. All must stay green.

**Phase 2: connection, settings and hotfixes plumbing**
- Settings:
  - `UserSettings` saves the database settings per client profile (`[database.wrath]`, `[database.retail]`). The retail default is `world`, with hotfixes `hotfixes`.
  - `ConnectionForm` gets a Hotfixes database field that is shown only for TrinityCore.
  - `SettingsResolver` reads a new `ACWORLD_TC_DB_INFO` variable (same 5-part form, plus an optional 6th part for hotfixes).
- `WorldReadSession.ReadSchema` uses a per-dialect table list. `DatabaseDiagnostics.Describe` says "not a TrinityCore world database" in TC mode. `TestAsync` also checks that the hotfixes database is reachable when it is configured.
- Tests: `SettingsResolverTests`, `DatabaseSettingsTests`, and a TC `DbFact` variant (`TcDbFact`) that skips without `ACWORLD_TC_DB_INFO`.

**Phase 3: retail client catalogs (prerequisite for drawing spawns)**
- Vendor the WoWDBDefs `.dbd` files at the pinned revision (`Data/definitions/revision.txt`):
  - CreatureDisplayInfo, CreatureModelData, CreatureDisplayInfoExtra if needed
  - GameObjectDisplayInfo
  - AreaTrigger, AreaTriggerActionSet
  - BroadcastText, ItemSparse, Item, SpellName
  - ContentTuning
  - WorldSafeLocs is not needed, because it is a TC DB table in master.
- Discover the FileDataIDs (listfile path such as `dbfilesclient/areatrigger.db2`, verified by layout hash, the same way as the existing `Db2Tables.FileIds`) and add them. Don't write IDs from memory.
- Add a retail creature and gameobject display catalog under `Scripts/Assets/Client`, alongside the Wrath catalogs. `hotfixes.creature_display_info` and the other hotfix rows override DB2 rows by ID. Register it in `ClientServices.OpenRetail` (Creatures/GameObjects/Areas are null today).
- Add `IClientLookup` in Assets (AreaTrigger rows, BroadcastText, ItemSparse name/quality/icon, SpellName). Add the corresponding retail corpus tests and fill in the `NEEDED-TESTS.md` entries.

**Phase 4: TrinityCore read-only overlays and spawn move export**
- `TcSpawnSource`:
  - creature/gameobject `id`, `spawnDifficulties` CSV, `PhaseId`/`PhaseGroup`/`phaseUseFlags`, and `creature.modelid` override.
  - Pools come from `pool_members` (type 0=creature, 1=GO). Events come from `game_event_creature`/`game_event_gameobject` (same shape).
  - `SpawnPreview` gets a dialect filter: difficulty IDs and phase ID in place of spawn/phase masks. The inspector UI shows those fields in TC mode.
- `TcGraveyardSource` reads `world_safe_locs` (LocX/LocY/LocZ/Facing, MapID). Links come from `graveyard_zone` (no Faction). Faction is derived from `conditions` SourceType 27 (SourceGroup=GhostZone, SourceEntry=graveyard, ConditionType 6 CONDITION_TEAM, value 469/67).
- `TcTeleportSource` reads `game_tele` (same schema). The `Continents` list comes from Map DB2 instead of the hardcoded `{0,1,530,571}`.
- Area triggers:
  - Positions and shapes come from client `AreaTrigger.db2`: Pos, ContinentID, Radius, Box*, ShapeType, Phase*. `hotfixes.area_trigger` rows overlay them by ID.
  - These join the TC tables keyed by the DB2 ID: `areatrigger_teleport.PortLocID → world_safe_locs`, `areatrigger_scripts`, `areatrigger_involvedrelation`, `areatrigger_tavern` and `smart_scripts` source_type 2.
  - TC's own `areatrigger` table holds server-spawned AreaTrigger entities (291 rows, shapes in `areatrigger_create_properties`). It is a separate, secondary read-only overlay kind, not the same record.
- Export: creature and GO moves use the same columns in TC (`position_x..orientation`, `guid`). `ISqlDialect` handles bigint guids.
- Tests (TcDb): Goldshire/Elwynn spawns, a known graveyard and its faction condition, AT 4 and its teleport, AT positions from the corpus. Visual check: `--client-shot-tile` on an Elwynn retail tile with spawns, graveyards and triggers visible.

**Phase 5: graveyard editing (TC)**
- Moving a graveyard updates `world_safe_locs` LocX/Y/Z (and Facing).
- Before moving, warn when the ID is referenced elsewhere. `areatrigger_teleport.PortLocID` currently has 0 overlaps, but `world_safe_locs` is shared by teleports, battlegrounds and other references.
- Link edits go to `graveyard_zone`. A faction edit inserts, updates or deletes the `conditions` row for SourceType 27, CONDITION_TEAM.
- `GraveyardDraft` stays dialect-neutral, and `ISqlDialect.GraveyardStatements` does the translation. Tests: `SqlExportTests` TC cases for each faction transition.

**Phase 6: creature properties (TC)**
- `TcTables` catalog:
  - `creature_template` (TC columns).
  - `creature_template_difficulty` (DifficultyID 0 row): levels via ContentTuningID, Health/Mana/Armor/DamageModifier, loot IDs, gold, TypeFlags, StaticFlags.
  - `creature_template_gossip` (CreatureID, MenuID) replaces `gossip_menu_id`.
  - `creature_trainer` (CreatureID, TrainerID, MenuID, OptionID) replaces `creature_default_trainer`. `trainer` has no `Requirement` column.
  - `gossip_menu_option` (GossipOptionID, OptionNpc, Language, Flags, GossipNpcOptionID, SpellID, OverrideIconID; no OptionIcon/OptionType/OptionNpcFlag).
  - `npc_text` (BroadcastTextID0-7 and Probability0-7 only; text from BroadcastText).
  - `npc_vendor` (+type, BonusListIDs, PlayerConditionID, IgnoreFiltering).
  - `creature_questender`/`creature_queststarter` gain VerifiedBuild. `npc_spellclick_spells` is the same.
- TC `NpcSchema`/`CreatureSchema`: `npcflag` is a 64-bit bigint, plus Classification, `unit_flags3`, and the TC OptionNpc enum. `StatsSection` uses ContentTuning instead of minlevel/maxlevel, and `creature_classlevelstats` has only basemana/attackpower/rangedattackpower.
- Vendor search and item tooltips use ItemSparse (DB2 plus `hotfixes.item_sparse`) instead of `item_template`.
- Tests: TcDb Goldshire innkeeper, vendor and trainer. `InteractionRows` TC catalog unit tests.

**Phase 7: quests (TC)**
- `TcQuestSchema`:
  - quest_template TC columns: ContentTuningID, RewardMoneyDifficulty/Multiplier, FlagsEx/Ex2/Ex3, RewardDisplaySpell1-3, RewardCurrency*, Portrait*, Expansion and so on. QuestLevel, MinLevel, RewardMoney, RequiredFaction*, RequiredNpcOrGo*, RequiredItem*, ObjectiveText* are gone.
  - Objectives become a new 1:N `quest_objectives` page (Type, Order, StorageIndex, ObjectID, Amount, Flags, Description; ID allocation via MAX(ID)+1).
  - `quest_template_addon` gains ScriptName. `quest_request_items` gains the emote delays.
- Name lookups for ObjectID depend on Type (creature, GO, item, currency…).
- `QuestModel.Check` per dialect, with TC defaults for `NewQuest`. Races and classes come from the retail lists (ChrRaces/ChrClasses DB2 or a static table).
- The conditional text tables (`quest_*_conditional`) are read-only in this phase.
- Tests: TcDb read of a known quest with objectives, and draft/SQL round-trip tests for objectives.

**Phase 8: SmartAI and speech (TC)**
- Generate `SmartCatalog.Trinity` from TrinityCore master `SmartScriptMgr.h`, the same way the AC catalog was generated. The ID ranges differ (TC: events ≤90, actions ≤153, targets ≤29; AC: 110/242/207), so nothing is reused by ID.
- `smart_scripts` TC columns:
  - `entryorguid` bigint and `Difficulties`.
  - No `event_param6`. Adds `action_param7` and `event/action/target_param_string`.
  - Source types 0, 1, 2, 3, 5, 9, 10, 11, 12 are used in the data. Scope 2 is keyed by AreaTrigger.db2 ID.
- `creature_text` gains `SoundPlayType`. Speech text and search use BroadcastText (`hotfixes.broadcast_text`, 237k rows, then client DB2 fallback).
- Editing broadcast text is a hotfixes export: a schema-qualified `hotfixes.broadcast_text` row plus a `hotfix_data` row (TableHash, RecordId, Status). STALE checks cover both databases.
- Tests: `SmartCatalogTests` for the TC catalog, `SmartRuleEditsTests` TC columns, and a TcDb Goldshire SmartAI creature.

**Phase 9: waypoints (TC)**
- `TcWaypointSource` reads `waypoint_path(PathId, MoveType, Flags, Velocity, Comment)` and `waypoint_path_node(PathId, NodeId, PositionX/Y/Z, Orientation, Delay)`. Creatures reference paths through `creature_addon`/`creature_template_addon.PathId`.
- TC has no per-node Action/ActionChance/Smooth and moves MoveType/Velocity to the path, so `WaypointEdit` needs a per-dialect view of which fields are editable.
- `ISqlDialect.WaypointStatements` writes `waypoint_path_node` position edits. Re-enable the waypoint layer in TC mode.
- Tests: TcDb read of a known path, `SqlExportTests` TC waypoint cases, and `--waypoint-smoke` against TC.

## Verification (per phase)
- `dotnet test Tests/ACWorldEdit.Tests -p:NuGetAudit=false` (default suite). Run the Db tests with `ACWORLD_DB_INFO` (AC, regression check) and with `ACWORLD_TC_DB_INFO` (TC).
- Retail corpus: `--settings corpus.runsettings` with `ACWORLD_RETAIL_DIR`.
- Run the `check-layering` skill after any Data/Foundation/Assets edit.
- Visual: `python launch_viewer.py` → retail client → `--client-shot-tile` on an Elwynn tile to confirm spawns, graveyards and triggers draw. Open the creature, quest and behavior editors in both modes and confirm they show the dialect's fields. Export SQL from the pending changes window and run it against a scratch copy of `world`.

---

## Appendix: schema findings (`acore_world` vs `world` TDB 1210.26091, via MySQL MCP)
Source for `docs/DB-DIFFERENCES.md`.

**Table sets**
- There are 157 AC-only tables. Most are `*_dbc` mirrors, which TC replaces with client DB2 plus `hotfixes`. The rest:
  - Items and text: `item_template(_locale)`, `broadcast_text(_locale)`, `npc_text_locale`, `acore_string`.
  - Waypoints and pools: `waypoint_data(_addon)`, `waypoints`, `waypoint_scripts`, `script_waypoint`, `pool_creature/gameobject/pool/quest`.
  - Graveyards and trainers: `game_graveyard`, `creature_default_trainer`, `npc_trainer`.
  - Dungeons: `dungeon_access_*`, `instance_encounters`.
  - Spells: `spell_bonus_data`, `spell_proc_event`, `spell_ranks`, `spell_scripts`.
  - Player data: `player_class_stats`, `player_race_stats`.
  - Other: `creature_multispawn`, `creature_sparring`, `creature_text_option*`, `quest_money_reward`.
- There are 103 TC-only tables:
  - World: `world_safe_locs`, `waypoint_path(_node)`, `pool_members`.
  - Quests: `quest_objectives(_locale/_completion_effect)`, `quest_*_conditional`, `quest_reward_*`, `quest_pool_*`, `quest_visual_effect`.
  - Creatures: `creature_template_difficulty`, `creature_template_gossip`, `creature_trainer`, `creature_template_sparring`, `creature_static_flags_override`, `creature_summoned_data`, `creature_movement_info`.
  - Area triggers: `areatrigger_template(_actions)`, `areatrigger_create_properties(_orbit/_polygon_vertex/_spline_point)`.
  - Phasing: `phase_area`, `phase_name`, `terrain_swap_defaults`, `terrain_worldmap`.
  - Retail systems: `conversation_*`, `playerchoice*`, `scenario*`, `spawn_tracking*`, `scene_template`, `gossip_menu_addon`, `vehicle_template`, `world_state`, `serverside_spell(_effect)`, `trinity_string`, `access_requirement`.
  - Views: `vw_*_with_labels`.
- `hotfixes` DB: `broadcast_text` (237k rows), and sparse overrides `item`, `item_sparse`, `spell_name`, `creature_display_info`, `area_trigger` (0 rows), `content_tuning`, `player_condition`, `quest_v2`. Also `hotfix_data(Id,UniqueId,TableHash,RecordId,Status,VerifiedBuild)` and `hotfix_blob`.

**Column differences in shared tables (AC → TC)**
- `creature`:
  - guid int → bigint.
  - spawnMask/phaseMask → spawnDifficulties(varchar CSV)/phaseUseFlags/PhaseId/PhaseGroup/terrainSwapMap.
  - curhealth/curmana → curHealthPct. npcflag int → bigint.
  - Removed: dynamicflags, CreateObject, Comment. Added: modelid, unit_flags2/3, StringId.
  - The local AC DB has `id` (code also probes `id1`).
- `gameobject`: guid → bigint, masks → spawnDifficulties/phase columns, Comment removed, terrainSwapMap/StringId added.
- `creature_addon`/`creature_template_addon`: path_id → PathId. bytes1/bytes2 are split into StandState/AnimTier/VisFlags/SheathState/PvPFlags. Adds MountCreatureID and ai/movement/meleeAnimKit.
- `creature_template`:
  - Removed: difficulty_entry_1-3, gossip_menu_id, minlevel/maxlevel, exp, speed_swim/flight, detection_range, rank, DamageModifier, dynamicflags, type_flags, lootid/pickpocketloot/skinloot, PetSpellDataId, mingold/maxgold, HoverHeight, Health/Mana/ArmorModifier.
  - Added: femaleName, TitleAlt, RequiredExpansion, VignetteID, scale, Classification, unit_flags3, trainer_class, WidgetSetID/UnitConditionID, StringId.
  - npcflag → bigint. Per-difficulty data moved to `creature_template_difficulty`.
- `creature_template_model`: Idx smallint → int (otherwise the same). `creature_template_locale`: +NameAlt, TitleAlt.
- `creature_classlevelstats`: TC keeps only level, class, basemana, attackpower, rangedattackpower, comment.
- `creature_template_movement`/`creature_movement_override`: Ground/Swim/Flight/Rooted → HoverInitiallyEnabled.
- `creature_equip_template`: + AppearanceModID1-3, ItemVisual1-3. `creature_text`: + SoundPlayType. `creature_questitem`: + DifficultyID. `creature_queststarter/ender`, `gameobject_queststarter/ender`, `gossip_menu`: + VerifiedBuild.
- `creature_formations`, `game_event_creature`, `spawn_group`: guids → bigint. `game_event`: + WorldStateId.
- `gameobject_template`: + Data24-34, ContentTuningId, RequiredLevel, StringId. `gameobject_template_addon`: + artkit4, WorldEffectID, AIAnimKitID. `gameobject_addon`: + WorldEffectID, AIAnimKitID.
- `gossip_menu_option`:
  - Removed: OptionIcon, OptionType, OptionNpcFlag.
  - Added: GossipOptionID, OptionNpc, Language, Flags, GossipNpcOptionID, SpellID, OverrideIconID.
  - BoxMoney → bigint.
- `npc_text`: TC has only Probability0-7 and BroadcastTextID0-7. The text, lang and emote columns are gone, and the text lives in BroadcastText.
- `npc_vendor`/`game_event_npc_vendor`: + type, BonusListIDs, PlayerConditionID, IgnoreFiltering.
- `trainer`: Requirement removed. `trainer_spell`: same.
- `graveyard_zone`: Faction removed. Faction is now `conditions` SourceType 27 (SourceGroup=GhostZone, SourceEntry=graveyard ID, ConditionType 6 team 469/67), 332 rows.
- Graveyard locations: AC `game_graveyard(ID,Map,x,y,z,Comment)`. TC `world_safe_locs(ID,MapID,LocX,LocY,LocZ,Facing,TransportSpawnId,Comment)`, shared with `areatrigger_teleport` (no ID overlap today). 4 graveyard_zone IDs dangle.
- `areatrigger`:
  - AC: mirror of AreaTrigger.dbc (entry, map, x, y, z, radius, box, orientation).
  - TC: server-side spawned AreaTrigger entities (SpawnId, AreaTriggerCreatePropertiesId, IsCustom, MapId, SpawnDifficulties, PosX/Y/Z, phases, ScriptName), 291 rows.
  - TC client triggers come from AreaTrigger.db2 plus `hotfixes.area_trigger`.
- `areatrigger_teleport`: target_map/position/orientation → PortLocID (→ world_safe_locs). `areatrigger_tavern`: faction removed. `areatrigger_scripts`, `areatrigger_involvedrelation`: same.
- `quest_template`:
  - Removed: QuestLevel, MinLevel, RequiredFactionId/Value1-2, RewardMoney, RewardDisplaySpell, RequiredPlayerKills, RewardTalents, RequiredNpcOrGo(Count)1-4, RequiredItemId/Count1-6, Unknown0, ObjectiveText1-4.
  - Objectives are now `quest_objectives` (Type 0-22, 61k rows).
  - Added: QuestPackageID, ContentTuningID, RewardXP/MoneyMultiplier, RewardBonusMoney, RewardDisplaySpell1-3, RewardArtifact*, FlagsEx/Ex2/Ex3, RewardChoiceItemDisplayID1-6, RewardSkillLineID/NumSkillUps, Portrait*, RewardFactionCapIn1-5, RewardFactionFlags, RewardCurrencyID/Qty1-4, Accepted/CompleteSoundKitID, AreaGroupID, Expansion, ManagedWorldStateID, QuestSessionBonus, ResetByScheduler, RewardFavor.
- `quest_template_addon`: + ScriptName. SpecialFlags int → tinyint, BreadcrumbForQuestId → int. `quest_request_items`: + EmoteOnCompleteDelay/IncompleteDelay. `quest_offer_reward`: Emote columns become signed. `quest_template_locale`: Title/Details/Objectives/EndText/CompletedText/ObjectiveText* → LogTitle/LogDescription/QuestDescription/AreaDescription/Portrait*/QuestCompletionLog. `quest_poi`: id/WorldMapAreaId/Floor → BlobIndex/Idx1/QuestObjectiveID/UiMapID/…; `quest_poi_points`: + Z.
- `smart_scripts`:
  - entryorguid → bigint. + Difficulties.
  - event_param6 removed. + event_param_string, action_param7, action_param_string, target_param_string.
  - Max IDs: AC event 110 / action 242 / target 207; TC 90 / 153 / 29.
  - TC source_types used: 0,1,2,3,5,9,10,11,12. Catalogs must be separate.
- `conditions`: + ConditionStringValue1. Source types ≥25 diverge. TC uses 25-36: 26 phase, 27 graveyard, 28/30 areatrigger, 29 conversation, 31 trainer spell, 32 object visibility, 33 spawn group…. AC uses 28-30 with different meanings.
- `points_of_interest`: + PositionZ, WMOGroupID, VerifiedBuild. `page_text`: + PlayerConditionID, Flags.
- Waypoints: AC `waypoint_data(id,point,position_*,orientation,velocity,delay,smoothTransition,move_type,action,action_chance,wpguid)`. TC `waypoint_path(PathId,MoveType,Flags,Velocity,Comment)` plus `waypoint_path_node(PathId,NodeId,PositionX/Y/Z,Orientation,Delay)`.
- Pools: AC `pool_creature/pool_gameobject(guid,pool_entry,chance,description)`. TC `pool_members(type,spawnId,poolSpawnId,chance,description)`.
- Loot tables (all `*_loot_template`): Reference → ItemType.
- Other non-editor tables:
  - `achievement_dbc` (rewritten), `achievement_reward_locale` Text→Body.
  - `battleground_template` (AC adds team sizes and levels), `command`.security.
  - `instance_template`.allowMount, `lfg_dungeon_template` + requiredItemLevel.
  - `pet_levelstats` min/max_dmg, `player_factionchange_*` comments, `playercreateinfo` (zone → npe_* columns), `playercreateinfo_cast_spell` + createMode, `playercreateinfo_item`.Note.
  - `reputation_spillover_template` faction6, `spell_area` autocast→flags, `spell_custom_attr` spell_id→entry, `spell_enchant_proc_data` (renamed columns), `spell_group_stack_rules`.description, `spell_proc` + SpellFamilyMask3/ProcFlags2, `spell_target_position` + OrderIndex.
  - `transports` + phase columns, `vehicle_(template_)accessory` + RideSpellID.
