# Viewer runtime

Covers `Scripts/Viewer/`, `Scripts/Rendering/`, `Scripts/Overlays/`, `Scripts/Ui/`, `Scripts/Diagnostics/` and `Shaders/`. This is the Godot layer. Everything here runs on the main thread except where noted.

## Scene and startup

`project.godot` runs `res://Main.tscn`. That scene holds one node, `TerrainViewer` (a `Node3D`, `Scripts/Viewer/TerrainViewer.cs`). The rest of the scene graph is built in code.

```mermaid
sequenceDiagram
    participant Main as Main.tscn
    participant TV as TerrainViewer._Ready
    participant UI as BuildUi
    participant CS as ClientServices (worker)
    participant M as ModelLibrary
    participant W as WorldMap

    Main->>TV: scene loaded
    TV->>TV: create TerrainRenderer, ModelLibrary, WorldObjectScene, Camera3D
    TV->>TV: create SkyLighting and five overlays
    TV->>UI: BuildUi (menus, toolbar, sidebar, editor windows, drafts)
    TV->>TV: ConfigureSmokeChecks (parse CLI flags)
    TV-->>TV: StartupPrompts (not awaited): DB test, client picker if none saved
    TV->>CS: LoadClient, Task.Run(ClientServices.Open)
    CS-->>TV: store, maps, lighting, areas, DatabaseEnabled
    TV->>M: SetStore(store) (Wrath loads catalogs)
    TV->>W: OpenMap, read WDT on worker
    TV->>TV: Teleport to a tile, stream terrain
```

Client selection:

- `UserSettings` (`Scripts/Ui/UserSettings.cs`) reads and writes `user://settings.cfg`.
- Environment variables override settings through `SettingsResolver`: `ACWORLD_CLIENT_PROFILE`, `ACWORLD_DATA_DIR`, `ACWORLD_RETAIL_DIR`, `ACWORLD_DB_INFO`, `ACWORLD_TC_DB_INFO`, `ACWORLD_LISTFILE`.
- `ClientPickerDialog` (`Scripts/Ui/ConnectionForm.cs`) offers the Wrath and Retail profiles, remembers one folder per profile and validates it with `ClientFolder.Validate`.

Shutdown (`_ExitTree`) cancels streaming, awaits the model worker and the pending tile task, then disposes the store and the drafts.

## TerrainViewer partial classes

`TerrainViewer` is one class split across files:

| File | Concern |
|---|---|
| `Viewer/TerrainViewer.cs` | Core coordinator: `_Ready`, `LoadClient`, `OpenMap`, streaming state, `Prepare`, camera movement, input, `_Process` |
| `Viewer/TerrainViewer.Ui.cs` | `BuildUi`: menus, toolbar, left navigation, bottom telemetry bar, editor window wiring, client and connection prompts |
| `Viewer/TerrainViewer.Export.cs` | Pending changes, SQL export, polling every 0.4 s, discard and export actions |
| `Viewer/TerrainViewer.Ground.cs` | `GroundAt`: combines terrain height and WMO floors for snapping |
| `Viewer/TerrainViewer.Listfile.cs` | One-time listfile prompt and in-app update (download on a worker, then reload the retail client) |
| `Viewer/TerrainViewer.Locate.cs` | Locate a creature from quest links: fly to a loaded spawn, or query the database and wait for the region to stream in |
| `Viewer/TerrainViewer.Teleports.cs` | `game_tele` dropdown, with a generation guard |
| `Diagnostics/TerrainViewer.*.cs` | Smoke workflows (see Diagnostics below) |

Other viewer files: `SkyLighting.cs` (sun, environment and skybox), `ClientLighting.cs` (light regions and profiles), `ClientServices.cs` (client open), `TerrainMinimap.cs` (a `Control` that raises `Jump` on click), `ScenePicking.cs` (box and sphere ray hits), `AreaTable.cs`.

## Streaming

```mermaid
flowchart TB
    cam["Camera tile"] --> want["UpdateWanted<br/>radius 0 to 3 around camera"]
    want --> rel["Release loaded tiles outside radius + 1<br/>hysteresis ring"]
    want --> reg["Sticky overlay query region<br/>radius + 3, re-centred near edge"]
    reg --> ovl["Overlays SetRegion"]

    want --> pick{"Pending decode?"}
    pick -->|none in flight| sched["Schedule nearest missing tile"]
    sched --> prep["Task.Run Prepare<br/>ReadTile, BLP decode, liquid decode,<br/>PrepareTile layer groups and splat atlas<br/>no Godot objects"]
    prep --> done["PreparedTile with generation"]
    done --> check{"generation matches<br/>and tile still wanted?"}
    check -->|no| drop["Discard"]
    check -->|yes| up["BeginTile, AddChild"]
    up --> chunks["BeginTerrain / AddGroup / AddLiquid<br/>max 8 items, 1.5 ms per frame"]
    chunks --> pub["Publish into loaded,<br/>chunkAreas, terrainHeights<br/>objects.SetTile"]
```

