# Status

The plan and open work for the TrinityCore database overhaul, and the project's other known gaps. Finished work is described in the architecture docs, not here. Schema differences are in `docs/DB-DIFFERENCES.md`. Target: TrinityCore `master` `world` schema (TDB 1210.26091) plus `hotfixes`; retail client 12.1.5.70575.

Phases 1–5 (interface extraction, connection and hotfixes, retail catalogs, read-only overlays and spawn moves, graveyard editing) are done. Phase 6 (creature properties) is built for TrinityCore: the template, stats, vendor, gossip, trainer and quest-link pages edit and export, with reference checks against the client and database. What remains of it is listed below as refinements, none of which blocks Phase 7. Phase 7 (quests) is built for TrinityCore: the quest editor edits and exports the five quest tables, with refinements listed below. Phase 8 (SmartAI and speech) is built for TrinityCore: the shared behavior panel edits a creature's `smart_scripts` rules, timed action lists, AIName and `creature_text` lines and exports them, with refinements listed below. Phase 9 (waypoints) is built for TrinityCore: the waypoint layer reads `waypoint_path` and `waypoint_path_node`, and a node's position, facing and delay edit and export, with refinements listed below. All nine phases are built; AreaTrigger rules are the one editor still off in TrinityCore mode (`TerrainViewer.AreaTriggerBehavior`).

## Decisions and design

- The dialect follows the client kind: Wrath335 uses AzerothCore and Retail uses TrinityCore. There is no autodetect, so a schema mismatch fails with a dialect-specific error.
- Hotfixes are read from a second database name on the same connection (default `hotfixes`), falling back to client DB2.
- Each phase delivers full read and edit/export parity for its area, with its own commit(s), tests, and a visual check where the UI changes.
- **Dialect-native rows.** Read-only overlay results are shared records translated by each dialect. Editing stays on the generic path, `RowEdit(Table, Key, Before, After)` → `SqlExport`, with each dialect's own table catalog, so structural differences (`quest_objectives` 1:N rows, `creature_template_difficulty`, `creature_template_gossip`) round-trip without a lossy neutral model. Editors switch descriptors (field groups, enums, catalogs), not data models.
- **`IWorldSchema` grows per phase.** It has `Tables` and `Sql`, plus the creature descriptors. `Smart` is the dialect's `SmartCatalog`; the quest pages are drawn by `AcQuestPanel` / `TcQuestPanel` over `AcTables` / `TcTables` instead of a schema descriptor. The AzerothCore statics (`InteractionTables`, `QuestModel`, `NpcFlags`) move behind `AcSchema` as each area is touched.
- **Creature pages are descriptors, not per-dialect classes.** A page is an ordered list of groups; a group is fields bound to one row or an item list (source table, parent filter column, per-item fields, nested lists, add/remove). Each dialect fills the descriptor in; one renderer draws it. Dialect-specific section adapters use `Ac` / `Tc` prefixes in `AzerothCore/` / `TrinityCore/` directories.
- **Editors read the dialect only through `IDBSource.Schema`:** `CreatureSectionRegistry` filters sections by schema support; `QuestPanel` is shared and `AcQuestPanel` / `TcQuestPanel` draw the pages that differ (TC replaces `RequiredNpcOrGo`/`RequiredItem` with an Objectives list); `BehaviorPanel` (and its creature `BehaviorSection` / `SpeechSection`) is shared and takes `SmartCatalog` from the open connection's dialect.

Verification per phase: the default suite; Db tests with `ACWORLD_DB_INFO` (AC regression) and `ACWORLD_TC_DB_INFO` (TC); the retail corpus with `--settings corpus.runsettings` and `ACWORLD_RETAIL_DIR`; `check-layering` after any Data/Foundation/Assets edit. Visually: `--client-shot-overlays` (retail, Goldshire); open the creature, quest and behavior editors in both modes and confirm they show the dialect's fields; export SQL from the pending changes window and run it against a scratch copy of `world`.

Status values: **partial**, **not started**, **deviates** (built differently from the plan).

## Coexisting forms

| Legacy form | Target | Where the legacy form remains |
|---|---|---|
| Static AC catalogs `InteractionTables`, `QuestModel`, `NpcFlags` | `IDBSource.Schema` / `IWorldSchema` (descriptors added per phase) | `Foundation/` (`NpcInteraction`, `QuestModel`), `Scripts/Data/AzerothCore/`, `Scripts/Ui/AzerothCore/` (the AC quest panel; the AC creature-property sections only use `NpcFlags.Gossip` and `NpcFlags.FlightMaster`, which have no descriptor yet), content smokes. Converted as each area is touched. |
| `InteractionTables.SmartScripts` / `CreatureText` in the SmartAI row helpers | `SmartCatalog.Scripts` / `CreatureText` | `SmartRule.Key`, `SmartRuleEdits.RulesOf`, `SpeechEdits` and `ContentNames.FillTexts` still name AzerothCore's descriptors. Both dialects share the table names and key columns, so rows resolve by name; switch them when the AC statics go. |
| `Ac*Source(DatabaseSettings)` constructors | `DBSource.Open(WorldConnection, IClientLookup?)` | Used only by the AC Db tests (`Tests/ACWorldEdit.Tests/Db/`). Switch those tests to `DBSource.Open`, then delete the constructors. |
| Pre-profile `[database]` settings section | `[database.wrath]` / `[database.retail]` | `SettingsResolver.Connection` falls back to it for AzerothCore. Remove once saved settings have moved to the per-profile sections. |
| `CreatureContext.View` and `TcView` | one shared creature read model | The two read models (`NpcInteraction`, `TcCreatureView`) sit side by side; sections use the one for their dialect. |
| AC `SearchPicker` vs TC `TcSection.SearchBox` | one search control | AC's has no length cap or stale-result guard and different status text. |
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

