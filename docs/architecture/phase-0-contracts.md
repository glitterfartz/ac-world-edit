# Phase 0 experimental contracts

Status: foundation proof, not Phase 1 functionality. Full acceptance is recorded in
`research/progress/phase-0.md`; none of the pending client/clone/performance gates is waived.

## Boundaries

- `Foundation/` references only .NET. World poses remain in server coordinates. Draft UUIDs have no
  numeric database allocation. Render nodes can be discarded without altering domain history.
- `Scripts/Assets/TerrainGeometry.cs` builds the exact triangle set shared by renderer and picking
  experiments, including holes. These DTOs cannot write ADT files.
- `AssetStore` opens an explicit low-to-high mount list. Current experimental profile is
  3.3.5.12340/enUS; actual client lookup precedence still needs differential proof. No automatic
  backup/archive discovery, alternate locales, HD profile or incremental MPQ patches are claimed.
- Read-only schema snapshots are separate from `SpawnRow` raw values and semantic `WorldPose`.
  Unknown/signed/null fields remain in immutable raw snapshots.
- The journal is single-writer and versioned, with monotonic sequence and SHA256 payloads. It flushes
  accepted commands before advancing memory. Undo/redo and branches are durable. Checkpoints use
  temp/flush/replace; only an unterminated final journal record is recoverable, with retained bytes
  and a diagnostic. Complete corrupt records stop recovery. Full-history records are a deliberate
  small-corpus prototype, not a scalable production journal or hardware power-loss guarantee.
- Export supports imported same-map creature pose updates only, with explicit id+guid predicates.
  SQL files are review-only and do not execute manifest preconditions. No apply API, credentials,
  new spawn creation, GO rotations, dependency-safe delete or updater history manipulation exists.
  A reverse bundle needs its post-state checked; it cannot undo intervening unrelated work safely.

## Adapter support matrix

| Capability | Foundation proof | Release status |
|---|---|---|
| Stock enUS archive reads | Explicit 17 archives, provenance, corpus | Client precedence/differential proof pending |
| WDT/ADT v18/BLP2 | 106 WDTs, four continental ADTs and textures; malformed fixtures | Sampled only; WMO-only maps explicitly unsupported |
| Coordinates/headings | Synthetic inverse/seam/cardinal tests | 12 real landmarks and two GO rotations pending |
| Terrain picking | Shared triangles; hole interior/boundaries | CPU proof; production interaction pending Phase 1 |
| Draft commands/recovery | Durable small-corpus tests and transient projection unload | Foundation experiment; production scale pending |
| Creature schema/move SQL | Read-only metadata fingerprint and deterministic offline bundle | Isolated clone DML/replay/reverse/conflict gate pending |
| GO import/write | Metadata recorded | No semantic quaternion/export support |
| WMO interior/terrain writing/scripts/live apply | None | Deferred outside Phase 0 |

## Shell decision (provisional ADR)

Keep the existing standalone Godot viewer as the provisional host. A matched fixture panel now runs
standalone and as an opt-in EditorPlugin using the installed 4.7.1 `EditorDock` API; both headless
fixture workflows pass. This establishes reuse and startup, not viewport workflow superiority.
The final shell decision awaits timed user trials with the same actual viewport workflow.
No engine fork, web frontend, plugin SDK or additional service is needed for this experiment.

## Resource budget and measurement

Retain the roadmap's proposed 60 FPS median, 33 ms streaming p95, 50 ms input p95, 2 ms upload,
5/15-second warm/cold teleport, 4 GiB RAM and 2 GiB VRAM targets until measured and agreed.
The benchmark mode uses radius 1 and 2,000 shared-mesh box nodes, teleports every 20 seconds,
and records frame-time distributions, upload time, process working set and settle times.
Boxes are synthetic and not terrain-grounded. Headless runs do not measure GPU performance.
Short graphical diagnostics do not replace the 30-minute movement, input/VRAM and controlled
cold/warm gates. Input/VRAM are explicitly null rather than inferred.
