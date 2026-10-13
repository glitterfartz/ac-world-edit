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
  stale-baseline detection, checksums and single-writer locking. Yaw editing and ground snapping
  were added in the step 3 checkpoint below.
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

Prioritize those visual gaps in further work. Position/yaw drafts and ground snapping are
implemented; client landmark/heading verification and reviewed SQL export remain later work. Deployment/export
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
- Fidelity follow-ups: reflections/refraction, underwater fog, exact client playback/blending and
  WMO flow overrides remain approximate. Light.dbc water/ocean/river color bands are now parsed
  but not yet wired into liquid tint (see the lighting section below). No source water editing or
  collision added.

## Lighting, shadows, time-of-day and zone/continent UI — implemented 2026-10-12

- Added real-time shadow casting: terrain chunks and native model/WMO/creature/GameObject geometry
  now cast shadows from the directional light (`CastShadow.On`); liquids, gizmo arms and fallback
  marker boxes stay non-casting on purpose (matches client water, keeps editor overlays inert).
  `DirectionalLight3D` uses 4-split parallel shadows with blended splits out to 400 yards.
- Added `Scripts/Assets/ClientLighting.cs`, reading Light.dbc/LightParams.dbc/LightSkybox.dbc/
  LightIntBand.dbc through the existing `DbcTable` reader (extended with positional `UAt`/`FAt`/
  `Position` accessors, since LightIntBand associates with a LightParams row by file position, not
  ID). Only each region's Default time-of-day slot and five bands (diffuse, ambient, sky top,
  sky horizon, fog) are used; the other seven weather/combat/dungeon slots and bands 8-17
  (secondary accents, water-specific colors) are explicit future fidelity work, not fabricated.
- The Light.dbc position/falloff coordinate convention (1/36-yard scale, tile-grid-corner origin,
  no recentering) was not assumed from memory: a scratch probe (gitignored,
  `artifacts/light-probe/`) brute-force-checked candidate scales/origins/axis orders against real
  WDT tile presence for Eastern Kingdoms, Kalimdor and Northrend, and this convention was the clear,
  consistent winner (126/126 Kalimdor, 96/97 Northrend, 77/81 Eastern Kingdoms local lights land on
  an existing tile; the few misses are plausible dev-leftover rows). Band semantics were checked
  the same way: EK's default LightParams bands show physically sensible day/night colors (warm
  orange diffuse at noon, blue-gray at night; bright horizon haze at noon, orange at sunset/sunrise).
- Added `Scripts/SkyLighting.cs`: a time-of-day clock (default 09:00, adjustable play/pause and
  speed), per-map region resolution (falloff-weighted blend between the map's default light and the
  nearest in-range local light), and application to the sun color/energy/approximate elevation,
  `WorldEnvironment` ambient/fog, a `ProceduralSkyMaterial` gradient, and the LightSkybox M2 model
  (loaded through the existing `ModelLibrary`/`ModelInstance` pipeline, camera-anchored, forced
  non-shadow-casting). Interior/WMO-only maps and maps without a usable lighting table keep the
  previous fixed neutral look rather than guessing at portal-gated interior light. Sun
  direction/elevation across the day is a simple, explicitly approximate cosine curve, not a
  reconstruction of the client's exact sun trajectory.
- Added a zone/continent title above the minimap. `TerrainChunk.AreaId` (MCNK offset +52, confirmed
  against the already-working offsets for height/holes/layers in the same header) is now parsed and
  kept per loaded tile; `Scripts/Assets/AreaTable.cs` resolves it through AreaTable.dbc (schema
  confirmed against known IDs: Elwynn Forest=12, Durotar=14, Stormwind City=1519, Ironforge=1537,
  Orgrimmar=1637). Shows "Parent — Subzone" when the chunk is inside a named subzone, plus the
  map's own Map.dbc display name as the continent/instance line. Interiors (no ADT) show only the
  map name.