State fields in `TerrainViewer`:

- `wanted`: tiles inside the radius.
- `pending` and `pendingKey`: the single decode task.
- `building` and `buildingNode`: the tile being uploaded.
- `loaded`: `Dictionary<TileKey, Node3D>`.
- `failed`: tiles that failed, so they are not retried every frame.
- `generation` plus a `CancellationTokenSource`: guards against map and reload races.

Model streaming (`Rendering/ModelLibrary.cs`) follows the same pattern, once per frame in `Update(camera)`:

1. `ConsumeDecodedModel` (queues a finished worker batch)
2. `UploadPendingModel` (at most 8 surfaces, 1.5 ms, shared by the queued entries; each publishes when its last surface is up)
3. `BindReadyInstances` (at most 8 per frame, 2 ms)
4. `ScheduleModelDecode` (tops up to three `Task.Run` workers, each decoding the next four nearest unbuilt variants back to back and stopping early after about 100 ms; workers start while earlier results upload, while fewer than three decoded entries are waiting and those hold under 256 MiB; `ConsumeDecodedModel` queues every finished worker)
5. `EvictDormantModels` (LRU over the entries no instance uses; their estimated size is held to a soft 256 MiB, and entries with live instances are neither counted nor evicted)

A published entry keeps only what instances and probes still read (`Entry.ReleaseUploadData`, `ModelData.ForProbes`): doodads, animation IDs and bounds, plus a WMO's batch positions, indices and liquid types. The decoded BLP pixels, palette pixels, normals, UVs, colors, skinning and bone tracks are dropped once the last surface is uploaded. `Entry.Bytes` then estimates the retained CPU data plus the entry's own GPU memory.

Caches: retained model data, palettes and uploaded meshes are keyed by `ModelKey` (path, replacement textures, baked texture, doodad set, animation, skybox flag, hair and fur). Shared textures are ref-counted by path. A `failed` set stops retries. Shader variants are cached by blend mode, two-sidedness, unlit and skybox.

## Rendering

```mermaid
flowchart LR
    subgraph Worker["Worker threads (CPU data only)"]
        tile["TerrainTile"]
        mdl["ModelData, palettes"]
    end
    subgraph Main["Main thread (Godot resources)"]
        tr["TerrainRenderer<br/>Tile_X_Y nodes, one mesh per layer group"]
        ml["ModelLibrary<br/>ArrayMesh, MultiMesh batches"]
        wos["WorldObjectScene<br/>Reconcile by tile and ModelKey"]
        sky["SkyLighting<br/>DirectionalLight3D, WorldEnvironment, skybox anchor"]
    end
    subgraph Shaders["Shaders/"]
        ts["terrain.gdshader<br/>4-layer splat, shadow, grid"]
        ls["liquid.gdshader<br/>animated atlas, depth alpha"]
        ms["model.gdshader<br/>GPU skinning, batch colour, blend modes"]
    end
    tile --> tr --> ts
    tr --> ls
    mdl --> ml --> ms
    wos --> ml
    sky --> ml
```

