# Status

Status of the TrinityCore database work (phases from `docs/TC-DB-Update-Plan.md`) and the project's other open work. Target: TrinityCore `master`, TDB 1210.26091; retail client 12.1.5.70575.

Status values: **done**, **partial**, **not started**, **deviates** (built differently from the plan), **unverified**.

## Overhaul summary

| Phase | Status |
|---|---|
| 1–5: interface extraction, connection and hotfixes, retail catalogs, read-only overlays and spawn moves, graveyard editing | done |
| 6a: shared creature page descriptors (`CreaturePage`) | in progress: Template, vendor, gossip, trainer and quest-link descriptors are built for both dialects; legacy stats/schema extraction remains |
| 6: creature properties | partial: TC read done; template, difficulty, vendor, gossip, trainer and quest-link editing done |
| 7–9: quests, SmartAI and speech, waypoints | not started |

### Coexisting forms

| Legacy form | Target | Where the legacy form remains |
|---|---|---|
| Static AC catalogs `InteractionTables`, `SmartCatalog`, `QuestModel`, `NpcFlags` | `IDBSource.Schema` / `IWorldSchema` (descriptors added per phase) | Remaining references: `Foundation/` (`NpcInteraction`, `QuestModel`, `SmartBehavior`, `SmartRuleEdits`, `SpeechEdits`), `Scripts/Data/AzerothCore/`, `Scripts/Ui/` (quest, behavior and creature-property panels), content smokes. Converted as Phases 6–8 reach each area. |
| `Ac*Source(DatabaseSettings)` constructors | `DBSource.Open(WorldConnection, IClientLookup?)` | Used only by the AC Db tests (`Tests/ACWorldEdit.Tests/Db/`). To remove: switch those tests to `DBSource.Open`, then delete the constructors. |
| Pre-profile `[database]` settings section | `[database.wrath]` / `[database.retail]` | `SettingsResolver.Connection` falls back to it for AzerothCore. To remove once saved settings have moved to the per-profile sections. |

### Gated or inconsistent right now

- TrinityCore mode: quest and behavior editors are off (`TerrainViewer.ContentEditors`), the waypoint layer is off, and creature properties edit the template, vendor, gossip and trainer pages (`CreatureContext.TcTemplateEditable` / `TcVendorEditable` / `TcGossipEditable` / `TcTrainerEditable` / `TcQuestsEditable`); quest content has no TC editor until Phase 7.
- Export treats a source with no `SourceDialects` entry as AzerothCore.

## Where work stopped

