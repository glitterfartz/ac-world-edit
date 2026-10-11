# Executive summary

## Recommendation
**Proposed:** retain the current C# Godot renderer for an initial standalone content editor, but move editor state, commands, validation and AzerothCore persistence into engine-independent .NET libraries. Build a typed content workspace with staged changes and reviewable SQL export. Prove Godot editing UX/performance in Phase 0; switch frontend only if measured acceptance gates fail. Use an existing client map editor for early terrain workflows rather than making native terrain writing a prerequisite.

This is an editor of development objects: a placed NPC links to its template, appearance, behavior, dialogue, services and quests. SQL is an inspectable export format. Its table layout should not define the default user workflow.

## What exists
**Confirmed:** Godot.NET.Sdk 4.7.1/net8.0 viewer, custom MPQ v0/v1 reads, Map.dbc map discovery, WDT terrain index, ADT v18 terrain, BLP2 texture decoding, four-layer materials, camera/minimap navigation and neighborhood streaming. No M2/WMO visualization, liquids, terrain collision, database layer, selection/gizmos, change journal or writer. [Viewer evidence](references/source-references.md#v1-viewer-configuration)

Read-only checks of the prebuilt asset binary pass representative continent tiles against OGClient and the corrected wow-data junction. The earlier HD-client failure was resolved by the user's junction correction; profile detection remains a requirement, not a current blocker. No fresh GPU performance benchmark exists. [Execution record](references/research-notes.md)

## Realistic first product
**Proposed MVP:** open a named AzerothCore project and matching client, load terrain plus labeled creature/GameObject proxies, search templates, select/place/move/yaw/duplicate/delete simple spawns, edit bounded spawn properties, show dependencies, undo/redo, recover drafts, validate, and export forward/reverse SQL plus a change manifest. Existing templates only; outdoor maps; no writes to the connected source. Clear proxies make useful spatial work possible before full M2/WMO support.

**Critical release gate:** coordinate/heading parity with client and server landmarks. If model/collision fidelity is insufficient, restrict placement to verified outdoor terrain and require in-game confirmation. Never describe a proxy scene as an exact client preview.

## Why incremental
Server-side placement and many quest/service/SmartAI operations are database-backed. Terrain/model/DBC visual changes require client artifacts; map collision and navigation outputs may also need regeneration. Code-defined behavior needs server rebuilds. These changes have different authority and deployment lifecycles. [Server loaders](references/source-references.md#a1-server-loading-and-entities), [assets](04-client-formats-and-assets.md)

## Largest risks
1. Coordinate or quaternion mistakes can produce convincing but incorrect placement. Establish a single transform adapter and golden landmarks.
2. Almost all relationships lack SQL foreign-key enforcement. Entity-aware validation and complete dependency read sets are essential. Only one declared world FK was observed. [Schema evidence](references/source-references.md#d1-read-only-test-database-evidence)
3. Runtime caches are not transactionally synchronized with SQL. MVP exports; testing initially uses a stopped isolated clone/restart.
4. Lossy terrain parser data cannot be used to write ADTs safely. A separate lossless format document model is required.
5. Schema/SmartAI drift, client patch provenance and ALE/Eluna incompatibility require explicit profiles.
6. Full editor/IDE/terrain scope is disproportionate for a small team. Reuse specialist tools and external IDEs first.

## Decisions to approve through experiments
- Standalone Godot application versus EditorPlugin shell: compare the same placement/import/export task.
- Managed readers versus StormLib: corpus compatibility, native packaging and error behavior.
- Internal SQL compiler versus generic ORM: use explicit adapters for signed/polymorphic fields and compound ownership.
- Native terrain authoring versus Noggit handoff: require lossless round-trip and client/server parity before native tools.
- External IDE versus embedded IDE: external first; embed only when integrated navigation measurably helps.

## Implementation priority
Phase 0 profile/coordinates/state/export proofs -> Phase 1 spawn MVP -> Phase 2 content aggregates -> Phase 3 SmartAI and IDE services -> Phase 4 client asset pipeline -> Phase 5 runtime bridge and additional core. Acceptance gates, not dates, determine progression. [Roadmap](11-development-roadmap.md)

