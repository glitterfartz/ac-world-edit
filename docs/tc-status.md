# Status

The plan and open work for the TrinityCore database overhaul, and the project's other known gaps. Finished work is described in the architecture docs, not here. Schema differences are in `docs/DB-DIFFERENCES.md`. Target: TrinityCore `master` `world` schema (TDB 1210.26091) plus `hotfixes`; retail client 12.1.5.70575. Delete the TrinityCore plan sections when Phase 9 lands.

Phases 1–5 (interface extraction, connection and hotfixes, retail catalogs, read-only overlays and spawn moves, graveyard editing) are done. Phase 6 (creature properties) is built for TrinityCore: the template, stats, vendor, gossip, trainer and quest-link pages edit and export, with reference checks against the client and database. What remains of it is listed below as refinements, none of which blocks Phase 7. Phases 7–9 (quests, SmartAI and speech, waypoints) are not started; their editors are off in TrinityCore mode (`TerrainViewer.ContentEditors`, waypoint layer).

## Decisions and design

- The dialect follows the client kind: Wrath335 uses AzerothCore and Retail uses TrinityCore. There is no autodetect, so a schema mismatch fails with a dialect-specific error.
- Hotfixes are read from a second database name on the same connection (default `hotfixes`), falling back to client DB2.
- Each phase delivers full read and edit/export parity for its area, with its own commit(s), tests, and a visual check where the UI changes.
- **Dialect-native rows.** Read-only overlay results are shared records translated by each dialect. Editing stays on the generic path, `RowEdit(Table, Key, Before, After)` → `SqlExport`, with each dialect's own table catalog, so structural differences (`quest_objectives` 1:N rows, `creature_template_difficulty`, `creature_template_gossip`) round-trip without a lossy neutral model. Editors switch descriptors (field groups, enums, catalogs), not data models.
- **`IWorldSchema` grows per phase.** It has `Tables` and `Sql`, plus the creature descriptors. Phases 7–8 add `QuestSchema` and `SmartCatalog Smart`. The AzerothCore statics (`InteractionTables`, `SmartCatalog`, `QuestModel`, `NpcFlags`) move behind `AcSchema` as each area is touched.
- **Creature pages are descriptors, not per-dialect classes.** A page is an ordered list of groups; a group is fields bound to one row or an item list (source table, parent filter column, per-item fields, nested lists, add/remove). Each dialect fills the descriptor in; one renderer draws it. Dialect-specific section adapters use `Ac` / `Tc` prefixes in `AzerothCore/` / `TrinityCore/` directories.
- **Editors read the dialect only through `IDBSource.Schema`:** `CreatureSectionRegistry` filters sections by schema support; `AcQuestPanel.Pages` will build pages from `QuestSchema` (TC adds an Objectives page and drops `RequiredNpcOrGo`/`RequiredItem`); `AcBehaviorPanel.Rules` will use `Schema.Smart`.

Verification per phase: the default suite; Db tests with `ACWORLD_DB_INFO` (AC regression) and `ACWORLD_TC_DB_INFO` (TC); the retail corpus with `--settings corpus.runsettings` and `ACWORLD_RETAIL_DIR`; `check-layering` after any Data/Foundation/Assets edit. Visually: `--client-shot-overlays` (retail, Goldshire); open the creature, quest and behavior editors in both modes and confirm they show the dialect's fields; export SQL from the pending changes window and run it against a scratch copy of `world`.

Status values: **partial**, **not started**, **deviates** (built differently from the plan).

## Coexisting forms

| Legacy form | Target | Where the legacy form remains |
|---|---|---|
| Static AC catalogs `InteractionTables`, `SmartCatalog`, `QuestModel`, `NpcFlags` | `IDBSource.Schema` / `IWorldSchema` (descriptors added per phase) | `Foundation/` (`NpcInteraction`, `QuestModel`, `SmartBehavior`, `SmartRuleEdits`, `SpeechEdits`), `Scripts/Data/AzerothCore/`, `Scripts/Ui/` (quest and behavior panels; the AC creature-property sections only use `NpcFlags.Gossip` and `NpcFlags.FlightMaster`, which have no descriptor yet), content smokes. Converted as Phases 6–8 reach each area. |
| `Ac*Source(DatabaseSettings)` constructors | `DBSource.Open(WorldConnection, IClientLookup?)` | Used only by the AC Db tests (`Tests/ACWorldEdit.Tests/Db/`). Switch those tests to `DBSource.Open`, then delete the constructors. |
| Pre-profile `[database]` settings section | `[database.wrath]` / `[database.retail]` | `SettingsResolver.Connection` falls back to it for AzerothCore. Remove once saved settings have moved to the per-profile sections. |
| `CreatureContext.View` and `TcView` | one shared creature read model | The two read models (`NpcInteraction`, `TcCreatureView`) sit side by side; sections use the one for their dialect. |
| AC `SearchPicker` vs TC `TcSection.SearchBox` | one search control | AC's has no length cap or stale-result guard and different status text. |
| AC-only `AcBehaviorPanel` / `AcQuestPanel` | shared panels | Phases 7–8 decide whether they become shared. |
| Export treats a source with no `SourceDialects` entry as AzerothCore | | Unknown sources are AC. |

