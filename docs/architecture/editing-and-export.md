# Editing, data access and SQL export

Covers `Scripts/Data/` (read-only database access) and `Foundation/` (the draft model, SQL generation and rule checks). Foundation has no Godot or MySQL references, and the tests compile it directly.

The guarantee this part of the system exists to keep: **the app never writes to the database.** Reads run inside read-only transactions that are never committed. Edits are kept in local drafts. The only output that changes the server is SQL that a person reviews and runs.

## Read path: `Scripts/Data`

```mermaid
flowchart LR
    settings["DatabaseSettings<br/>SettingsResolver: env over saved"] --> sess["WorldDatabase.OpenAsync<br/>RepeatableRead, read-only transaction"]
    sess --> ws["WorldReadSession<br/>schema probe, Restrictions, parameter binding"]
    ws --> src["*Source classes"]
    src --> recs["Foundation records<br/>CreatureSpawn, GraveyardRecord, NpcInteraction, ..."]
    recs --> ov["Overlays and editors"]
```

- `WorldDatabase` (`Scripts/Data/WorldDatabase.cs`) holds the connection string in memory. `SourceKey` is a SHA-256 of `host:port/database` (plus `#TrinityCore` for TrinityCore, so AzerothCore keys are unchanged); drafts are tied to it.
- `WorldReadSession` reads `information_schema.COLUMNS` for the dialect's known tables (`WorldReadSession.SchemaTables`), then `RequireDialect` rejects a database of the other dialect. A missing column raises `SpawnSchemaException`, so a query fails rather than guessing.
- User values are always bound as parameters. Table and column names come from code. LIKE wildcards in search text are escaped and terms are capped at 80 characters.
- `Restrictions` builds EXISTS filters over event, pool and conditions tables so spawn queries can respect the preview policy.
- `DatabaseSettings` (`Scripts/Data/DatabaseSettings.cs`) parses `host;port;user;password;database`. The connection string sets short timeouts, disables `LOAD DATA LOCAL` and does not pool. `ToString` omits the password.
- `SettingsResolver` (`Scripts/Data/SettingsResolver.cs`) lets environment variables (`ACWORLD_DB_INFO`, `ACWORLD_TC_DB_INFO`, `ACWORLD_DATA_DIR`, `ACWORLD_RETAIL_DIR`, `ACWORLD_CLIENT_PROFILE`) override saved settings. An invalid `ACWORLD_DB_INFO` falls back to saved values silently.

### Dialects: `IDBSource`

Reads go through `IDBSource` (`Scripts/Data/IDBSource.cs`), opened with `DBSource.Open`, which picks the dialect and takes the client's `IClientLookup`. AzerothCore lives in `Scripts/Data/AzerothCore/` (`Ac*Source`), TrinityCore in `Scripts/Data/TrinityCore/` (`Tc*Source`). TrinityCore coverage and gaps are tracked in [`docs/tc-status.md`](../tc-status.md); schema differences are in [`docs/DB-DIFFERENCES.md`](../DB-DIFFERENCES.md).

Callers outside `Scripts/Data` use `DBSource.Open` and `IDBSource.Schema`, not the source classes. The Db tests do the same (`Test.AcSource` wraps `DBSource.Open`).

### Source classes and the tables they read

AzerothCore (`Scripts/Data/AzerothCore/`):

