# Feature specifications

All behavior below is **proposed**. Existing capability evidence is in 02 and 03; this document specifies product behavior, scope and failure handling.

## Workspace and editor shell
Open Project selects a core profile, client profile, read-only world source/snapshot and local draft directory. Present a health summary: supported schema, core revision, client build, missing required archives and available capabilities. Separate Development, Staging and Production identities by explicit name and connection metadata; MVP has export only.

Main layout: project/template palette left, world viewport center, contextual inspector right, changes/validation/diagnostics bottom. Layout persists locally. Scene tree groups entities by map/region/type/phase/event, with search and virtualized results. Toolbar mode is visible: navigation, select, place, path, client terrain. Only supported modes activate.

## F1 World scene and navigation
- Outdoor map selection, minimap jump, coordinate teleport and bookmark.
- Stream tiles while retaining selection/edit state by stable ID; unloaded selection has a location shortcut.
- Independent toggles: terrain, static geometry, creatures, GOs, paths, triggers, relationships.
- Show map difficulty/spawn mask, phase and event filters. Explain filtered records; do not pretend static filters predict active runtime events.
- Template/display errors become selectable labeled proxies with diagnostic links.
- Search results offer name/ID/map/position; load region around result.
- Acceptance: navigation does not lose draft changes, selection or path commands on unload/reload; map switching cannot show stale async results.

## F2 Selection and transforms
Click/box select, hover ID/name, additive selection, focus camera, hierarchy synchronization. Gizmos support translate and yaw for creature spawns; MVP GO editing is yaw only with verified quaternion conversion. Manual coordinate entry always available. Gizmo uses current camera scale and does not move world terrain.

On drag, preview only. Release creates one transform command; Escape restores original. Multiple selection creates one atomic command with per-entity before/after poses. Snapping options: grid distance, terrain triangle, existing point; surface normal alignment is capability/type-dependent and initially disabled. Terrain holes have no snap hit. Interior/flying/transport placements should be explicitly unsupported in MVP.

Acceptance: undo/redo restores exact domain values; moving one template instance never moves others. Off-screen dirty spawns remain saved.

## F3 Placement and spawn inspector
Search an existing template by name/entry/display; drag onto verified terrain or enter coordinates. Display a preview proxy and template provenance. On commit assign a project-local UUID, resolve SQL GUID only during export. Inspector separates Placement from Shared Template; each inherited override has an explicit badge. Default masks are profile/map-derived, not a universal hard-coded value.

MVP editable fields: position, orientation, spawn/phase masks, respawn seconds, stationary/random movement and wander radius for ordinary creatures; GO yaw, respawn and supported state. Preserve all nonexposed values. Delete prompts through a dependency panel when backlinks/shared references exist; complex entities are read-only until their deletion semantics are implemented.

## F4 Integrated NPC/content inspectors
Definition inspector tabs: identity/appearance, stats/faction, AI, dialogue, quests, vendor/trainer, loot, dependencies. References use searchable semantic pickers with autocomplete and jump-to-definition. Show shared record consumers and offer clone/link/edit-shared choices. Recommended values are curated recipes tied to core profile, not undocumented SQL defaults.

For new creature templates, clone a tested baseline recipe, allocate definition/model/service IDs by namespace, then author required fields. Hide VerifiedBuild from routine editing; it is evidence metadata and must not imply sniff verification. Detailed raw fields are an advanced diagnostic view, with unsupported fields preserved.

## F5 Quest authoring
Structured quest editor covers text/locales, requirements, four NPC/GO and six item objectives, rewards/choice rewards, starters/enders and prerequisites. Graph view summarizes prerequisite semantics; objectives themselves are structured forms. Authoring more objectives than the 3.3.5 schema supports is blocked or requires explicitly scripted staged quests.

Compiler validates signed target encoding, item/spell/faction references, slot capacity, empty/duplicate slots, counts, quest relation wiring and prerequisite cycles/contradictions. Cycles are classified by semantics rather than indiscriminately deleting edges; unsupported loops require expert review. Preview is client-style text/data, not a fully simulated quest runtime.

## F6 Patrol and triggers
Path tool: choose NPC -> Create/Clone Route -> click terrain points -> drag/reorder -> set wait/orientation/move style -> attach. Show direction arrows, point numbers and shared ownership. Use waypoint_data and addon wiring for normal patrols; use SmartAI waypoints only through a behavior profile. Preserve spline addon and scripts; unsupported complex paths can be viewed without rewriting them.

Trigger overlays show source and coordinate space, sphere/box/cylinder support only when known. Missing shape provenance displays an anchor instead of inventing bounds. Server-defined and client DBC triggers are different assets.

## F7 Behavior editor
Start with SmartAI event cards: When, Constraints, Action, Target, Follow-up. Provide direct numeric row view for expert inspection. Graph is a presentation of source-supported events, links and timed lists; no arbitrary execution order implied by visual layout. Unknown enums/parameters remain intact; edit/export capability depends on profile. Conditions editor presents OR groups of AND predicates and typed references.

Lua/C++ escape hatch opens the configured IDE at script definition with the selected entity context. AI backend selection warns about competing script ownership and necessary rebuild/runtime modules.

## F8 Asset catalog and client tools
Separate template catalog from raw asset browser: Creature entry, display ID, model path and ADT placement ID are not interchangeable. Index DBC and listfile/path references into a local catalog with winning archive, build/profile and thumbnail diagnostics. Lazy previews; cancellation/resource budgets; failed preview never blocks SQL editing.

First client-authoring integration: open selected tile/asset in configured specialist tool, reindex project overlay, show changed asset manifest. Native height/paint/water is gated by lossless writer tests. Raw file edits never become unreviewed database changes.

## F9 Change review and diagnostics
Changes panel presents semantic changes grouped by object/task, touched rows, dependencies, warnings, SQL diff and deployment requirements: DB, client patch, server rebuild, geometry extraction, restart. Errors deep-link to inspector fields or graph nodes. Block export on wrong profile, unresolved new ID, invalid reference, stale baseline or unsupported changed semantics; warnings require acknowledgement recorded in manifest.

Export produces stable forward SQL, conditional reverse SQL, read/write-set manifest and human release notes. Exporting is not applying; editor UI tracks these as distinct states. Checkpoints survive crash and allow recovery without executing commands against a database.

## Cross-cutting acceptance
Keyboard navigation, readable field labels/units, searchable enum descriptions, tooltips with source provenance, mixed-value multi-edit state, autosave status and cancellable long jobs. Set targets for response latency and resource limits in Phase 0 hardware benchmark; avoid hiding errors behind generic success messages.

