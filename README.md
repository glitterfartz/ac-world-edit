# ACWorldEdit world viewer

A C# / Godot 4.7.1 .NET viewer for the Wrath 3.3.5 MPQs in `wow-data`.

## Run

Open `project.godot` in the **.NET** edition of Godot, build the C# project, and press **F5**. It automatically opens Azeroth tile **34,49**, near Elwynn.

Or run `./Launch-Viewer.ps1` from PowerShell. The launcher builds the project and locates Godot from the existing shortcut. Supply `-GodotPath` to use another installation. Requires .NET SDK 8 and Godot .NET 4.7.1; SharpZipLib 1.4.2 handles MPQ bzip2 compression.

The simple roadmap is in [PLAN.md](PLAN.md); current results and limits are in [PROGRESS.md](PROGRESS.md).

## Existing creatures

The right panel reads `WorldDatabaseInfo` from the external file
`X:\AzerothCore\azerothcore-wotlk\build\bin\Debug\configs\worldserver.conf`.
Set `ACWORLD_SERVER_CONFIG` before launching or change the path in the panel and click **Refresh creatures**.
Credentials stay outside the project. Queries run in read-only transactions; nothing is written to the world database.

- Nearby creatures refresh when the camera changes tile region or map.
- Click a creature model or choose a list entry to select it and inspect its GUID, template, display, coordinates, heading and masks.
- Filter the loaded list by name, GUID or template ID. **Fly to selected creature** brings the camera to its position.
- Queries show at most 2,000 creatures; reduce the nearby tile radius if the limit is reached.

Creatures within 350 yards load native M2/SKIN models and display textures, with native Stand/idle
animation on the GPU. Selecting a creature exposes **Preview animation**, populated from that
model's available clips and client animation names. Previewing Walk or another clip changes no
spawn behavior. A labeled capsule remains when an appearance cannot be resolved.

Selection exposes a colored movement gizmo: red is view X, green is height, blue is view Z.
Drag an axis to move; release saves a local draft. **Esc** cancels the drag. **Undo move** and
**Redo move** persist history. Drafts reopen from Godot's `user://drafts/creature-positions.json`
under the application data directory. A source position conflict prevents applying a stale draft.
Horizontal moves preserve height; there is no terrain snapping, yaw editing or SQL export yet.
Queries still locate spawns by their source positions. A draft moved far outside its source region
reappears when that source region is queried again; a cross-region draft index is follow-up work.

The editor shows database placements, including inactive event/pool spawns. Spawn/phase masks
are displayed without simulation or filtering. Model picking uses bounding
boxes, while fallback markers use a screen-space target; terrain occlusion is not tested.

## Explore

- Hold **right mouse** to look and fly; **WASD** moves relative to the camera.
- **Q/E** descends/ascends; **Shift** accelerates; the **wheel** adjusts speed.
- **Esc** releases the mouse. Losing window focus also releases it.
- Choose a world and tile X/Y, then click **Open world**. An unavailable tile selects the closest terrain tile.
- WMO-only worlds open their global building directly; tile controls are disabled. Fly to explore interiors.
- Click land in the minimap to jump. Mint tiles are loaded; white marks the camera.
- Nearby tile radius defaults to **1**, loading up to a 3×3 neighborhood. Increase for a wider view or use 0 for one tile.
- Toggle cell borders to see the 16×16 terrain chunks in each tile.
- Enter another MPQ directory and click **Load archives** to reopen the asset source.

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
- `dotnet run --project Tests/AssetChecks.csproj -- --draft`: draft persistence/conflict/recovery checks.
- Godot `-- --visual-smoke`: native models, animation menu, gizmo drag/reopen/undo/redo, short warm
  frame timings, map cancellation and a Stockade interior check. Saves native/interior/walk previews
  under ignored `artifacts/`; uses isolated test drafts and leaves your application draft intact.
- Godot `-- --gizmo-smoke`: focused headless movement/selection check with an isolated draft.
- Godot `-- --reload-smoke`: interrupt a partially uploaded native model by reloading archives,
  then verify the replacement map/models finish loading.
- `dotnet format whitespace ACWorldEdit.csproj --no-restore --verify-no-changes`: check the
  committed C# formatting rules; run without `--verify-no-changes` to apply them.

Liquid schema reference: [WoWDBDefs LiquidType](https://github.com/wowdev/WoWDBDefs/blob/master/definitions/LiquidType.dbd).

Format and engine references: [StormLib](https://github.com/ladislav-zezula/StormLib), [Warcraft ADT implementation](https://github.com/wowemulation-dev/warcraft-rs), and [Godot ArrayMesh](https://docs.godotengine.org/en/4.7/classes/class_arraymesh.html).

## Code layout

- `Scripts/TerrainViewer.cs`: camera navigation, terrain streaming and archive/map lifetime.
- `Scripts/TerrainViewer.Ui.cs`: viewer controls and minimap layout.
- `Scripts/Diagnostics/`: opt-in viewer/creature smoke workflows; normal editing does not run them.
- `Scripts/Assets/`: engine-independent archive, texture, terrain, model and animation decoders.
- `Scripts/ModelLibrary.cs`, `TerrainRenderer.cs`, `LiquidRendering.cs`: main-thread GPU resources.
- `Scripts/CreatureOverlay.cs`, `TranslationGizmo.cs`, `Data/CreatureSource.cs`: read-only spawns,
  selection and draft movement.
- `Foundation/`: engine-independent coordinates, spawn records and durable creature drafts.
- `Tests/`: native asset corpus, synthetic format fixtures and draft recovery checks.

The obsolete Phase 0 shell/plugin, synthetic benchmark, generic draft workspace and experimental
SQL exporter have been removed. Reviewed SQL export remains future work in PLAN.md.
The useful independent MPQ comparison remains available through `./Tests/Run-StormLibChecks.ps1`.
