# Status

Status of the TrinityCore database work (phases from `docs/TC-DB-Update-Plan.md`) and the project's other open work. Target: TrinityCore `master`, TDB 1210.26091; retail client 12.1.5.70575.

Status values: **done**, **partial**, **not started**, **deviates** (built differently from the plan), **unverified**.

## Foundation and design

| Feature | Status | Notes |
|---|---|---|
| `WorldDialect`, dialect follows client kind (no autodetect) | done | `ClientProfile.Dialect` |
| `WorldConnection(World, Hotfixes, Dialect)` | done | Legacy `Ac*Source(DatabaseSettings)` constructors remain and always build an AC `WorldDatabase`. |
| `IDBSource` + `DBSource.Open(connection, lookup)` | done | Only the area-trigger overlay passes the client lookup. |
| Sources split into `Data/AzerothCore/Ac*` and `Data/TrinityCore/Tc*` | done | |
| `ITableCatalog`, `AcTables`; `InteractionTables` as a facade | done | |
| `SmartCatalog`, `QuestModel`, `NpcFlags` behind `AcSchema` | deviates | They stay static AC data; moved per area in Phases 6–8. |
| `IWorldSchema` (`Dialect`, `Tables`, `Sql`) | partial | `QuestSchema`, `NpcSchema`, `CreatureSchema`, `Smart` come with Phases 6–8. |
| `ISqlDialect` (`AcSqlDialect`, `TcSqlDialect`) | done | AC export output unchanged. |
| `TcSchema` (empty table catalog), returned by `TcDBSource.Schema` | done | TC row and waypoint edits are refused instead of exported as AC SQL. |
| Export uses each source's dialect | done | `SourceDialects` records each opened source in `sources.json`; unknown sources are AC. |
| Drafts use the source's table catalog | done | `InteractionDraft`/`ContentDrafts` resolve each source's catalog through `SourceDialects`. |
| `SourceKey` hashes the dialect (TC only) | done | AC keys unchanged. |
| Guids widened to `ulong` | done | Spawn search still binds the guid as `uint`; harmless today (max ≈ 11.8M). |
| `Spatial`/`Restrictions` per dialect | deviates | TC has its own copies in `TcSpawnSource`; the AC versions stay on the shared `WorldReadSession`. |
| `ColumnKind` 64-bit kinds (`Long`, `ULong`) | done | Validated and exported as plain numbers; not yet used by any table. |
| Graveyard healer lookup | deviates | TC reuses `AcGraveyardSource.ReadHealers` (works on current TDB). |

## Connection, settings and errors

| Feature | Status | Notes |
|---|---|---|
| Per-profile settings `[database.wrath]` / `[database.retail]` | done | Retail defaults `world` / `hotfixes`; AC still reads the legacy `[database]` section. |
| `ACWORLD_TC_DB_INFO` (optional 6th part for hotfixes) | done | |
| Connection form hotfixes field (TC only, optional) | done | Blank means no hotfixes database. |
| Per-dialect schema probe and "not a TrinityCore world database" error | done | |
| Schema mismatch detected up front | done | Both directions: TC needs `creature.spawnDifficulties` + `world_safe_locs`, AC needs `creature.spawnMask` + `game_graveyard`. |
| Hotfixes database test | done | Connects and requires a `hotfix_data` table. |
| Hotfix reads fall back to client DB2 | done | Display and area-trigger hotfixes; the area-trigger fallback is silent. |
| User-facing error text in the status bar (`DatabaseFeatureException`) | done | |

## Retail client data (plan Phase 3)

| Feature | Status | Notes |
|---|---|---|
| WoWDBDefs submodule and DB2 FileDataIDs | done | ID check by corpus test: unverified (needs client data). |
| Retail creature and GameObject display catalogs | done | Includes humanoids (baked skins, customization geosets, hair/eye textures). |
| `hotfixes.creature_display_info` overrides | done | Reads 3 of 4 texture columns; `creature_display_info_extra` not read (empty in TDB). |
| Other hotfix overrides (`gameobject_display_info`, `item_sparse`, `spell_name`, `broadcast_text`, `content_tuning`) | not started | `RetailGameObjectCatalog.Override` exists but is never called. |
| `IClientLookup` (area triggers, broadcast text, items, spells) | done | Items, spells and broadcast text are not wired into any editor yet. |
| Retail area names (`AreaLookup`) | not started | Null for retail. |
| `AreaTriggerActionSet`, `ContentTuning` tables | not started | Registered, not read. |

## Read-only overlays (plan Phase 4)

| Feature | Status | Notes |
|---|---|---|
| TC creature and GameObject spawns | done | Difficulty CSV, phase, `modelid` override, `pool_members`, events, object-visibility conditions (type 32). |
| Difficulty / phase preview filter | partial | Phase groups ignored: a spawn with only a `PhaseGroup` is hidden while masks are respected. `phaseUseFlags` read, not used. Difficulty list hardcoded. |
| Inspector shows TC difficulty and phase | done | |
| TC graveyards (`world_safe_locs`, `graveyard_zone`, team conditions) | done | Only safe locations linked from `graveyard_zone` are shown. |
| Teleports from `game_tele` | deviates | Reuses `AcTeleportSource` (same schema); no `TcTeleportSource`. Continents come from the Map DB2. |
| Area triggers from `AreaTrigger.db2` + `hotfixes.area_trigger` | partial | Polygon/cylinder shapes skipped; trigger phases not filtered by the previewed phase. |
| Area-trigger links (teleport, scripts, quest, tavern, SmartAI type 2) | done | |
| Server-spawned `areatrigger` overlay | not started | |
| Creature and GameObject move export | done | Same statements as AC with `ulong` guids. |
| Overlay smoke (`--client-shot-overlays`) | done | Passed 2026-10-25: 178 creatures, 53 GameObjects, 25 graveyards, 102 triggers. |

