# Phase 0 acceptance evidence

Status: foundation implementation and bounded proofs completed; Phase 0 release gates remain open.
This report separates measured results from pending resources. It does not authorize Phase 1.
See `phase-0.md`, `../brainstorming/phase-0.md` and `../../docs/architecture/phase-0-contracts.md`.

## Milestone / deliverable coverage

| Requirement | Evidence | Status / limitation |
|---|---|---|
| Explicit client mounts | `Scripts/Assets/ClientMountProfile.cs`, required named archives, backup exclusion | Implemented for provisional stock 12340/enUS profile |
| Source-built parser identity/corpus | Fresh `Tests/AssetChecks.csproj` build, source/binary hashes in provenance JSON | Passed bounded corpus; no historical binary substituted |
| Corpus/golden/malformed formats | 66 terrain WDTs, 40 explicit WMO-only rejections; four continental ADTs/BLPs; synthetic ADT and malformed WDT/ADT/BLP/alpha/chunk fixtures | Passed sampled supported formats; interior WMO/rendering unsupported |
| Differential backend reads | Official checksum-verified StormLib 9.40, 133 exact byte comparisons | Passed same explicit profile; not actual client discovery parity |
| Viewport/state boundary | Engine-free Foundation; transient Godot projection unload smoke; CPU geometry separate from Godot resources | Passed prototype; no production placement UI |
| Position/heading | 12 synthetic inverse samples, seam sides, cardinal headings; 1,024 actual MCNK anchors within 0.001303 units; representable SQL float fixture within 0.01 | Math/source checks pass; real client landmark alignment remains pending |
| Hole-aware picking | Shared renderer/picker triangles, hole interior/boundary/full-hole fixtures | Passed CPU proof; production picking UI outside scope |
| GO rotation basis | GO schema captured; no quaternion authoring/export implemented | Pending two actual GO rotation/client/model fixtures; no support claimed |
| Commands/undo/recovery | Place/move/duplicate/delete, restart state hashes/UUIDs, map change, branch invalidation, durable undo/redo, torn journal, interrupted checkpoint, corruption rejection, writer lock | Passed bounded prototype; no compaction/large-session/power-loss hardware guarantee |
| Schema fingerprint/typed spawn mapping | Read-only live metadata fixture; required column/type/null/PK validation; raw row to typed server pose | Passed offline creature-move contract; not a full production database adapter |
| Unknown values preserved | Immutable raw cells; unknown text, signed equipment value, null VerifiedBuild retained | Passed fixture; consistent live bulk snapshot strategy pending |
| Deterministic SQL bundle | Forward/reverse SQL and raw before/after manifest; explicit id+guid; invariant float literals; reordered metadata/French culture test | Passed review-only same-map creature move; no execution API |
| Stale rows / IDs | Changed/missing baseline and colliding GUID fixture rejection | Passed offline preflight; concurrent/phantom protection requires real clone test |
| Apply/replay/reverse/failure mid-DML | No writable connection or apply implementation | Pending disposable clone and explicit test-target write authorization; existing server data stays untouched |
| Source untouched by normal edit flow | Asset reader FileAccess.Read; oracle MPQ_OPEN_READ_ONLY; SELECT-only connector; drafts/bundles/artifacts project-owned | Supported by API boundaries and executed command scope; no database-wide checksum claimed |
| Standalone vs EditorPlugin | Identical synthetic search/place/move/undo/redo/review/reopen panel; disposable copied editor host | Both fresh headless smoke workflows pass; timed full viewport usability trial pending |
| Shell ADR/support matrix | `docs/architecture/phase-0-contracts.md` | Provisional standalone; final choice pending matched actual workflow |
| Resource budget | Roadmap targets retained; actual short graphical captures stored separately | Provisional until agreed baseline and complete measurements |

## Resource-dependent gates

Latest graphical diagnostic (`phase-0-viewport-headroom.json`): 60 seconds on the local RTX 5080,
nine terrain tiles and 2,000 synthetic boxes. Raw callback-interval stationary median is
7.7107 ms (~130 FPS), streaming p95 12.488 ms, upload p95 1.8256 ms, maximum upload 17.7196 ms,
process peak ~429 MiB. A 1.5 ms soft scheduling deadline improves p95 while preserving evidence
of indivisible upload hitches. The strict 2 ms per-frame target is not passed. Input/VRAM,
controlled warm/cold and 30-minute behavior remain unmeasured. Earlier smoothed-delta frame
quantiles are explicitly archival and do not supply current acceptance evidence.

1. **Client parity:** designated disposable client/test workspace, 12 documented outdoor landmarks
   across quadrants/maps/seams, reference headings, and two GO rotations. Existing client directories
   must remain unchanged, including runtime-generated configuration/cache files.
2. **Database integrity:** separately designated disposable MySQL clone/fixture target and an
   authorized test-write workflow. Verify forward/replay/reverse, concurrent row/ID conflicts,
   shared dependencies, unrelated rows unchanged and rollback after mid-DML failure. The existing
   read-only connector is sufficient for schema work and does not need replacement credentials.
3. **Shell/performance:** agree minimum hardware and compare the same actual viewport workflow in
   both shells. Run the 30-minute movement case, measure input latency and VRAM, and distinguish
   controlled cold/warm loading. Short runs do not pass these gates.
4. **Before Phase 1:** choose owned numeric ID ranges/export packaging and consistent snapshot
   strategy; accept or narrow the declared client/core support profile. These choices cannot be
   inferred from MAX(guid)+1 or a successful parser sample.

## Deferred scope

WMO interior rendering, M2 models, template/quest/script authoring, terrain/client writing, live
apply and server builds remain deferred. No existing client/server files or database content were
modified. Research now lives in this repository by explicit user instruction.

## Resume

Read `phase-0.md`, this report, brainstorm notes and Git status/recent commits. Re-run a check only
when relevant code changes or new evidence warrants it. Fulfill the remaining gates in isolated
targets and record results; do not reinterpret a pending gate as a pass.