- Added missing difficulty rows on the TC Template page: an "Add difficulty…" list of the common difficulties the creature lacks, plus an ID box (0–255). The new `creature_template_difficulty` row copies the shown difficulty, or uses the trinitycore.info defaults (modifiers 1, everything else 0) when the creature has none; it is then shown. `DifficultySpec` gained `NewRow`, `Common`, `MaxId`, `Select`, `CannotAdd` and `Add`; `TcDifficultyTests` covers defaults, copying, refusals and history/export. Gaps: removing a difficulty row, and checking that the ID is a real Difficulty.db2 row.
- Fixed TC trainer types: `trainer.Type` is 0 None, 1 Talent, 2 Tradeskill, 3 Pet (trinitycore.info), not Class/Tradeskill/Pet; blacksmith trainers showed as Pet.
- Ran the TC screenshot workflow against the retail client: gossip, difficulty add, vendor, trainer and quest-link checks pass (`CLIENT_SHOT_DIFFICULTY_EDIT_OK`, `CLIENT_SHOT_TRAINER_EDIT_OK`, `CLIENT_SHOT_QUEST_EDIT_OK`). The difficulty, trainer and quest pages were inspected. The AzerothCore workflow still passes. Default suite: 732 passed, 4 skipped.
- Enabled TC quest-link editing: remove starter/ender links, and link a quest from a `quest_template` title/ID search (`TcDBSource.SearchQuestsAsync`, shared with AC's `SearchQuests`) as starter, ender or both, with undo/redo and SQL export. `TcQuestLinkRules` checks the quest ID is set and exists in `quest_template` (the search result seeds the lookup before the next reload). `TcQuestLinkTests` covers validation, persistence, history and export. Default suite: 726 passed, 4 skipped. Gaps: quest content stays Phase 7, and link availability conditions are not shown.
- Enabled TC trainer editing: trainer type and greeting, spell fields (cost, level, skill, `ReqAbility1-3`), remove, client spell search and add by ID, with undo/redo and SQL export. `TcTrainerRules` checks type, spell existence when the client is open, required level and required abilities. A warning shows when several `creature_trainer` bindings share the trainer (`TcTrainerView.SharedBy`, read at load).
- `TcTrainerTests` covers validation, persistence, history and export. Default suite: 723 passed, 4 skipped. The AzerothCore screenshot workflow still passes.
- Trainer gaps: assigning or creating trainers and rebinding gossip options (`creature_trainer`), shared-trainer copying, skill-line existence checks, and `VerifiedBuild`. Spell edits still apply to every creature that shares the trainer.

- Enabled TC gossip editing using `GossipPageSpec`: option fields/add/remove, menu selection and Back/submenu navigation, option/confirmation broadcast IDs, and all eight greeting broadcast/weight pairs. Literal text edits clear the corresponding override atomically. `TcGossipRules` checks service values, confirmation fields and weighted greetings; asynchronous reference checks use `IDBSource.MenuExistsAsync` and exact-ID `SearchBroadcastAsync`, dropping results when the creature changes.
- `TcGossipTests` covers validation, persistence, history and apply/revert SQL; `TrinityReadDbTests.GossipReferenceChecksResolveMenusAndExactBroadcastIds` covers live reference reads. Build passed; default suite: 717 passed, 4 skipped. Godot's TC screenshot workflow exercises the editor and saves the option, submenu and expanded greeting pages for inspection.
- Gossip gaps: root assignment, creating greeting rows, shared-menu copying, condition/SmartAI remapping and language/POI/client-option/spell/icon existence checks. Removing an option retains trainer bindings and other external references; those editors remain read-only. Broadcast content/hotfix export remains Phase 8; this step edits IDs and weights.

- Enabled TC vendor editing through the shared descriptors: add by retail item search or item/currency ID, edit fields, remove, undo/redo and SQL export. Search uses `IClientLookup.SearchItems` on a worker, caps terms/results, and drops results on creature changes.
- Added `Foundation/TrinityCore/TcVendorRules`, checked during TC creature commits. Validates the Vendor flag, signed smallint slots, item existence when the client is available, type, stock/restock pairing for items, positive currency quantity, bonus-list integer syntax and IgnoreFiltering. Stock/restock save together. Negative item IDs remain reference rows.
- `TcVendorTests` covers rejection without history changes, stock and retail fields, currency rules, duplicate/key collisions, reopen, undo/redo, removal and TC SQL export. Build passed; default suite: 705 passed, 4 skipped.
- Currency, extended-cost, bonus-list and player-condition existence checks remain open; their lookup catalogs are not wired. TC shared vendor references are shown without expansion. Item icons, detailed retail tooltips and `hotfixes.item_sparse` overrides remain open.
- Extended `--client-shot --client-shot-overlays` to exercise TC item search/add, stock validation and undo/redo, with an additional search screenshot. Both dialects passed and the vendor pages were visually inspected (`artifacts/vendor-retail-editor.png`, `artifacts/vendor-retail-search.png`, `artifacts/vendor-wrath-editor.png`).
- Existing gaps: AC gossip `BoxBroadcastTextID`, AC trainer `ReqAbility1-3`, and trainer/quest-link build metadata remain outside the catalogs. `--interaction-smoke` retains its previously reported window-open failure; the screenshot workflow opens the windows directly.
- Fixed false creature-property conflicts from discarded edits: conflict detection, effective views and exports now use remaining row changes, and later edits take a fresh baseline after cancellation. Existing saved histories need no cleanup; `InteractionConflictTests` covers both dialects.
- Next: continue remaining Phase 6 work (legacy schema extraction, `AcStatsSection` via ContentTuning, gossip and vendor gaps), then Phase 7.

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
| `TcSchema` (`TcTables` catalog, creature tables only), returned by `TcDBSource.Schema` | done | TC quest, SmartAI and waypoint edits are refused instead of exported as AC SQL. |
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
| Hotfix rows with several `VerifiedBuild` versions | partial | Broadcast text takes the highest build; display and area-trigger reads take whichever row comes last. |
| User-facing error text in the status bar (`DatabaseFeatureException`) | done | |

## Retail client data (plan Phase 3)

| Feature | Status | Notes |
|---|---|---|
| WoWDBDefs submodule and DB2 FileDataIDs | done | ID check by corpus test: unverified (needs client data). |
| Retail creature and GameObject display catalogs | done | Includes humanoids (baked skins, customization geosets, hair/eye textures). |
| `hotfixes.creature_display_info` overrides | done | Reads 3 of 4 texture columns; `creature_display_info_extra` not read (empty in TDB). |
| Other hotfix overrides (`gameobject_display_info`, `item_sparse`, `spell_name`, `broadcast_text`, `content_tuning`) | not started | `RetailGameObjectCatalog.Override` exists but is never called. |
| `IClientLookup` (area triggers, broadcast text, items, spells, creature scaling) | done | Item names/search serve the TC vendor editor; spell names serve read-only trainer pages; broadcast names and exact-ID checks serve gossip editing. `ITcStatData` serves ContentTuning, ExpectedStat, ExpectedStatMod and ContentTuningXExpected rows (corpus-tested), used by the TC stats calculation. |
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
| Overlay smoke (`--client-shot-overlays`) | done | Checks either dialect's creature pages and saves gossip, Template, vendor, trainer and quest screenshots (`client-shot-creature-*.png`), along with overlays and editable graveyards. In TC it also exercises the gossip, difficulty, vendor, trainer and quest-link editors. |

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
| Creature properties: `TcTables` catalog | done | Template, difficulty, gossip (menus, texts, options), trainers (`creature_trainer`, `trainer`, `trainer_spell`), vendor, quest links, spell clicks. |
| Creature properties: TC read (`TcInteractionSource`, `TcCreatureView`) | done | All menus (1:N) and submenus, trainers bound to gossip options, vendor, quest titles; texts from `hotfixes.broadcast_text`, then client DB2. |
| Creature properties: read-only TC sections | done | Template with a difficulty picker, gossip (every menu and submenu), trainers with their gossip option, vendor, quests. The window lists only the open database's sections; checked in `--client-shot-overlays` on Innkeeper Farley. |
| Text placeholders in TC gossip text | not started | `$B`, `$N` and similar tokens show raw. |
| Item icons in the TC vendor page | not started | Names and quality only; icons need the ItemSparse/Item icon FileDataID wiring. |
| Creature properties: TC editing | partial | Template page edits `creature_template` and the picked `creature_template_difficulty` row (drafts, SQL export, tests). A missing difficulty row can be added (copied from the shown one, or with defaults). Vendor supports item search/add, currency IDs, fields, removal, validation and history/export. Gossip supports option fields/add/remove, root/submenu selection and Back navigation, option/confirmation broadcast references, and all eight greeting ID/weight pairs. Trainer supports type, greeting and spell edits (see Where work stopped). Quest links support remove and add from a quest search. |
| Shared page descriptors: groups of row-bound fields and item lists, filled in per dialect; one renderer | partial | `CreaturePage` / `FieldSpec` / `PageRenderer` draw both Template pages. `ItemListSpec` / `VendorPage` draw the AC vendor editor and TC vendor editors through `RenderItems`; AC's compact read-only view stays specialized. `CreaturePageTests`, `ItemListTests`, `GossipPageTests` and `CreatureServicePageTests` guard the descriptors and edits. Gossip uses `GossipPageSpec` for option fields, text links and greeting storage; specialized greeting controls remain. `TrainerPageSpec` covers trainer storage, bindings and spell fields; `QuestLinksPageSpec` covers starter/ender lists and grouped role edits. AC keeps specialized quest icons and navigation; TC quest links use the same descriptors through `RenderItems`. |
| TC 64-bit `npcflag` bits and Classification | done | Folded into `TcCreaturePage` (`CreatureSchema` removed). | `IWorldSchema.Creature`; `unit_flags*` edit as raw hex, OptionNpc and `NpcSchema` come with the gossip editor. |
| `AcStatsSection` via ContentTuning | not started | Phase 6. |
| Vendor search and item tooltips via ItemSparse + `hotfixes.item_sparse` | partial | Client ItemSparse search by name/ID is wired; icons, detailed tooltips and hotfix overrides remain open. |
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
| TC `npcflag` bits | deviates | Checked against TrinityCore master `UnitDefines.h`: the low 32 bits are `NPCFlags` (vendor/trainer/quest-giver bits match AC; 0x4 and 0x8000000+ differ), the high 32 bits are `NPCFlags2`. Named in `TcCreatureView.DescribeNpcFlags`. |

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
