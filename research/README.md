# AzerothCore integrated world editor research

Completed 2026-10-11. This is a technical proposal, not an implemented editor.

Implementation now starts with [Phase 0 progress](progress/phase-0.md) and
[brainstorming](brainstorming/phase-0.md). Research was moved into this repository at the user's
request and is tracked with implementation. Historical paths/scope below describe the original
research session; current implementation scope is governed by `../AGENTS.md` and the phase tracker.

**Strongest recommendation:** retain Godot for the initial viewport after a measured shell/performance trial; keep editing state, validation and SQL compilation in engine-independent .NET libraries with explicit core/client adapters. Deliver a reversible server-spawn editor first, integrate specialist terrain tools and external IDEs, and gate native client writing/live editing on separate proofs.

## Read first
[Executive summary](01-executive-summary.md) -> [MVP](12-mvp-specification.md) -> [architecture](06-editor-architecture.md) -> [database contract](07-database-abstraction.md) -> [roadmap](11-development-roadmap.md).

## Document index
| Document | Contents |
|---|---|
| [01 Executive summary](01-executive-summary.md) | Recommendations, evidence, MVP and principal risks |
| [02 Existing project assessment](02-existing-project-assessment.md) | Actual Godot code/dependencies, parsing/rendering/streaming, reuse and frontend alternatives |
| [03 AzerothCore architecture](03-azerothcore-architecture.md) | Databases, content systems, authority, runtime and migration boundaries |
| [04 Client formats and assets](04-client-formats-and-assets.md) | Current coverage, coordinates, models, lossless client editing, tooling and patch pipeline |
| [05 Feature specifications](05-feature-specifications.md) | Shell, viewport, inspectors, quests, paths, scripting, catalogs and validation UX |
| [06 Editor architecture](06-editor-architecture.md) | ADRs, components/interfaces, state/jobs/workspace, plugins, tools and future runtime bridge |
| [07 Database abstraction](07-database-abstraction.md) | Typed aggregates, ownership, IDs, conflict detection, SQL export and safe reversibility |
| [08 Scripting and IDE](08-scripting-and-ide.md) | SmartAI semantics, conditions, ALE/Eluna, C++, LSP/DAP and debugging limits |
| [09 User workflows](09-user-workflows.md) | All ten requested step-by-step scenarios, managed work, artifacts and realistic limits |
| [10 Feasibility and risks](10-feasibility-and-risks.md) | Feasibility classes, risk register, reuse/licensing and proof experiments |
| [11 Development roadmap](11-development-roadmap.md) | Six incremental phases, dependencies, milestones, deliverables and acceptance gates |
| [12 MVP specification](12-mvp-specification.md) | Included/excluded scope, implementation slices and 15 release acceptance scenarios |
| [13 Open questions](13-open-questions.md) | Unverified assumptions, owners/experiments and decision gates |
| [Source references](references/source-references.md) | Inspected local paths/symbols and external primary-source URLs |
| [Research notes](references/research-notes.md) | Commands, database queries, correction history, observed results and research limits |

## Supporting evidence index
- [Direct OGClient asset checks](references/asset-check-run.txt), [timing](references/asset-check-timing.txt).
- [Corrected wow-data junction asset checks](references/corrected-workspace-asset-check-run.txt), [timing](references/corrected-workspace-asset-check-timing.txt).
- Historical modified-HD-client run: [failure log](references/workspace-asset-check-run.txt), [timing](references/workspace-asset-check-timing.txt). Superseded by the user correction and successful rerun; not a current stock-client blocker.
- [Schema engines/approximate counts](references/database-query-0.txt), [entity/behavior column metadata](references/database-query-1.txt), [quest/relationship/loot/update columns](references/database-query-2.txt), [declared foreign keys](references/database-query-3.txt).
- [Creature orphan audit](references/database-extra-0.txt), [creature indexes](references/database-extra-1.txt), [update include paths](references/database-extra-2.txt), [empty example SmartAI lookup](references/database-extra-3.txt).
- [Viewer source hashes](references/viewer-source-hashes.json), [inspected file/binary hashes](references/inspection-hashes.json).
- [Documentation validation](references/document-validation.txt).

## Evidence and non-destructive scope
Local core revision: `5ddc706b7fdcbda01164862652a9b61d60e9a8e0`; read-only test DB reports `ACDB 335.17-dev` and matching revision prefix. Client executable metadata: 3.3.5.12340.

The user corrected wow-data from a modified HD-client junction to `X:\AzerothCore\OGClient\Data`. The corrected path passes the preexisting asset binary's four-continent checks, opening 18 archives and discovering 101 maps. This confirms sampled parsing, not universal asset compatibility, current-source build identity, GPU performance or editing support.

Only SELECT queries were issued through the read-only test MySQL MCP. No live database connection, database modifications, repository/client edits, server compilation/launch or deployment occurred. New files are exclusively in `X:\AzerothCore\research`. Relevant inspected viewer/source/binary hashes were rechecked unchanged.

## Requirement coverage
| RESEARCH.md area | Primary deliverables |
|---|---|
| Existing project investigation and engine comparison | 02; source references V1–V3 |
| Core architecture/databases/content authority | 03, 07, 08; A1–A4/D1 |
| Client formats, editing and tooling | 04, 10 |
| Integrated UX/content/quest/behavior/IDE | 05, 08, 09 |
| Extensible technical design and integrity | 06, 07 |
| Ten developer workflows | 09 |
| Feasibility, risks, unknowns and proofs | 10, 13 |
| Roadmap and useful MVP | 11, 12 |

Throughout: **Confirmed** = inspected/observed, **Derived** = calculation/inference, **Proposed** = future design, **Unverified** = experiment required. Acceptance criteria are targets, not claims of tests already passed.

