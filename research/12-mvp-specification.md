# Minimum viable product specification

## Goal
Let an AzerothCore developer inspect outdoor spawn content, place/move ordinary NPCs and GameObjects, and obtain reversible, reviewed SQL without manually joining tables or writing routine coordinate updates.

**Proposal:** Windows-first standalone Godot C# application backed by engine-independent .NET editing/adapters. Source world connection is read-only. Export is the release artifact.

## Supported profile
Pinned AzerothCore WoTLK schema compatible with observed ACDB 335.17-dev/core revision; client 3.3.5a build 12340 with explicit locale/mounts. The corrected wow-data junction is suitable for initial corpus tests; it is not a permanent profile identifier. Unknown profiles open diagnostic/read-only mode where possible.

Outdoor ADT maps only. Ordinary spawn records with known references. Template/model metadata can be cataloged; render selectable labeled proxies. Initially exclude transports, interior/WMO-only maps, event/pool/multispawn-dependent editing, specialized quaternion cases and script-owned deletion until their semantics are implemented.

## Included
- Named project/profile setup, source/schema compatibility report.
- Map/minimap/fly navigation, coordinate jump and terrain ray picking.
- Region creature/GO overlay with searchable virtual hierarchy.
- Search existing templates by ID/name, with display/model resolution diagnostics.
- Select, move, yaw, multi-move, duplicate, place and dependency-aware delete for supported ordinary spawns.
- Creature position/masks/respawn/stationary-or-random movement/wander-radius fields.
- GO position/yaw/masks/respawn/supported state; preserve other rotation/addon data.
- Stable domain IDs, preview drag, command undo/redo, autosaved journal/checkpoints.
- Typed map/template/pose/mask/reference validation and shared/unsupported badges.
- Frozen export bundle: SQL, reverse SQL, manifest/preconditions and notes.
- Source provenance, error links and explicit client/code/restart requirements.

## Excluded
New creature/GO definitions, quest/service/loot/gossip writing, patrol creation, arbitrary SmartAI scripting, animated M2/WMO rendering, native terrain/texture/water writers, embedded IDE/debugger, live writes/reloads, production deployment, extra cores/expansions and collaboration service. Read-only references/path overlays may be added if they do not jeopardize the core release.

## Suggested implementation slices
| Slice | Concrete result | Gate |
|---|---|---|
| M0 Project/profile | Reliable mount/schema and supported map display | Phase 0 evidence |
| M1 Spawn read model | Visible proxies with correct names/locations | Reference/pose parity |
| M2 Commands | Move/place/duplicate and durable undo | Exact recovery on unload/reopen |
| M3 Inspectors | Bounded fields with inheritance/errors | Raw unsupported columns preserved |
| M4 Export | Deterministic bounded SQL and read-set bundle | Clone apply/reverse/replay tests |
| M5 Usability | Complete placement/review task | Developer trial and performance targets |

## Acceptance scenarios
1. Open corrected client and test world profile; report actual 18 selected archive inventory if that manifest is used, map catalog and schema support. Counts from research are fixture expectations, not hard-coded universal validation.
2. Load known outdoor spawn fixture; verify position and headings against agreed landmarks/tolerances.
3. Place existing creature template, move/yaw it, undo/redo, leave tile, return and reopen project; pose and UUID unchanged.
4. Multi-move N supported spawns; one undo restores all, without modifying shared templates.
5. Duplicate placement resets identity and excludes unsupported inherited ownership; export allocates no colliding GUID.
6. Delete ordinary spawn with no backlinks; reverse bundle restores exact original data. Referenced/unsupported spawn deletion is blocked with an explanation.
7. Edit masks/respawn/random movement; validation catches unsupported difficulty/phase/negative-or-nonfinite values and preserves hidden fields.
8. GO yaw export produces correct client rotation; complex original rotations remain read-only if not understood.
9. Introduce missing template/map reference in fixture; diagnostic points to field; export blocked. No auto-delete.
10. Change baseline row or insert colliding ID externally in test fixture; stale plan is rejected at export/preflight.
11. Export repeated unchanged draft twice; file content/deterministic plan matches except documented bundle identity/timestamps.
12. In isolated clone, forward SQL changes only planned rows; reverse restores baseline under post-state preconditions. Mid-DML failure rolls back under selected runner policy.
13. Kill/restart at autosave boundary; valid checkpoint/journal recovers and torn tail is reported.
14. Confirm normal editing writes only project draft/cache/export, never source DB/client/core; query account enforces reads.
15. Meet agreed Phase 0 navigation/input/resource targets with no stale map attachments.

These future tests are necessary for release; only read-only research asset/schema checks were executed here.

## Export UX
Review page says “Ready to export,” shows additions/moves/deletions and deployment needs, then saves bundle in project exports. Success says where the bundle is and that the target has not been changed. Each row/ID is inspectable, but user sees semantic object/task first. SQL editor remains optional expert inspection.

The export manifest carries exact original rows and query/read sets; reverse SQL requires verifying expected post-state. Operator applies through established AC custom/pending workflow to a clone. No claim of self-guarding SQL or transactional file/runtime rollback.

## Definition of done
All scenarios pass on a declared client/schema/hardware profile; limitations shown in-product; generated exports meet local SQL conventions; no unintended external writes; source-level diagnostics available; first-user task trial demonstrates practical reduction in manual SQL work. A polished viewport alone does not satisfy the MVP.

