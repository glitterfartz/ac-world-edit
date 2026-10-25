# AzerothCore vs TrinityCore world database differences

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