- **Terrain** (`Rendering/TerrainRenderer.cs`): the tile worker (`PrepareTile`) groups a tile's MCNKs by their layer list and concatenates each group into one surface, so a tile draws as one `MeshInstance3D` per distinct layer list (about 20–40) instead of 256 chunk meshes. UV is chunk-local; UV2 is the chunk's cell in the tile's 1024x1024 RGBA splat atlas (64x64 per chunk: RGB layer opacity, A baked shadow), uploaded once per tile by `BeginTerrain`. Each group gets a `ShaderMaterial` on `terrain.gdshader` with its four layer textures, which stay shared and reference-counted across tiles (a per-tile texture array would copy them into every tile). `CellCount` feeds the status bar. Liquids are children of a `Liquids` node and use `liquid.gdshader`, one shared material per texture.
- **Terrain shader**: samples the splat atlas at the chunk-local UV clamped to texel centers inside the chunk's cell, which matches the old per-chunk clamp-to-edge maps. Big-alpha mode uses independent layer weights; legacy mode blends in sequence. Vertex tint and shadow follow, with an optional cell grid.
- **Liquid shader**: unshaded, no depth writes, animated atlas at 30 fps. Water alpha comes from vertex alpha and Fresnel; lava and slime (kind 2 or higher) are opaque and emissive.
- **Models** (`Rendering/ModelInstance.cs`, `Rendering/ModelLibrary.cs`): `ModelInstance` is a `Node3D` that holds its transforms. The decode worker merges opaque batches that share every material input (`SurfaceMerge`, skyboxes excepted) and the entry uploads that list, then drops it; ground probes read the unmerged batch positions and indices the entry keeps (`ModelData.ForProbes`). Unskinned, unanimated, non-sky batches share one `ShaderMaterial` per material key (shader variant, texture, blend, colour mode, static colour and UV rows), reference-counted per entry. Instanced placements use a `MultiMeshInstance3D` per mesh with visibility range 1600. Single instances use range 400. Skyboxes are unculled and shadowless.
- **Model shader** (`Shaders/model.gdshader`): GPU skinning from the `animation_palette` texture; batch colour and UV animation from `material_palette`; `vertex_color` mode 1 (interior baked light) or 2 (exterior colour as emission). `ModelLibrary` patches the shader text per variant (`render_mode` and an `ALPHA` line). The skybox variant places vertices on a 7500-yard shell.
- **WMO placement** (`Rendering/WorldObjectScene.cs`): `SetTile`, `RemoveTile`, `SetGlobals` and `SetLayers` each trigger `Reconcile`. Objects are de-duplicated by `(IsWmo, Id)` with sticky owner tiles, grouped by tile and `ModelKey`, and attached as one batch per group. `FloorHeights` feeds ground probes.
- **Sky and lighting** (`Viewer/SkyLighting.cs`): `ClientLighting` supplies light regions and profiles. Each frame it blends bands at `tick = hours * 120`, drives the sun, ambient, sky gradient and fog, and attaches the skybox model.

## Camera and input

All camera and input handling is in `TerrainViewer.cs`.

- **Flight:** hold right mouse to capture the cursor. Mouse motion changes yaw and pitch (pitch clamped to ±1.55). `MoveCamera` reads physical W, A, S, D, Q and E; Shift multiplies speed by 4; the wheel changes speed between 5 and 1500.
- **`_Input`** runs before shortcuts. While captured it swallows keys other than Escape, so flight keys do not trigger toolbar modes. While an editor is dragging it routes motion, release and Escape to that editor.
- **`_UnhandledInput`** runs after the UI. Left click tries the active editor's gizmo first, then the nearest ray hit across creatures, GameObjects, graveyards and triggers (triggers lose ties). A short right click selects a creature and opens its properties window (for TrinityCore only the template page is editable so far). Creatures, GameObjects, graveyards and waypoints each have their own draft; the selected kind owns the gizmo, undo and drop-to-ground.
- **Focus loss** releases capture and cancels drags.
- **Shortcuts** come from `EditorMenu` (Ctrl+Z, Ctrl+Y, End, Ctrl+Shift+C, F, Ctrl+Q) and `EditorToolbar` (Q, W, E for modes, Ctrl+Shift+Z).

Per-frame order in `_Process`:

1. `MoveCamera` (when captured)
2. `UpdateTerrainStreaming`
3. `UpdatePendingLocate`
4. Status, menu and toolbar refresh
5. `sky.Update`
6. Labels, telemetry and minimap
7. Creature, GameObject, graveyard, waypoint and trigger `Update(camera)`
8. `interaction.Track(creatures.SelectedSpawn)`
9. `models.Update(camera)`
10. Sidebar counts
11. `PollPending`
12. `RunSmokeChecks`

## Overlays

Five overlays are children of `TerrainViewer`. They share one shape (`RegionOverlay`):

- `Connection` gives the `WorldConnection` (settings, hotfixes database and dialect); queries open it with `DBSource.Open`. `SetRegion` `SetRegion` receives the query region from `UpdateWanted`.
- `RegionQuery<T>` (`Overlays/RegionQuery.cs`) runs one cancellable, generation-checked query on a worker. A search box has a 0.3 s debounce.
- Results are diffed against existing markers by ID.
- Each overlay exposes `FindHit`, `Select`, `Deselect`, `SelectionChanged`, `FocusRequested(WorldPose)`, `SetLayerEnabled` and `BuildPanel()`, which the sidebar hosts.

