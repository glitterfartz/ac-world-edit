# Progress

## Current state — native world preview and local position drafts

The minimap is inside the left panel. Native creatures, terrain doodads/buildings and WMO-only
interiors now load from the stock client. Selecting a creature exposes its movement gizmo and
available native animation clips. Native outdoor and WMO liquids now render. Stand/idle is the default; other clips preview in place.
Database, server configuration and client assets remain read-only. Credentials stay external.

Saved checkpoints: e89db16 simplified roadmap; 30b71de read-only creature selection;
65bd63a native model/idle/world-object/interior/gizmo implementation;
ea45d17 native clip previews and validation.

## Implemented

- CreatureDisplayInfo/ModelData/Extra and AnimationData DBC lookup; MD20/SKIN geometry, texture
  variations and native bone tracks, including aliases and external .anim files.
- WDT global WMO interiors; ADT M2/WMO placements; WMO groups/materials/vertex lighting and selected
  doodad sets. ADT references deduplicate by object ID. Placement facing is separate from creatures.
- Shared meshes, materials and full-resolution model textures; MultiMesh batches; one decode worker,
  incremental surface uploads and GPU animation palettes. Nearby creatures render within 350 yards.
  Dormant resources evict above a soft 256 MiB target; in-use resources may exceed it.
- Native model bounding-box selection, fallback markers, filtered list, details and fly-to-selection.
- Axis gizmo drag/cancel; durable local position drafts; undo/redo/reopen; source identity isolation,
  stale-baseline detection, checksums and single-writer locking. No yaw editing or terrain snapping.
- Updated PLAN.md/AGENTS.md/README.md; user's future-workflow additions preserved.

## Evidence — 2026-10-11

- Build passed with zero warnings/errors.
- Real asset corpus: 116 M2 assets, 52 WMO assets, 1,898 material texture decodes, all 39 WMO-only
  maps in this client. Nine native animated models had varying sampled frames. Wolf Stand/Walk/Run
  clips parsed at 3333/800/734 ms with 100/25/23 native pose samples respectively.
- Fourteen outdoor WMO placements: projected geometry stayed inside source MODF bounds with zero
  measured horizontal excess. This validates the static placement basis, not complete client parity.
- Live database smoke: 1,198 Elwynn spawns, viewport/list selection and cancellation/map switching
  to 564 Kalimdor spawns passed. Terrain streaming moved/unloaded/revisited nine tiles successfully.
- Graphical visual smoke: textured creatures/trees, native Walk/Stand menu switching, gizmo drag,
  matching model transform, draft reopen/undo/redo and map-34 Stockade interiors passed. Interior
  geometry, lighting, textures and embedded doodads were visually inspected; map 35 also checked.
- Warm Elwynn sample on RTX 5080 at the user's 2440x1400 viewport: 5,845 unique static placements,
  254 native creatures, 336 cached model variants, 444 textures and about 164 MiB estimated cache.
  About 710 frames over four seconds: median 4.17 ms, 95th percentile 5.56 ms; settled FPS ~234.
  This is a short scene-specific check, not broad hardware/large-city performance certification.
- Standalone draft checks passed move/reopen/undo/redo, branching, source isolation/conflicts,
  single-writer locking and checksum rejection. Smoke tests use isolated artifact drafts.
- Previews/logs remain in ignored artifacts/native-preview.png, walk-preview.png, interior-preview.png,
  native-model-checks.txt and native-graphical.out.txt. Reproduction commands are in README.md.

## Remaining visual fidelity and editing work

Full client parity remains incomplete: detailed character hair/clothing/equipment composition,
particles/ribbons, advanced multitexture/environment shaders, material/UV animation, billboard bones,
portal culling and client skies. Water lighting/reflections/refraction, underwater fog, WMO flow
overrides and procedural liquid entry 100 remain incomplete; water tint/opacity are approximations. Basic NPC baked textures/geosets are previews. Native spline
tracks use linear approximation; long clips cap at 601 samples. Unsupported material cases are
reported, including a base-texture fallback for the tested weapon rack. Animation variations use
one deterministic clip rather than live server state. Selection does not test terrain occlusion.
Drafts far outside their source query region reappear when that source region is queried again;
a cross-region draft index remains follow-up work.

Prioritize those visual gaps in further work. Local position previews are implemented; yaw editing,
client landmark/heading verification and reviewed SQL export remain later work. Deployment/export
must not be declared ready without those checks and an authorized disposable-database apply/reverse
proof. Source data and unknown row fields remain untouched.

## Water rendering — 2026-10-11

- Added MH2O layers, visibility bitmaps, per-vertex heights/depth and native height/UV grids;
  depth-only oceans use their native flat level. Legacy MCLQ and WMO MLIQ grids preserve hidden cells.
  WMO liquid IDs follow root/group flags and legacy type conversion; lava/slime retain native UVs.
- LiquidType.dbc selects native BLP frame sequences. Shared per-archive CPU atlases, GPU animation
  at 30 Hz, static meshes and one outdoor batch per type/tile avoid per-frame geometry work.
  Outdoor GPU textures/materials release on unload; WMO resources use existing shared cache/batches.
  Water uses approximate blue tint, depth opacity and view-angle blending; lava/slime use native RGB.