## Graveyard editing (plan Phase 5)

| Feature | Status | Notes |
|---|---|---|
| Move / rename updates `world_safe_locs` | done | Facing not editable (the draft has no facing). |
| Link add/remove/comment in `graveyard_zone` | done | |
| Faction as one plain team row in `conditions` (type 27) | done | Revert of a removed team row re-inserts it with a generated comment. |
| Battleground / world-state links locked | done | Faction change and unlink refused for links with any other conditions. |
| Warn when the safe location is a teleport target | partial | Counts `areatrigger_teleport` only (0 overlaps in current TDB); battleground start points not checked. |
| Graveyard editing enabled in TC | done | Locked-link controls and teleport line not yet inspected visually (e.g. Alterac Valley graveyard 169). |

## Editors (plan Phases 6–9)

| Feature | Status | Notes |
|---|---|---|
| Creature properties: `TcTables` (`creature_template`, `_difficulty`, `_gossip`, `creature_trainer`, `gossip_menu_option`, `npc_text`, `npc_vendor`, quest starters/enders) | not started | Phase 6. Editor gated off in TC. |
| TC `NpcSchema` / `CreatureSchema` (64-bit `npcflag`, Classification, `unit_flags3`, OptionNpc) | not started | Phase 6. |
| `StatsSection` via ContentTuning | not started | Phase 6. |
| Vendor search and item tooltips via ItemSparse + `hotfixes.item_sparse` | not started | Phase 6. |
| Section registry and sections take field groups from the schema | not started | Phase 6. |
| Quests: `TcQuestSchema`, `quest_objectives` page, ObjectID lookups, `QuestModel.Check` per dialect | not started | Phase 7. Editor gated off in TC. |
| SmartAI: `SmartCatalog.Trinity`, TC `smart_scripts` columns, `creature_text.SoundPlayType` | not started | Phase 8. Editor gated off in TC. |
| Speech via BroadcastText; hotfix export with `hotfix_data` | not started | Phase 8. |
| Waypoints: `TcWaypointSource`, per-dialect `WaypointEdit`, `waypoint_path_node` export | not started | Phase 9. Layer disabled in TC. |
| Data-correctness check of the Phase 6 tables | done | Live TDB 1210.26091 checked; findings in the rows below. |
| Creature gossip menus | deviates | `creature_template_gossip` is keyed (CreatureID, MenuID): 1:N, not the single `gossip_menu_id` the plan maps it to (7 of 7,104 creatures have several menus). |
| Creature trainers | deviates | `creature_trainer` is keyed (CreatureID, MenuID, OptionID): a trainer belongs to a gossip option (741 of 975 rows), not just to the creature. 23 creatures have several trainers. |
| `trainer_spell` columns | deviates | Not "the same" as AC: TC adds `ReqAbility1-3`. |
| Per-difficulty template data | deviates | 3,648 templates have no `DifficultyID=0` row in `creature_template_difficulty` (101,919 rows are for other difficulties), so stats/loot/gold need a difficulty choice or a fallback. |
| `npc_vendor` key | deviates | `type` is part of the key (item vs currency); 1 of 49,853 rows is not an item. |
| TC `npcflag` bits match AC's low 32 bits | unverified | Needs TrinityCore's `NPCFlags` enum; only the spirit-healer bit (0x4000) is confirmed by data. |

## Viewer workflow

| Feature | Status | Notes |
|---|---|---|
| Drafts kept across a client switch | not started | Switching client discards all drafts after a confirmation, although source keys already keep them apart. |
| Diagnostics use throwaway drafts | done | `*-smoke` and `--client-shot*` runs write under `artifacts/drafts/`. |

## Tests

| Feature | Status | Notes |
|---|---|---|
| TC connection and read Db tests (`TcDbFact`) | done | Spawns, graveyards (incl. locked links), teleports, area triggers, hotfixes. |
| TC export unit tests | done | Spawn moves, graveyard transitions, refused edits. |
| `Db2Tables` thread safety | partial | `Table` loads are locked. Retail test classes still run one at a time (`[Collection(RetailData.Collection)]`); drop that once the retail corpus tests pass in parallel. |
| `RetailCorpusTests.EveryMapWdtParsesAndOneTilePerTerrainMapDecodes` accepted failures | not started | Stale "until the retail WMO milestone" comments; tighten or remove the filter. |

## Retail creatures

| Feature | Status | Notes |
|---|---|---|
| Equipment (`NPCModelItemSlotDisplayInfo` → `ItemDisplayInfo`) | not started | |
| Customization beyond geosets and hair/eye (bone sets, skinned models, particle colours) | not started | |
| Unchosen geoset groups | deviates | Variant 1, except ears (7) and head (32) use variant 2; rule from data, not a documented client rule. |
| Humanoid whose extra has no bake material | partial | Drawn without customization (every geoset shows). |
| Parent skeletons (`.skel` `SKPD`) | not started | |
| Animation names (AnimationData DB2) | not started | Preview menu shows IDs only. |
| M2 multitexture batches and spline tracks | partial | Base texture only; splines interpolated linearly. |

## Retail terrain and rendering

| Feature | Status | Notes |
|---|---|---|
| Height texturing (MHID/MTXP), MAMP scale | not started | |
| Terrain layers 5–8 | not started | Only the first 4 are drawn. |
| Uncompressed alpha size | partial | Follows the WDT big-alpha flag; decide per layer if a map disagrees (not seen on 12.1.5). |
| Path-named placements | partial | Resolve only through the community listfile. |
