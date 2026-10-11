# Progress

## Current state — native world preview and local position drafts

The minimap is inside the left panel. Native creatures, terrain doodads/buildings and WMO-only
interiors now load from the stock client. Selecting a creature exposes its movement gizmo and
available native animation clips. Stand/idle is the default; other clips preview in place.
Database, server configuration and client assets remain read-only. Credentials stay external.

Saved checkpoints: e89db16 simplified roadmap; 30b71de read-only creature selection;
65bd63a native model/idle/world-object/interior/gizmo implementation.

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
portal culling, liquids and client skies. Basic NPC baked textures/geosets are previews. Native spline
tracks use linear approximation; long clips cap at 601 samples. Unsupported material cases are
reported, including a base-texture fallback for the tested weapon rack. Animation variations use
one deterministic clip rather than live server state. Selection does not test terrain occlusion.
Drafts far outside their source query region reappear when that source region is queried again;
a cross-region draft index remains follow-up work.

Prioritize those visual gaps in further work. Local position previews are implemented; yaw editing,
client landmark/heading verification and reviewed SQL export remain later work. Deployment/export
must not be declared ready without those checks and an authorized disposable-database apply/reverse
proof. Source data and unknown row fields remain untouched.