- Evidence: build passed; liquid checks cover 49 ADTs, 3,564 layers and 216,230 visible cells.
  Synthetic masks, native heights, depth-only oceans, legacy/mixed MCLQ grids, WMO hidden cells
  and malformed arrays passed.
  All 25 classic liquid catalog entries decoded; native water has 30 animation frames with varying pixels,
  fast-river sequences 16. Identical sequence atlases share CPU storage. Procedural entry 100 has
  an unavailable source texture and reports unsupported; unsupported liquid surfaces are skipped.
- Model corpus passed: 116 M2, 52 WMO, 1,988 material/atlas decodes, all 39 interior-only maps;
  fourteen outdoor placements still meet source MODF bounds. Nineteen tested WMOs contain liquids,
  including Stormwind, Blackfathom, Coilfang, Blackrock lava and Hellfire slime.
- Graphical lake (Azeroth tile 32,49) and Blackfathom map 48 inspected: native heights, visible waves,
  transparent lakebed, shoreline terrain clipping and WMO pool/building occlusion. Nine outdoor
  liquid batches cover nine loaded tiles; Blackfathom has 17 native liquid surfaces.
  An isolated four-second RTX 5080 lake sample at 2440x1400 had median 15.38 ms and p95 16.67 ms
  across 191 frames (8,475 static objects, nine tiles). This is scene-specific, not a baseline
  comparison or broad performance certification.
  Paired water-region screenshots show animated pixel changes; native atlas frame checks also pass.
  Captures/logs are ignored artifacts/water-preview.png, water-animated.png, water-interior.png,
  water-interior-animated.png, liquid-checks.txt and water-model-checks.txt.
- Terrain unload/move/revisit smoke passed nine tiles at each step. Client, server and database data
  remain unchanged. User viewport configuration and unrelated generated UID files remain unstaged.
- Fidelity follow-ups: client Light.dbc water colors, reflections/refraction, underwater fog,
  exact client playback/blending and WMO flow overrides. No source water editing or collision added.

## Refactor and cleanup — completed 2026-10-11

- Saved structural checkpoint 5b277b7. Removed the retired shell/plugin and synthetic benchmark,
  duplicate generic draft workspace, unused SQL exporter and stale research-linked documents.
  Active durable creature drafts and all useful native asset/draft checks remain.
- Moved the independent MPQ comparison runner to Tests/Run-StormLibChecks.ps1. Viewer UI and
  opt-in smoke workflows now have separate partial files. Binary/DBC/creature catalog/map/liquid
  catalog/model binding helpers have focused files; AnimationPalette names all supported clips.
- Applied .editorconfig rules, expanded compressed statements/control flow, removed unused
  imports/members and formatted project XML/shaders. Extensive class/method and algorithm comments
  document formats, transforms, ownership, cancellation, persistence and unsupported behavior.
- Streaming and model-cache update methods now delegate named lifecycle steps. Animation palette
  texture/uniform names match their purpose. Removed the unused playback override from liquid shader.
- Fixed interrupted model uploads leaking their explicit resource ownership on archive reload:
  the in-progress entry now releases meshes/palette/shared texture references before store reset.
- Validation: main build zero warnings/errors; formatting verification passed. Asset/geometry and
  malformed-input checks passed, including 133 independent byte comparisons against pinned StormLib.
  Liquid corpus: 49 ADTs, 3,564 layers, 216,230 visible cells. Model corpus: 116 M2, 52 WMO, 1,988
  material/atlas decodes, 39 interior maps and fourteen MODF bounds checks. Draft recovery checks passed.
- Graphical regression passed real viewport/list selection, native Walk/Stand, gizmo movement and
  reopen/undo/redo, canceled query/map switching and Stockade interiors. Nine-tile streaming
  unload/revisit passed. Archive reload interrupted a partial GPU upload and returned to nine
  loaded tiles, 333 ready models and 442 textures. Blackfathom liquid graphical check passed.
  Evidence remains under ignored artifacts/refactor-*.txt, refactor-*.out.txt and preview PNGs.
- Source client/server/database data and the user's viewport configuration remain unchanged.
  Event/pool/phase visibility filters remain future work; this cleanup does not change spawn display.
- Automatic approval review rejected recursive deletion of obsolete ignored Experiments build
  caches with "blocked by policy". Those caches remain ignored; their source projects/classes
  are removed, and generic bin/obj exclusions prevent stale generated code entering the build.

## Visibility controls and GameObjects — in progress

- Added immutable bounded preview settings and SQL-only creature/GameObject adapters. Event
  membership (including negative event links), phase/difficulty overlap, pool membership, hidden
  helpers, SQL conditions, script bindings and initially despawned GameObjects filter before LIMIT.
- Default: phase 1/normal, SQL-scheduled events, no pool alternatives/helpers/conditional objects/
  initially despawned objects. Script-bound spawns remain included because bindings do not prove
  invisibility. Missing required metadata fails closed; broad inspection still has hard bounds.
- Read-only SQL evidence: GUID 96098/template 26724 is hidden by defaults and visible with All
  stored spawns or manual event 31. Phase mismatch, nearest-first three-row cap, script filter,
  cancellation and 59 ordinary GameObjects at the example location passed.
- Remaining: native GameObject rendering/selection, performance-aware layer toggles, polished
  scene/creature/GameObject inspector and end-to-end rendering/UI/filter tests.
