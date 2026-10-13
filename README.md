# ACWorldEdit world viewer

A C# / Godot 4.7.1 .NET viewer for the Wrath 3.3.5 MPQs in `wow-data`.

## Run

Open `project.godot` in the **.NET** edition of Godot, build the C# project, and press **F5**. It automatically opens Azeroth tile **34,49**, near Elwynn.

Or run `./Launch-Viewer.ps1` from PowerShell. The launcher builds the project and locates Godot from the existing shortcut. Supply `-GodotPath` to use another installation. Requires .NET SDK 8 and Godot .NET 4.7.1; SharpZipLib 1.4.2 handles MPQ bzip2 compression.

The simple roadmap is in [PLAN.md](PLAN.md); current results and limits are in [PROGRESS.md](PROGRESS.md).

## Scene explorer and existing spawns

The right panel reads `WorldDatabaseInfo` from the external file
`X:\AzerothCore\azerothcore-wotlk\build\bin\Debug\configs\worldserver.conf`.
Set `ACWORLD_SERVER_CONFIG` before launching or open **Scene → Database connection…**, change the path and click **Apply & refresh**.
Credentials stay outside the project. Queries run in read-only transactions; nothing is written to the world database.

The right sidebar has **Scene**, **Creatures** and **GameObjects** tabs. Scene controls visibility
and loading; entity tabs provide search, a selection list, details and **Fly to selection**.

- Toggle scenery props (including embedded WMO doodads), buildings/interiors, creatures and GameObjects.
  Hidden layers release their nodes and stop scheduling model/spawn work.
- Normal preview matches phase **1** and normal difficulty. It includes SQL-scheduled event spawns
  and excludes inactive events, pool alternatives, hidden helpers/ghost-only creatures, SQL visibility/
  respawn conditions and initially despawned GameObjects.
- Choose **No events**, **Selected events** or **All event spawns** to inspect event associations.
  **All stored spawns** also bypasses gameplay masks and includes the special-case categories.
- Phase masks accept decimal or hex; **3** matches phase 1 OR phase 2. Difficulty uses overlapping bits.
  These controls only affect the preview.
- **Script-bound spawns** is on by default: a SQL script/AI binding does not imply invisibility.
  Turning it off excludes those bindings; SQL cannot detect every script or determine runtime visibility.
- Search by name, GUID or template filters SQL before LIMIT. The camera refreshes its bounded
  neighborhood on map/region changes and each 64-yard query cell; typing is debounced.

Defaults are **200 yards**, **384 nearest SQL rows** and **128 admitted models per spawn layer**.
The Low/Balanced/High budgets allow 64/128/256 models; High increases the SQL cap to 768.
View distance is limited to 60–600 yards. Every mode, including All stored spawns, keeps these caps.
Only admitted instances queue model work; nearby name labels default off and are capped at 64 per
layer plus the selection label. Native counts have tooltips showing SQL matches and admission counts.
Each spawn layer has one asynchronous query slot, cancellation and stale-result rejection.

Creatures use native M2/SKIN/display textures and GPU Stand/idle animation. The selection animation
menu lists available native clips. GameObjects resolve GameObjectDisplayInfo.dbc to native M2/WMO
models, materials and animation data, using database quaternion rotation and template scale.
Unsupported appearances retain bounded capsules/boxes. GameObjects are read-only previews: DB state
is shown, but open/close, interaction and transport/script behavior are not simulated.

### Placement editing

The toolbar picks the tool for a selected creature: **Select (Q)** only picks, **Move (W)** shows
colored axes (red view X, green height, blue view Z) and **Rotate (E)** shows a yellow ring whose
pointer marks the heading. Drag a handle; release saves one local draft command and **Esc** cancels.
Hold **Ctrl** while rotating for 15° steps, or choose a fixed **Rotation step**. Q/W/E only switch
tools while the camera is not flying.

- **Drop to ground (End)** snaps Z to the highest loaded terrain or building (WMO) surface at or
  below 3 yd above the creature. **Snap on drop** (off by default) does the same when a move drag is
  released. Probes fail closed with a reason, leaving Z unchanged, when nothing qualifies, the tile is
  not loaded, a covering building is still loading or the buildings layer is hidden. They use render
  triangles, not the client's collision meshes, and do not include M2 doodads such as bridges.
- The Selection card shows heading in radians and degrees with a compass point (server heading 0
  faces north/+X and increases toward west/+Y). Type X/Y/Z/heading° and press **Enter** to commit.
  Fields you leave alone keep their exact stored values; headings are normalized to [0, 2π).