- Added time-of-day controls (pause/resume, 0-24h scrub slider, speed presets) to the main panel,
  and wrapped that panel's content in a `ScrollContainer` anchored above the bottom telemetry bar
  (matching the Scene explorer's own pattern) so the added controls — and anything added later —
  cannot clip off-screen on a shorter window; previously this panel had no scroll and only fit by
  luck.
- Evidence: main build and `dotnet format whitespace --verify-no-changes` both passed clean. Ran
  the live viewer twice via the Godot MCP (`run_project`/`get_debug_output`/`stop_project`) over
  Elwynn/Stormwind on continent 0 for 30+ seconds each, including creature/WMO streaming; no
  exceptions or new warnings from the lighting/area/UI code, only pre-existing unrelated model
  warnings (weapon rack base-texture fallback, Tauren replaceable-texture type 8). This confirms
  runtime stability, not visual correctness — nobody has yet eyeballed the sky/shadow/zone-label
  rendering in the running editor, which this repo's own standard requires before calling visual
  fidelity work done. Do that before relying on this for anything client-facing.
- Not done: Light.dbc's seven non-Default weather/dungeon/death LightParams slots; bands 8-17
  (water/ocean/river colors, which could later replace the approximate liquid tint); WMO interior
  portal-based light isolation (interiors keep the old flat look); exact sun trajectory; a
  position-based pick among several overlapping local lights only uses the smallest-radius one,
  never a true multi-light blend.

## Streaming/smoothness pass — implemented 2026-10-12

User report: flying around made placeholder creature markers flicker against native models, native
models/terrain scenery popped in and out, and models took a long time to load. Root causes found by
reading the streaming paths, not by guessing:

- `CreatureOverlay`/`GameObjectOverlay` rebuilt a creature/GameObject's whole native MultiMesh batch
  whenever its GUID list changed — including pure *reorder* with no membership change, which happens
  on nearly every tick while flying because the candidate list is sorted by live camera distance.
  Rebuilding tears down and recreates the `ModelInstance`, which starts unbuilt (fallback marker
  visible) until the next bind pass. Fixed both to compare membership (`SetEquals`) instead of
  order (`SequenceEqual`).
- `WorldObjectScene` (ADT doodad/WMO placement) deduplicates border-shared objects referenced by more
  than one neighboring tile by recomputing "which tile owns it" from scratch, in tile order, on
  every single tile load/unload. Whichever tile happened to be enumerated first that call could
  reassign a shared prop's ownership away from an already-loaded, unrelated tile, tearing down and
  rebuilding that tile's batch for no reason — this is what flickered terrain scenery/buildings
  specifically near tile borders. Fixed with a persistent `owner` map: an object keeps its current
  owner tile as long as that tile stays loaded, regardless of what else streams in or out.
- `TerrainViewer.UpdateWanted()` released a loaded tile the instant it left the exact `radius`
  window, so turning or wobbling right at the boundary repeatedly released and reloaded the same
  tile. Added one extra ring of hysteresis (`radius + 1`) before release; the load window itself is
  unchanged.
- The same method also rebuilt the creature/GameObject SQL query region tightly around the camera
  tile every call, so crossing any tile boundary (~533 yards — easy to do several times a minute
  while flying) produced a *new* `CreatureRegion` record and triggered `SetRegion` → a full requery.
  Made the region sticky with a 3-tile pad: it only moves once the camera drifts near its edge, or
  the radius setting itself changes.
- Deeper issue once the above landed: the overlays' own 64-yard quantized camera-query trigger
  (`SpawnQueryPoint`, pre-existing, meant to bound DB load) was *itself* still destructive —
  `Restart()` synchronously freed every marker/native-model/label before the replacement query
  returned, so the UI visibly emptied and showed "Loading nearby creatures…"/"Loading nearby
  objects…" on every quantized step, independent of the region fix above. Split both overlays'
  restart into a soft path (routine camera movement: cancel and reissue the query, leave the current
  display untouched) and the existing hard path (explicit filter/layer/map changes: wipe and
  reload). Added `CreatureOverlay.ReconcileSpawns`/the GameObject result handler's `lookup` diff to
  merge the arriving result by GUID — removing creatures/objects no longer present, keeping existing
  markers' nodes and native models exactly as they are, and only queuing genuinely new spawns for
  the existing incrementally-budgeted marker creation. `GameObjectOverlay.Reconcile()` already diffed
  its fallbacks/labels/batches this way; only its `Restart()` was destructive.
- `ModelLibrary.UploadPendingModel` uploaded exactly one surface per frame regardless of budget, so
  a multi-surface model (a creature with several materials, a WMO) held the single background decode
  worker idle for several extra frames per model purely waiting on the main-thread upload queue
  before the next model could even start decoding. Changed to a soft per-frame time budget (up to 8
  surfaces / ~1.5 ms), mirroring the pattern terrain chunk upload already used.
- Added `VisibilityRangeEndMargin` to native model/doodad instances. With the default (disabled)
  fade mode this acts as pure hysteresis around the distance cutoff (confirmed in the Godot 4.7 API
  docs before relying on it) — geometry doesn't pop back in and out from being near the exact
  boundary distance, at no added render cost.
- Evidence: build and `dotnet format whitespace --verify-no-changes` both passed clean after every
  edit. Re-ran the live viewer via the Godot MCP (`run_project`/`get_debug_output`/`stop_project`)
  multiple times over Elwynn/Stormwind for 20-35 seconds each; no exceptions, same pre-existing
  unrelated model warnings as before. The Godot MCP has no input-injection or screenshot capability
  available in this session, so this only confirms the new code paths run without crashing under
  real streaming load — it does **not** confirm the reported flicker is actually gone, since nothing
  here could simulate flying the camera. Please fly around and confirm before considering this done;
  if it's still happening, the next place to look is whether `ModelLibrary`'s single global decode
  worker (shared across creatures/GameObjects/static props/WMO/skybox, one model at a time) is
  simply too slow for how many distinct new models appear at once in dense areas — that would need a
  real multi-worker redesign, which this pass deliberately avoided given the risk to the existing
  decode/upload/cache lifecycle invariants.

## SmartAI rule composer and grouped type pickers — implemented 2026-10-13

- Prior checkpoint: d6de8ee locate focus fix.
- User feedback: "+ Add rule ▾" looked like the full SmartAI list but was 8 templates. Now **+ Add
  rule** opens an inline composer (When / Do / On dropdowns with category headings, Search…,
  server help, sentence preview, Add/Cancel; nothing is drafted until Add) and templates moved to
  an explicit **Templates ▾** button. Rule editing uses the same grouped choosers.
- SmartCategories groups types by server code keywords. Server range markers (TC_END, AC_START,
  AC_END) and a reserved slot were listed as types: they are now excluded from pickers and
  validation reports them as unknown. Real counts: 93 events, 178 actions, 37 targets (the earlier
  96/182/40 included 10 markers).
- Fixed: reference labels wrapped to one letter per line inside parameter rows.
- Evidence: `--draft` checks every selectable type appears once in the groups and markers are
  rejected; `--content-smoke` opens the composer through its button, checks the dropdowns hold all
  92/178/37 choices (Then excluded from events), chooses Hit by spell → Summon creature → Itself
  through the real dropdown signals, checks the preview and that nothing is drafted before Add,
  then adds it; screenshots reviewed. The open dropdown list itself is not screenshotted.

## Locate creatures across maps — implemented 2026-10-13

- Prior checkpoint: a46c7f6 quest and behavior editors.
- Quest editor **Fly to** (givers) now uses TerrainViewer.LocateCreature: a loaded spawn is selected
  as before; otherwise a bounded read-only spawn lookup (creature.id1, 500 rows) picks the current
  map's nearest spawn or the first on a client map, opens that map at its tile, focuses it and
  selects the spawn when the streamed region contains it (30 s; a spawn hidden by SQL visibility
  leaves the camera there with an explanation). Right-panel "Fly to selection" is unchanged.
- Picker searches no longer list unnamed rows except by exact ID (an empty search led with blank
  creatures; the content smoke had chosen one through a stale result and now waits for its term).
- Evidence: `--content-smoke` locates Kaltunk (outdoors, Valley of Trials) from Goldshire: Kalimdor
  opens, GUID 3442 is selected, and the camera stays 17 yd from him after terrain loads and after
  his tile is reloaded under the focused camera; it also checks the picked objective is Kobold
  Vermin. `--content` live checks, `--gizmo/--visual-smoke` and format pass.
- Fixed after user report (focus needed a second click): a tile finishing under the camera lifted
  it 80 yd above terrain even right after a deliberate focus. Focus now holds until keyboard
  movement or a teleport; reproduced without the fix (camera 81 yd away), passes with it.

## Quest editor and SmartAI behavior editor — implemented 2026-10-13

- Prior checkpoint: 6dedce1 creature interactions/edit mode/quest editor skeleton.
- Data (verified in server SmartScriptMgr.h/.cpp, ObjectMgr::LoadQuests, CreatureTextMgr): quest_
  template (all columns but VerifiedBuild), quest_template_addon, quest_request_items,
  quest_offer_reward, creature_queststarter/ender; smart_scripts (key entryorguid/source_type/id/
  link; event_param1-6), creature_template.AIName/ScriptName, creature_text (a resolving
  BroadcastTextId replaces the line's text). All reads stay SELECT-only and bounded.
- Foundation: SmartCatalog (generated by Tools/generate_smart_catalog.py from SmartScriptMgr.h:
  96 events, 182 actions, 40 targets, curated names/sentences/parameter kinds for common types),
  SmartBehavior (rule sentences, natural timers, chains, loader checks, AIName fix), QuestModel
  (vocabulary, defaults, quest loader checks). One ContentDrafts instance shares the interaction
  draft file and undo history across the interaction, quest and behavior editors.
- UI: QuestEditorWindow (+ .Pages) and BehaviorEditorWindow (+ .Rules, .Speech) with EditorFields
  (commit-on-leave fields, flag grids, references) and PickerPopup (bounded search pickers).
  Edits that change smart_scripts keys (reorder, Then, delete) are delete + insert in one command.
  New quest IDs come from max(database, draft) + 1; new action lists from a free ID in entry×100+0..99
  checked against the database and draft. Access: inspector Behavior…, interaction window,
  quest givers, toolbar/menu Quests and Behavior.
- Evidence: build 0 warnings; `--draft` adds content checks (new quest rows, quest rules, sentences,
  chains, rekeyed swap, AI fix, missing link/spawn, speech, undo); `--content` live: Kobold Camp
  Cleanup, Wild Turkey, Stormwind City Guard with action lists 6800-6804 and 14 speech lines;
  `--content-smoke` passes (Farley rule/speech/Then/reorder/action list #29500/undo/redo; quest
  26035 via the real picker, rejected missing reward item, givers, all pages, undo, reopen) with
  screenshots reviewed. Regression `--interaction/--visual/--gizmo/--visibility-smoke` and format
  pass.
- Fixed during review: preview note placeholders, missing behavior title, faint placeholders,
  "(ms)" captions on second fields, redundant "→ itself" on self-only actions.
- Limits: conditions, C++ scripts, spawn-level ScriptName, live/player state and SmartAI runtime
  are not simulated; uncurated SmartAI types show raw labelled parameters with the server comment;
  GameObject/item quest starters, quest POIs, locales and conditions are not edited; new IDs are
  claims until export (step 4).

## Creature interactions, edit mode and quest editor skeleton — implemented 2026-10-13

- Prior checkpoints: 94d0e58 graveyard markers/drafts; 1e1a910 creature labels.
- Data (verified in server ObjectMgr/PlayerGossip/QueryHandler): creature_template npcflag/
  gossip_menu_id/subname, gossip_menu, npc_text (broadcast_text wins when its ID resolves),
  gossip_menu_option (shown only if OptionNpcFlag & npcflag), creature_queststarter/ender +
  quest_template, npc_vendor (negative item = reference list) + item_template, trainer/
  trainer_spell/creature_default_trainer (legacy npc_trainer is not loaded by the server),
  npc_spellclick_spells, smart_scripts events 64/62/19/20, conditions 14/15/19/23 (counted only).
  Client: Spell.dbc 136/153, TaxiNodes 1-5, ItemExtendedCost 1-13, ItemDisplayInfo 5, gossip/quest
  and item icon BLPs.
- Foundation: InteractionRows (table registry with keys + column whitelist), InteractionDraft
  (grouped row commands on DraftFile, undo/redo, conflicts, reserved IDs, frozen worker view),
  NpcInteraction (view builder, placeholder rendering) and InteractionRules (server loader errors;
  npcflag warnings with fixes).
- InteractionSource reads the NPC scope plus draft-touched rows and follows reachable menus/texts/
  trainers in the effective view; InteractionWindow (+ .Edit) is the client-style preview/editor;
  QuestEditorWindow + QuestSource are a read-only skeleton. Short right-click (<6 px, <350 ms)
  interacts; dragging still looks.
- Evidence: `--interactions` live: Innkeeper Farley (2 menus, 4 options incl. bind, 12 items),
  William Pestle (4 quests), Noblegarden Vendor (10 items), Priestess Josetta (240 named trainer
  spells); DBC offsets; search escaping; cancellation. `--draft` adds interaction command/rule/
  conflict checks. `--interaction-smoke` passes end to end (shared menu 1291 forked to 90003);
  screenshots reviewed (gossip, merchant grid with client icons, quest detail, edit gossip/info,
  quest editor). Settled FPS in the Goldshire inn is the same with the edit window open (33) and
  closed (33). Regression `--visual/--visibility/--graveyard/--reload-smoke`, format and other
  checks pass.
- Fixed during review: smoke stand-off distance, ambiguous quest-title click, screenshot frame lag,
  editor label wrapping, toggled-button padding, windows growing from stale wrapped-label sizes,
  and smokes now isolate creature/graveyard drafts too.
- Limits: conditions/SmartAI/C++/player state are not evaluated (counted and warned); forking a menu
  drops its SQL conditions (warned); only npc_text group 0 is editable; quest content, SmartAI,
  conditions and event vendors are not editable; reserved IDs are claims until export; shared
  records affect every user unless forked.

## Graveyard markers and local drafts — implemented 2026-10-13

- Prior checkpoints: 00955a1 reload smoke fix; 1e1a910 creature labels above model heads.
- Tables: `game_graveyard` (ID, Map, x, y, z, Comment; no orientation) and `graveyard_zone`
  (ID, GhostZone, Faction, Comment; PK ID+GhostZone), per server `GameGraveyard.cpp`; GhostZone is an
  AreaTable ID (area checked before zone). Spirit healers are creature spawns with npcflag 0x4000.
- `Foundation/DraftFile.cs` holds the lock/checksum/atomic-replace mechanics now shared by
  `CreatureDraft` (unchanged file format; a pre-extraction envelope still loads) and the new
  `GraveyardDraft` (whole-record edits, same stale-baseline rules, server-rule validation).
- `Scripts/Data/GraveyardSource.cs`: read-only, parameterized, region-bounded (limit 128,
  nearest-first) graveyards, their links and spirit healers within 40 yd.
- `GraveyardOverlay` (+ `.Ui`, `.Editing`): tombstone billboard (editor-drawn icon, not a client
  asset), faction ring, label, Graveyards tab and layer, Move gizmo, drop to ground, typed position,
  name, link add/remove/faction/comment, reset, Copy `.go`. `IPlacementEditor` routes Edit menu,
  toolbar and drags to the selected kind; Rotate is disabled for graveyards. Selection is exclusive
  across creatures, GameObjects and graveyards.
- Evidence: live tables 685 graveyards / 704 links, 0 orphan links, 0 bad factions, 0 unknown
  GhostZones (166 distinct), 103 graveyards with no link. Nearest Goldshire graveyard 106 links Elwynn
  Forest (12, Alliance) and 717, with Spirit Healer #79628. `--draft`, `--graveyards`,
  `--graveyard-smoke` (viewport click on graveyard 854, edits, rule rejections, reopen, layer hide,
  Kalimdor switch), `--visual-smoke`, `--visibility-smoke` and format checks pass. Screenshot
  reviewed: icon, mixed-faction ring, label and inspector render; Rotate greyed for graveyards.
- Limits: edits are local only (export is step 4); battleground/default graveyard IDs are
  referenced from C++ (effects SQL cannot show); spirit healers are not moved; graveyards far
  outside their source region reappear only when that region is queried, as with creatures.

## Skybox depth fix — implemented 2026-10-13

- Prior checkpoint: 47d2574 step 3 placement editing, menu bar and toolbar.
- Cause: LightSkybox M2s were drawn as ordinary camera-centered geometry. Their nested layers are
  only ~120-600 yd across (Hellfire 361 yd with inner rock/cloud layers, Icecrown raid skies 119-243
  yd), so world farther than a layer failed the depth test and vanished behind the sky. A uniform
  scale-out was tried and rejected: inner layers still landed in front of distant terrain.
- Fix: `ModelKey.Skybox` variants get their own shader (`ModelLibrary.SkyboxShader`). Each vertex is
  projected along its view direction to `SkyLighting.ShellDistance` (7500, inside the 8000 far
  plane), drawn in the translucent pass after the background without writing depth, unlit and
  fog-free, with native batch order kept through render priority. This is renderer-convention
  independent (the project uses the Compatibility renderer). Skybox instances are never
  range-culled or shadow-casting; the old per-frame shadow workaround is gone.
- Evidence: new graphical `--skybox-smoke` covers 32 views across Hellfire, Bone Wastes, two
  Icecrown Citadel raid skies, two Northrend auroras and Zul'Drak. Sky over opaque world is at most
  0.38% (0.0000 for Hellfire and IceCrownRaidSky02). With the fix disabled the same check measured
  60-99.8% in Hellfire/Icecrown and 3-39% elsewhere. Screenshots reviewed: Hellfire terrain and
  mountains render in front of the starry sky; translucent precipice crystals still show sky.
  `--visual-smoke`, `--water-interior-smoke` and format checks pass.
- `--reload-smoke` had been timing out at HEAD too (stash-verified): its first stage waited for a
  frame with textures uploaded but no model published, and the first model now uploads within one
  frame's budget, so that frame never occurred. Fixed in the next commit with a diagnostic
  `ModelLibrary.HoldPartialUploads` (one surface per frame, no publication) that the smoke sets
  until the reload discards the held entry. Three consecutive runs pass: partial upload with one
  texture interrupted, discarded, then 9 tiles / 302 models / 402 textures reloaded.
- Limits: views where no skybox resolves (region center off the terrain, nested region winning)
  are reported as skipped. Skybox fidelity beyond depth (exact client layer blending/animation) is
  not verified against the client.

## Step 3 placement editing, menu bar and toolbar — implemented 2026-10-12

- Prior checkpoints: 1c8cb97 Scripts folder layout; 1d61646 AGENTS tools/references.
- `TransformGizmo` (renamed from TranslationGizmo, uid kept) adds a Rotate mode: a yellow yaw ring
  with a heading pointer, Ctrl/fixed-step snapping and cancel restoring yaw. Rotation edits set only
  `Rotation.Y`; the creature heading mapping is unchanged. Commits normalize to [0, 2π) like
  `Position::NormalizeOrientation`. Untouched components keep exact doubles (a move never rewrites
  heading; a rotation never rewrites X/Y/Z). Undo/redo now restore rotation as well as position.
- Ground probes: `TerrainHeights` keeps ~150 KB of height/hole data per loaded tile (same lifetime
  as the tile node); `GroundProbe` tests WMO render triangles (liquids excluded) with cached
  per-surface bounds. Rule: highest surface at or below Y + 3 yd; otherwise fail closed with a
  reason (no ground, tile not loaded, covering WMO still loading, buildings layer hidden).
- `CreatureOverlay.Editing`: Drop to ground, Snap on drop (off by default), typed X/Y/Z/heading°,
  Reset to source and Copy `.go xyz X Y Z map O` (argument order from cs_go.cpp). Inspector shows
  heading in rad/degrees with a compass point.
- Menu bar + toolbar strip (`Scripts/Ui/EditorMenu.cs`, `EditorToolbar.cs`). The left panel keeps
  navigation; MPQ path/Load archives, cell borders and the controls text moved to File/View/Help.
  MenuBar did not fire accelerators for closed menus in Godot 4.7.1, so EditorMenu matches them in
  `_ShortcutInput` and consumes the event. Keys are consumed during right-mouse flight so Q/W/E
  fly rather than switch tools.
- Evidence: main and test builds clean; whitespace format checks pass. `--placement`, `--draft`,
  default asset and `--models` checks pass. Real-data `--gizmo-smoke` and graphical
  `--visual-smoke`: rotate 3.2727→4.8435 rad (error 0.0000) with exact coordinates; drop to ground
  Z 67.530→67.481 via terrain (~2.5 ms incl. first-call cost); fail-closed probe; Goldshire lumber
  mill roof 70.24 chosen over terrain 48.04 (0.8 ms); Ctrl+Z (menu) and E/Q/W (toolbar) through
  real input events. `--reload-smoke` and `--visibility-smoke` still pass. Screenshot review of
  `artifacts/native-preview.png` confirmed strip/panel layout and the pose grid.
- Measured worst probe: Stormwind city WMO (672,786 triangles, 209 surfaces) 12.6 ms first probe,
  1.0 ms warm in Release (~100 ms first probe in Debug).
- Limits: render triangles approximate client collision; M2 doodads (bridges, docks) are not snap
  targets; WMOs without MODF bounds are always probed. Not verified: in-client landmark/heading
  parity (step 3 stays open until checked via `.go xyz`), the gizmo ring and menus by hand, and
  key blocking during flight.

## Scripts folder layout — completed 2026-10-12

- Prior checkpoints: 30ca255 streaming pass; 0fc3b8e binary reader docs.
- Grouped `Scripts/` into `Viewer/`, `Rendering/`, `Overlays/`, `Ui/`, and split `Assets/` into
  `Archives/`, `Formats/`, `Client/`, `Models/`, `World/` (see README "Code layout"). Each `.cs.uid`
  moved with its script; namespaces are unchanged. Earlier entries cite pre-move paths.
- `AreaTable`/`ClientLighting` moved from `Assets/` to `Viewer/`: they use Godot `Color`/`GD`, which
  had broken the engine-independent `Tests/AssetChecks.csproj` build since 625ffc7.
- Validation: main and test projects build with 0 warnings/errors; whitespace format check passes.
  Not yet run: Godot editor reimport or graphical smoke after the path change.

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

## Visibility controls and GameObjects — implemented 2026-10-11

- Added immutable bounded preview settings and SQL-only creature/GameObject adapters. Event
  membership (including negative event links), phase/difficulty overlap, pool membership, hidden
  helpers, SQL conditions, script bindings and initially despawned GameObjects filter before LIMIT.
- Default: phase 1/normal, SQL-scheduled events, no pool alternatives/helpers/conditional objects/
  initially despawned objects. Script-bound spawns remain included because bindings do not prove
  invisibility. Missing required metadata fails closed; broad inspection still has hard bounds.
- Read-only SQL evidence: GUID 96098/template 26724 is hidden by defaults and visible with All
  stored spawns or manual event 31. Phase mismatch, nearest-first three-row cap, script filter,
  cancellation and 59 ordinary GameObjects at the example location passed.
- Saved SQL/filter checkpoint 5e5f403. Native GameObjectDisplayInfo.dbc lookup now uses shared
  M2/WMO geometry, materials, GPU animation palettes, template scale and normalized SQL quaternions.
  GO rotation uses the model coordinate boundary without the ADT half-turn. Model/list/viewport
  selection and fly-to work; unsupported displays use bounded boxes. GameObjects remain read-only.
- Replaced the old creature panel with a scrollable Scene explorer: Scene / Creatures / GameObjects,
  consistent cards, clear selection identity, SQL restriction details, event picker, hex/decimal phase
  masks, difficulty, display budgets and optional name labels. Kept animation and durable creature
  movement/history controls. External credentials remain outside the repository and logs.
- Layer switches remove native requests/instances for scenery props (including embedded WMO doodads),
  buildings/interiors and spawn layers. Disabled spawn layers stop queries. Selection of either entity
  deselects the other, canceling unfinished creature movement while retaining its identity for restore.
- Defaults: 200-yard 3D range, 384 nearest SQL rows and 128 admitted instances per spawn layer;
  name labels off. Low/Balanced/High budgets admit 64/128/256; High allows 768 SQL rows. Range stays
  within 60–600 yards. Labels cap at 64 nearby per layer plus selected labels. Broad preview retains
  spatial, SQL, fallback and native admission limits. Camera centers quantize to 64 yards with a
  query halo; searches/mask typing are debounced. Each layer has one cancellable query slot.
- Acceptance evidence: read-only SQL/policy checks passed masks, positive/negative event semantics, explicit
  event 31, conservative pool/helper/condition defaults, script bindings, three-row caps, normalized
  GO rotations, nearest order, spatial bounds, parameterized hostile search text and cancellation.
  Real GameObject display checks: 59 SQL rows, 12 M2 and 3 WMO paths, 189 material decodes and
  twelve sampled animation palettes. Native corpus: 116 M2 / 52 WMO / 1,988 textures / 39 interiors.
- Graphical --visibility-smoke passed native GameObject list/ray/input selection, all four hidden
  layers with zero spawn records/model queue, event/phase controls, rapid canceled preview changes
  and dense All stored preview at 600 yards: 384 creature + 384 GO rows, exactly 64 admitted per
  layer under Low budget. The cap includes native requests and fallbacks. No source writes occurred.
  Layout screenshots inspected at 2440x1400 and 1440x900; corrected wrapped count/phase captions.
- Existing graphical regression passed creature viewport/list selection, Stand/Walk menu, durable
  gizmo drag/reopen/undo/redo, map-switch cancellation and Stockade interior. Draft checks passed.
  A 240-frame default-scene sample at 2440x1400 measured median 11.11 ms / p95 12.12 ms on RTX 5080;
  another isolated run measured 7.28 / 9.09 ms. These are scene-specific observations, not broad
  certification or controlled before/after comparisons. Nine streamed tiles held 5,845 static props.
- Final checks passed: archive reload interrupted partial GPU upload and returned to nine tiles,
  302 ready models and 402 textures. Inspected an isolated native Bundle of Wood GO screenshot
  with the matching database identity, material, scale and selection. Final build: zero warnings/errors;
  formatting and diff checks passed. Entity UI is split into partial files, separate from rendering.
  Ignored evidence: artifacts/visibility-*.out.txt, visibility-*.err.txt and visibility-*.png.
- Limits: SQL schedules approximate ordinary repeating events using DB time; holiday/manual/world
  event state, live pool winners, C++/SmartAI and player visibility are not simulated. Script bindings
  are optional metadata filters, not an invisibility verdict. GO DB state is shown; interactions,
  transport motion/open-close simulation and GO editing are deferred. Bounding-box picking has no
  terrain/triangle occlusion. Existing native material/client-fidelity limitations still apply.
- PLAN.md, README.md and AGENTS.md now describe this scope and the conservative filters. Source
  client/server/database data, user viewport changes and unrelated UID files remain untouched.
