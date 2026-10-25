# TrinityCore support: plan status

Checked against `docs/TC-DB-Update-Plan.md` on branch `trinity-master` (HEAD `5e5de08`, 2026-10-25). Every row below was confirmed by reading the code, not by checkboxes, TODO notes or commit messages. Items marked **unverified** could not be confirmed from the code alone (they need a live database, client data or a visual run). No tests or smokes were run for this review.

Status values: **done**, **partial**, **not started**, **deviates** (built differently from the plan), **unverified**.

This document uses the plan's phase numbering. It also holds the project's other open work (section 5), which used to live in `docs/TODO.md`.

**Target release:** TrinityCore `master` with TDB 1210.26091 (`world.version`: core rev 868213670b76, 2026-10-24). `docs/DB-DIFFERENCES.md` compares against the same TDB. The `hotfixes` rows are verified up to build 69497 (12.1.0). The retail client in use is 12.1.5.70575, so when TrinityCore publishes a newer TDB, check for hotfix rows the newer client no longer matches.

## 1. Status table

### Decisions and Phase 1 scope decisions

| Item | Status | Evidence | Notes |
|---|---|---|---|
| Dialect follows client kind, no autodetect | done | `Scripts/Viewer/ClientProfile.cs:13`, `Scripts/Viewer/TerrainViewer.Ui.cs:43,449` | |
| Schema mismatch fails with a dialect-specific error | partial | `Scripts/Data/WorldDatabase.cs:150-156` (`RequireDialect`), `Scripts/Data/DatabaseSettings.cs:82` | Only TrinityCore is checked (2 columns: `creature.spawnDifficulties`, `world_safe_locs.ID`). AzerothCore mode has no up-front check, so a TC database in Wrath mode fails later on the first missing column, with a generic `Required database metadata unavailable` message. |
| Hotfixes from a second DB name on the same connection, falling back to client DB2 | done | `Scripts/Data/WorldConnection.cs:41`, `Scripts/Data/TrinityCore/TcHotfixSource.cs`, `Scripts/Viewer/TerrainViewer.Hotfixes.cs`, `TcAreaTriggerSource.ReadHotfixes` | Fixed in step 1b/1c: both display and area-trigger hotfixes fall back, and a blank hotfixes name is allowed. |
| `docs/DB-DIFFERENCES.md`, linked from CLAUDE.md | done | `docs/DB-DIFFERENCES.md`, `CLAUDE.md:53` | |
| Waypoints behind `IDBSource` (AC only), layer off in TC | done | `IDBSource.cs:28`, `AcWaypointSource.cs`, `TcDBSource.cs:41`, `Scripts/Ui/SceneSidebar.cs:159-176` | Waypoint export is still a hardcoded `waypoint_data` statement in `SqlExport.Waypoint`, not behind `ISqlDialect` (which doesn't exist; see below). |
| Statics become facades over `AcTables`/`AcSchema` | deviates | `Foundation/InteractionRows.cs:31,325-350`, `Foundation/WorldDialect.cs:33-38` | `InteractionTables` is a facade over `AcTables`. `SmartCatalog`, `QuestModel` and `NpcFlags` were **not** moved behind `AcSchema`: its doc comment says they "remain its static data". |
| `SourceKey` hashes dialect for TC only | done | `WorldDatabase.cs:33-36`; tests `DatabaseSettingsTests.cs:56-72` | |
| Guid widening to `ulong` | done | `Foundation/CreatureSpawn.cs:7`, `CreatureDraft.cs:7`, `GameObjectDraft.cs:68`, `Data/GraveyardRecords.cs:9`, `Data/WaypointRecords.cs:16`, `IDBSource.cs:22` | `WorldReadSession.AddSearch` still binds `@searchId` as `uint` (`WorldDatabase.cs:286`), so a guid above 2³² can't be searched. That's harmless today (max ≈ 11.8M). |
| Non-DB home for `SafeError`, `MaxRows`/`MaxSearch`, `HealerRadius`, `Continents`/`Pick`, `FillTexts` | done (FillTexts unverified) | `Scripts/Data/DbLimits.cs`, `Scripts/Data/TeleportRecords.cs:9-15` | I didn't locate where `FillTexts` ended up. |
| `ColumnKind` 64-bit kind before Phase 6 | not started | — | Belongs to Phase 6. |
| `SqlExport.Build` AC output byte-identical | unverified | `Foundation/SqlExport.cs:36,56` (defaults to `AcTables`) | Code path looks unchanged for AC. `SqlExportScriptTests` was not run. |
| Connection snapshot before `Task.Run` | done | `TerrainViewer.cs:91`, `TerrainViewer.Hotfixes.cs:16,24`, overlays capture `config` before `query.Start` | |

### Design types

| Item | Status | Evidence | Notes |
|---|---|---|---|
| `Foundation/WorldDialect.cs` enum | done | `Foundation/WorldDialect.cs:7-11` | |
| `WorldConnection(World, Hotfixes, Dialect)` replaces `Func<DatabaseSettings>` | done | `Scripts/Data/WorldConnection.cs:9`; overlays use `Connection = () => DatabaseConnection()` (`TerrainViewer.cs:142`) | Legacy `XSource(DatabaseSettings)` constructors remain on every `Ac*Source` (e.g. `AcTeleportSource.cs:11`). They always build an AC `WorldDatabase`. |
| `IDBSource` with the listed members | done | `Scripts/Data/IDBSource.cs:10-44` | `ReadWaypointsAsync` was added. Members match the plan. |
| `DBSource.Open(WorldConnection, IClientLookup?)` | done | `IDBSource.cs:55-60` | Only `AreaTriggerOverlay.cs:113` passes the lookup. |
| `IClientLookup` (AreaTrigger, BroadcastText, ItemSparse, SpellName) | done | `Scripts/Assets/Client/ClientLookup.cs:16-32,38` | Item, spell and broadcast lookups are built but nothing in the app calls them yet (only corpus tests do). |
| `IWorldSchema` with `Tables`, `QuestSchema`, `Smart`, `NpcSchema`, `CreatureSchema`, `ISqlDialect Sql` | partial | `Foundation/WorldDialect.cs`, `Foundation/SqlDialect.cs` | `Dialect`, `Tables` and `Sql` exist, and `TcDBSource.Schema` returns `TcSchema` (step 2). `QuestSchema`, `NpcSchema`, `CreatureSchema` and `Smart` come with Phases 6–8. |
| `ITableCatalog`, `AcTables` | done | `Foundation/WorldDialect.cs:14-20`, `Foundation/InteractionRows.cs:31,317-318` | |
| Sources moved to `Data/AzerothCore/Ac*`, `Data/TrinityCore/Tc*` | done | `Scripts/Data/AzerothCore/*`, `Scripts/Data/TrinityCore/*` | |
| Tests csproj uses `Scripts/Data/**/*.cs` with `%(RecursiveDir)` | done | `Tests/ACWorldEdit.Tests/ACWorldEdit.Tests.csproj:12` | |
| `Spatial`/`Restrictions` become per-dialect | deviates | `WorldDatabase.cs:208-253,290-314` (AC, still on shared `WorldReadSession`), `TcSpawnSource.cs:129-206` (TC copy) | TC has its own private copies. The AC versions stay on the shared session class rather than moving to `AzerothCore/`. |
| Shared helpers stay in `WorldDatabase.cs` | done | `WorldDatabase.cs:93,259,277,316` | |

### UI switching

| Item | Status | Evidence | Notes |
|---|---|---|---|
| `ClientProfile.Dialect`, Retail `SupportsDatabase=true` | done | `ClientProfile.cs:13,16` | |
| `ClientServices.OpenRetail` sets `DatabaseEnabled=true` | done | `ClientServices.cs:69` | The doc comment at `:35-37` still says "database features are off". |
| `TerrainViewer` builds `WorldConnection` from profile + per-profile settings | done | `TerrainViewer.cs:89-91`, `ConnectionForm.cs:44-57`, `UserSettings.cs:38-56` | |
| Editors read the dialect only through `IDBSource.Schema` | not started | `TerrainViewer.Ui.cs:315` (`ContentEditors` gate) | The editors are switched off for TC instead. They still use static `InteractionTables`, `SmartCatalog` and `QuestModel`. |
| `CreatureSectionRegistry` filters by schema | not started | — | |
| Template/Stats/Gossip/Trainer/Vendor sections take field groups from schema | not started | e.g. `GossipSection.cs:168`, `TrainerSection.cs:70,124` use `InteractionTables.*` | |
| `QuestPanel.Pages` from `QuestSchema` | not started | — | |
| `BehaviorPanel.Rules` uses `Schema.Smart` | not started | — | |

### Phase 1: docs and interface extraction

| Item | Status | Evidence | Notes |
|---|---|---|---|
| DB-DIFFERENCES.md + CLAUDE.md link | done | see above | |
| Add `WorldDialect`, `WorldConnection`, `IDBSource`, `IWorldSchema`, `ITableCatalog`; `AcDBSource` | done | `Foundation/WorldDialect.cs`, `Data/AzerothCore/AcDBSource.cs` | |
| `SmartCatalog`/`QuestModel` data behind `AcSchema` | deviates | `Foundation/WorldDialect.cs:32` | See the statics row above. |
| Replace every `new XSource(...)` with `DBSource.Open` | done | 23 `DBSource.Open` call sites in overlays, `QuestPanel`, `BehaviorPanel*`, `CreatureProperties/*`, `TerrainViewer.Locate/Teleports` | The one direct construction left is `new TcHotfixSource(...)` in `TerrainViewer.Hotfixes.cs:16`, and it is intentional. |
| Widen guids; check-layering + suites green | done (suites unverified) | guid rows above | Suites were not run. |

### Phase 2: connection, settings, hotfixes plumbing

| Item | Status | Evidence | Notes |
|---|---|---|---|
| Per-profile `[database.wrath]`/`[database.retail]`; retail defaults `world`/`hotfixes` | done | `SettingsResolver.cs:39,49-81`, `DatabaseSettings.cs:15`, `WorldConnection.cs:16-17` | AC still falls back to the legacy `[database]` section. |
| `ConnectionForm` Hotfixes field, TC only | done | `ConnectionForm.cs:19,35,50-53` | |
| `ACWORLD_TC_DB_INFO` with optional 6th part | done | `SettingsResolver.cs:16,41`, `WorldConnection.cs:23-38` | |
| `ReadSchema` per-dialect table list | done | `WorldDatabase.cs:117-144,158-170` | |
| `Describe` says "not a TrinityCore world database" | done | `DatabaseSettings.cs:82` | |
| `TestAsync` checks the hotfixes DB is reachable | done | `DatabaseSettings.cs:111-128` | It only opens a connection. It doesn't check that `area_trigger`/`creature_display_info` exist, so an unrelated database passes. |
| Tests: `SettingsResolverTests`, `DatabaseSettingsTests`, `TcDbFact` | done | `Tests/.../Data/SettingsResolverTests.cs:101-161`, `Data/DatabaseSettingsTests.cs:56-131`, `Support/Attributes.cs:118-124`, `Db/TrinityConnectionDbTests.cs` | |

### Phase 3: retail client catalogs

| Item | Status | Evidence | Notes |
|---|---|---|---|
| DBDs from `Data/WoWDBDefs` submodule | done | `Db2Tables.cs:7-9`, commit `36fc76c` | |
| FileDataIDs for listed tables | done | `Db2Tables.cs:30-50` | The comment says the IDs came from the listfile and are checked by a corpus test. The corpus test was not run (**unverified**). `AreaTriggerActionSet` and `ContentTuning` are registered but nothing reads them. |
| Retail creature + GameObject display catalogs, registered in `OpenRetail` | done | `Scripts/Assets/Client/RetailCatalogs.cs:41,224`, `ClientServices.cs:69` | |
| `hotfixes.creature_display_info` overrides DB2 | done | `TcHotfixSource.cs:31-62`, `RetailCatalogs.cs:106-121`, `TerrainViewer.Hotfixes.cs` | Only 3 of the 4 `TextureVariationFileDataID` columns are read. `creature_display_info_extra` and customization option rows in `hotfixes` are not read (both empty in TDB 1210.26091), so a hotfixed humanoid resolves its extra against the client. |
| "The other hotfix rows" override DB2 | partial | `RetailGameObjectCatalog.Override` (`RetailCatalogs.cs:230`) is never called outside tests | No `gameobject_display_info`, `item_sparse`, `spell_name`, `broadcast_text` or `content_tuning` hotfix reads. `RetailClientLookup` serves client DB2 only. |
| Areas registered for retail | not started | `ClientServices.cs:69` passes `null` for `AreaLookup` | Retail has no area names. |
| `IClientLookup` + corpus tests | done (tests unverified) | `ClientLookup.cs`, `Tests/.../Corpus/RetailCatalogTests.cs:150-172` | |
| Fill `NEEDED-TESTS.md` entries | unverified | `docs/NEEDED-TESTS.md` has no Phase 3 / lookup entries | Either they were all written, or the entries were never added. I can't tell which. |

### Phase 4: read-only overlays and spawn move export

| Item | Status | Evidence | Notes |
|---|---|---|---|
| `TcSpawnSource`: `id`, `spawnDifficulties`, phase columns, `modelid` override | done | `TcSpawnSource.cs:22-60,63-119` | |
| Pools from `pool_members`; events from `game_event_*` | done | `TcSpawnSource.cs:160-206` | |
| Object-visibility conditions (`conditions` source 32) | done (unplanned detail) | `TcSpawnSource.cs:16,182` | |
| `SpawnPreview` dialect filter (difficulty ID, phase ID) | partial | `Foundation/SpawnPreview.cs:26-29,75-79`, `TcSpawnSource.cs:135-139` | **PhaseGroup is ignored**: a spawn with `PhaseId=0, PhaseGroup≠0` is hidden whenever masks are respected, and there's no phase-group preview. `phaseUseFlags` are read but don't affect filtering. |
| Inspector shows TC fields | done | `CreatureOverlay.cs:598`, `GameObjectOverlay.cs:364`, `SpawnPreview.cs:139-141`, `SceneSidebar.cs:364-417` | Difficulty list is hardcoded (`SceneSidebar.cs:364-374`). |
| `TcGraveyardSource` (`world_safe_locs` + `graveyard_zone` + team conditions) | done | `TcGraveyardSource.cs` | Only rows linked from `graveyard_zone` are shown (`:52`). |
| `TcTeleportSource` reads `game_tele` | deviates | `TcDBSource.cs:18,26-27` reuses `AcTeleportSource` | It works because the schema is the same, but there's no `TcTeleportSource`. |
| `Continents` from Map DB2 | done | `TerrainViewer.Teleports.cs:89-92` | Done in the viewer, not in the source. |
| Area triggers from `AreaTrigger.db2` + `hotfixes.area_trigger` | done | `TcAreaTriggerSource.cs:19-60`, `TcHotfixSource.cs:65-84` | Polygon/cylinder shapes are skipped (`:38`). The trigger's own `PhaseID`/`PhaseGroupID` aren't filtered by the previewed phase. |
| Links: `areatrigger_teleport → world_safe_locs`, scripts, involvedrelation, tavern, `smart_scripts` type 2 | done | `TcAreaTriggerSource.cs:91-144` | |
| TC server-spawned `areatrigger` as a secondary overlay | not started | `TcAreaTriggerSource.cs:12-13` says it is not read | |
| Export: creature and GO moves for TC | done | `Foundation/SqlDialect.cs` (`TcSqlDialect` reuses the AC move statements); no dialect gate in `CreatureOverlay.Editing.cs` / `GameObjectOverlay.Editing.cs` | Same columns in both cores, `ulong` guid. Test: `SqlDialectTests.TrinityCoreSpawnMovesUseTheSameStatementsWithBigGuids`. |
| `ISqlDialect` handles bigint guids | done | `Foundation/SqlDialect.cs` (`TcSqlDialect`) | TC moves reuse the AC statements; guids are `ulong`. Test: `SqlDialectTests.TrinityCoreSpawnMovesUseTheSameStatementsWithBigGuids`. |
| Tests (TcDb): Goldshire spawns, graveyard + faction, AT teleport, AT positions | done | `Tests/.../Db/TrinityReadDbTests.cs` | It uses AT 78 (Deadmines) instead of AT 4. |
| Visual check: `--client-shot-tile` with overlays | done | `TerrainViewer.ClientSmoke.cs:116-161` (`--client-shot-overlays`) | Run 2026-10-25 against retail 12.1.5.70575 + TDB 1210.26091: 178 creatures, 53 GameObjects, 25 graveyards, 102 triggers; screenshot inspected. |

### Phase 5: graveyard editing (TC)

| Item | Status | Evidence | Notes |
|---|---|---|---|
| Move updates `world_safe_locs` | done | `TcSqlDialect.Graveyard` (`Foundation/SqlDialect.cs`) | Updates `LocX/LocY/LocZ/Comment`. Facing isn't edited, because `GraveyardRecord` has no facing. |
| Warn when the ID is referenced elsewhere | done (teleports only) | `TcGraveyardSource.ReadTeleportUses`, `GraveyardSite.TeleportUses`, `GraveyardOverlay.Editing.MoveMessage`, inspector line in `GraveyardOverlay.Ui.cs` | Only `areatrigger_teleport.PortLocID` is counted; battleground start points and other references are not. No current TDB graveyard is also a teleport target (0 overlaps), so the warning hasn't been seen with real data. |
| Link edits to `graveyard_zone`; faction via `conditions` 27 | done | `TcSqlDialect.Graveyard` | One plain `CONDITION_TEAM` row per link, inserted, updated (`ConditionValue1` only) or deleted. A new row's preflight requires the link to have no conditions. A reverted delete re-inserts the row with a generated TDB-style comment (the original comment isn't read). Links with other conditions (Alterac Valley, other battlegrounds, PvP zones) are **locked**: `GraveyardSite.LockedZones`, and the overlay refuses faction changes and unlinking for them. |
| `ISqlDialect.GraveyardStatements` | done | `ISqlDialect.Graveyard` | Tests: `Sql/TcGraveyardExportTests.cs` (position, link add/remove, neutral→Alliance→Horde→neutral, comment), `TrinityReadDbTests.BattlegroundLinksAreLockedAndPlainTeamLinksAreNot`. The preflight guards were checked against the live rows for graveyard 106 through a read-only query. |
| Graveyards editable in TC | done | `GraveyardOverlay.Editing.cs` (dialect gate removed); smoke `ClientSmoke.cs:152-161` now asserts `graveyardsEditable=True` and passed | The locked-link controls and the teleport line in the graveyard inspector weren't visually inspected: the smoke frames Goldshire (no locked links, no teleport overlap). |