| Source | Tables read |
|---|---|
| `AcCreatureSource` | `creature` joined to `creature_template`; `creature_template_model` if present, otherwise `modelid1..4` and `scale`; event, pool and condition filters |
| `AcGameObjectSource` | `gameobject` joined to `gameobject_template`; event, pool and condition filters |
| `AcGraveyardSource` | `game_graveyard`, `graveyard_zone`; spirit healers from `creature` joined to `creature_template` (npcflag) |
| `AcTeleportSource` | `game_tele` |
| `AcWaypointSource` | `waypoint_data`; paths found through `creature_addon.path_id`, else `creature_template_addon.path_id` |
| `TcWaypointSource` | `waypoint_path_node` and `waypoint_path`; paths found through `creature_addon.PathId`, else `creature_template_addon.PathId`. Each node carries its path's move type and velocity; the path is marked `WorldDialect.TrinityCore` |
| `AcAreaTriggerSource` | `areatrigger`, `areatrigger_scripts`, `areatrigger_teleport`, `areatrigger_involvedrelation`, `areatrigger_tavern`, `quest_template`, `smart_scripts` |
| `AcInteractionSource` | `creature_template`, `creature`, `creature_queststarter`, `creature_questender`, `npc_vendor`, `creature_default_trainer`, `npc_spellclick_spells`, `gossip_menu`, `gossip_menu_option`, `npc_text`, `trainer`, `trainer_spell`, `item_template`, `quest_template`, `broadcast_text`, `points_of_interest`, `conditions`, `smart_scripts` |
| `AcContentSource` | the four quest tables, starter and ender links, `creature_template` (AIName), `smart_scripts` (source types 0, 9, 2), `creature_text`, `areatrigger_scripts`, `areatrigger`, `creature`, `gameobject_template`, `item_template` |

`AcContentSource` also provides name search (`SearchAsync`). `AcInteractionSource` uses `InteractionCatalog` (from `Scripts/Assets/Client`) to look up client-side names and icons.

TrinityCore (`Scripts/Data/TrinityCore/`, opened through `TcDBSource`; coverage in [`docs/tc-status.md`](../tc-status.md)):

| Source | Reads |
|---|---|
| `TcSpawnSource` | `creature` / `gameobject` with their templates, `creature_template_model`; `pool_members`, event and `conditions` filters |
| `TcGraveyardSource` | `world_safe_locs`, `graveyard_zone`, team rows in `conditions`, teleport overlaps from `areatrigger_teleport`; healers via `AcGraveyardSource.ReadHealers` |
| `TcAreaTriggerSource` | client `AreaTrigger.db2` (through `IClientLookup`, with polygon corners from its Path tables) joined to `areatrigger_scripts`, `areatrigger_teleport` → `world_safe_locs`, `areatrigger_involvedrelation`, `areatrigger_tavern`, `smart_scripts` (type 2); server spawns from `areatrigger` with their `areatrigger_create_properties` shape and `areatrigger_create_properties_polygon_vertex` corners, and their template's `smart_scripts` (types 11 and 12) |
| `TcInteractionSource` | the `TcTables` creature tables (template, difficulty, gossip, trainers, vendor, quest links, spell clicks); the template, difficulty (including level scaling), vendor, gossip option and greeting, trainer and quest-link rows are editable; `creature_classlevelstats` is read for the stats page; `SearchQuestsAsync` searches `quest_template` |
| `TcContentSource` | the quest snapshot: `quest_template`, `quest_template_addon`, `quest_request_items`, `quest_offer_reward`, `quest_objectives` (by `QuestID`) and the creature starter/ender links, with creature, object and quest names from the world database and item and spell names from the client; also the creature, object, item, spell and quest search |
| `TcBehaviorSource` | the behavior snapshot of one creature: `smart_scripts` (source types 0 and the timed action lists of type 9 they run), `creature_template` (AIName, ScriptName), `creature_text`, with creature, object and quest names from the world database, item and spell names from the client, and each linked broadcast text from the hotfixes database before the client; also the used action list IDs |
| `TcHotfixSource` | `hotfixes` database: `creature_display_info`, `creature_model_data`, `broadcast_text`, `area_trigger` (including `ShapeID`; the path tables are not read), `item_sparse` (vendor item name and quality) |

