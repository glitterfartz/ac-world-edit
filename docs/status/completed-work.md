# Completed work

What is built, per phase and area, with the tests and smokes that prove it. Only finished work is here;
everything still open is in [editor-plan.md](../plans/editor-plan.md), and how the built parts work is in
[architecture/](../architecture/overview.md). Schema differences are in
[reference/database-differences.md](../reference/database-differences.md).

Target: TrinityCore `master` `world` (TDB 1210.26091) plus `hotfixes`; retail client 12.1.5.70575.

## Phases 1–9

- **Phase 1: interface extraction.** `IDBSource`, `IWorldSchema`, dialect selection and the shared read
  session. See [editing-and-export.md](../architecture/editing-and-export.md).
- **Phase 2: connection and hotfixes.** World connection with write access, a second hotfixes database on
  the same connection, falling back to client DB2.
- **Phase 3: retail catalogs.** TrinityCore table catalogs, DB2 table mapping, difficulty, area and content
  tuning reads.
- **Phase 4: read-only overlays and spawn moves.** Creature, GameObject, graveyard, area-trigger and
  teleport overlays; spawns could be moved through a draft and exported.
- **Phase 5: graveyard editing.** Graveyard links, safe locations and healer lookups, with validation and
  `--graveyard-smoke`.
- **Phase 6: creature properties (TrinityCore).** Template, stats, vendor, gossip, trainer and quest-link
  pages edit and export, with reference checks against client and database.
- **Phase 7: quests (TrinityCore).** The quest editor edits and exports the five quest tables.
- **Phase 8: SmartAI and speech (TrinityCore).** A shared behavior panel edits a creature's
  `smart_scripts` rules, timed action lists, AIName and `creature_text` lines and exports them; AreaTrigger
  rules edit client triggers (source type 2) and server-spawned trigger templates (11, 12).
- **Phase 9: waypoints (TrinityCore).** The waypoint layer reads `waypoint_path` and `waypoint_path_node`;
  a node's position, facing and delay edit and export.

## Phase 6: creature properties

