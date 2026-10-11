# Developer workflows

All workflows are **proposed**, grounded in [server authority](03-azerothcore-architecture.md), [assets](04-client-formats-and-assets.md) and [persistence contracts](07-database-abstraction.md). “Automatically” means designed responsibility, not an existing viewer capability.

## 1. Open a world and place an NPC
1. Open Project shows core revision/schema and client profile; select read-only test source or snapshot.
2. Choose map and region; terrain loads, hierarchy shows filtered spawn records, labeled proxies appear.
3. Search template palette by name/entry; preview appearance/provenance where supported.
4. Drag template onto terrain, with hover position and terrain triangle hit; choose heading.
5. Inspector shows template link, phase/difficulty, respawn and movement defaults.
6. Commit placement; Changes shows “Add NPC” and a project UUID. Undo removes the draft placement.
7. Validate; review collision-free configured GUID allocation and dependency read set.
8. Export forward/reverse SQL and manifest; test with the separate clone workflow.

**Managed:** coordinate conversion, reference lookup, draft identity, masks, change history, ID resolution and bounded SQL.
**Artifacts:** creature row; addon only when actually needed; no new ADT or client patch for existing appearance.
**Limit:** absent static WMO/collision context can make outdoor scene misleading; in-game confirmation required.
**First version:** verified outdoor terrain plus proxies, existing templates, ordinary spawns only.
**Long term:** complete appearance/static context, contextual flags and test-server preview. No direct “save to live world” implicit in placement.

## 2. Create a creature template and configure behavior
1. Choose Create Creature; select a documented archetype or clone an existing supported definition.
2. Identity inspector asks name/description and ID namespace; new definition remains symbolic.
3. Appearance browser selects an existing display ID; multiple appearances expose scale/probability and source evidence.
4. Stats/faction form validates level ranges, modifiers and supported references.
5. Behavior tab selects stationary/random/patrol or SmartAI; a script backend is explicit.
6. Services tab adds vendor/trainer/dialogue/quest capabilities with appropriate flags and relation records.
7. Impact review lists cloned versus shared loot/gossip/services and difficulty templates.
8. Export and test definition plus one placement; verify appearance, faction, interaction and AI.

**Managed:** definition/model row composition, inheritance, clone versus link choice and dependency checks.
**Artifacts:** creature_template and required creature_template_model/extras; SmartAI/service rows as authored. New model visuals require client asset/DBC changes; novel mechanics may need C++/Lua and rebuild.
**Limit:** changing a shared template affects every referenced spawn; difficulty variants and script ownership need explicit treatment.
**First version:** post-MVP Phase 2 guided cloning of supported definitions.
**Long term:** reusable NPC recipes, difficulty-aware balance and behavior previews. Do not invent sniff VerifiedBuild values.

## 3. Design a quest with NPCs/items/objectives
1. Create Quest opens structured text/requirements/objectives/rewards panels.
2. Enter title/log/description and choose locale, with missing translations visible.
3. Add objective using creature/GO/item pickers; counts and slot capacity are shown.
4. Select starter/ender NPCs or GOs from scene/catalog; relation overlays appear.
5. Add reward/choice items, spells/reputation where supported, with reference validation.
6. Draw a named prerequisite edge; choose completion/active/breadcrumb/exclusive-group semantics rather than generic ordering.
7. Review quest flags and relation/service availability on involved NPCs.
8. Export aggregate changes; test on a fresh test character with eligible and ineligible states.

**Managed:** signed GO target encoding, fixed slots, relation rows, addon/text/locale mappings and dependency graph.
**Artifacts:** quest_template/addon and authored auxiliary/relation records; existing item references need no asset patch. New spell/item presentation may.
**Limit:** static eligibility graph cannot fully simulate player state/scripts. Quest chains have source-specific signed semantics.
**First version:** Phase 2 structured authoring for standard kill/collect/GO objectives; external game test.
**Long term:** quest POI tools, richer graph visualization and automated controlled-character scenarios.

