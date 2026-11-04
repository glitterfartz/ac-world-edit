# AzerothCore vs TrinityCore world database differences

Findings compare `acore_world` with the TrinityCore master `world` schema (TDB 1210.26091) plus its
`hotfixes` database, against retail client 12.1.5.70575. Re-check a table against the live databases
and the official pages before relying on a difference, and update this page when a finding changes an
editor.

ACWorldEdit edits against the AzerothCore (3.3.5) world schema for Wrath clients and, as the TrinityCore backend lands, against the TrinityCore master world schema for the retail client. The dialect follows the client kind (`ClientKind.Wrath335` uses AzerothCore, `ClientKind.Retail` uses TrinityCore); there is no autodetection.

Findings compare `acore_world` with the TrinityCore master `world` schema (TDB 1210.26091) plus its `hotfixes` database. Add to this document whenever a new difference affects the editors.

Schema references for column meanings, flags and enums:
- AzerothCore world: https://www.azerothcore.org/wiki/database-world
- TrinityCore master world: https://trinitycore.info/database/master/world/home

## Table sets
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
  - The visibility preview counts spawns per phase: AzerothCore expands each `phaseMask` into its bits (a mask with every bit set counts as "every phase"); TrinityCore groups by `PhaseId` (non-zero) and names it from `phase_name`. `PhaseGroup` is not expanded yet.
  - Retail systems: `conversation_*`, `playerchoice*`, `scenario*`, `spawn_tracking*`, `scene_template`, `gossip_menu_addon`, `vehicle_template`, `world_state`, `serverside_spell(_effect)`, `trinity_string`, `access_requirement`.
  - Views: `vw_*_with_labels`.
- `hotfixes` DB: `broadcast_text` (237k rows), and sparse overrides `item`, `item_sparse`, `spell_name`, `creature_display_info`, `area_trigger` (0 rows), `content_tuning`, `player_condition`, `quest_v2`. Also `hotfix_data(Id,UniqueId,TableHash,RecordId,Status,VerifiedBuild)` and `hotfix_blob`.

## Column differences in shared tables (AC → TC)
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
  - `Classification` replaces `rank` with different values: 0 Normal, 1 Elite, 2 Rare Elite, 3 Obsolete (AC: Boss), 4 Rare, 5 Trivial, 6 Minus Mob. `npcflag` is a 64-bit column (low 32 bits NPCFlags, high 32 NPCFlags2).
  - npcflag → bigint. Per-difficulty data moved to `creature_template_difficulty`.