| Item | What landed |
| --- | --- |
| Schema-driven section registry and field groups | Both dialects' Template and Stats pages take their groups and fields from the schema (`IWorldSchema.TemplatePage` / `StatsPage`, drawn by one `PageRenderer`), so a field is added in one descriptor; AC's stats page lost its hand-built pickers and its level and multiplier checks became `AcStatsPage.Check`. Gossip, Trainer, Vendor, Quests and Behavior stay per-dialect (their editors differ in kind); `CreatureSectionRegistry` lists the section classes and the `Supports(dialect)` filter picks them. Tests: `StatsPageTests`, `CreaturePageTests`. |
| Gossip | Menus and broadcast texts are searched and picked. New greeting, option, confirmation text or greeting slot stages a `hotfixes.broadcast_text` row (VerifiedBuild -1, numbered above the hotfixes database, the client's BroadcastText table and the draft) plus its `hotfix_data` row (TableHash 35137211), exported with `hotfixes.`-qualified names. `OverrideIconID` is checked as a FileDataID against the client's files. Removing a TC option removes the open creature's `creature_trainer` binding (`ItemListSpec.Dependents`). Copying a menu copies its texts' and options' `conditions` rows (sources 14/15) and re-points the creature's own `GOSSIP_SELECT` rules. The TC page mirrors AC's flow (greeting box, variants behind a button, option cards with service, submenu, confirmation and Advanced). Tests: `PhaseSixRealDataTests` (Anachronos 15192, menu 6539, 43 AC / 51 TC conditions and 16 gossip-select rules against both live databases). |
| Stats calculator | Ported from `Creature::ApplyLevelScaling`, `UpdateLevelDependantStats`, `CalculateMinMaxDamage` and `DB2Manager::EvaluateExpectedStat` in TrinityCore master: content tuning from map difficulty then area (walking `ParentAreaID`, except critters), the server's Mythic+ milestone season 0, `Rate.Creature.*.HP` from `settings.cfg` typed in the stats page, melee and ranged hit lines with attack power and swing time. `creature_template` gained `BaseAttackTime`, `RangeAttackTime`, `BaseVariance` and `RangeVariance`; `MythicPlusSeason` and `MapDifficulty` are in `Db2Tables` and loaded by `RetailCorpusTests`. |
| AC gaps | `gossip_menu_option.BoxBroadcastTextID` and `trainer_spell.ReqAbility1-3` are in `AcTables`, the editor pages, new-row defaults, the confirmation text lookup and the loader's column check. `VerifiedBuild` is deliberately left out of every catalog. |
| Headless smokes | `--interaction-smoke` and `--behavior-smoke` pass headless against AzerothCore, sending clicks through `GetViewport().GetFinalTransform()` (run with `ACWORLD_CLIENT_PROFILE=wrath`). |

## Phase 7: quests

| Item | What landed |
| --- | --- |
| Names on the quest pages | `RecordPicker` names and illustrates items, spells, currencies, skills, factions, mail templates, titles and zones, with client icons and WoW-style tooltips. |
| Race picker | Everyone, Alliance, Horde or per-race checkboxes; TC's 64-bit mask uses `ChrRaces` names when the retail client is open and a shared fallback list otherwise. |
| Quest givers | Creatures and GameObjects share one two-tab picker with model thumbnails; either kind can offer or complete. |

## Phase 8: SmartAI and speech

- Shared behavior panel over `smart_scripts`, the timed action lists they run, AIName and `creature_text`,
  taking `SmartCatalog` from the connection's dialect.
- AreaTrigger rules for TrinityCore: client triggers (type 2, `areatrigger_scripts` SmartTrigger) and
  spawned trigger templates (11, or 12 when custom, edited through the `areatrigger_ai` column group) read,
  validate against TC's per-type event masks and export.
- TC client smoke stages 70–72: Hogger's behavior and speech, a new rule, validation, `Difficulties`, undo.
- `--areatrigger-smoke` runs in both dialects (TrinityCore: Stormwind spawn 290 and its template's rule).

## Phase 9: waypoints

| Item | What landed |
| --- | --- |
| Path-level properties | TrinityCore's `MoveType`, `Velocity` and `Flags` on `waypoint_path` edit on the path card (empty velocity means each creature's own speed), with a warning counting the spawns and SmartAI rules that share the path. |
| Add, delete and reorder nodes | Nodes append (click the ground), insert (Alt-click the nearest segment) and delete (followers renumber) as shared-draft row edits; new paths start at the selected creature and a path deletes with its header. Waypoints moved from `WaypointDraft` onto `RowEdit` with a one-time import of the old draft. |
| Path links | "Link to path…" (spawn and template, re-attaching an orphan), "New path", "Unlink" and "Delete path" (blocked while anything else uses it, unlinking the spawn in the same command). The creature delete dialog warns when the creature's path would be left unused. |
| Smokes | TC client smoke stages 80–82: select a Goldshire path point, check editable fields, move, delay, undo, redo, revert and the TC export. |

## Phase 10: editor foundation

| Item | What landed |
| --- | --- |
| Nullable columns (`TableSchema.Nullable`, `Row.NullValue`) | Row, loader and export; TC spawn overrides inherit with NULL. |
| Spawn and addon tables in `AcTables` / `TcTables` | `creature`, `gameobject`, `creature_addon`, `gameobject_addon`. |
| `IdAllocator`, `IDBSource.MaxIdAsync`, `InteractionDraft.MaxStaged` | Next free ID per (table, key column) above the database max and staged inserts. |
| `SpawnEdits` | Place, duplicate, move and delete as row edits, with `References`. Tests: `SpawnEditsTests`, including move-then-delete. |
| `SpawnPoses` and `IDBSource.ReadSpawnRowsAsync` | Spawn poses as `creature` / `gameobject` row edits on the shared content draft; the old `creature-positions.json` / `gameobject-positions.json` drafts are abandoned, not migrated. Tests: `SpawnPosesTests`, `SpawnRowDbTests` (live, both databases). |
| Overlays on `SpawnPoses` | `CreatureOverlay` and `GameObjectOverlay` (`Attach(drafts)`, row loaded on select) move through `SpawnPoses`; `CreatureDraft`, `GameObjectDraft`, `CreatureMove`, `GameObjectMove` and the dialects' move statements are gone; `PendingChanges.Collect` and `SqlExport.Build` lost their move parameters. |
| `ConditionCatalog` | 56 AzerothCore and 61 TrinityCore condition types with sentences and parameter kinds, and 31 / 36 source types with key-column labels, generated from each server's `ConditionMgr.h` by `Tools/generate_condition_catalog.py`. Tests: `ConditionCatalogTests`. |
| Entity page shell | `EntityPage` plus `PropertiesWindow` (frame, section picker, Undo/Redo, status), `EntitySection`, `IEntityContext` and `PageRenderer` in `Scripts/Ui/Properties`. Creature and GameObject windows are its first two clients, each one context, one section base and one window. |
| Content browser | `ContentBrowser` (View → Content browser, Ctrl+B): bottom panel with Creatures and Game objects tabs, name/entry search and a type filter over `IDBSource.SearchTemplatesAsync`. Double-click opens the matching properties window; dragging a hit into the world places a spawn. Each hit shows a model thumbnail rendered off-screen through `ModelLibrary` and cached under `user://thumbs/<profile>/`. Tests: `TemplateSearchDbTests`, `--content-browser-smoke`. |

## Phase 11: spawn authoring

| Item | What landed |
| --- | --- |
| Placed and deleted spawns draw | `CreatureOverlay.PlaceAsync` reads the template (name, model, equipment, movement) and the next GUID, places the spawn as one draft command and selects it once drawn; undo, redo and Pending Changes discards redraw. Staged spawns skip the preview filters. Tests: `StagedSpawnTests`, `SpawnRowDbTests`, `--spawn-authoring-smoke` on both dialects. |
| Duplicate and delete | Edit menu (Ctrl+D, Del), toolbar and inspector buttons; `SpawnPoses.Delete` / `Duplicate`; the spawn read includes the addon row. |
| Spawn rules (`SpawnRules`) | Row-level rules run on every spawn command in both dialects: template, map, difficulty per map, phase, movement type vs wander radius, equipment set. |
| Spawn inspector | `SpawnInspector` (Scripts/Ui) in both inspectors from `SpawnFields`: respawn, wander radius (with a viewport ring), movement, visibility, difficulty, emote, flags and template overrides, with inherited values faded, a "Use template" button and a refusal that says why and snaps back. Named controls replaced the raw fields (spinner, boxed flag checkboxes with per-flag tooltips, dropdowns and pickers). Tests: `SpawnFieldsTests`, `SpawnRowDbTests`, `--spawn-authoring-smoke`. |
| Spawn addon (Appearance) | Creature: mount model and creature, stance, drawn weapons, emote, auras; GameObject: invisibility. The row is inserted on first edit and updated afterwards; AC's stance and weapons are the low bytes of `bytes1` / `bytes2`, written without touching the other bytes. The loader refuses a stance above 9, weapons above 2 and repeated or non-numeric auras. |
| Template vs spawn scope | The creature window edits the template and says so in its title and subtitle; the right-hand inspector edits one spawn. The template's **Appearance** tab edits `creature_template_addon` and counts the spawns that follow it and those with their own row. A spawn's `creature_addon` replaces the template's completely: fields start from the template, stay read-only until the addon is read, show inherited values faded, and "Use the template's appearance" keeps a row that holds a waypoint path. Markers play stance and emote and ride their mount, glued to the animated `MountMain` seat bone (shared `creature_clock`), each mount at its own scale, flapping when airborne if the model has a flight clip. Tests: `SpawnAppearanceTests`, `TemplateAddonTests`, `ScopeTextTests`, `--spawn-authoring-smoke`, screenshots `spawn-authoring-template-window.png` and `-appearance.png`. |
| Place from a template by dragging | Dragging a content browser row into the world arms `PlacementTool` (`TemplateList` carries the template, `ViewportDropSurface` receives the drop): a ghost follows the cursor over the ground, dropping places the spawn facing the camera, Shift keeps placing, Esc stops. The ground is the terrain height map falling back to the building-floor probe, then lifted onto a floor within a few yards. |
| Multi-select | `SpawnGroupSelection` (a ring under each extra member), `SpawnPoses.EditMany` / `DeleteMany` / `DuplicateMany` / `NextGuids`; Ctrl-click, Shift-drag box, group copy, delete and move as one undoable command, one kind at a time. Ctrl-clicking the other kind switches the selection; a gizmo handle press wins over Ctrl and Shift. Tests: `StagedSpawnTests`, `--spawn-authoring-smoke`. |
| Delete impact and cleanup | `IDBSource.CountSpawnReferencesAsync` / `ReadSpawnReferencesAsync` (`DbReferences`) count the rows that name a spawn (pools, spawn groups, events, equipment swaps, linked respawns, formations, per-GUID SmartAI), counting distinct rows so the dialog's numbers equal what cleanup deletes. The dialog offers **Remove these too**: those rows plus a waypoint path nothing else walks delete in the same undoable command. A read bounded at 500 rows offers no cleanup rather than a partial one. Tests: `SpawnImpactDbTests`, `ReferenceCleanupDbTests` (both dialects), `SpawnImpactTests`, `DeleteImpactDialogTests`, `--spawn-authoring-smoke`. |
| Template "Used by" page | `UsedBySection` (creature) and `GameObjectUsedBySection` list what names a template through `TemplateReferences` and `DbTemplateReferences` (one `COUNT(*)` per reference table, missing tables skipped): spawns, per-template SmartAI, loot, pickpocketing and skinning loot, vendor lists and quest links. One `ReferenceCountCard` renders them and says so when the read failed. Tests: `TemplateReferencesTests`, `TemplateReferencesDbTests`, `UsedBySectionTests`. |
| Retail graveyard duplicate links | Duplicate ghost-zone links are refused (unknown-area check still missing, see the plan). |

## Phase 12: GameObjects (built parts)

Generated per-dialect `AcGameObjectTypes` / `TcGameObjectTypes` catalogues (`Tools/generate_gameobject_catalog.py`),
`gameobject_template`, `gameobject_template_addon`, `gameobject_queststarter` / `questender` / `questitem` in
both table catalogs, `DbGameObjectTemplate` and `DbGameObjectSpawns`, and `GameObjectPropertiesWindow` with
Template, Type properties, Quests and Spawns pages. The content browser, the inspector's Properties button
and a short right-click open the window.

## Phase 14: movement (built parts)

Node move/facing/delay, add/insert/delete/renumber, new paths, path-level properties, spawn and template
link/unlink, SmartAI path users, formations (membership, offsets, group AI, turn-around points, leader lines,
polar drag, duplicate remap, leader picked by GUID or viewport click) and the preview animation.

## Visual scripting (Milestones 0–3)

The scripting pipeline, persistence layer, Godot editor MVP and authoring depth are built; typed
entity pickers and domain packs start at Milestone 4.

| Area | What landed |
| --- | --- |
| Research (M0) | GraphEdit 4.7.2 canvas contract confirmed (slot rows, port indices, `ConnectionRequest` veto, zoom/scroll maths, frames, shortcut isolation, 200-node scale) and live ALE facts re-checked (Lua 5.2, compatibility mode, global `RemoveEventById`, timer callback shape, registration-abort semantics, `.reload ale`). Recorded in [architecture/scripting.md](../architecture/scripting.md). |
| Document model | `ScriptDocument`/graph/node/pin/edge/comment/variable/function with camelCase JSON, canonical sorted-key SHA-256, unknown-field preservation at every level, and a format gate (`CurrentFormat = 1`). |
| Catalog | 25 curated semantic capabilities, 230 generated ALE events with registrations/scope/argument order, `PowerType`/`QuestStatus` enums, strict loader integrity checks, and `Tools/generate_script_catalog.py`. Listed in [reference/scripting-capabilities.md](../reference/scripting-capabilities.md). |
| Validation | Type registry (pin ids/colours/assignability) and the full `SC01xx`–`SC06xx` diagnostic set: missing/duplicate nodes and pins, wiring and cycles, scope, returns, variables/functions, reachability, locals and large integers. |
| Compiler | `ScriptIrBuilder` → `IrModule` → `LuaEmitter`, deterministic Lua 5.1-compatible output with `__vars`/`__fns`, event handlers and registrations; omitted optional arguments use host defaults. `ScriptCompiler.OutputName` gives `acw_<slug>_<shortid>.lua`. |
| Templates | Embedded `blank`, `creature-death` and `command` graphs; loading clones fresh ids without mutating the template. |
| Persistence and export | `ScriptWorkspace` (scan/load/atomic save/delete, stamps, duplicate-id and malformed-file issues, path containment), `ScriptManifest` (`.acworld-scripts.json`, ownership, hashes, orphans) and `ScriptExportService` (compile-first, all-or-nothing, refuses foreign/edited files). No file I/O in Foundation. |
| Editor (M2) | View → Script editor… opens a lazily built `ScriptEditorWindow` inside `ScriptWindowFrame`; `ScriptGraphCanvas`/`ScriptPortMap`/`ScriptNodeView` render typed nodes and enforce connection rules, while `ScriptDocumentSession` supplies a 100-entry undo/redo history and hash-based dirty state. Palette search, typed inspector fields, event scope fields, variables, problems with node centring, Lua preview, Ctrl+S/Z/Y/Enter/Shift+E/F/Home/0/Escape shortcuts, workspace save/load, export with the reload hint, and per-profile folder/auto-export settings. |
| Authoring depth (M3) | `flow.for_range` lowering (`for i = from, to`), user-function graphs (parameter get, exec roots, Return slots, function signature editing), copy/paste/duplicate with id remap and multi-select deletion, comment frames (position/size persisted), and `spell-cast`/`quest-giver` templates with golden Lua. |
| Proof | xUnit `ScriptTypeSystemTests`, `ScriptDocumentTests`, `ScriptCatalogTests`, `ScriptValidatorTests`, `ScriptCompilerTests`, `ScriptCompilerGoldenTests`, `ScriptTemplateTests`, `ScriptWorkspaceTests`, `ScriptExportTests`, `ScriptingSettingsTests`, `ScriptDocumentSessionTests` (1677 tests in the suite). Golden Lua for the creature-death and command templates. gdUnit `ScriptCanvasTests` (11) and `ScriptEditorWindowTests` (13) cover port mapping, model-backed connections, undo/redo, palette, inspector, problems, save/load, export, copy/paste/duplicate, comments and function graphs; the View menu change is pinned by the updated UI goldens. |

## Data, overlays and rendering

| Item | What landed |
| --- | --- |
| Draw calls | Terrain draws one mesh per layer list per tile over a splat atlas; opaque same-material model batches merge at decode; static materials are shared. Render CPU and GPU roughly halved (one run; see [performance.md](../status/performance.md)). |
| Parallel model decode | Three decode workers (`ModelLibrary.DecodeWorkers`); Stormwind arrival (1416 models) 30.3 s → 19.1–19.3 s, flight frame times unchanged. |
| Model memory | A published model keeps only its doodads, animation IDs and (WMOs) probe geometry; the 256 MiB limit applies to models no instance uses. Stormwind cache 1903 → 318 MiB after arrival; flight evictions about 390 → about 205. |
| Terrain height blending | Layers weight by `height * scale + offset` from the `_h` alpha and MTXP with the client's suppression of layers far below the tallest; MTXP texture scale applies. Test: `SplitTerrainTests` (parameters read); the blend is checked by eye in Khaz Algar (`--tele-name=TheDawnbreaker --tele-tile=33,29`). |
| Generated navigation inspection | AzerothCore MMap 20 and TrinityCore MMap 16 readers, detail surfaces, polygon edges, tile bounds, off-mesh lines, opacity/X-ray, terrain filters and polygon picking. See [viewer-runtime.md](../architecture/viewer-runtime.md#navigation-overlay). |
| WoW: Forever client data | The `wowf` product opens from the shared retail install (`_forever_` folder or `ACWORLD_CLIENT_PRODUCT=wowf`); map list, terrain, lighting, liquids, M2/WMO models, creature displays and GameObjects load through the retail readers. `--client-smoke` opens build 1.60.1.70790 with 69 maps and streams an Azeroth tile; `ForeverCorpusTests` checks every known table, the WDT/tile and a creature model. |
| Shared install assets | `CascFiles` opens the live product beside a non-live one and reads shared BLP payloads by the same FileDataID; DB2 files, WDT/ADT structure and M2/WMO geometry stay product-local. `LiveProductFiles` records the IDs served by the shared storage. |
| Sun shadows | Custom cascade pass (`Rendering/SunShadows.cs`, `Rendering/ShadowCasterScene.cs`) renders terrain, WMO shells and WMO/ADT doodads into 1/3/4 orthographic `SubViewport`s on user layers 17-20, packs 24-bit sun-space depth (`Shaders/shadow/shadow_caster.gdshader`) and publishes the maps plus a cascade data texture through the `client_shadow_*` globals. Terrain and model shaders run the retail mask math per fragment (`Shaders/shared/client_shadow.gdshaderinc`: cascade select, 1/5/9-tap PCF, edge fade, 5 % split blend) and attenuate only the sun's diffuse and specular; `ShadowSunStrength` gates it on the sun disc's elevation and diffuse. Creatures and M2 GameObjects cast too: `ModelLibrary` splits a detailed entry into caster-material buckets (opaque shared, opaque skinned, alpha-tested) because a `MultiMesh` cannot take per-surface overrides, and the caster shader skins through `Shaders/shared/client_skinning.gdshaderinc` and alpha-tests blend-mode-1 hair/capes from the static UV rows; creatures share layer 19 and M2 GameObjects layer 20 with the doodads. Animated and moving detail casters refresh at `AnimatedRefreshHz` (Low 20, Medium 15, High 15) guarded by a separate `TransformVersion`, while camera, sun and registration changes stay immediate. View → Sun shadows saves `[video] shadow_quality` (unset defaults to Low); `--shadow-quality` overrides it. Tests: xUnit `ShadowCascadesTests`, `ShadowDepthPackingTests`, `ShadowCasterPolicyTests`, `ShadowQualityTests`, `ShadowSunStrengthTests`, `ShadowShaderTests`; gdUnit `ShadowSettingsTests`; smokes `--shadow-caster-smoke` (maps and depth probe, Forward+ and Compatibility), `--shadow-smoke` (off/low/medium/high and night) and `--shadow-creature-smoke` (per-cascade draw-count isolation of the Creature and GameObject layers plus the throttled animation refresh). `--perf-breakdown` adds the detail-caster rows. Cost in [performance.md](performance.md). |
| CI | GitHub Actions: every push and pull request builds and runs the xUnit suite, gdUnit4 runs in its own `viewer-tests` job, `master` pushes export, smoke-run and upload a 7-day Windows build, and a `vX.Y.Z` tag creates a draft release. |

## Notes

Assigning or creating a TrinityCore gossip menu also sets the gossip bit in `creature_template.npcflag` when
it is missing (a template edit affecting every spawn of the entry; the export shows it as a separate
`UPDATE`).