- **Reset to source** is an undoable move back to the database pose.
- **Copy .go** copies `.go xyz X Y Z map heading` for comparing the pose in the client.
- **Ctrl+Z / Ctrl+Y** (or Ctrl+Shift+Z) undo and redo. Drafts reopen from Godot's
  `user://drafts/creature-positions.json` under the application data directory. A source position
  conflict prevents applying a stale draft. SQL export is not implemented yet.
Queries still locate spawns by their source positions. A draft moved far outside its source region
reappears when that source region is queried again; a cross-region draft index is follow-up work.

SQL schedules approximate ordinary repeating events using database time. Holiday/manual/world-event
state, live pool winners, player quests/auras, SmartAI and C++ visibility are not evaluated. Pool and
conditional toggles include all candidates instead of fabricating live winners. GUID **96098** /
template **26724** is tied to event **31** and stays hidden by default; Selected event 31 exposes it.
Missing required visibility metadata reports a safe error rather than silently showing everything.
Model picking uses bounding boxes; creature fallbacks use a screen-space target. Terrain occlusion
and precise animated triangle selection are not implemented.

### Creature interactions

**Right-click a creature without dragging** (or press **Interact…** in its inspector) to open a
client-style window built from the database: gossip text and options with client gossip icons
(submenus, Back, options hidden by `OptionNpcFlag`), available/completable quests with details,
vendor items in the merchant grid with client item icons and gold/silver/copper or extended cost,
trainer spells, spell click, flight master and SmartAI/C++ notes. Conditions, SmartAI, C++ scripts
and player state are counted but not evaluated. Holding and dragging the right mouse still looks.

**Edit** turns the window into an editor over a local interaction draft
(`user://drafts/interactions.json`; undo/redo; stale-row conflicts): greeting text (clears the
broadcast ID so the edit shows in game), options (icon, text, type, NpcFlag, submenu, order,
add with a new submenu), quest links, vendor items (search, stock, restock, extended cost),
trainer assignment and spells, spell clicks, npcflag and subname. Edits are checked against the
server's loader rules; npcflag inconsistencies the server only logs appear as warnings with a
**Fix**. Shared menus and trainers say how many creatures use them and offer **Make a copy for this
NPC** (copying a menu drops its SQL conditions, as warned). **Open in Quest Editor** and
**Behavior…** lead to the editors below; all three share one draft and one undo history.

### Quest editor

