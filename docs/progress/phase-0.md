# Phase 0 foundation validation

Scope: foundation contracts and isolated experiments only; no Phase 1 UI or client/server writes.
Roadmap: `research/11-development-roadmap.md`; experiments in document 10.
Full tracker: `research/progress/phase-0.md`.
Brainstorming: `research/brainstorming/phase-0.md`.

| Milestone | Status | Evidence / remaining gate |
|---|---|---|
| P0-1 explicit mounts/current-source corpus | in progress | Build source, record supported WDT/ADT/BLP corpus and malformed fixtures; differential backend pending |
| P0-2 coordinate/picking | in progress | 12 synthetic inverse positions, seam sides, four headings, triangle ray hit/miss pass; real landmarks/GO rotations pending |
| P0-3 commands/recovery | in progress | Engine-independent state/journal passes restart, undo/redo and corruption experiments; viewport unloading integration pending |
| P0-4 schema/export | in progress | Read-only probe and offline deterministic move/export checks pass; real clone DML pending isolated authorized target |
| P0-5 shell/performance | not started | Matched standalone/plugin experiment; agreed hardware and 30-minute GPU run pending |

P0-1 evidence: `./Launch-Viewer.ps1 -CheckAssets` freshly builds source and passes four continental
ADT samples, their BLP textures, all discovered WDTs (terrain or explicit WMO-only rejection),
and five malformed-input checks. Log: `research/progress/phase-0-asset-checks.txt` includes asset
SHA256 and selected archive provenance. Explicit 17 mounts exclude backup-enUS; 106 maps replace
the historical heuristic's 101. This change requires client/differential precedence verification;
it is not proof of matching the actual client's archive lookup. No client files were written.

P0-3 evidence: `dotnet run --project Experiments/FoundationChecks.csproj -p:NuGetAudit=false`
passes place/move/duplicate/delete, restart stable IDs, durable undo/redo, branch invalidation,
map change, interrupted checkpoint, torn-tail recovery without phantom command, checksum
rejection and single-writer exclusion. Full-history journal snapshots deliberately bound this
prototype to small fixtures; journal compaction, long-session scale and power-loss hardware
guarantees are not claimed. The production viewer has no editing state yet.

P0-4 evidence: current read-only creature/gameobject metadata is saved in
`Experiments/Fixtures/spawn-schema.json`. Fingerprint includes ordered columns/types/null/default/keys,
charset/collation and source/server/SQL-mode identity. Fixture hash:
`23F43207F5A61242B9F7172FA7D7F11C03077898B6AB80A094FF0C2EA1EEAE5B`.
The console harness passes raw unknown/signed/null retention, immutable row snapshots, deterministic
forward/reverse/manifest under reordered schema and French culture, bounded id+guid predicates,
stale/missing source and ID-collision rejection, missing/type-changed schema rejection.
Scope is same-map creature pose updates only. SQL is review-only; no executable guard/apply service,
dependency checks, new-ID allocator, GO rotation exporter or actual transaction proof is claimed.

Research relocation: 38 files moved into `research/` with SHA256 equality checked before updates.
Current guidance and trackers use repository paths. Historical research references remain archival.
Milestone commits: `a27a638` mounts/corpus; `b28cb7d` domain/recovery.

Next: shared CPU terrain geometry/picking and minimal matched shell experiments.