### Phase 6: creature properties (TC)

| Item | Status | Evidence | Notes |
|---|---|---|---|
| `TcTables` catalog (all listed tables) | not started | no `TcTables` in the repo | |
| TC `NpcSchema`/`CreatureSchema` (64-bit `npcflag`, Classification, `unit_flags3`, OptionNpc) | not started | — | |
| `StatsSection` ContentTuning | not started | — | |
| Vendor search via ItemSparse + `hotfixes.item_sparse` | not started | `TcDBSource.cs:51` throws; `IClientLookup.SearchItems` exists but isn't wired | |
| Tests | not started | — | |

### Phase 7: quests (TC)

| Item | Status | Evidence | Notes |
|---|---|---|---|
| `TcQuestSchema`, `quest_objectives` page, addon/request-items columns, ObjectID name lookup, `QuestModel.Check` per dialect, conditional tables, tests | not started | `TcDBSource.cs:42,52` throw | `quest_objectives` is in the schema-probe list only (`WorldDatabase.cs:139`). |

### Phase 8: SmartAI and speech (TC)

| Item | Status | Evidence | Notes |
|---|---|---|---|
| `SmartCatalog.Trinity`, TC `smart_scripts` columns, `creature_text.SoundPlayType`, BroadcastText search, hotfix export with `hotfix_data`, tests | not started | `TcDBSource.cs:43-47` throw | `TcAreaTriggerSource` already counts `smart_scripts` type-2 rows (read-only). |