Toolbar **Quests** (or View → Quest editor, or a quest's detail page) opens it. Find a quest on the
left, or **+ New quest** / **Duplicate** (the next free ID above the database and the draft). Pages:
**Overview** (title, kind, availability, quest log zone from AreaTable, levels, time limit, races and
classes, options), **Story** (offer, progress and turn-in texts with gestures; buttons insert `$N`,
`$C`, `$R`, `$B`, `$G`), **Objectives** (kill/use with search pickers, items to collect, quest drops,
reputation goals, starting item), **Rewards** (gold/silver/copper, XP tier, fixed and choice items,
reputation, spells, honor, mail), **Chain & requirements** (previous/next/follow-up/breadcrumb,
exclusive group, skill and reputation), **Givers** (who offers and completes it; **Fly to** finds a
spawn in the database, opens its map when needed, travels there and selects it once loaded; open
their behavior) and **Preview** (the quest as the client shows it). Problems appear at the bottom;
an edit that introduces a server loader error (missing item/creature, zero counts…) is not saved.

### Behavior editor (SmartAI)

**Behavior…** in the creature inspector, the interaction window, a quest giver or the toolbar opens
it for a creature. Rules read as sentences ("Every 8 s in combat (first after 3 s): cast Fireball →
its victim"). **+ Add rule** opens a composer with When / Do / On dropdowns listing every type the
server supports (93 events, 178 actions, 37 targets) under category headings, a **Search…** for
each, the server's help and a live sentence preview; **Add rule** then opens its details.
**Templates ▾** offers ready-made starting points (say on aggro, cast on a timer, low-health spell,
death line, wave back, quest scene). **Edit** shows the same grouped When / Do / On choosers and
typed fields (seconds, spells, creatures, emotes, text groups, positions), chance, only-once,
phases and difficulty. **+ Then** adds a linked step; ▲▼ reorder; **Copy** duplicates a chain;
Delete keeps chains consistent. Scopes: all spawns of the entry, this spawn only, and action lists
(**Create list** allocates a free ID near entry×100 and opens it; steps wait seconds after the
previous one). **Speech** edits `creature_text` groups and lines (says/yells/emote, language, range,
chance, gesture) and flags lines that a broadcast text overrides. Adding the first rule to a
creature on the default AI switches it to SmartAI; other AIs and C++ `ScriptName`s are warned about.

### Graveyards

The **Graveyards** layer draws a tombstone icon over each `game_graveyard` row in the streamed region,
with a ground ring colored by its `graveyard_zone` links (blue Alliance, red Horde, grey neutral,
white mixed, dark when unlinked). Click an icon or choose it in the **Graveyards** tab to edit X/Y/Z
(Move tool, typed fields, Drop to ground), the name and its zone links: pick an area or zone by
name/ID, choose a faction, edit or remove links. Edits go to a separate local draft
(`user://drafts/graveyards.json`) with undo/redo and stale-source detection, and are checked against
the server's loading rules (zone exists in AreaTable, faction 0/469/67, no duplicate zone, name at
most 255 characters). Nearby spirit healers are listed but are separate creature spawns and do not
move with the graveyard. Battleground and default graveyards are also referenced by core C++ code.

## Explore

The menu bar holds commands: **File** (load archives from a chosen directory, open the selected
world, database connection, quit), **Edit** (undo/redo, drop to ground, reset, copy .go), **View**
(fly to selection, Scene explorer, terrain cell borders, panel visibility) and **Help** (controls).
Layer and SQL visibility stay in the Scene explorer.

- Hold **right mouse** to look and fly; **WASD** moves relative to the camera.
- **Q/E** descends/ascends; **Shift** accelerates; the **wheel** adjusts speed.
- **Esc** releases the mouse. Losing window focus also releases it.
- Choose a world and tile X/Y, then click **Open world**. An unavailable tile selects the closest terrain tile.
- WMO-only worlds open their global building directly; tile controls are disabled. Fly to explore interiors.
- Click land in the minimap to jump. Mint tiles are loaded; white marks the camera.
- Nearby tile radius defaults to **1**, loading up to a 3×3 neighborhood. Increase for a wider view or use 0 for one tile.
- **View → Terrain cell borders** shows the 16×16 terrain chunks in each tile.
- **File → Load archives…** chooses another client Data directory and reopens the asset source.

## Implementation

Archives remain read-only. The experimental stock 3.3.5.12340/enUS profile names 17 archives explicitly in `Scripts/Assets/ClientMountProfile.cs`; the last listed archive containing an asset wins. Backup archives and unlisted overlays are excluded. Missing required archives fail with their paths. This profile is provisional pending comparison with actual client lookup; other locales/HD clients are not supported profiles. Map.dbc discovers maps; WDT supplies terrain availability and the alpha-map format.

ADT meshes preserve the staggered 145-vertex chunk grid, four triangles per square, hole masks, MCNR normals, MCCV tint, and MCSH shadows. BLP2 palette, raw BGRA, DXT1, DXT3, and DXT5 textures are decoded. Layers repeat eight times per chunk, with packed/full-byte/RLE alpha maps and legacy sequential or large-alpha weighted blending. Texture patches use the first available mip at or below 512 pixels.

MPQ reads, terrain parsing, and texture decoding run on a background worker. The main thread builds at most eight chunks per frame under a soft 1.5 ms deadline, leaving headroom for the proposed 2 ms upload target; one indivisible upload can exceed it. Camera movement unloads distant nodes, discards obsolete requests, and releases texture-cache entries after their chunks disappear. Generation checks protect map switching. Missing textures appear magenta and report their paths; corrupt tiles report an error without retrying every frame.

Native ADT M2 doodads and WMO buildings load with the terrain. WDT global WMOs support interior-only
maps. WMO groups, material batches, vertex colors and default/selected doodad sets load from MPQs.
Duplicate ADT references are deduplicated by object ID. Repeated models use MultiMesh batches;
meshes/materials/textures are shared. Decoding and native animation palette sampling run on one
worker. GPU uploads build one surface per frame, and dormant model resources evict above a soft
256 MiB cache target. Active resources can exceed that target. Model textures retain their full
source mip resolution. Idle/selected-clip skinning uses a shared GPU palette sampled at up to 30 Hz,
with interpolated frames; long clips are capped at 601 samples.

Outdoor MH2O layers and legacy MCLQ grids, plus WMO MLIQ interiors, now render native liquid heights
and visibility masks. LiquidType.dbc selects animated BLP sequences for water/oceans, lava and slime.
Textures decode on workers into shared CPU atlases (up to 128 pixels per frame); shaders animate
at 30 Hz without CPU mesh updates. Outdoor grids batch by type per tile, with reference-counted GPU
textures/materials released on unloading. WMO liquids share existing model resources and placements.
Water depth controls transparency; preview tint, opacity and view-angle blending are approximations.
Client water lighting/reflections/refraction, underwater fog and WMO flow overrides remain incomplete.
The stock procedural liquid entry 100 references an unavailable texture and reports as unsupported.

This is a native asset preview, not complete client rendering parity. Character models use baked
NPC body textures and a basic geoset selection; equipment, detailed hair/clothing composition,
particles/ribbons, advanced multitexture/environment shaders, animated material/UV tracks,
billboard bones and portal culling remain incomplete. Spline bone tracks use linear approximation
and report that limitation. Templates with multiple model choices use the first available entry
unless the spawn specifies an override; live server random model/equipment state is not simulated.
Client skies, terrain collision, source editing/export and modern split ADT/CASC assets
are not implemented. Client landmark/heading parity remains unverified for deployment.

## Verification

Run `./Launch-Viewer.ps1 -CheckAssets` for archive, map, terrain, texture, and alpha-map checks. Run `./Launch-Viewer.ps1 -Smoke` for the Godot streaming test.

The smoke test loads nearby tiles, moves to a different neighborhood, checks unloading, and returns to verify revisiting. Running Godot with `-- --terrain-smoke` in graphical mode also saves `artifacts/terrain-preview.png`.

Run Godot with `-- --creature-smoke` for a live read-only database check of selection and cancellation/map switching. It requires the configured world database and the default Azeroth/Kalimdor terrain. Graphical mode saves `artifacts/creature-preview.png`.

- `dotnet run --project Tests/AssetChecks.csproj -- wow-data --models`: real M2/WMO/texture corpus,
  native Stand/Walk/Run clips, all interior-only WDT maps and outdoor WMO placement-bound checks.
- `dotnet run --project Tests/AssetChecks.csproj -- wow-data --liquids`: native liquid textures,
  masked/height/depth-only grids, malformed arrays and 49 representative ADTs.
- Godot `-- --water-smoke` and `-- --water-interior-smoke`: outdoor lake and Blackfathom liquid
  previews, animated screenshots and short warm frame samples in ignored `artifacts/`.
- `dotnet run --project Tests/AssetChecks.csproj -- --spawns`: live read-only SQL visibility, budgets,
  masks, event overrides, search escaping and cancellation checks.
- `dotnet run --project Tests/AssetChecks.csproj -- wow-data --gameobjects`: database display paths,
  native M2/WMO geometry, materials and animation palettes.
- Godot `-- --visibility-smoke`: GameObject model/list selection, layer unloading, the event-31
  mannequin, masks, canceled generations and broad-preview model admission caps.
- `dotnet run --project Tests/AssetChecks.csproj -- --draft`: draft persistence/conflict/recovery checks,
  including heading-only moves.
- `dotnet run --project Tests/AssetChecks.csproj -- --placement`: heading normalization, terrain
  height probes (flat/slope/hole), step-up choice and WMO floor probes without client data.
- Godot `-- --visual-smoke`: native models, animation menu, gizmo drag/reopen/undo/redo, short warm
  frame timings, map cancellation and a Stockade interior check. Saves native/interior/walk previews
  under ignored `artifacts/`; uses isolated test drafts and leaves your application draft intact.
- Godot `-- --gizmo-smoke`: focused movement/rotation/selection check with an isolated draft: ring
  rotation with cancel/undo/redo, drop to ground, fail-closed probes, a WMO floor chosen over terrain,
  and real Ctrl+Z/Q/W/E key events through the menu and toolbar.
- Godot `-- --reload-smoke`: interrupt a partially uploaded native model by reloading archives,
  then verify the replacement map/models finish loading.
- `dotnet run --project Tests/AssetChecks.csproj -- wow-data --graveyards`: live read-only graveyard
  table integrity against AreaTable.dbc, bounded regional reads, search escaping and cancellation.
- Godot `-- --graveyard-smoke`: live graveyard markers, a real viewport click on an icon, list
  agreement, move/drop/name/link edits with rule rejections, draft reopen, layer hiding and a map
  switch, using an isolated draft. Graphical runs save `artifacts/graveyard-preview.png`.
- `dotnet run --project Tests/AssetChecks.csproj -- wow-data --interactions`: client DBC offsets
  (Spell, TaxiNodes, ItemExtendedCost, ItemDisplayInfo) and real Goldshire innkeeper, quest giver,
  vendor and trainer interactions; bounded, escaped searches and cancellation.
- Godot `-- --interaction-smoke` (graphical): right-drag vs short right-click, gossip/vendor/submenu/
  quest pages, quest editor from a quest and the toolbar, Edit mode (fork, new submenu, greeting,
  vendor add/remove, flag warning fix, undo/redo, reopen) and close on map switch, on isolated
  drafts; saves `artifacts/interaction-*.png`.
- `dotnet run --project Tests/AssetChecks.csproj -- wow-data --content`: live read-only quest and
  behavior snapshots (names, rules as sentences, action lists, speech, free action-list IDs).
- Godot `-- --content-smoke` (graphical): Behavior editor on Innkeeper Farley (rule, speech line,
  Then step, reorder, action list, undo/redo) and Quest Editor (create, picker objective, story,
  rejected reward, givers, every page, undo, draft reopen, then locating an outdoor giver on another
  map and keeping the camera on it while terrain loads) on
  an isolated draft; saves
  `artifacts/content-*.png`.
- Godot `-- --skybox-smoke` (graphical): visits Light.dbc regions with skyboxes in Outland,
  Icecrown Citadel and Northrend, four headings each, and fails if the skybox changes more than 1%
  of opaque world pixels (found by rendering over two solid backgrounds; open sky and translucent
  surfaces are excluded). Saves views, plus leak masks on failure, under ignored `artifacts/`.
- `dotnet format whitespace ACWorldEdit.csproj --no-restore --verify-no-changes`: check the
  committed C# formatting rules; run without `--verify-no-changes` to apply them.

Liquid schema reference: [WoWDBDefs LiquidType](https://github.com/wowdev/WoWDBDefs/blob/master/definitions/LiquidType.dbd).

Format and engine references: [StormLib](https://github.com/ladislav-zezula/StormLib), [Warcraft ADT implementation](https://github.com/wowemulation-dev/warcraft-rs), and [Godot ArrayMesh](https://docs.godotengine.org/en/4.7/classes/class_arraymesh.html).

## Code layout

- `Scripts/Viewer/`: `TerrainViewer` (camera navigation, terrain streaming, archive/map lifetime and
  its `.Ui` controls), minimap, sky/time-of-day lighting, viewport picking, and the Godot-bound
  `ClientLighting`/`AreaTable` DBC readers that feed them. `TerrainViewer.Ground` combines terrain and
  WMO probes for placement snapping.
- `Scripts/Rendering/`: main-thread GPU resources for models, terrain, liquids and world objects.
- `Scripts/Overlays/`: bounded native creature/GameObject spawn projection, selection, creature
  draft placement (`CreatureOverlay.Editing`) and the move/rotate gizmo; each overlay keeps its
  inspector UI in a `.Ui` partial.
- `Scripts/Ui/`: menu bar, toolbar, scene sidebar and entity inspector.
- `Scripts/Diagnostics/`: opt-in viewer/creature smoke workflows; normal editing does not run them.
- `Scripts/Assets/`: engine-independent decoders, compiled into `Tests/` as well:
  - `Archives/`: MPQ reading, the client mount profile and the shared `AssetStore`.
  - `Formats/`: low-level binary readers, chunk streams, DBC tables and BLP textures.
  - `Client/`: DBC-backed creature/GameObject display catalogs and liquid textures.
  - `Models/`: M2/WMO model decoding and animation data.
  - `World/`: ADT/WDT terrain, liquids, map metadata, placed world objects and ground probes.
- `Scripts/Data/`: shared read-only SQL snapshots and creature/GameObject visibility filtering.
- `Foundation/`: engine-independent coordinates, spawn records, the shared `DraftFile` and durable
  creature and graveyard drafts.
- `Tests/`: native asset corpus, synthetic format fixtures and draft recovery checks.

The obsolete Phase 0 shell/plugin, synthetic benchmark, generic draft workspace and experimental
SQL exporter have been removed. Reviewed SQL export remains future work in PLAN.md.
The useful independent MPQ comparison remains available through `./Tests/Run-StormLibChecks.ps1`.