## Open work

### Phase 6 refinements

| Item | Status | Notes |
|---|---|---|
| Schema-driven section registry and field groups | not started | A refactor, not a feature: sections do not yet take their field groups from the schema. |
| Gossip | partial | Copying a menu does not copy the `conditions` or SmartAI rows that name it. `OverrideIconID` (a FileDataID) is not checked. Removing an option keeps trainer bindings and other external references. |
| Stats calculator | partial | Content tuning 0 (the server then uses the map's or area's tuning), Mythic+ modifiers (a modifier limited to start in a season is skipped, one limited to end in a season is kept), `Rate.Creature.*` taken as 1, conditional content tuning redirects, attack power / speed in the damage line. |
| AC gaps | partial | Gossip `BoxBroadcastTextID`, trainer `ReqAbility1-3` and trainer/quest-link build metadata are outside the catalogs. |
| Headless smokes | partial | `--interaction-smoke` passes windowed but, headless, the simulated right-click does not open the window; `--behavior-smoke` times out at `pick-template` headless (passes windowed). |

### Phases 7–9

| Item | Status | Notes |
|---|---|---|
| Quests: `TcQuestSchema`, `quest_objectives` page, ObjectID lookups, `QuestModel.Check` per dialect | not started | Phase 7. Editor gated off in TC. |
| SmartAI: `SmartCatalog.Trinity`, TC `smart_scripts` columns, `creature_text.SoundPlayType` | not started | Phase 8. Editor gated off in TC. |
| Speech via BroadcastText; hotfix export with `hotfix_data` | not started | Phase 8. |
| Waypoints: `TcWaypointSource`, per-dialect `WaypointEdit`, `waypoint_path_node` export | not started | Phase 9. Layer disabled in TC. |

### Phase plans

Phase 6 is built; its refinements are in the table above. Phases 7–9 are not started:

**Phase 7: quests**
- `TcQuestSchema`: `quest_template` TC columns (ContentTuningID, RewardMoneyDifficulty/Multiplier, FlagsEx/Ex2/Ex3, RewardDisplaySpell1-3, RewardCurrency*, Portrait*, Expansion, ...). QuestLevel, MinLevel, RewardMoney, RequiredFaction*, RequiredNpcOrGo*, RequiredItem*, ObjectiveText* are gone.
- Objectives become a 1:N `quest_objectives` page (Type, Order, StorageIndex, ObjectID, Amount, Flags, Description; ID allocation via MAX(ID)+1). ObjectID name lookups depend on Type (creature, GO, item, currency, ...).
- `quest_template_addon` gains ScriptName; `quest_request_items` gains the emote delays.
- `QuestModel.Check` per dialect, with TC defaults for `NewQuest`. Races and classes come from the retail lists (ChrRaces/ChrClasses DB2 or a static table).
- The conditional text tables (`quest_*_conditional`) are read-only in this phase.
- Tests: TcDb read of a known quest with objectives; draft/SQL round-trip tests for objectives.

**Phase 8: SmartAI and speech**
- Generate `SmartCatalog.Trinity` from TrinityCore master `SmartScriptMgr.h`, the same way as the AC catalog. ID ranges differ (TC events ≤90, actions ≤153, targets ≤29; AC 110/242/207), so nothing is reused by ID.
- `smart_scripts` TC columns: `entryorguid` bigint, `Difficulties`, no `event_param6`, adds `action_param7` and `event/action/target_param_string`. Source types 0, 1, 2, 3, 5, 9, 10, 11, 12 occur; scope 2 is keyed by AreaTrigger.db2 ID.
- `creature_text` gains `SoundPlayType`. Speech text and search use BroadcastText (`hotfixes.broadcast_text`, then client DB2).
- Editing broadcast text is a hotfixes export: a schema-qualified `hotfixes.broadcast_text` row plus a `hotfix_data` row (TableHash, RecordId, Status). STALE checks cover both databases.
- Tests: `SmartCatalogTests` for the TC catalog, `SmartRuleEditsTests` TC columns, a TcDb Goldshire SmartAI creature.

