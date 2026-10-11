# Progress

## Milestone 1: display and select existing creatures — complete

The active plan is PLAN.md. The user's future-workflow additions remain intact; research was
deliberately removed. Roadmap simplification was saved in commit e89db16; implementation was saved in 30b71de.

- External worldserver configuration supplies credentials in memory. Fixed, parameterized SELECT
  queries run asynchronously in read-only transactions, bounded to 2,000 spawns plus a limit check.
- Labeled capsule proxies display existing creature positions and headings. Viewport/list selection
  highlights a proxy and shows GUID, template, map, coordinates, heading and spawn/phase masks.
- Loaded-list filtering, refresh and fly-to-selection are available. Region changes cancel queries;
  generation checks discard stale results. Marker creation is bounded per frame on the main thread.
- Connection/configuration errors are sanitized; credentials are absent from repo, UI and logs.

## Acceptance evidence — 2026-10-11

- Build: .NET/Godot build passed with zero warnings/errors.
- Real spawn display: actual world database returned 1,198 creatures near the default Elwynn tile.
  Graphical preview inspected in artifacts/creature-preview.png: Prowler GUID 81341, template 118,
  server position (-9321.020, -1352.160, 67.530), readable label, selected proxy and matching details.
- Selection agreement: live creature smoke projects/clicks a marker and asserts both selected GUID
  and ItemList metadata match. Graphical check also confirms the list scrolls to the selected entry.
- Stale-result protection: smoke starts refresh then immediately switches map. Previous query is
  cancelled/discarded; Kalimdor returns 564 records, all map 1. Headless and graphical smoke passed.
- Navigation regression: terrain smoke loaded nine tiles, moved to another region, unloaded/reloaded
  and revisited successfully. No creature changes to the terrain streaming pipeline.
- Source safety: adapter contains only SELECT, begins a read-only transaction and rolls it back.
  No client/server configuration, source assets, privileges or database rows were modified.

## Limits and next milestone

Proxies are not creature models. Picking uses screen-space targets without terrain occlusion.
All spawn/phase masks are displayed; phase filtering and scripted simulation remain future work.
The 2,000-result cap can omit spawns in dense regions and is reported in the UI. Full in-game
coordinate/heading parity and comprehensive failure/limit stress testing are not claimed.

Next requested implementation milestone: move existing creatures in saved drafts with undo/redo.
Verify positions/headings against actual client landmarks before enabling editing. SQL export
follows later and requires application/reversal proof in an authorized disposable database.

## Native visual milestone — implementation checkpoint

User expanded scope to native creature models/materials/idle animation, outdoor objects/buildings,
interior WMO maps and a local movement gizmo, with client visual fidelity taking priority.

Implemented native MD20/SKIN and WMO group readers, CreatureDisplayInfo/model/extra DBC resolution,
ADT/WDT placements, WMO doodad sets, batched shared models/materials, GPU idle palettes and incremental
background loading. Minimap is now inside the left panel. A selected creature has world-axis handles;
moves are durable local drafts with undo/redo, isolated source keys and baseline conflict detection.

Checkpoint evidence: build passes; corpus parsed 116 M2 assets and 52 WMO assets covering all 39
WMO-only maps in this client; native model textures decoded. Graphical Elwynn preview showed textured
wolves/trees with 5,845 unique static placements and 254 nearby creature models. Interior rendering
of map 35 was visually inspected, including textured walls, lamps and placed doodads. Headless gizmo
check passed drag, model transform, draft reopen and undo/redo; standalone draft checks also passed
source isolation/conflict, history branching, checksum rejection and single-writer locking.

Continuing visual/performance validation before final handoff. Native idle data is used; full client
render parity is not claimed. Character equipment/appearance composition, particles/ribbons, advanced
multitexture/environment shaders, portal culling and non-idle playback are still unsupported. Such
material limitations are reported. Source assets, configuration and database remain read-only.