### Phase 9: waypoints (TC)

| Item | Status | Evidence | Notes |
|---|---|---|---|
| `TcWaypointSource`, per-dialect `WaypointEdit`, `WaypointStatements`, re-enable layer, tests | not started | `TcDBSource.cs:41` throws; `SceneSidebar.cs:167-176` disables the layer | `waypoint_path(_node)` is in the schema-probe list only. |

## 2. Functional gaps (not in the plan, needed end to end)

1. **The schema abstraction is only half used.** Export and pending-change fingerprints now use each source's schema (step 2c). `ContentDrafts`/`InteractionDraft` still default to `AcTables` (`InteractionDraft.cs:32`). They need the source's catalog before any TC editor opens (Phase 6).
2. ~~**Error text is lost in the overlays.**~~ Fixed (step 1a).
3. ~~**A broken hotfixes database kills the area-trigger layer.**~~ Fixed (step 1b). `TestAsync` still only checks that the hotfixes database connects, not that its tables exist.
4. ~~**Hotfixes can't be turned off.**~~ Fixed (step 1c).
5. **Phase groups are not previewable.** See Phase 4. Spawns in a phase group only show with "respect masks" off. Area triggers ignore phase entirely.
6. **Drafts from the other core are dropped on profile switch.** `ConfirmDiscardDrafts` (`TerrainViewer.Ui.cs:471-507`) discards all drafts when switching client, even though `SourceKey` already separates them. That makes it impossible to keep AC and TC work in progress side by side. (This may be intended, so check it before changing.)
7. ~~**TC creature/GO move export is untested.**~~ Covered by `SqlDialectTests` (step 2).
8. **Locate / spawn search is AC-shaped for some paths.** `AcGraveyardSource.ReadHealers` is shared with TC (`TcGraveyardSource.cs:31`). It probes `creature.id1` and uses the AC/TC-shared npcflag bit `0x4000`. That works on current TDB, but the code lives in the AC source.
9. **Retail area names are missing.** `AreaLookup` is null for retail (`ClientServices.cs:69`), so any area-name display is blank in TC mode. Unverified which UI surfaces show it.
10. **The client lookup is built but not wired.** Item, spell and broadcast lookups from DB2 exist but no editor or tooltip uses them in TC mode, because the editors are gated off.
11. **The AC/TC check runs one way only.** Pointing Wrath mode at a TC database isn't caught up front (`RequireDialect` checks TC only).
12. **Stale docs.** `ClientServices.OpenRetail` doc comment (`:35-37`) says database features are off.

