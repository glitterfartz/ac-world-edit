# Feasibility, risk register and decision experiments

## Feasibility classification
| Capability | Feasibility | Dependencies/qualification |
|---|---|---|
| Terrain navigation | Existing | Corrected stock-like client samples pass; full map corpus/performance still open |
| Outdoor spawn proxies, transforms, export | High after foundations | Coordinate/picking/state/export proof |
| Template/services/gossip/standard quest forms | Moderate | Typed aggregate semantics and complete dependencies |
| Ordinary patrol routes | Moderate | Shared path protection, movement wiring, game navigation test |
| SmartAI card editor | Moderate | Versioned metadata, lossless row import, subset runtime fixtures |
| Full arbitrary visual scripting | Poor MVP fit | Cannot replace unsupported code/state semantics |
| External IDE links and build diagnostics | High/moderate | Configured toolchains and source context |
| Embedded LSP editor | Moderate | Text widget integration, installed backend annotations |
| Universal Lua/C++ debugger | High effort/unverified | Backend instrumentation, symbols, debugger adapters |
| Model/static-world visualization | Substantial | M2 skins/materials/appearance and WMO groups/interiors |
| Native terrain/texture writer | High risk/high effort | Lossless model, seams/offsets, patch/test pipeline |
| Native water/new maps/animated model authoring | Very high | Specialized client formats and runtime compatibility |
| Live transactional world editor | New infrastructure | Runtime bridge and cache/state policy; no universal current API |
| Multiple expansions/cores | Substantial | Two adapter dimensions; proof per capability |
| Multiplayer collaborative editing | Deferred | ID coordination, shared conflicts, permissions and server services |

Assessments are proposals based on inspected code, not effort measurements. Rendering, server-content editing and client authoring remain separate.

## Risk register
| Risk | Evidence/status | Impact | Mitigation / gate |
|---|---|---|---|
| Coordinate/rotation mismatch | Transform inferred, no visual parity test | Wrong spawns despite plausible viewport | Golden landmarks, headings, GO rotations, SQL round-trip |
| Weak relationship enforcement | Only one declared FK in world metadata | Orphans/broken interactions | Typed references, dependency queries, delete plans |
| Concurrent authors | No editor reservation system inspected | ID conflicts/lost changes | Symbolic drafts, owned ranges, recheck, serialized deployment |
| Shared-record ownership | Loot/paths/gossip referenced by many | Changes spread unexpectedly | Consumers panel, explicit clone/shared edit |
| Runtime caches | Loaders/reload command patterns | DB/runtime disagreement | MVP export; clone restart; prove each reload |
| Lossy render DTO as writer | TerrainTile discards chunks | Corrupt client data | Separate lossless document, unchanged byte preservation |
| Archive precedence/profile | HD history; recursive mount heuristic | Wrong assets/client mismatch | Explicit build/locale/mount/hash manifest |
| Schema drift | Historical base SQL vs effective schema | Invalid SQL/wrong meaning | Probe fingerprint and disabled unsupported edits |
| ALE/Eluna mismatch | Upstream redirect/compatibility notice | Broken scripts/completion | Engine/revision/dialect profile |
| Renderer scale | Per-chunk nodes/materials; serial preparation | Latency/memory/frame spikes | Profile, budgets, batching/caches, fallback shell |
| Recovery/revert ambiguity | DB plus files/runtime | Partial release or unsafe rollback | Staged manifest, backups, conditional compensation |
| Scope/maintainability | Full world/DB/IDE/editor vision | Multi-year expansion before utility | Export-first spawn MVP and external tools |
| Redistribution | Client assets proprietary; varied tool licenses | Distribution constraints | User-supplied assets, provenance, dependency notice review |
| Native tool/plugin execution | External processes and parsers | Crash/code-execution exposure | Trusted configuration, argument arrays, bounds, isolation |

## Licensing and intellectual property
Confirmed source notices: StormLib MIT; warcraft-rs workspace MIT OR Apache-2.0; Noggit GPL3; ALE repository GPL3; AC has GNU GPL context and v2-or-later source headers. Viewer has no root LICENSE in inspected inventory. These notices guide reuse choices; file/dependency-level obligations must be reviewed before shipping. Source notices do not grant rights to redistribute WoW client assets.