| Overlay | Files | Data source | Editable |
|---|---|---|---|
| `CreatureOverlay` | `CreatureOverlay.cs`, `.Editing.cs`, `.Staged.cs`, `.Ui.cs` | `IDBSource.ReadCreaturesAsync` | Yes: move, rotate, drop, undo and redo through `SpawnPoses`; opens the properties window and the behavior editor |
| `GameObjectOverlay` | `GameObjectOverlay.cs`, `.Editing.cs`, `.Staged.cs`, `.Ui.cs` | `IDBSource.ReadGameObjectsAsync` | Yes: move and rotate through `SpawnPoses` |
| `GraveyardOverlay` | `GraveyardOverlay.cs`, `.Editing.cs`, `.Ui.cs` | `IDBSource.ReadGraveyardsAsync` | Yes: position, name and zone links through `GraveyardDraft` |
| `WaypointOverlay` | `WaypointOverlay.cs`, `.Editing.cs`, `.Ui.cs` | `IDBSource.ReadWaypointsAsync` | Yes: point positions and properties through `WaypointDraft`. For TrinityCore the card shows only facing and delay as editable, the path's move type and velocity read-only, and hides smooth transition and actions |
| `AreaTriggerOverlay` | `AreaTriggerOverlay.cs`, `.Ui.cs`, `AreaTriggerShapes.cs` | `IDBSource.ReadAreaTriggersAsync` | No; opens the behavior editor on a client trigger's rules or (TrinityCore) a spawned trigger template's |

AreaTrigger volumes are keyed by `AreaTriggerKey` (a client trigger ID, or a TrinityCore spawn ID; list rows carry `ListId`, which sets the top bit for spawns). `AreaTriggerShapes` builds each shape's fill and outline in the marker's local space: spheres, boxes, bounded planes and cylinders scale unit meshes (so `--areatrigger-smoke` can check rendered corners against `AreaTriggerRecord.Contains`); polygons (a prism triangulated with `Geometry2D.TriangulatePolygon`) and disks (an annulus) are built per trigger. A bound the server leaves open (a client polygon has no floor) is drawn 5 yd below or 20 yd above. Picking tests the unit sphere or the slab of the mesh bounds. Labels, Fly to selection and Copy .go use `AreaTriggerRecord.Focus`, which for a polygon is its corners' centroid: many client polygons sit at the map origin with world-space corners.

Creature models are shown for the nearest `Preview.ModelLimit` (128) creatures within `Preview.ViewDistance`, refreshed every 0.2 s. Creatures with a model hide their capsule placeholder once the model is ready.

### Editing

`IPlacementEditor` (`Overlays/IPlacementEditor.cs`) is implemented by `CreatureOverlay`, `GameObjectOverlay`, `GraveyardOverlay` and `WaypointOverlay`. `TerrainViewer.ActiveEditor` sends menu, toolbar and viewport commands to whichever editor owns the selection.

Spawn editors (`CreatureOverlay`, `GameObjectOverlay`) also implement the default members `CanDuplicate`, `CanDelete`, `Duplicate()` and `Delete()`, which graveyards and waypoints leave off. They appear as Edit menu items (Duplicate Ctrl+D, Delete Del), toolbar actions and inspector buttons. A copy goes two yards ahead of its source, dropped to the ground, under the next free GUID, and is selected; a delete removes the spawn and its addon row in one command. Both need the spawn's row, which is read when it is selected, so the commands stay disabled until it arrives. A delete removes only those two rows. When pools, spawn groups, event links, linked respawns, formations or per-GUID SmartAI still name the spawn, a dialog lists them first (`SpawnDeleteDialog`, counts from `IDBSource.CountSpawnReferencesAsync`; for a creature also the waypoint path it walks when nothing else uses it, from `IDBSource.ReadSpawnPathsAsync`); removing them with the spawn is not built yet (see `tc-status.md`).

The creature properties window (`CreaturePropertiesWindow`) edits the creature template: its title reads "Creature template: {name}" and its subtitle (`ScopeText.Header`) names the entry and spawn count and points to the inspector for a single spawn. The Appearance tab (`AppearanceSection`) edits `creature_template_addon` with `TemplateAddons`, reusing `SpawnInspector` in its appearance-only mode.

Selecting several spawns: Ctrl-click adds or removes the spawn under the cursor, and a Shift-drag draws a box that adds everything inside it (`TerrainViewer.Selection.cs`, `SpawnGroupSelection`, `CreatureOverlay.Group.cs`, `GameObjectOverlay.Group.cs`). The members get a ring on the ground; Delete, Duplicate and a gizmo move then act on the primary and the members as one command. A press on a gizmo handle starts its drag even with Ctrl or Shift held (Ctrl also snaps a rotation). Ctrl-clicking a spawn of the other kind switches the selection to that kind.

`PlacementTool` (`Overlays/PlacementTool.cs`, owned by `TerrainViewer`, input in `_UnhandledInput`) places a new spawn from a template chosen in the creature or GameObject inspector's `PlaceCard`: the ghost is recomputed every frame from the last cursor position (so it stays under the cursor while the camera flies) and hides over panels; `RayGround` marches the camera ray against the terrain height map and, in terrain holes, the building-floor probe.