## 3. Unplanned TC-related work found in the code

- **Humanoid creature rendering** for retail/TC: `CreatureDisplayInfoExtra`, `ChrCustomization*` and `TextureFileData` tables (`Db2Tables.cs:34-42`), baked skins and geoset variants, and hotfixed displays keeping their extra (`RetailCatalogs.cs`, `TrinityReadDbTests.cs:151-169`). The plan only said "CreatureDisplayInfoExtra if needed".
- **Object-visibility conditions** (`conditions` source type 32) as the TC "conditional spawn" restriction (`TcSpawnSource.cs:16,182`).
- **`--client-shot-overlays` smoke** with a graveyard-editable assertion for TC (`TerrainViewer.ClientSmoke.cs:116-161`). `--client-shot*` runs now use throwaway drafts under `artifacts/drafts/` (`ContentDraftFiles.LocalPath`). Before this, they opened the user's real drafts, and a running viewer's lock made the graveyard draft fail to open.
- **Drafts are discarded on client switch, after a confirmation** (`TerrainViewer.Ui.cs:471-507`, commit `a672724`).
- **The retail test classes now run one at a time** (`[Collection(RetailData.Collection)]`), because `Db2Tables` is not thread-safe (see section 5).
- **The TC difficulty preview list** in the sidebar (`SceneSidebar.cs:364-374`).
- **GameObject move/turn drafts and export** (`GameObjectDraft.cs`, `SqlExport.cs:131-143`, `GameObjectOverlay.Editing.cs`). The plan listed GO move export as Phase 4 new work, but the full GO editing overlay and its smoke go beyond that.
- **`TcDBSource` explains each unbuilt editor** with an "X is not available for the TrinityCore world database yet" error, tested in `DatabaseSettingsTests.cs:74-79`.