Teleports reuse `AcTeleportSource` (same `game_tele` schema). `TcBehaviorSource.ReadAreaTriggerAsync` reads AreaTrigger rules: a client trigger's (`smart_scripts` type 2, `areatrigger_scripts`; the trigger must be in the client's AreaTrigger.db2 or the hotfix rows) or a spawned trigger template's (types 11 and 12 by `areatrigger_template`, with the spawn's `ScriptName` as the `areatrigger_ai` row and its create properties' `ScriptName` as the fallback the server uses).

## Draft model: `Foundation/`

Each kind of edit has its own draft type. All of them are backed by `DraftHistory<T>` and store their state in a JSON file under `user://drafts/`.

| Draft | File | Covers |
|---|---|---|
| `CreatureDraft` | `CreatureDraft.cs` | Creature positions. `Move`, `Revert`, `Undo`, `Redo`, `NetMoves()` |
| `GameObjectDraft` | `GameObjectDraft.cs` | GameObject positions and rotation |
| `GraveyardDraft` | `GraveyardDraft.cs` | Graveyard location, name and zone links, dialect-neutral (`ISqlDialect.Graveyard` maps them to tables). ID and map cannot change |
| `WaypointDraft` | `WaypointDraft.cs` | Waypoint positions and properties of either dialect (`WaypointProps.EditableOn` says which properties a dialect stores: TrinityCore only facing and delay) |
| `ContentDrafts` | `ContentDrafts.cs` | One shared `InteractionDraft` for the interaction, quest and behavior editors |
| `InteractionDraft` | `InteractionDraft.cs` | Generic row edits (insert, update, delete); several edits undo as one command. Each source's table catalog comes from `SourceDialects` (`sources.json`); an unknown source is AzerothCore |
| `InteractionRows` | `InteractionRows.cs` | The row model and `AcTables`, the AzerothCore catalog (`InteractionTables` is its legacy static facade); TrinityCore's catalog is `TcTables` (`TcTables.cs`) |

`AcTables` lists the editable AzerothCore tables: `creature_template` (npcflag, gossip_menu_id, subname), `gossip_menu`, `npc_text`, `gossip_menu_option`, `creature_queststarter`, `creature_questender`, `npc_vendor`, `creature_default_trainer`, `trainer`, `trainer_spell`, `npc_spellclick_spells`, `creature_ai` (a logical name for `creature_template.AIName`), the four quest tables, `smart_scripts`, `creature_text`, and `areatrigger_scripts`. `TcTables` holds the creature-property tables, the five quest tables (`quest_template`, `quest_template_addon`, `quest_request_items`, `quest_offer_reward`, `quest_objectives`), `smart_scripts`, `creature_text`, `areatrigger_scripts` and `areatrigger_ai` (a logical name for `areatrigger.ScriptName`, keyed by `SpawnId`). `TcQuestRules` holds TrinityCore's quest vocabulary (objective types and flags) and the loader checks.

### Undo, redo and freshness

Creature page metadata lives in `CreaturePage.cs`. `ItemListSpec` describes child rows by source table, parent column, ordering, fields and a complete new-row factory. Its add/remove helpers use the dialect's table; changing a primary-key field produces a delete plus insert in one command. `VendorPage` supplies the AC and TC vendor descriptors through `IWorldSchema.VendorPage`. `ItemListTests` covers selection, defaults, grouped edits, undo/redo and export. `IWorldSchema.GossipPage` supplies `GossipPageSpec` (menu text links, options and the greeting table) from `GossipPage.cs`. Its option text helper clears the broadcast override atomically; its reorder helper deletes both old keys before inserting replacements. `GossipPageTests` covers the dialect-specific defaults, selection, text edits, reordering, undo/redo and export. List ordering uses signed 64-bit comparisons to accommodate signed vendor slots and the full unsigned 32-bit gossip IDs. `IWorldSchema.TrainerPage` supplies `TrainerPageSpec` (trainer storage, creature bindings and spell fields) from `TrainerPage.cs`. Spells sort by required level then spell ID; new rows include all catalog requirements. `IWorldSchema.QuestLinksPage` supplies starter/ender lists from `QuestLinksPage.cs`; `SetRoles` changes both roles as one command, retaining existing rows. `CreatureServicePageTests` covers selection, complete defaults, requirements, role changes, undo/redo and SQL export.