`TransformGizmo` (`Overlays/TransformGizmo.cs`) draws three axis arrows and a yaw ring. It has Select, Move and Rotate modes, and emits `Changed(GizmoEdit)` events. Preview events have `Commit = false`; exactly one commit fires per gesture. Rotation snaps in steps, or 15° with Ctrl held.

A committed creature move goes through `SpawnPoses.Move(sourceKey, guid, before, after)`, a row edit on the content draft. The displayed pose is `poses.Effective(...)`, and `poses.Conflict` flags a change in the source database.

## Ui windows

| Window | File | Role |
|---|---|---|
| Client picker | `ClientPickerDialog` in `Ui/ConnectionForm.cs` | Profile and folder selection |
| Connection | `Ui/ConnectionForm.cs` | Database settings; tested on a worker before saving |
| Sidebar | `Ui/SceneSidebar.cs` | Layers, spawn preview policy, connection, one tab per overlay, unsaved-changes button |
| Editor base | `Ui/ContentEditorWindow.cs`, `Ui/ContentPanel.cs` | Window frame (drag, size pin) and frameless panel base; both carry the draft hooks, commits and the shared client catalog |
| Creature properties | `Ui/CreatureProperties/` | One window per selected creature: a section picker on the left and the chosen section on the right (see below) |
| Quest editor | `Ui/QuestEditorWindow.cs` (search and frame; swaps in the open database's panel), `Ui/QuestPanel.cs` + `.Pages.cs` (shared loading, drafting, story, chain and givers), `Ui/AzerothCore/AcQuestPanel.cs`, `Ui/TrinityCore/TcQuestPanel.cs` (overview, objectives, rewards and preview pages) | `quest_template` and related tables, with `AcQuestModel.Check` (AzerothCore) or `TcQuestRules.Check` (TrinityCore) |
| Behavior panel | `Ui/BehaviorPanel.cs` (with the speech page), `.Rules.cs` | SmartAI rules written as sentences, with entry, spawn and action-list scopes, plus speech; embedded in the creature window by the shared `BehaviorSection` and `SpeechSection`. The same panel serves both databases through `SmartCatalog.For(dialect)`: TrinityCore adds a Difficulties field, text fields for the string parameters and a speech line's sound play type |
| AreaTrigger behavior | `BehaviorEditorWindow` in `Ui/BehaviorPanel.cs` | A window around `BehaviorPanel` for AreaTrigger rules, which have no creature: a client trigger's (source type 2, with its `areatrigger_scripts` name) or a TrinityCore spawned trigger template's (11/12, with the spawn's `areatrigger.ScriptName`), opened with an `AreaTriggerOwner` |
| Pending changes | `Ui/PendingChangesWindow.cs` (`PendingChangesWindow`, `SqlPreviewWindow`) | Lists pending changes; the viewer performs discard and export. The SQL preview shows the apply or revert script an export would write |
| Menus and toolbar | `Ui/EditorMenu.cs` (`EditorMenu`, `EditorToolbar`) | Callback-driven menus and mode buttons |
| Form controls | `Ui/EditorFields.cs`, `Ui/InspectorUi.cs` | Number, text and picker fields; shared inspector layout |
| Client styling | `Ui/ClientStyle.cs` | Dark editor-window theme, quality colours, gossip icons |

### Creature properties window

`CreaturePropertiesWindow` is only the frame: header (name, Undo/Redo), the section picker (`ItemList`) and a status line. The data lives in `CreatureContext` (the selected spawn, the interaction rows merged with the draft, the connection and draft hooks, commit and ID helpers). The dialect-specific steps (the catalog load, building the read model from the effective rows, the commit rule check) sit behind `ICreatureDialect` (`AcCreatureDialect`, `TcCreatureDialect`), picked by `CreatureDialects.For`. The loaded creature is one `CreatureContext.View` (an `ICreatureView`: `AcCreatureView` or `TcCreatureView`); sections read their own through `AcView` / `TcView` and share one `Editable` gate (a content draft is open). Each page is a `CreatureSection` (Gossip, Quests, Vendor, Trainer, Behavior, Speech, Info). Quests is one `QuestsSection` for both dialects: it reads `ICreatureView.Starts` / `Ends` (`QuestLinkView`), the link tables and rule warnings from `IWorldSchema.QuestLinksPage` (`QuestLinksPageSpec.SetRoles`, `Check`), per-quest condition counts from `Lookup.QuestConditionCounts` (filled for both dialects), and a dialect-matched `QuestPanel` from `CreatureContext.Quest` for the embedded editor.

**Template page descriptors.** The Template page (both dialects) is data: `IWorldSchema.TemplatePage` is a `CreaturePage` (`Foundation/CreaturePage.cs`) with shared ordered groups (identity, interaction, combat, movement, loot, links, flags) and the fields each dialect puts in them (`AcCreaturePage`, `TcCreaturePage`). A `FieldSpec` names its table, column, label, tip, editor kind (text, whole, decimal, choice, flags, check) and row binding (template row or the difficulty row picked on the page); signedness and text length come from the `TableSchema` column, with optional field-specific numeric limits. A flag field carries its named bits, or none for raw-hex editing. `PageRenderer` draws any page, including the difficulty picker; when editable it also adds a missing difficulty row through `DifficultySpec.Add` (a copy of the shown row, or the dialect's `NewRow` defaults), then shows it, and removes the shown row through `DifficultySpec.Remove`; a section adds its own controls to a group through `PageRenderer.After` (`PageRenderer.Picked` is the difficulty row shown). `TcStatsPage` is a second, small `CreaturePage` over the same difficulty row (content tuning, level deltas, scaling expansion, the four multipliers); `TcStatsSection` (in `TcTemplateSection.cs`) draws it and, under the multipliers, the level, health, mana, armor, weapon damage and attack power `TcStatCalculator` (`Foundation/TrinityCore/`) gives from the client's ContentTuning, ExpectedStat, ExpectedStatMod and ContentTuningXExpected tables (`ITcStatData`, served by `RetailClientLookup`) and the world database's `creature_classlevelstats` (`InteractionLookupData.TcClassStats`). Add a field by adding one `FieldSpec`; `CreaturePageTests` checks that every field's column exists and that the pages cover their tables.

**Item-list descriptors.** Vendor uses `IWorldSchema.VendorPage` (`VendorPage` in Foundation): an `ItemListSpec` selects child rows by parent and ordering, supplies complete new rows, and declares per-item `FieldSpec` values bound with `RowBinding.Item`. `PageRenderer.RenderItems` draws the item fields, including remove controls when editable. The page itself is the shared abstract `VendorSection`: read-only it is a paged two-column grid of icon rows with game tooltips, with a draft open it is the item editor followed by an item search whose results carry icons (`SearchPicker`). `AcVendorSection` and `TcVendorSection` only turn their read model into `VendorEntry` values (name, quality colour, icon, price, stock, shared-list source, tooltip) and supply the item search and the controls only that dialect has (TC: paired stock/restock editor, add by ID, vendor rules). Items of a shared vendor list are read-only cards. `ItemListTests` covers the descriptors and edits. TC enables editing through `CreatureContext.Editable`; `TcVendorSection` searches the retail `IClientLookup` by name or exact ID (30 results, on a worker), and adds items or currencies by ID. Search results are discarded when the creature changes. Stock and restock save together. `TcVendorRules` validates new errors during every TC commit; `TcVendorTests` covers stock, currency quantities, flags, key collisions, persistence, undo/redo and export. A negative ID is a shared vendor reference: TC shows it as a card with the first 25 items of the list. Currency, cost, bonus-list and player-condition existence checks still need their lookup catalogs. Gossip uses `IWorldSchema.GossipPage` (`GossipPageSpec` / `GossipPage`): menu text links and option lists select rows by MenuID, with dialect-specific option fields and greeting storage. `GossipPageSpec.IconOf` / `ServiceOf` read the dialect's own option columns (AC `OptionIcon` / `OptionType`, TC `OptionNpc`) so the pages draw an option's client icon and follow it (`GossipService`: open the quests, vendor or trainer page, or say which client window it opens) through `CreatureSection.OpenService`; `EnableGossip` sets the gossip npcflag a menu needs. `IDBSource.SearchMenusAsync` finds menus by ID or greeting text (AC: `npc_text`; TC: broadcast text in hotfixes then the client, then the `npc_text` rows that use it); `CreatureSection.ChooseMenu` / `ChooseBroadcast` open the picker popup over it and over `SearchBroadcastAsync`. TC gossip lists options as icon rows that a click follows, puts a search button beside every menu and broadcast ID field, and creates a menu or a submenu from a picked broadcast text (a new menu also sets the gossip npcflag). `RenderItems` accepts field controls and an after-item callback for resolved broadcast text, AC icon selection, validated submenu edits and navigation/reordering. AC keeps its greeting editor, shared-menu copying and compact preview; TC selects among all root menus and reachable submenus with Back navigation, edits option fields/add/remove through `Editable`, and edits all eight greeting broadcast ID/weight pairs together per slot. `TcGossipRules` validates new rule errors on commit. Submenu and broadcast-reference edits check existence asynchronously through `IDBSource`, ignoring results after creature changes; TC broadcast lookup currently accepts an exact ID only. Broadcast previews show both text variants when different; literal confirmation edits clear their broadcast override atomically. Menus and greetings can be shared, which the editor labels. Trainer bindings remain read-only. Option text edits clear the broadcast override in one command. `GossipPageTests` covers defaults, menu selection, reordering, broadcast overrides, undo/redo and export. Trainer uses `IWorldSchema.TrainerPage` (`TrainerPageSpec`) for bindings, storage and spell fields, rendered through `RenderItems`; AC retains assignment, shared-trainer copying and spell search, while TC keeps gossip-bound trainers (the bindings stay read-only) and edits the trainer type and greeting plus its spells through `Editable`: spell fields, removal, client spell search (30 results, on a worker) and add by ID, with a warning when several creature bindings share the trainer (`TcTrainerView.SharedBy`). `TcTrainerRules` validates on commit and `TcTrainerTests` covers it. Quests use `IWorldSchema.QuestLinksPage` (`QuestLinksPageSpec`) for starter/ender lists and grouped role edits. AC keeps its combined giver/ender icons, role menu, search and embedded quest editor; TC renders separate Starts and Ends lists through `RenderItems`, with removal and a quest search to link a quest as starter or ender when `Editable`. `CreatureServicePageTests` covers trainer requirements and quest-role history/export.

Dialect-specific sections live in `Ui/CreatureProperties/AzerothCore/` (`Ac` class/file prefix) and `TrinityCore/` (`Tc` prefix). Common context, registry, base section and rendering stay in `Ui/CreatureProperties/`.

To add a section: derive from `CreatureSection` (or `AcInteractionSection` for pages that draw `AcCreatureView`), implement `Id`, `Title` and `Refresh`, and add one factory to `CreatureSectionRegistry`. A page both dialects have (stats, gossip, quests, template, trainer, vendor) reads its id, title and picker icon from the shared `PickerPages` table in `CreatureSection.cs`, so the AzerothCore and TrinityCore entries match. `CreatureSection` also holds the widgets both dialects draw with: `IconRow` (client icon plus optional game tooltip), `QuestIcons` (the ! and ? slot), `SearchBox` and `SearchPicker` (search with "+" result buttons that carry icons and tooltips). Sections with their own data keep it in a `ContentPanel` (as Behavior and Quests do); the context creates and shares those panels. `ctx.ShowSection(id, argument)` navigates between sections.

Cross-window wiring is set up in `BuildUi`:

- the creature inspector's Properties button, a short right-click and the Behavior menu entries open the creature window on a section;
- The "Creature Templates" toolbar button searches `creature_template` by name or ID (for either database, through `IDBSource.SearchAsync`) in the shared `PickerPopup` and opens the creature window on the pick, as a quest giver card does;
- the creature window and quest givers open the standalone quest editor, and the quest editor's giver cards open the creature window on its Behavior section;
- the interaction summary updates the creature inspector;
- the sidebar's review button opens the pending-changes window.

Every content editor shares one `ContentDrafts` instance and resyncs through its `Changed` event.

## Diagnostics

Smoke workflows run inside the real app. They are opt-in CLI flags passed after `--`, parsed in `ConfigureSmokeChecks` (`Diagnostics/TerrainViewer.Smoke.cs`):

| Flag | Workflow |
|---|---|
| `--terrain-smoke` | Teleport away and back; check for stale tiles |
| `--water-smoke`, `--water-interior-smoke` | Aim at the largest native liquid; frame timing (p50, p95) |
| `--creature-smoke`, `--visual-smoke`, `--gizmo-smoke` | Ray pick, gizmo editing, shortcuts, animation preview, map switching |
| `--reload-smoke` | Force a partial model upload, reload the client, check recovery |
| `--visibility-smoke` | Layers, preview policies, events, phase |
| `--skybox-smoke` | Checks that the skybox does not overwrite opaque pixels |
| `--graveyard-smoke` | Pick, move, undo, comments, links, layer hiding |
| `--waypoint-smoke` | Live waypoint paths near the start, a viewport click on a point, and the edit workflow |
| `--gameobject-smoke` | Select a GameObject, drag the real move handle, typed turn, reopen, undo/redo, exported SQL |
| `--areatrigger-smoke` | Trigger click, volume check, behavior rule add and undo. AzerothCore: trigger 5140 in Utgarde Pinnacle; TrinityCore: Stormwind spawn 290 (template 188) |
| `--interaction-smoke` | Gossip, vendor, quests, edit mode, reopen (run it windowed; headless the right-click does not open the window) |
| `--spawn-authoring-smoke` | Spawn authoring in Goldshire on an isolated draft (headless works). Creatures and GameObjects: `PlaceAsync` draws, lists and selects the new spawn with the template's name and model, exports an INSERT, undo removes it and redo restores it; a creature placed after an undo gets a fresh GUID. Then the spawn fields (respawn, movement, a wander radius and its ring in the viewport, an override with Use template, a refused value). Then Ctrl+D and the Delete key (creatures) and the inspector buttons (GameObjects) copy and delete spawns, including a source spawn whose delete is undone, and a referenced spawn whose delete asks first (Keep leaves it, Delete anyway removes it, undo restores it). Separate from `--interaction-smoke`; grows with each spawn-authoring slice. A windowed run (no `--headless`) also saves `artifacts/spawn-authoring-{ghost,placed,fields,group,delete-dialog}.png`; the delete dialog is a separate window and is not captured |
| `--content-smoke` | Behavior rules, speech, quest create and validation |
| `--behavior-smoke` | Behavior editor driven by mouse and key events |
| `--perf-breakdown` | Parks the camera at five Stormwind poses once streaming is idle and measures render CPU/GPU time and main/shadow-pass draw counts with each scene category (terrain, liquid, WMO shells, embedded doodads, ADT doodads, creatures, GameObjects, markers) hidden or shadow-less, plus sun shadows off. Run windowed; method and results in [`performance.md`](../performance.md) |
| `--perf-smoke` | Stormwind City streaming benchmark (run windowed): arrival, a fixed 70 yd/s flight and settle phases, printing `PERF_*` frame-time percentiles, per-step `_Process` time, render CPU/GPU time, tile and region-query latency, model counts and GC. Method and results in [`performance.md`](../performance.md) |
| `--client-smoke`, `--client-shot`, `--client-shot-tile=X,Y`, `--client-shot-water`, `--client-shot-overlays` | Client open check; screenshots to `artifacts/` (combine `--client-shot --client-shot-overlays` to keep the UI, show database layers over Goldshire, and capture gossip, Template, vendor, trainer, stats and quest pages for either dialect; TC also checks gossip submenu/Back navigation, add/text, greeting saves, validation, history and export (saving `client-shot-creature-submenu.png` and `client-shot-creature-greeting.png`), plus adding and removing a difficulty row (`client-shot-difficulty-add.png`), item search, add, stock validation and undo/redo (`client-shot-vendor-search.png`), a trainer spell edit with validation and undo and a refused unknown trainer binding (`client-shot-trainer-edit.png`), a gossip menu copy and its undo, the New menu control, an unknown extended cost refused after the client reference tables load, and a vendor row pointing at a shared vendor list showing its items (`client-shot-vendor-shared.png`), the stats page with scaling validation and undo (`client-shot-creature-stats.png`), and unlinking a quest with validation and undo, then opening the TC quest editor on that quest: adding an objective (a missing target is only a warning) and undoing it, renaming the quest, an unknown reward item or currency refused (`client-shot-quest-editor.png`), and the Hogger quest's objectives page (`client-shot-quest-objectives.png`), then the creature template search and Hogger's behavior editor: his four SmartAI rules read as sentences, a template rule added, a chance of 101% refused, the Difficulties field set and everything undone (`client-shot-behavior.png`, the expanded rule `client-shot-behavior-rule.png`), and his speech lines with their broadcast texts and sound play type (`client-shot-speech.png`)) |

Side effects of any flag ending in `-smoke`: startup database and client prompts are skipped, and drafts go to `res://artifacts/drafts/<guid>/` instead of `user://drafts/<profile>/`. Success prints `*_PASSED` and quits; failure pushes an error and quits with code 1.

`launch_viewer.py --smoke` builds the project and runs Godot headless with `--terrain-smoke`. For deterministic Goldshire UI screenshots, pass `--client-smoke --client-shot --client-shot-overlays --client-shot-tile=31,49`; the smoke flag skips the normal random database teleport at startup.

## Smoke workflows and retail testing

Smoke workflows are opt-in flags passed after `--` to Godot (`--terrain-smoke`, `--creature-smoke`, `--client-smoke`, ...). `--client-shot` / `--client-shot-tile=X,Y` (windowed) saves `artifacts/client-shot.png` framed on the largest nearby building with the UI hidden; `--client-shot-water` frames the largest liquid.

Test retail with pre-Cataclysm areas such as Goldshire/Elwynn Forest: the database's spawns come from 3.3.5, so later zones often have no creatures.