## 4. Work in progress

These steps come before plan Phases 6–9. They fix bugs that already reach retail users and add the seam the editor phases need. Each row is updated as the work lands.

| Step | Status | Notes |
|---|---|---|
| 1a. Keep TrinityCore error text: `DatabaseFeatureException`, shown as-is by `DbErrors.Safe` | done | `Scripts/Data/DbLimits.cs`; used by `TcDBSource`, `TcAreaTriggerSource`, `DBSource.Open`, `WorldDatabase`. It derives from `Exception` (`InvalidDataException` is sealed); `DatabaseDiagnostics.Describe` shows it too. Tests: `DatabaseSettingsTests.StatusTextKeepsFeatureMessagesAndHidesParserInternals`, `TrinityCoreAreaTriggersWithoutClientDataSayWhy`. |
| 1b. Area-trigger hotfix read failure falls back to client AreaTrigger.db2 | done | `TcAreaTriggerSource.ReadHotfixes`. The fallback is silent (sources have no status channel). Test: `TrinityReadDbTests.AnUnreadableHotfixesDatabaseLeavesTheClientTriggers`. |
| 1c. Blank hotfixes field means "no hotfixes database" | done | `DatabaseDiagnostics.TestAsync` no longer rejects it; placeholder says optional. Test: `TrinityConnectionDbTests.ABlankHotfixesDatabaseIsOptional`. |
| 2a. `ISqlDialect` behind `IWorldSchema.Sql`; `SqlExport.Build` takes an `IWorldSchema` (AC output byte-identical) | done | `Foundation/SqlDialect.cs` (`SqlStep`, `ISqlDialect`, `AcSqlDialect`, `TcSqlDialect`); the AC statements moved unchanged from `SqlExport`. `SqlExportScriptTests` and `SqlExportTests` still pass. |
| 2b. `TcSchema` with empty table catalog + `TcSqlDialect` (moves; refuses waypoints/rows); `TcDBSource.Schema` stops throwing | done | `Foundation/WorldDialect.cs` (`TcSchema`, `TcTables`, `WorldSchemas.For`). Tests: `Sql/SqlDialectTests.cs`. |
| 2c. Export uses each source's schema (`BuildSqlExports`) | done | `Foundation/SourceDialects.cs` remembers each opened source's dialect in `sources.json` (a source key is a one-way hash, so it can't be decoded later). `TerrainViewer.Export.cs` records the open source on every pending refresh and passes `SchemaFor` to `PendingChanges.Collect` and `BuildSqlExports`. Unknown sources stay AzerothCore. The editors still build `InteractionDraft` with the AC catalog; they're gated off in TC until Phase 6. |
| 3a. TC graveyard statements: `world_safe_locs`, `graveyard_zone`, team `conditions` row (type 27) | done | Plan Phase 5; see the Phase 5 table. |
| 3b. Warn when a safe location is also a teleport target | done | Plan Phase 5; teleports only. |
| 3c. Enable graveyard editing in TC; flip the overlay smoke check | done | Plan Phase 5. Graveyard inspector not visually inspected. |

Next: plan Phases 6–9 in order. Run the data-correctness review (Part 2 of the prompt below) before Phase 6. Open items from this pass: a visual check of the graveyard inspector on a locked link (e.g. Alterac Valley graveyard 169), and the gaps in section 2.

## 5. Other open work (moved from `docs/TODO.md`)

Work that is known to be unfinished outside the TrinityCore plan. Deferred tests live in `docs/NEEDED-TESTS.md`.

### Tests

- **`Db2Tables` is not thread-safe, and xUnit runs test classes in parallel.** Retail test classes shared `RetailData.Tables` concurrently. Adding a TrinityCore + retail test made 8 tests fail with "Operations that change non-concurrent collections must have exclusive access". Every class that reads `RetailData` now joins the `RetailClient` collection (`[Collection(RetailData.Collection)]`), so those classes run one at a time. A new retail test class must add the attribute too. Making `Db2Tables.Table` thread-safe (a lock around the cache) would remove the trap. The viewer is unaffected today: the creature catalog loads its tables on the open worker and builds its indexes behind thread-safe `Lazy` values.
- `CLAUDE.md` says a command-line `--filter` overrides `default.runsettings`; it does not. Corpus/KnownBug tests still need `--settings corpus.runsettings` (or `all.runsettings`) alongside the filter.
- `RetailCorpusTests.EveryMapWdtParsesAndOneTilePerTerrainMapDecodes` still has comments saying global-WMO placements are a gap "until the retail WMO milestone", but that milestone is done. Re-check the accepted-failure filter and tighten or remove it.

### Retail creatures

- Equipment is not composed: `NPCModelItemSlotDisplayInfo` → `ItemDisplayInfo` (weapons, shoulders, helmets, capes and the armour geosets they switch).
- Customization elements other than geosets and hair/eye materials are ignored: bone sets (`ChrCustomizationBoneSet`, e.g. body proportions), skinned models (`ChrCustomizationSkinnedModel`, separate hair/horn models), conditional models, display info, particle colours.
- Groups a display does not choose show variant 1, except ears (7) and head (32), which show variant 2. That rule comes from the data (variant 1 is an empty stub in those groups) and the wiki's DNE notes, not from a documented client rule.
- A humanoid whose extra has no bake material is drawn without customization, so every geoset shows.
- Parent skeletons (`.skel` `SKPD`) are not followed.
- Retail `AnimationNames` is empty (no AnimationData DB2 loaded), so the animation preview menu shows IDs only.
- M2: multitexture batches use their base texture, and spline tracks are interpolated linearly.

### Retail terrain and rendering

- Height texturing (MHID/MTXP) and MAMP scale are not supported. Only the first 4 of up to 8 layers are drawn.
- Uncompressed alpha size follows the WDT big-alpha flag. If a map's alpha spans ever disagree with it (not observed on 12.1.5), decide the size per layer from the span.
- Placements that name models by path resolve only through the community listfile.

---

## Original task prompt

Read TC-DB-Update-Plan.md, the plan for adding TrinityCore database
support. Do not modify any code during this task.

### Part 1: Plan status

Verify every item in the plan against the actual code. Don't rely on
checkboxes, comments, or commit messages. Confirm each item by finding
the implementation.

Write the results to docs/tc-status.md with:

1. A status table: each plan item, its status (done / partial / not
   started / deviates from plan), and the file paths that show it.
   For "partial" and "deviates," explain exactly what's missing or
   different.
2. Functional gaps: things the plan doesn't mention but TrinityCore
   support needs to actually work end to end, such as UI paths,
   validation, import/export, or features that currently only work
   with AzerothCore.
3. Unplanned work: anything TC-related in the code that isn't in the
   plan.

Mark anything you can't confirm as "unverified" instead of guessing.

### Part 2: Architecture review

Once Part 1 is done, spawn subagents in parallel, each with one focus
and the status doc as context:

- Abstraction: Is the AzerothCore/TrinityCore split cleanly separated,
  or do AC-specific assumptions leak into shared code? Would adding a
  third core be straightforward?
- Data correctness: Where do the two schemas differ, and does the code
  handle each difference correctly? Look for hardcoded columns, enums,
  IDs, or ranges that are only valid for one core.
- Duplication and consistency: Is logic copy-pasted between the AC and
  TC paths instead of shared? Are similar problems solved different ways?
- Robustness and tests: Error handling for missing or mismatched schema,
  connection or version detection, and how much of the TC path is
  covered by tests.

Each subagent should report findings with file paths, a severity
(high / medium / low), and a suggested fix.

### Output

Combine the findings into docs/tc-review.md, deduplicated and sorted
by severity. End with a short recommended order of work covering both
the unfinished plan items and the architecture fixes. Then stop and
wait for my review before changing anything.
