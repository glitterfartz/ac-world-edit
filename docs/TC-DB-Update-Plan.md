# TrinityCore master world DB as the retail editing backend

The plan for the remaining TrinityCore work. Status, deviations and gaps are in `docs/tc-status.md`; schema differences in `docs/DB-DIFFERENCES.md`. Phases 1–5 (interface extraction, connection and hotfixes, retail client catalogs, read-only overlays and spawn moves, graveyard editing) are done and described in the architecture docs. Delete this file when Phase 9 lands.

Target: TrinityCore `master` `world` schema (TDB 1210.26091) plus `hotfixes`.

**Decisions (from the user)**
- The dialect follows the client kind: Wrath335 uses AzerothCore and Retail uses TrinityCore. There is no autodetect, so a schema mismatch fails with a dialect-specific error.
- Hotfixes are read from a second database name on the same connection (default `hotfixes`), falling back to client DB2.
- Each phase delivers full read and edit/export parity for its area.

## Design

- **Dialect-native rows.** Read-only overlay results are shared records translated by each dialect. Editing stays on the generic path, `RowEdit(Table, Key, Before, After)` → `SqlExport`, with each dialect's own table catalog, so structural differences (`quest_objectives` 1:N rows, `creature_template_difficulty`, `creature_template_gossip`) round-trip without a lossy neutral model. Editors switch descriptors (field groups, enums, catalogs), not data models.
- **`IWorldSchema` grows per phase.** Today it has `Tables` and `Sql`. Phases 6–8 add `NpcSchema` (npc flags, gossip option kinds), `CreatureSchema` (ranks/classification, flag tables), `QuestSchema` and `SmartCatalog Smart`. The AzerothCore statics (`InteractionTables`, `SmartCatalog`, `QuestModel`, `NpcFlags`) move behind `AcSchema` as each area is touched.
- **Creature pages are descriptors, not per-dialect classes.** A page is an ordered list of groups; a group is fields bound to one row (template or selected difficulty) or an item list (source table, parent filter column, per-item fields, nested lists, add/remove). Each dialect fills the descriptor in (tables, fields, enums, flag lists); one renderer draws it. Custom slots cover what is not declarative (subname, gossip-menu check, spell clicks, validation fixes). Template, vendor, gossip, trainer and creature quest-link descriptors are built; TC Template, vendor, gossip and trainer editing are enabled; quest-link editing still needs enabling, validation and search. Dialect-specific section adapters use `Ac` / `Tc` prefixes in `AzerothCore/` / `TrinityCore/` directories; shared descriptors and renderers stay common.
- **Editors read the dialect only through `IDBSource.Schema`:**
  - `CreatureSectionRegistry` filters sections by schema support.
  - `AcTemplateSection`, `AcStatsSection`, `AcGossipSection`, `AcTrainerSection` and `AcVendorSection` take their field groups and enums from the schema.
  - `AcQuestPanel.Pages` builds pages from `QuestSchema`. TC adds an Objectives page and drops `RequiredNpcOrGo`/`RequiredItem`.
  - `AcBehaviorPanel.Rules` uses `Schema.Smart`.

## Phases (each phase = its own commit(s), tests, and a visual check where the UI changes)

**Phase 6: creature properties (TC)** (read side done; see `tc-status.md`)
- `TcTables` catalog: `creature_template` (TC columns); `creature_template_difficulty` (levels via ContentTuningID, Health/Mana/Armor/DamageModifier, loot IDs, gold, TypeFlags, StaticFlags; needs a difficulty choice, not only `DifficultyID=0`); `creature_template_gossip` (1:N menus); `creature_trainer` (bound to a gossip option); `gossip_menu_option` (GossipOptionID, OptionNpc, Language, Flags, GossipNpcOptionID, SpellID, OverrideIconID); `npc_text` (BroadcastTextID0-7, Probability0-7); `npc_vendor` (+type in the key, BonusListIDs, PlayerConditionID, IgnoreFiltering); quest starter/ender links; `npc_spellclick_spells`.
- TC `NpcSchema`/`CreatureSchema`: 64-bit `npcflag`, Classification, `unit_flags3`, the TC OptionNpc enum. The TC stats page uses ContentTuning instead of minlevel/maxlevel; `creature_classlevelstats` has only basemana/attackpower/rangedattackpower.
- Vendor search uses client ItemSparse through `IClientLookup` (implemented). Detailed tooltips and `hotfixes.item_sparse` overrides remain to be added. Shared-list expansion also remains; validation covers item, currency, extended cost, bonus list and player condition existence, fields and stock rules.
- Tests: TcDb Goldshire innkeeper, vendor and trainer; `TcTables` catalog unit tests.