### Phase 7 refinements

| Item | Status | Notes |
|---|---|---|
| Quest tables outside the editor | not started | `quest_*_conditional`, `quest_objectives_completion_effect`, `quest_reward_choice_items`, `quest_reward_display_spell`, `quest_poi`, locale tables and the `conditions` rows of source types 19 and 20 are neither read nor edited. |
| Objective fields | partial | The panel edits type, amount, target, text and flags; `Flags2`, `Visible`, `ConditionalAmount`, `ProgressBarWeight` and `ParentObjectiveID` are whitelisted but have no control. Targets of types without a search (currencies, factions, area triggers, criteria trees, pet battles) are typed IDs. |
| Reference checks | partial | Creature, object, item, spell, currency, follow-up and chain quests, and the zone (when `AreaLookup` exists) are checked. Faction, emote, area trigger, criteria tree, `ContentTuningID`, `QuestInfoID`, `QuestSortID` for retail and the skill and title rewards are not. |
| Names on the quest pages | partial | Factions, emotes, currencies and content tuning show IDs; the retail zone name needs `AreaLookup` (not built for retail). |
| Race picker | partial | Everyone, Alliance or Horde, or a typed 64-bit mask; no per-race checkboxes (ChrRaces names are not read). |
| Quest givers | partial | Creatures only, as in AzerothCore; object and item starters are not edited. |
| Content-smoke equivalent for TC | not started | The TC quest editor is covered by the client smoke (an objective add and undo, validation, rename, screenshots) and unit tests, not by a copy of `--content-smoke`. |

### Phase 8 refinements

| Item | Status | Notes |
|---|---|---|
| AreaTrigger rules for TC | not started | `source_type` 2 is keyed by AreaTrigger.db2 ID, and server-side triggers run through `areatrigger_template.ScriptName` (source type 12). `TcDBSource.ReadAreaTriggerBehaviorAsync` throws and the AreaTrigger editor stays off. |
| Other script source types | not started | TC also uses types 1 (object), 3, 5, 10, 11 and 12; only creature rules (0) and the timed action lists (9) they run are read. |
| Loader rules | partial | `SmartCatalog.Check` mirrors the shared checks (unknown types, ranges, links, named references, SmartAI on the creature) plus TC's deprecated difficulty flags. TC's per-type validation (`IsEventValid`, `CheckUnusedEventParams` / `ActionParams` / `TargetParams`: unused parameters must be 0, ranges, existing spells and creatures) is not reproduced, so the server may reject a row the editor accepts. |
| Type curation | partial | Of the selectable TC types 49 of 83 events, 57 of 139 actions and 19 of 31 targets have a written sentence; the rest show their name and the parameter labels read from the server comment, and 5 actions and 1 target show raw numbered fields. |
| Names for parameters | partial | Spells and items are named from the retail client; creatures, objects and quests from the world database. Emotes, text emotes, faction templates, game events, sound kits and the scene or conversation IDs show numbers (no TC catalog behind the pickers). |
| `Difficulties` | partial | A free text field of difficulty IDs; the values are not checked against Difficulty.db2. |
| Speech | partial | Lines link an existing broadcast text (hotfixes first, then the client) and edit `Text`, type, language, range, chance, gesture, sound and `SoundPlayType`. Editing a broadcast text itself, which is a `hotfixes.broadcast_text` row plus a `hotfix_data` row with stale checks against both databases, is not started. |
| Smokes | partial | The TC client smoke (stages 70–72) opens Hogger's behavior and speech, adds a rule, checks validation and `Difficulties`, and undoes. `--behavior-smoke` and `--content-smoke` still run against AzerothCore only; there is no TC copy of the action-list and Then-chain flows beyond unit tests. |

### Phase 9 refinements

| Item | Status | Notes |
|---|---|---|
| Path-level properties | not started | TrinityCore keeps `MoveType`, `Velocity` and `Flags` on `waypoint_path`, shared by every creature on the path. The card shows move type and velocity read-only as "Path move type" and "Path velocity", and the export refuses a node edit that changes them; editing them needs a path-level control that warns how many creatures share the path. `Flags` and `Comment` are not read. |
| Add, delete and reorder nodes | not started | As in AzerothCore, only existing nodes move or change; there is no way to insert, remove or renumber a node or create a path. |
| Waypoints from other users | not started | Paths are found through `creature_addon` / `creature_template_addon.PathId` only. SmartAI `WAYPOINT_START` paths and `script_waypoint`-style users are not followed. |
| Smokes | partial | The TC client smoke (stages 80–82) selects a Goldshire path point, checks which fields are editable and runs the move, delay, undo, redo and revert workflow with the TC export. `--waypoint-smoke` still has no TC run of its own. |

### Foundation, data and overlays

| Item | Status | Notes |
|---|---|---|
| `QuestModel`, `NpcFlags` behind `AcSchema` | deviates | Static AC data; moved per area as it is touched. |
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