Proposed distribution: editor code and owned content/scripts/patch metadata; clients/assets supplied locally by users, no bundled Blizzard data or raw full client archive. Prefer external Noggit handoff initially; an external process boundary alone is not a legal guarantee that all licensing issues disappear. Avoid copying AC/Noggit code into a differently licensed product without review. No legal compliance guarantee is made. [Licensing evidence](references/source-references.md#e3-asset-tooling)

## Proof-of-concept experiments — not performed
### P0-1 Client corpus and current-source test identity
Owner: asset lead. Build isolated copy only when implementation begins; route outputs outside original sources. Test all supported stock-profile WDTs and representative ADTs/BLPs with provenance. Differentially compare StormLib reads. Gate: exact decoded byte agreement for selected paths, supported-format failures explicit, no arbitrary archive mixing. Failure: restrict profile or replace archive backend.

### P0-2 Coordinates and picking
Owner: viewport lead. Compare at least 12 outdoor landmarks spanning all quadrants/maps and tile seams, including known spawn positions and four headings; add two GO rotations when supported. Verify inverse transform and terrain hit on hole boundaries. Suggested gate: horizontal error <=0.05 world units, exported persisted position error <=0.01 for representable samples, intended heading <=0.01 rad; report client/source quantization limits. Failure blocks placement/export.

### P0-3 State, undo and recovery
Owner: domain lead. Place/move/duplicate/delete, unload/reload, map switch, undo/redo, interrupt autosave at checkpoints. Gate: restored row/domain hashes equal expected state, no phantom commands, torn journal detected, UUID stable. Failure: repair command journal before feature growth.

### P0-4 SQL integrity in an isolated clone
Owner: adapter lead. Apply/replay/reverse supported plans under agreed updater policy, concurrent conflicting row/ID cases, shared dependencies and failure mid-DML. Gate: unrelated rows unchanged, planned rows exact, stale plans fail preflight, transactional failures restore baseline. This future write experiment is not authorized/executed in this research.

### P0-5 Godot shell/performance comparison
Owner: UX/viewport lead. Same search/place/move/review workflow in standalone and minimal EditorPlugin shell. Record input latency/task completion/friction and frame times on designated developer hardware. Benchmark 3x3 terrain, 2,000 proxies, region-query latency, switching/teleport and 30-minute movement. Gate targets in roadmap; select simplest shell meeting them. Do not use four sample parser timings as GPU evidence.

### P3-1 SmartAI round-trip and execution
Owner: behavior lead. Fixture source-specific events, linked rows, timed lists, phases, negative owners and conditions. Gate: byte/value preservation of unchanged SQL rows, zero broken links, loader diagnostics and controlled encounter behavior match intended semantics. Failure narrows supported palette.

### P4-1 Client write and geometry parity
Owner: asset lead. Unchanged ADT/WDT round-trip, one sculpt/paint/doodad/WMO/water fixture each as scope expands. Independent reopen, client load, server height/LOS/path parity. Gate: no lost unknown data, offsets/reference counts correct, seamless shared vertices, artifact hashes tied to profile. Failure retains external authoring only.

### P5-1 Runtime bridge consistency
Owner: server integration lead. Transient preview lifecycle, authenticated capability negotiation, thread dispatch, idempotent retry, cache revision and disconnect cleanup. Gate: no live DB implicit writes, no stale state acknowledgement, reset/restart restores predictable state. Failure uses restart-based testing.

## Cost and alternatives
Relative effort: spawn editor small-to-medium; higher-level content medium-to-large; complete asset renderer large; lossless terrain pipeline very large; full IDE/runtime debugging very large. No defensible calendar estimate without staffing/corpus/user trials.

A sustainable alternative is a Godot scene view plus typed SQL bundle tool, specialist terrain editor and external IDE. That delivers the central no-manual-SQL workflow while limiting ownership of difficult file writers and debugger infrastructure. The proposal adopts this as the initial product and adds tighter integration only after evidence.