Each draft holds a `DraftHistory<T>` (`Foundation/DraftHistory.cs`):

- Opening a draft takes an exclusive lock on a sibling `.lock` file, so a second writer fails.
- The file is a JSON envelope `[payload, SHA-256(payload)]`. Saves write a `.tmp` file, flush it and move it over the real file.
- On load, a checksum mismatch, an unknown version or an out-of-range cursor throws. A bad file is rejected, never silently reset. Each entry is re-validated by its draft type.
- Entries before the cursor are applied; entries from the cursor onward are the redo branch. Appending drops the redo branch. Undo and redo move the cursor.
- Snapshots are immutable, so worker threads can read them.

Freshness uses each row's remaining net change. Discarded or mutually cancelling edits keep their undo history but neither report conflicts nor replace database values in the effective view. A later edit of that row starts a new baseline from the current snapshot; SQL export uses that baseline too. `InteractionConflictTests` covers discarded template changes, cancelled inserts/deletes, reopen, frozen views and subsequent edits for both dialects. Every edit records the value it replaced (`Before`). A new edit is refused if the current effective value no longer matches. Per-row staleness shows up as a conflict, and the database value is displayed instead of the draft value.

`ContentDrafts.Commit` also refuses edits that introduce a new rule error, using `InteractionRules` and the quest and SmartAI checks. TC creature commits use `TrinityCore/TcGossipRules` for service IDs, confirmation booleans/costs, greeting weights and, when the client or database can answer, language, spell, client-option and point-of-interest references (the gossip section also assigns, unassigns, copies and creates a creature's menus and greetings), `TrinityCore/TcTrainerRules` for trainer type, spell and skill-line existence (when the client is open), required level and required abilities, and each `creature_trainer` binding (trainer exists, gossip option exists; the trainer section assigns, rebinds, copies and removes bindings), `TrinityCore/TcQuestLinkRules` for quest IDs that must exist in `quest_template`, and `TrinityCore/TcVendorRules` for vendor checks: vendor flag, signed slot range, item/type validity, paired item stock/restock, positive currency quantity, bonus-list integer syntax and filtering boolean. Item, currency, extended cost, player condition and bonus list existence is checked against the retail client when available (`IClientLookup.Exists`, `ClientRecord`), and `TcStatRules` checks the difficulty ID the same way. Shared-reference expansion remains a gap. Stock and restock are one command so a valid limited-stock row can be created from unlimited stock.

## Draft lifecycle

```mermaid
flowchart TB
    snap["Database snapshot<br/>read-only Source"] --> eff["Effective view<br/>draft applied over snapshot"]
    eff --> edit["User edit in overlay or editor"]
    edit --> rule{"Rule checks<br/>QuestModel.Check, SmartBehavior.Check, InteractionRules"}
    rule -->|error| refuse["Refused, shown in UI"]
    rule -->|ok| hist["DraftHistory.Append<br/>saved to user://drafts/*.json"]
    hist --> eff
    hist --> net["NetMoves / NetEdits<br/>first Before to last After"]
    net --> pend["PendingChanges.Collect<br/>IDs are SHA-256 of source and script"]
    pend --> poll["Sidebar and Pending window<br/>polled every 0.4 s"]
    pend --> build["SqlExport.Build per source"]
    build --> files["exports/yyyyMMdd-HHmmss/<br/>source-apply.sql, source-revert.sql"]
    files --> mark["PendingChanges.MarkExported<br/>drafts are not modified"]
    mark --> person(["Person reviews and runs SQL"])
```

Details:

- **Net changes.** Draft entries are collapsed per source, GUID or key: the first `Before` to the last `After`. Changes that end up at the baseline are dropped.
- **Pending changes** (`Foundation/PendingChanges.cs`): each net change becomes a `PendingChange`. Its ID is a SHA-256 of the source and its apply script, built with a fixed timestamp so the ID is stable. The set of exported IDs is saved as a JSON array.
- **Discard** calls the owning draft's `Revert`. Reverts are themselves undoable entries.
- **Local file locations** come from `ContentDraftFiles` (`Scripts/Ui/UserSettings.cs`): `user://drafts/creature-positions.json`, `gameobject-positions.json`, `graveyards.json`, `waypoints.json`, `interactions.json`, `sources.json`, `exported.json`. Smoke runs use `res://artifacts/drafts/<guid>/`.

## SQL generation: `Foundation/SqlExport.cs`

`SqlExport.Build(source, creatures, graveyards, rows, ..., waypoints, schema, gameObjects)` returns `SqlScripts(Apply, Revert, Changes, Summary)`. It never connects to anything. `schema` (default `AcSchema`) picks the dialect: content rows use `schema.Tables`, and moves, graveyards and waypoints use `schema.Sql` (`AcSqlDialect` or `TcSqlDialect` in `Foundation/SqlDialect.cs`). The viewer passes each source's schema from `SourceDialects`.

Each script has the same structure:

1. A header comment and `SET NAMES utf8mb4;`.
2. A read-only preflight: one `SELECT ... IF(COUNT(*)=1,'ok','STALE')` per change. Rows that must not exist yet use `COUNT(*)=0`.
3. `START TRANSACTION;`, the statements, and `COMMIT;`.
4. A post-check that confirms the new state.

The revert script is the same shape, with the statements in reverse order.

Per change:

- **Creature move:** `UPDATE creature SET position_x, position_y, position_z, orientation WHERE guid = ...` with a guard that the four columns still hold their old values (floats compared with `ABS(col - v) < 0.0001`).
- **GameObject move:** the same pattern on `gameobject`.
- **Graveyard position or name:** an `UPDATE` guarded by ID, position and comment, on `game_graveyard` (AzerothCore) or `world_safe_locs` (TrinityCore).
- **Graveyard links:** `INSERT`, `DELETE` or `UPDATE` on `graveyard_zone`, one per changed link. TrinityCore has no `Faction` column; the faction is a team row in `conditions` (source type 27).
- **Waypoint point:** an `UPDATE` on `waypoint_data` for AzerothCore, or on `waypoint_path_node` for TrinityCore, keyed by path and point; only the changed columns are set and guarded. `TcSqlDialect` exports position, facing and delay and refuses an edit that changes the path-level move type or velocity, smooth transition or action instead of dropping it.
- **Content rows:** insert writes every allowed column; delete matches every allowed column; update changes and guards only the key plus the changed columns. Restricting updates this way is what lets `creature_ai` and `creature_template` share a table safely.

Escaping and guards:

- Text is compared with `BINARY COALESCE(col,'') = BINARY '...'`, and strings are MySQL-escaped with backslashes.
- Numbers are re-parsed before they are written, so only canonical values appear unquoted.
- INSERT statements have no guard by design. A duplicate primary key makes the script fail, which is the intended signal.

## Export flow in the viewer

`Scripts/Viewer/TerrainViewer.Export.cs` ties it together:

- `CollectPending` feeds creature `NetMoves`, graveyard `NetEdits` and content `NetEdits` into `PendingChanges.Collect`.
- `BuildSqlExports` groups changes by source and calls `SqlExport.Build` once per group.
- `WriteSqlExports` writes UTF-8 without BOM to `user://drafts/exports/<yyyyMMdd-HHmmss>/<source-prefix>-apply.sql` and `-revert.sql`.
- `ExportAllPending` writes, calls `MarkExported`, then opens the folder.

## SmartAI and quest rules

SmartAI is one engine over two catalogs; `QuestModel` and `NpcInteraction` remain AzerothCore static data (TrinityCore has `TcQuestRules` and `TcCreatureView`).

- `SmartCatalog` (`Foundation/SmartBehavior.cs`, `SmartCategories.cs`) is an instance per dialect, `SmartCatalog.Ac` and `SmartCatalog.Trinity` (reached through `IWorldSchema.Smart`). Each holds its event, action and target types, how many parameter columns `smart_scripts` has (events 6/5, actions 6/7, targets 4), the event-flag checkboxes, its tables (`smart_scripts`, `creature_text`, the `creature_ai` AIName group) and the loader rules (`Check`). The ID ranges differ, so nothing is shared by ID.
- The type arrays are generated: `AcSmartTypes` (`Foundation/AcSmartTypes.cs`) and `TcSmartTypes` (`Foundation/TrinityCore/TcSmartTypes.cs`) come from `Tools/generate_smart_catalog.py` over each server's `SmartScriptMgr.h`, with hand-written names and sentences. TrinityCore reuses an AzerothCore sentence only when the code name and parameter comment match; the rest read their parameters from the comment. `SmartTypes.cs` holds `SmartType`, `SmartParam` and `SmartParamKind`. `SmartCatalog.IsMarker` hides range markers, `RESERVED` slots and slots the server marks "UNUSED".
- `SmartBehavior` (`Foundation/SmartBehavior.cs`) turns rows into readable sentences, groups linked rows into chains, and checks rules that mirror `SmartScriptMgr.cpp`. `SmartRule` reads a missing parameter column as 0 (and the string parameters as empty), so one routine serves both row shapes.
- AreaTrigger scripts: `SmartScope.IsAreaTrigger` covers source types 2 (client trigger), 11 and 12 (TrinityCore spawned trigger templates, `IsTriggerEntity`). `SmartCatalog.AllowedForAreaTrigger` follows each core's event masks (AzerothCore: On trigger and Then; TrinityCore adds "Area trigger left", and spawned triggers also run the timed updates); a spawned trigger is an object, so Self and its own action lists are allowed. `CheckAreaTriggerScript` and `EnableTriggerScript` check and fix the script name that runs the rules (SmartTrigger in `areatrigger_scripts`, SmartAreaTriggerAI in the spawn's `areatrigger_ai` row).
- `SmartRuleEdits` (`Foundation/SmartRuleEdits.cs`) does the row math for add, delete, swap, duplicate and copy-scope; every function takes the dialect's `SmartCatalog` for its tables. Changing a key column becomes a delete plus an insert. `SpeechEdits` does the same for `creature_text` (TrinityCore lines carry `SoundPlayType`).
- `QuestModel` (`Foundation/QuestModel.cs`) holds the quest vocabulary and checks rules that mirror `ObjectMgr::LoadQuests`.
- `NpcInteraction` (`Foundation/NpcInteraction.cs`) is a read-only view of one NPC's interaction data and its validation rules.

## Types by file

| File | Types |
|---|---|
| `WorldCoordinates.cs` | `Point3`, `WorldPose`, `WorldCoordinates` (server to view conversion, tile maths, ray hits), `PoseText` |
| `CreatureSpawn.cs` | `CreatureSpawn`, `CreatureRegion`, `CreatureRegionResult` |
| `SpawnPreview.cs` | `SpawnPreview`, `EventPreviewMode`, `SpawnQueryPoint`, `SpawnVisibility`, `GameObjectSpawn` (phase and event filters, `QueryLimit` 384, `ModelLimit` 128) |
| `AreaTrigger.cs` | `AreaTriggerRecord` (shape, server membership test `Contains`, `Focus`, spawn identity), `AreaTriggerShape`, `AreaTriggerKind`, `AreaTriggerKey`, `AreaTriggerOwner` (whose rules: source type 2, or 11/12 with the spawn), `AreaTriggerTeleport` |
| `PendingChanges.cs` | `PendingKind`, `PendingChange`, `PendingChanges` |
| `SqlExport.cs` | `SqlScripts`, `SqlExport` |
