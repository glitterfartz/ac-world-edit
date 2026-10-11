# Phase 0 foundation validation

Scope: foundation contracts and isolated experiments only; no Phase 1 UI or client/server writes.
Roadmap: `research/11-development-roadmap.md`; experiments in document 10.
Full tracker: `research/progress/phase-0.md`.
Brainstorming: `research/brainstorming/phase-0.md`.

| Milestone | Status | Evidence / remaining gate |
|---|---|---|
| P0-1 explicit mounts/current-source corpus | complete (bounded corpus proof) | 133 selected assets match StormLib; synthetic golden ADT/malformed checks pass. Universal/client lookup compatibility not claimed |
| P0-2 coordinate/picking | in progress | 12 synthetic positions, 1,024 real MCNK anchors, seam/heading/hole tests pass; actual client landmarks/GO rotations pending |
| P0-3 commands/recovery | complete (bounded prototype) | Domain state hashes restore; durable commands, interruption, corruption and transient Godot projection unload pass. Production scale deferred |
| P0-4 schema/export | in progress | Read-only probe and offline deterministic move/export checks pass; real clone DML pending isolated authorized target |
| P0-5 shell/performance | in progress | Both fixture shells pass; short graphical 9-tile/2,000-proxy capture recorded. Full UX/hardware/30-minute/input/VRAM gates pending |

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
Milestone commits: `a27a638` mounts/corpus; `b28cb7d` domain/recovery;
`0200d66` offline export and research relocation.

P0-1 additional evidence: `./Experiments/Run-StormLibChecks.ps1` downloads the official pinned
StormLib 9.40 DLL into ignored artifacts, verifies release ZIP and x64 DLL SHA256, opens archives
read-only and confirms exact decoded byte agreement on 133 selected assets. This compares the
same explicit mount profile, not the running client's entire discovery/locale behavior.
Synthetic v18 ADT fixture plus bad WDT/BLP/duplicate MCNK checks now pass.
`research/progress/phase-0-stormlib-checks.txt` records 1,024 actual MCNK coordinate anchors;
maximum projected horizontal error is 0.001303 units. These are source anchors, not 12 visual client landmarks.

Shared CPU mesh geometry is used by the production renderer and picking tests. Hole interior,
both sides of its boundary, and all-hole chunks pass. Fresh headless streaming smoke loads nine
tiles, unloads/moves, and returns: `research/progress/phase-0-streaming-smoke.txt`.
Both matched fixture shells pass (`phase-0-shell-checks.txt`); the editor host is a disposable
project copy, and the user's plugin configuration stays unchanged. Disposable Godot projection
unload preserves domain state. See `docs/architecture/phase-0-contracts.md` for support matrix/ADR.

Performance diagnostic: Windows OpenGL 3.3/NVIDIA 617.14/RTX 5080, 60 seconds, nine terrain tiles,
2,000 synthetic proxies. The corrected Stopwatch capture (`phase-0-viewport-after-budget.json`)
reports stationary median 7.789 ms (~128 FPS), streaming p95 12.773 ms, upload p95 2.2104 ms,
max upload 18.1942 ms, process peak ~421 MiB; uncontrolled settle samples 3.48/3.08/2.65 s.
The initial `before-budget` report used smoothed Godot delta for frames, so its frame quantiles
are historical and not authoritative; upload durations were Stopwatch measurements.
An elapsed-time upload limit improves scheduling but does not pass the 2 ms target yet.

Next: refine measured upload scheduling; save final acceptance report and resource-dependent gates.