## 4. Create an NPC patrol in 3D
1. Select ordinary creature placement; open Movement.
2. Create Route or clone shared route. Consumers list warns if existing path is shared.
3. Click terrain to add points; hover shows coordinates and invalid-hole/no-surface state.
4. Drag/reorder points; set delay, heading and movement style per point.
5. Preview arrows and distances; visualize spline points only when supported.
6. Attach route; editor stages waypoint_data and creature_addon.path_id plus movement change together.
7. Undo the whole attachment/route command or individual point edits.
8. Export; test actual travel with server collision/pathfinding.

**Managed:** path ID namespace, point ordering, addon wiring, shared ownership and reversible route changes.
**Artifacts:** normal waypoint_data/addon and any authored scripts/spline addon. No ADT change.
**Limit:** terrain-snapped line does not establish navigable path. Bridges/interiors/flight need proper geometry and movement semantics. SmartAI waypoints is a separate route backend.
**First version:** Phase 2 ordinary simple patrols; view complex paths without rewriting.
**Long term:** navigation overlays, formation/escort support and runtime path diagnostics.

## 5. Construct an interactive encounter
1. Create Encounter project object; select/add actors and an arena region.
2. Ownership panel labels existing shared definitions, owned new rows and script sources.
3. SmartAI cards define source-supported triggers, actions, targets and explicit links/timed lists.
4. Add conditional groups and text lines using typed context/references.
5. If behavior cannot compile to SmartAI, choose installed ALE/Eluna or C++ profile and open source in IDE.
6. Validate actor references, timers/phase/link semantics and conflicting AI ownership.
7. Review full owner-block SQL plus script/build/restart requirements.
8. Test reset, success, failure, evade/wipe and repeated entry in an isolated server.

**Managed:** encounter membership metadata, references, full SmartAI block compilation, script links and diagnostic navigation.
**Artifacts:** SmartAI/conditions/text/spawn rows; Lua files require supported module; C++ requires compiled module/script registration and restart. Existing actors/assets do not imply client changes.
**Limit:** graph preview is not runtime simulation; instances can need code-managed persistence and boss state.
**First version:** Phase 3 curated SmartAI recipes, external IDE and manual encounter tests.
**Long term:** bridge traces, actor state inspection and scenario replay, with proven backend compatibility.

## 6. Edit a GameObject and its interactions
1. Select placed GO; inspector identifies placement/template/type and consumers.
2. Move/yaw the instance; quaternion/heading conversion is verified by profile.
3. Choose Edit Definition or Clone for this placement.
4. Type-specific fields show chest loot/restock or door lock/autoclose with named units.
5. Add quest/gossip/SmartAI relationships through supported forms.
6. Shared loot/locks/spells display read-only reference or explicit clone/edit choices.
7. Validate display, type-data, state, masks and interaction wiring.
8. Export/test interaction, rotation, collision and respawn.

**Managed:** type-tagged data mapping, shared template scope, reference validation and synchronized authored rotation.
**Artifacts:** gameobject placement, template/addon/relations/behavior as changed; display/model creation requires client assets/DBC.
**Limit:** some GO types use data client-side or have specialized server code; changing type can reinterpret every data slot. Never silently retain an incompatible data interpretation.
**First version:** MVP basic placement/yaw/state for supported ordinary GOs; Phase 2 definition/interactions.
**Long term:** broader GO types, animation states and contextual gameplay testing.

## 7. Modify terrain or place a client world asset
1. Switch to Client Assets workspace; source mounts are immutable.
2. First implementation opens selected tile in configured Noggit-style tool against project-owned copies.
3. Author height/texture or static doodad/WMO placement; reimport changed overlay paths.
4. Editor shows asset diff and affected tiles/boundaries/placement IDs.
5. Later native tools issue brush/placement commands into a lossless AssetDocument.
6. Validate chunks/offsets/seams/references and reopen with independent parser.
7. Build staged MPQ patch and appropriate server maps/vmaps/mmaps.
8. Test matched client/server bundle for load, visual seams, collision, water and navigation.
9. Export artifact manifest and rollback copies; deploy client and server artifacts together.