**Phase 9: waypoints**
- `TcWaypointSource` reads `waypoint_path(PathId, MoveType, Flags, Velocity, Comment)` and `waypoint_path_node(PathId, NodeId, PositionX/Y/Z, Orientation, Delay)`. Creatures reference paths through `creature_addon`/`creature_template_addon.PathId`.
- TC has no per-node Action/ActionChance/Smooth and moves MoveType/Velocity to the path, so `WaypointEdit` needs a per-dialect view of which fields are editable.
- `TcSqlDialect.Waypoint` writes `waypoint_path_node` position edits. Re-enable the waypoint layer in TC mode.
- Tests: TcDb read of a known path, `SqlExportTests` TC waypoint cases, `--waypoint-smoke` against TC.

### Foundation, data and overlays

| Item | Status | Notes |
|---|---|---|
| `IWorldSchema` | partial | `QuestSchema` and `Smart` come with Phases 7–8. |
| `SmartCatalog`, `QuestModel`, `NpcFlags` behind `AcSchema` | deviates | Static AC data; moved per area in Phases 6–8. |
| `Spatial`/`Restrictions` per dialect | deviates | TC has its own copies in `TcSpawnSource`; AC stays on the shared `WorldReadSession`. |
| Teleports from `game_tele` | deviates | Reuses `AcTeleportSource` (same schema); no `TcTeleportSource`. |
| Graveyard healer lookup | deviates | TC reuses `AcGraveyardSource.ReadHealers`. |
| Guids widened to `ulong` | partial | Spawn search still binds the guid as `uint` (harmless, max ≈ 11.8M). |
| `ColumnKind` 64-bit kinds | partial | Validated and exported; no table uses them yet. |
| Hotfix rows with several `VerifiedBuild` versions | partial | Broadcast text takes the highest build; display and area-trigger reads take whichever row comes last. |
| WoWDBDefs FileDataID corpus check | not started | Needs client data. |
| Other hotfix overrides (`gameobject_display_info`, `spell_name`, `content_tuning`) | not started | `RetailGameObjectCatalog.Override` exists but is never called. Vendor item names and quality already read `item_sparse` (and gossip text `broadcast_text`). |
| Retail item tooltips | not started | The TC vendor page shows name, quality and icon only; a tooltip needs the ItemSparse stat, armor and damage columns. |
| `creature_display_info_extra` | not started | Not read (empty in TDB); one of four texture columns of `creature_display_info` unread. |
| Retail area names (`AreaLookup`) | not started | Null for retail. |
| `AreaTriggerActionSet`, `ContentTuning` tables | not started | Registered, not read. |
| Difficulty / phase preview filter | partial | Phase groups ignored (a spawn with only a `PhaseGroup` is hidden); `phaseUseFlags` read, not used; difficulty list hardcoded. |
| Area triggers from `AreaTrigger.db2` + `hotfixes.area_trigger` | partial | Polygon/cylinder shapes skipped; trigger phases not filtered by the previewed phase. |
| Server-spawned `areatrigger` overlay | not started | |
| Graveyard: warn when the safe location is a teleport target | partial | Counts `areatrigger_teleport` only; battleground start points not checked. |
| Graveyard: locked-link controls and teleport line | unverified | Not inspected visually (e.g. Alterac Valley graveyard 169). |
| Drafts kept across a client switch | not started | Switching client discards all drafts after a confirmation, although source keys already keep them apart. |

### Tests

| Item | Status | Notes |
|---|---|---|
| `Db2Tables` thread safety | partial | Retail test classes still run one at a time (`[Collection(RetailData.Collection)]`); drop that once the retail corpus tests pass in parallel. |
| `RetailCorpusTests.EveryMapWdtParsesAndOneTilePerTerrainMapDecodes` accepted failures | not started | Stale "until the retail WMO milestone" comments; tighten or remove the filter. |

### Retail creatures and rendering

| Item | Status | Notes |
|---|---|---|
| Equipment (`NPCModelItemSlotDisplayInfo` → `ItemDisplayInfo`) | not started | |
| Customization beyond geosets and hair/eye | not started | Bone sets, skinned models, particle colours. |
| Unchosen geoset groups | deviates | Variant 1, except ears (7) and head (32) use variant 2; rule from data, not a documented client rule. |
| Humanoid whose extra has no bake material | partial | Drawn without customization (every geoset shows). |
| Parent skeletons (`.skel` `SKPD`) | not started | |
| Animation names (AnimationData DB2) | not started | Preview menu shows IDs only. |
| M2 multitexture batches and spline tracks | partial | Base texture only; splines interpolated linearly. |
| Height texturing (MHID/MTXP), MAMP scale | not started | |
| Terrain layers 5–8 | not started | Only the first 4 are drawn. |
| Uncompressed alpha size | partial | Follows the WDT big-alpha flag; decide per layer if a map disagrees. |
| Path-named placements | partial | Resolve only through the community listfile. |