- `creature_template_model`: Idx smallint → int (otherwise the same). `creature_template_locale`: +NameAlt, TitleAlt.
- `creature_classlevelstats`: TC keeps only level, class, basemana, attackpower, rangedattackpower, comment. Health, armor and damage come from the client's ExpectedStat.db2 instead (row for the level and `HealthScalingExpansion`, else the expansion -2 row), scaled by ContentTuningXExpected modifiers, the class modifier row (ExpectedStatMod 4 warrior, 2 paladin, 3 rogue, 1 mage) and the template multipliers (`Creature::GetMaxHealthByLevel`, `DB2Manager::EvaluateExpectedStat`). `TcStatCalculator` reproduces this.
- `creature_template_difficulty` (checked against [TC](https://trinitycore.info/database/master/world/creature_template_difficulty) and TDB 1210.26091): replaces AC's level range, `exp`, the multipliers and loot/gold columns. The creature's level is `ContentTuning` top level (its `MaxLevel` plus what `MaxLevelType` adds: 1 → +1, 2 → the max level of the server's expansion, 3 → the previous expansion's) plus a random value between `LevelScalingDeltaMin` and `LevelScalingDeltaMax` (smallint, signed; TrinityCore's C++ calls them `DeltaLevelMin/Max`). Without a `ContentTuningID` the server takes the tuning from the map's difficulty or the area. `HealthScalingExpansion` is -1 (current expansion) to 11; other values load as 0. The server also multiplies health and damage by its `Rate.Creature.*` rates.
- Checked against TDB 1210.26091 (the plan's one-to-one mappings do not hold):
  - `creature_template_gossip` is keyed (CreatureID, MenuID): 1:N, not a single `gossip_menu_id` (7 of 7,104 creatures have several menus).
  - `creature_trainer` is keyed (CreatureID, MenuID, OptionID): a trainer belongs to a gossip option (741 of 975 rows), and 23 creatures have several trainers. `trainer_spell` adds `ReqAbility1-3` (AzerothCore's `trainer_spell` has them too, as `ObjectMgr::LoadTrainerSpell` reads them and refuses a spell that does not exist). `trainer.Type` is 0 None, 1 Talent, 2 Tradeskill, 3 Pet.
  - `npc_vendor`'s key includes `type` (item vs currency; 1 of 49,853 rows is not an item).
  - 3,648 templates have no `DifficultyID=0` row in `creature_template_difficulty` (101,919 rows are for other difficulties), so stats, loot and gold need a difficulty choice or a fallback.
  - TC `npcflag` low 32 bits are `NPCFlags` (vendor/trainer/quest-giver bits match AC; 0x4 and 0x8000000+ differ), the high 32 bits `NPCFlags2` (named in `TcCreatureView.DescribeNpcFlags`).
- `creature_template_movement`/`creature_movement_override`: Ground/Swim/Flight/Rooted → HoverInitiallyEnabled.
- `creature_equip_template`: + AppearanceModID1-3, ItemVisual1-3. `creature_text`: + SoundPlayType. `creature_questitem`: + DifficultyID. `creature_queststarter/ender`, `gameobject_queststarter/ender`, `gossip_menu`: + VerifiedBuild.
- `creature_formations`, `game_event_creature`, `spawn_group`: guids → bigint. `game_event`: + WorldStateId.
- `gameobject_template`: + Data24-34, ContentTuningId, RequiredLevel, StringId. `gameobject_template_addon`: + artkit4, WorldEffectID, AIAnimKitID. `gameobject_addon`: + WorldEffectID, AIAnimKitID.
- `gossip_menu_option`:
  - Removed: OptionIcon, OptionType, OptionNpcFlag.
  - Added: GossipOptionID, OptionNpc, Language, Flags, GossipNpcOptionID, SpellID, OverrideIconID.
  - BoxMoney → bigint. Shared gossip descriptors use TC `OptionNpc` service values rather than AC `OptionType` / `OptionNpcFlag`. Checked against the official [AC](https://www.azerothcore.org/wiki/gossip_menu_option) and [TC](https://trinitycore.info/database/master/world/gossip_menu_option) pages. The TC wiki currently lists BoxMoney as int; the existing catalog and TDB check use bigint. AzerothCore's table also has `BoxBroadcastTextID` (`ObjectMgr::LoadGossipMenuItems` ignores one that is not a broadcast text), and the AC catalog carries it.
- `npc_text`: TC has only Probability0-7 and BroadcastTextID0-7. The text, lang and emote columns are gone, and the text lives in BroadcastText.
- `npc_vendor`/`game_event_npc_vendor`: + type, BonusListIDs, PlayerConditionID, IgnoreFiltering. In `npc_vendor`, TC's primary key includes `type` (item vs currency) in addition to AC's `(entry,item,ExtendedCost)`. Both use signed smallint slots; AC's stock limit is an unsigned tinyint, TC's an unsigned int. Checked against the official [AC](https://www.azerothcore.org/wiki/npc_vendor) and [TC](https://trinitycore.info/database/master/world/npc_vendor) schema pages for the vendor descriptors. TC also supports negative item IDs as shared-list references (the current TC view shows the reference row without expansion). TC requires the base Vendor flag (0x80); stock/restock pairing applies to items only, while currency rows require a positive maxcount (quantity). Verified against [ObjectMgr::IsVendorItemValid](https://github.com/TrinityCore/TrinityCore/blob/master/src/server/game/Globals/ObjectMgr.cpp). Currency, extended-cost, bonus-list and player-condition existence checks are not implemented yet; only item existence and bonus-list numeric syntax are checked by the TC editor.
- Trainer descriptors follow [AC trainer](https://www.azerothcore.org/wiki/trainer), [AC trainer_spell](https://www.azerothcore.org/wiki/trainer_spell), [TC trainer](https://trinitycore.info/database/master/world/trainer) and [TC trainer_spell](https://trinitycore.info/database/master/world/trainer_spell). TC removes `Requirement`. TC `trainer.Type` is 0 None, 1 Talent, 2 Tradeskill (default), 3 Pet; AC assigns one default through [creature_default_trainer](https://www.azerothcore.org/wiki/creature_default_trainer), while TC [creature_trainer](https://trinitycore.info/database/master/world/creature_trainer) keys bindings by creature/menu/option. Both spell lists sort by required level then spell ID. Both catalogs read and edit cost, level, skill requirements and the three required abilities, and omit trainer/spell `VerifiedBuild`. Required level is limited to an unsigned byte in the descriptor.
- Creature quest-link descriptors cover `id` / `quest` pairs in [AC starters](https://www.azerothcore.org/wiki/creature_queststarter), [AC enders](https://www.azerothcore.org/wiki/creature_questender), [TC starters](https://trinitycore.info/database/master/world/creature_queststarter) and [TC enders](https://trinitycore.info/database/master/world/creature_questender). TC's extra `VerifiedBuild` remains outside the current read catalog. These descriptors cover creature links only; the quest content is edited through `TcTables.QuestTemplate`, `QuestAddon`, `QuestRequestItems`, `QuestOfferReward` and `QuestObjectives`.
- `graveyard_zone`: Faction removed. Faction is now `conditions` SourceType 27 (SourceGroup=GhostZone, SourceEntry=graveyard ID, ConditionType 6 team 469/67), 332 rows.
- Graveyard locations: AC `game_graveyard(ID,Map,x,y,z,Comment)`. TC `world_safe_locs(ID,MapID,LocX,LocY,LocZ,Facing,TransportSpawnId,Comment)`, shared with `areatrigger_teleport` (no ID overlap today). 4 graveyard_zone IDs dangle.
- `areatrigger`:
  - AC: mirror of AreaTrigger.dbc (entry, map, x, y, z, radius, box, orientation).
  - TC: server-side spawned AreaTrigger entities (SpawnId, AreaTriggerCreatePropertiesId, IsCustom, MapId, SpawnDifficulties, PosX/Y/Z, phases, ScriptName), 291 rows.
  - TC client triggers come from AreaTrigger.db2 plus `hotfixes.area_trigger`. Besides AC's sphere and box they are polygons (ShapeType 3: corners from Path.db2 `ShapeID`, offsets from the position with the first node dropped, no floor, capped at VolumeHeight; many sit at the map origin with world-space corners) and cylinders (4: Radius, BoxHeight tall from the position); other shapes never fire.
  - A TC spawn's shape is its `areatrigger_create_properties` `Shape` and `ShapeData0-7` (sphere radius, box half extents, polygon half height with `_polygon_vertex` corners, cylinder and disk radii, height and floor offset, bounded plane half extents), turned by the spawn's orientation.
- `areatrigger_teleport`: target_map/position/orientation → PortLocID (→ world_safe_locs). `areatrigger_tavern`: faction removed. `areatrigger_scripts`, `areatrigger_involvedrelation`: same.
- `quest_template`:
  - Removed: QuestLevel, MinLevel, RequiredFactionId/Value1-2, RewardMoney, RewardDisplaySpell, RequiredPlayerKills, RewardTalents, RequiredNpcOrGo(Count)1-4, RequiredItemId/Count1-6, Unknown0, ObjectiveText1-4.
  - Objectives are now `quest_objectives` (Type 0-22, 61k rows), keyed by a global `ID` with `QuestID` naming the quest. `ObjectID` is a creature (types 0 kill, 3 talk to), game object (2), item (1), currency (4, 16, 17), spell (5), faction (6, 7, 18) or area trigger (10, 19, 20) entry; `StorageIndex` is the tracking slot (0-23, unique per quest). Amounts are positive for counted types; area-trigger objectives store 0 and reputation objectives a reputation value (3000, 21000, ...). The type and flag names come from TrinityCore's `QuestObjectiveType` / `QuestObjectiveFlags` (the official page documents the columns without descriptions).
  - `QuestType` 3 is a world quest. `AllowableRaces` is a 64-bit mask with one bit per ChrRaces ID and all bits set (18446744073709551615) for every race, not 0; the Alliance-only and Horde-only masks are 6130900294268439629 and 12261800583900083122. `AllowableClasses` (in the addon) still uses 0 for every class and adds Monk (512), Demon Hunter (2048) and Evoker (4096).
  - Added: QuestPackageID, ContentTuningID, RewardXP/MoneyMultiplier, RewardBonusMoney, RewardDisplaySpell1-3, RewardArtifact*, FlagsEx/Ex2/Ex3, RewardChoiceItemDisplayID1-6, RewardSkillLineID/NumSkillUps, Portrait*, RewardFactionCapIn1-5, RewardFactionFlags, RewardCurrencyID/Qty1-4, Accepted/CompleteSoundKitID, AreaGroupID, Expansion, ManagedWorldStateID, QuestSessionBonus, ResetByScheduler, RewardFavor.
- `quest_template_addon`: + ScriptName. SpecialFlags int → tinyint, BreadcrumbForQuestId → int. `quest_request_items`: + EmoteOnCompleteDelay/IncompleteDelay. `quest_offer_reward`: Emote columns become signed. `quest_template_locale`: Title/Details/Objectives/EndText/CompletedText/ObjectiveText* → LogTitle/LogDescription/QuestDescription/AreaDescription/Portrait*/QuestCompletionLog. `quest_poi`: id/WorldMapAreaId/Floor → BlobIndex/Idx1/QuestObjectiveID/UiMapID/…; `quest_poi_points`: + Z.
- `smart_scripts`:
  - entryorguid → bigint. + Difficulties.
  - event_param6 removed. + event_param_string, action_param7, action_param_string, target_param_string.
  - Max IDs: AC event 110 / action 242 / target 207; TC master 90 / 159 / 30. Slots TC marks "UNUSED, DO NOT REUSE" (events 12, 14, 18, 30, 39, 66, 67; many actions) fail its loader. The IDs also mean different things (46 is AC `AREATRIGGER_ONTRIGGER`, TC `AREATRIGGER_ENTER`; 40 is AC `ESCORT_REACHED`, TC `WAYPOINT_REACHED`), so the catalogs are separate.
  - Event flags: TC drops the per-difficulty bits (0x2-0x10, cleared with a warning; `Difficulties` lists them) and adds 0x20 "action list waits". Phase mask is 12 bits in both.
  - String parameters: TC `event_param_string` (scene trigger name), `action_param_string`, `target_param_string` (a StringId that narrows creature and object target searches).
  - TC source_types used: 0,1,2,3,5,9,10,11,12. Type 2 is keyed by AreaTrigger.db2 ID (run by `areatrigger_scripts` SmartTrigger, events enter 46, exit 88 and link); 11 and 12 by `areatrigger_template` (IsCustom 0 or 1), run by a spawn's `areatrigger.ScriptName` SmartAreaTriggerAI (else its create properties' `ScriptName`), which also accept the timed updates (1, 60). AC event 46 names the trigger in param1; TC's has no parameter.
  - `creature_text` + SoundPlayType (0 normal, 1 from the creature); Language is unsigned in TC. `BroadcastTextId` links `hotfixes.broadcast_text` (client BroadcastText.db2), not a world table.
- `conditions`: + ConditionStringValue1. Source types ≥25 diverge. TC uses 25-36: 26 phase, 27 graveyard, 28/30 areatrigger, 29 conversation, 31 trainer spell, 32 object visibility, 33 spawn group…. AC uses 28-30 with different meanings.
- `points_of_interest`: + PositionZ, WMOGroupID, VerifiedBuild. `page_text`: + PlayerConditionID, Flags.
- Waypoints: AC `waypoint_data(id,point,position_*,orientation,velocity,delay,smoothTransition,move_type,action,action_chance,wpguid)`. TC `waypoint_path(PathId,MoveType,Flags,Velocity,Comment)` plus `waypoint_path_node(PathId,NodeId,PositionX/Y/Z,Orientation,Delay)`. TC has no per-node velocity, move type, smooth transition or action: speed and walk/run belong to the whole path (shared by every creature using it). Creatures name the path in `creature_addon.PathId` / `creature_template_addon.PathId` (AC `path_id`). The editor reads each node with its path's values and edits only position, facing and delay.
- Pools: AC `pool_creature/pool_gameobject(guid,pool_entry,chance,description)`. TC `pool_members(type,spawnId,poolSpawnId,chance,description)`.
- Loot tables (all `*_loot_template`): Reference → ItemType.
- Other non-editor tables:
  - `achievement_dbc` (rewritten), `achievement_reward_locale` Text→Body.
  - `battleground_template` (AC adds team sizes and levels), `command`.security.
  - `instance_template`.allowMount, `lfg_dungeon_template` + requiredItemLevel.
  - `pet_levelstats` min/max_dmg, `player_factionchange_*` comments, `playercreateinfo` (zone → npe_* columns), `playercreateinfo_cast_spell` + createMode, `playercreateinfo_item`.Note.
  - `reputation_spillover_template` faction6, `spell_area` autocast→flags, `spell_custom_attr` spell_id→entry, `spell_enchant_proc_data` (renamed columns), `spell_group_stack_rules`.description, `spell_proc` + SpellFamilyMask3/ProcFlags2, `spell_target_position` + OrderIndex.
  - `transports` + phase columns, `vehicle_(template_)accessory` + RideSpellID.

- `conditions`: the same 15 columns in both, plus TC's `ConditionStringValue1` (varchar 64) before `NegativeCondition`; AC's `ScriptName` is char(64), TC's varchar(64). `TcTables.Conditions` and `AcTables.Conditions` carry them; only gossip menu sources (14 text, 15 option) are read and copied before Phase 12.

### TC gossip editing rules

The [master option schema](https://trinitycore.info/database/master/world/gossip_menu_option) uses `OptionNpc` (0-based service enum) rather than AC `OptionType`/`OptionNpcFlag`, and has separate `OptionBroadcastTextID` and `BoxBroadcastTextID` overrides. Literal edits clear the corresponding override. The editors draw an option's client icon from `OptionNpc` 1-9 (the same numbers as the 3.3.5 gossip icons; other services use the chat icon) and follow it by `OptionNpc` 1 vendor, 2 taxi, 3 trainer (AC: `OptionType` 3/15, 4, 5, plus 2 quest giver). A TC menu has no text of its own, so searching menus by text goes through broadcast text: `broadcast_text` matches (hotfixes, then the client) → `npc_text.BroadcastTextID0..7` → `gossip_menu`. TC `BoxMoney` is an unsigned int; the existing wide row representation is retained for compatibility, but the editor rejects costs above uint32. Confirmation code is boolean.

TC [npc_text](https://trinitycore.info/database/master/world/npc_text) holds eight broadcast IDs and weights, without AC literal greeting text. ID 0 is reserved. The [TC loader](https://github.com/TrinityCore/TrinityCore/blob/master/src/server/game/Globals/ObjectMgr.cpp) ignores positive weights without broadcasts and rows with no positive sum. The editor rejects those states and negative/nonfinite weights; it accepts positive relative totals (the wiki recommends a total of 1, the loader does not require it). IDs/weights save together, and previews resolve hotfixes before client DB2. A new broadcast text is a `hotfixes.broadcast_text` row (VerifiedBuild -1, which the server loads as its own: `WHERE (VerifiedBuild > 0) = ?`) plus a `hotfixes.hotfix_data` row (TableHash 35137211 is read from the client's BroadcastText.db2 header); the export names the tables with the connection's hotfixes database, and the new ID is above the highest in both that database and the client's table. Changing an existing text is not built. `VerifiedBuild` stays outside the gossip catalogs and is preserved on updates; new option rows use the server default.

## Retail item values

The retail `ItemSparse` table (and `hotfixes.item_sparse`, which mirrors it) stores no stat values, armor, damage or durability, and the TrinityCore world database has no `item_template`. Stat types and their percentages are stored; the numbers are computed from the DB2 gametables the way TrinityCore does (`ItemTemplate::GetArmor`/`GetDPS`, `Item::GetItemStatValue`, `Item::GetItemLevel`). Bonus lists are not applied.