**Managed:** overlay provenance, unique client IDs, changed-file manifest and extraction dependency tracking.
**Artifacts:** ADT/WDT/assets/DBC as needed, client patch and derived server geometry; SQL only for independently authored server content.
**Limit:** new maps/material/animation/water features are substantially harder than placing existing server spawns.
**First version:** Phase 4 handoff/import/preview; native writing only after corpus gates.
**Long term:** seamless native brushes and asset placement. Server NPC placement is never exported as MDDF/MODF.

## 8. Test and deploy safely
1. Changes panel lists semantic changes and DB/client/code/geometry/restart requirements.
2. Freeze bundle, schema/profile fingerprints, chosen IDs and before/after rows.
3. Configure an isolated test clone and validate its identity/revision; production is a different target.
4. Create/check backup and stop or serialize content writers.
5. Recheck preconditions; apply through one chosen migration/update path with bounded DML.
6. Restart test server or use a experimentally proven subsystem reload.
7. Load matched client, inspect loader logs and execute feature-specific checklist.
8. Review results and package hashes; operator deploys approved bundle to named environment.
9. On failure diagnose and restore/compensate against known state, keeping audit trail.

**Managed:** eventually target checks, diagnostics and apply state; MVP only export/manifests.
**Artifacts:** bundle plus backups; code/client artifacts as required; updater history follows AC mechanisms.
**Limit:** DB transactions cannot atomically cover file distribution/runtime caches. Lost commit acknowledgements require state inspection.
**First version:** manual apply to clone, restart, in-game verification; no connected writes.
**Long term:** controlled runner, scoped runtime bridge and synchronized artifact releases.

## 9. Resolve a broken relationship
1. Validation reports “spawn references missing creature definition,” linking row and field.
2. Select issue; scene proxy remains visible at original position.
3. Dependency inspector explains reference and baseline/profile evidence.
4. Search supported replacement definition or recover owned definition from known source.
5. Stage RebindReference; delete is a separate dependency-aware choice.
6. Validate impacted relations/shared consumers and show semantic/SQL diff.
7. Export/test correction; leave unsupported references preserved if not safely understood.

**Managed:** reverse-reference queries, typed ID namespace, affected aggregate recalculation and reversible repair.
**Artifacts:** only explicitly repaired rows; no automatic global cleanup.
**Limit:** absent row may be intentional module/runtime context; distinguish definite missing reference from unsupported semantics. The research sample had zero creature-template orphans; this is a hypothetical task, not an observed defect.
**First version:** MVP supported spawn/template/Map checks and actionable errors.
**Long term:** cross-system diagnostics and curated repair recipes.

## 10. Add another emulator adapter
1. Create profile for a pinned alternate-core revision and compatible client build.
2. Run schema/capability probe against fixtures/read-only copy; save comparison.
3. Implement spawn/template mapping and coordinate/update policies behind ICoreAdapter.
4. Map semantic fields; leave unsupported capabilities disabled rather than guessed.
5. Add round-trip/import/export fixtures for nulls, signed IDs, shared rows and deletion.
6. Test same domain commands against adapter snapshots and deterministic SQL outputs.
7. Run isolated server/client placement acceptance using that core.
8. Publish adapter support matrix with known read/write/script/client limitations.

**Managed:** common viewport/commands/diagnostics reused; alternate schema remains inside adapter.
**Artifacts:** editor adapter/plugin and fixtures; core changes only if a future bridge is deliberately supported.
**Limit:** TrinityCore 3.3.5 being a similar emulator does not imply identical schemas, ID allocation, SmartAI codes or migration procedures.
**First version:** Phase 5 narrow creature/GO placement adapter.
**Long term:** broader content and expansion adapters; new expansions also require ClientFormatAdapter and protocol/artifact proof.