**Phase 7: quests (TC)**
- `TcQuestSchema`: quest_template TC columns (ContentTuningID, RewardMoneyDifficulty/Multiplier, FlagsEx/Ex2/Ex3, RewardDisplaySpell1-3, RewardCurrency*, Portrait*, Expansion, ...). QuestLevel, MinLevel, RewardMoney, RequiredFaction*, RequiredNpcOrGo*, RequiredItem*, ObjectiveText* are gone.
- Objectives become a 1:N `quest_objectives` page (Type, Order, StorageIndex, ObjectID, Amount, Flags, Description; ID allocation via MAX(ID)+1). ObjectID name lookups depend on Type (creature, GO, item, currency, ...).
- `quest_template_addon` gains ScriptName; `quest_request_items` gains the emote delays.
- `QuestModel.Check` per dialect, with TC defaults for `NewQuest`. Races and classes come from the retail lists (ChrRaces/ChrClasses DB2 or a static table).
- The conditional text tables (`quest_*_conditional`) are read-only in this phase.
- Tests: TcDb read of a known quest with objectives; draft/SQL round-trip tests for objectives.

**Phase 8: SmartAI and speech (TC)**
- Generate `SmartCatalog.Trinity` from TrinityCore master `SmartScriptMgr.h`, the same way as the AC catalog. ID ranges differ (TC events ≤90, actions ≤153, targets ≤29; AC 110/242/207), so nothing is reused by ID.
- `smart_scripts` TC columns: `entryorguid` bigint, `Difficulties`, no `event_param6`, adds `action_param7` and `event/action/target_param_string`. Source types 0, 1, 2, 3, 5, 9, 10, 11, 12 occur; scope 2 is keyed by AreaTrigger.db2 ID.
- `creature_text` gains `SoundPlayType`. Speech text and search use BroadcastText (`hotfixes.broadcast_text`, then client DB2).
- Editing broadcast text is a hotfixes export: a schema-qualified `hotfixes.broadcast_text` row plus a `hotfix_data` row (TableHash, RecordId, Status). STALE checks cover both databases.
- Tests: `SmartCatalogTests` for the TC catalog, `SmartRuleEditsTests` TC columns, a TcDb Goldshire SmartAI creature.

**Phase 9: waypoints (TC)**
- `TcWaypointSource` reads `waypoint_path(PathId, MoveType, Flags, Velocity, Comment)` and `waypoint_path_node(PathId, NodeId, PositionX/Y/Z, Orientation, Delay)`. Creatures reference paths through `creature_addon`/`creature_template_addon.PathId`.
- TC has no per-node Action/ActionChance/Smooth and moves MoveType/Velocity to the path, so `WaypointEdit` needs a per-dialect view of which fields are editable.
- `TcSqlDialect.Waypoint` writes `waypoint_path_node` position edits. Re-enable the waypoint layer in TC mode.
- Tests: TcDb read of a known path, `SqlExportTests` TC waypoint cases, `--waypoint-smoke` against TC.

## Verification (per phase)
- Default suite; Db tests with `ACWORLD_DB_INFO` (AC regression) and `ACWORLD_TC_DB_INFO` (TC). Retail corpus with `--settings corpus.runsettings` and `ACWORLD_RETAIL_DIR`.
- `check-layering` after any Data/Foundation/Assets edit.
- Visual: `--client-shot-overlays` (retail, Goldshire); open the creature, quest and behavior editors in both modes and confirm they show the dialect's fields. Export SQL from the pending changes window and run it against a scratch copy of `world`.
