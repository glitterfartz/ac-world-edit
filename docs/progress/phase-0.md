# Phase 0 foundation validation

Scope: foundation contracts and isolated experiments only; no Phase 1 UI or client/server writes.
Roadmap: `X:/AzerothCore/research/11-development-roadmap.md`; experiments in document 10.
Full tracker: `X:/AzerothCore/research/progress/phase-0.md`.
Brainstorming: `X:/AzerothCore/research/brainstorming/phase-0.md`.

| Milestone | Status | Evidence / remaining gate |
|---|---|---|
| P0-1 explicit mounts/current-source corpus | in progress | Build source, record supported WDT/ADT/BLP corpus and malformed fixtures; differential backend pending |
| P0-2 coordinate/picking | not started | Mathematical fixtures first; 12 real landmarks and two GO rotations required |
| P0-3 commands/recovery | not started | Engine-independent state, journal and interruption tests |
| P0-4 schema/export | not started | Offline fixtures and read-only schema probe; real clone DML pending isolated authorized target |
| P0-5 shell/performance | not started | Matched standalone/plugin experiment; agreed hardware and 30-minute GPU run pending |

P0-1 evidence: `./Launch-Viewer.ps1 -CheckAssets` freshly builds source and passes four continental
ADT samples, their BLP textures, all discovered WDTs (terrain or explicit WMO-only rejection),
and five malformed-input checks. Log: `research/progress/phase-0-asset-checks.txt` includes asset
SHA256 and selected archive provenance. Explicit 17 mounts exclude backup-enUS; 106 maps replace
the historical heuristic's 101. This change requires client/differential precedence verification;
it is not proof of matching the actual client's archive lookup. No client files were written.

Next: implement engine-independent coordinate, state/recovery and bounded export experiments.
