# Open questions and decision gates

The proposal distinguishes unknowns from blockers. None prevents completing this research; several block later implementation releases.

| Question | Current evidence | Owner / experiment | Decision affected |
|---|---|---|---|
| Are prebuilt checks identical to current source? | Binary passes; no rebuild done; hash recorded | Asset lead, isolated source build P0-1 | Trust/coverage baseline |
| Do coordinate/heading equations match client landmarks? | Derived from renderer/grid/normal conventions, not visually tested | Viewport lead, P0-2 | Placement export release |
| Which GameObject full rotations are safe? | SQL quaternion/orientation confirmed | Viewport/core lead, yaw/full rotation fixtures | MVP yaw vs advanced transform |
| Which archive mount policy exactly matches selected locale/client? | Corrected stock-like 18 archives pass; recursive heuristic includes backup candidates | Asset lead, differential path/locale tests | Explicit ClientProfile |
| Should MPQ read backend be StormLib? | Custom feature subset works on samples | Asset lead, corpus and packaging comparison | Parser backend |
| Does standalone Godot beat EditorPlugin for content tasks? | Existing viewer runtime shell, extension APIs available | UX lead, P0-5 trial | Frontend ADR |
| What hardware/resource targets should be supported? | Historical RTX 5080 log only; no fresh GPU measurement | Product/viewport owner benchmark | Performance budget |
| What custom numeric ranges can this project own? | No safe editor reservations established | Project operator, allocation policy | New IDs and collaboration |
| Can source snapshots be captured consistently? | Read-only metadata/queries available; no bulk export performed | Adapter lead, stable read transaction/snapshot trial | Offline baseline provenance |
| Which reloads refresh existing objects/AI safely? | cs_reload loaders inspected, active instance behavior untested | Runtime lead, clone experiments | Test/reload matrix |
| What Lua engine/version is desired and installed? | modules contains no Lua engine; upstream ALE diverged | Script lead/user project setting, pinned build | API stubs/examples/reload |
| Which upstream pending/custom/module workflow will exports target? | All include paths exist; local SQL rules inspected | Core maintainer/operator | SQL packaging/lint/replay |
| How to coordinate updater and editor application audit? | Updater hashes/state confirmed | Deployment lead, replay/idempotency experiment | Exactly one apply path |
| Can newer/unknown fields round-trip safely? | Row preservation proposed | Adapter lead, additive/type-change fixtures | Capability disable policy |
| Which quests require code/client beyond standard slots? | Fixed fields, signed relationships confirmed | Content lead, representative authored corpus | Quest compiler scope |
| Can full SmartAI semantics be described from enums alone? | Validators contain contextual rules | Behavior lead, curated metadata/corpus P3-1 | Supported visual palette |
| Are Noggit outputs compatible with selected client/toolchain? | Upstream scope/source verified; no local trial | Asset lead, pinned handoff experiment | First terrain integration |
| Which ADT unknown chunks/offsets must preserve exact bytes? | Current reader discards many | Asset lead, lossless P4-1 corpus | Native writer release |
| Can server geometry rebuild be incremental/reproducible? | Extraction code exists; no build performed | Tooling lead, changed-tile/full-rebuild comparison | Patch pipeline cost |
| How close must asset preview match client shading/animation? | Current terrain approximate; models absent | Product/asset lead, user trials | Renderer depth vs utility |
| Is embedded IDE worth maintaining? | External LSP/IDE ecosystem exists | Product lead after usage data | IDE phase scope |
| Which debugger can attach to selected Lua/C++ runtime? | No instrumentation tested | Runtime/debug lead | DAP capabilities |
| What alternate core revision is first? | TrinityCore branch existence only | Adapter maintainer, narrow fixtures | Extension design proof |
| What license should viewer/editor use? | No viewer root license; candidate tools differ | Maintainer/dependency review | Distribution/reuse |
| What client asset redistribution is permitted for intended release? | No rights audit conducted | Release owner/appropriate legal review | Packaging policy |

## Required decisions before Phase 1
Choose shell after comparison; declare client/core support profile; approve coordinate/rotation fixtures; define ID namespace policy; set hardware targets; select export packaging and baseline snapshot strategy. These are engineering choices to resolve during implementation preparation, not requests to mutate the existing repositories now.

## Deferred decisions
Full terrain writing, new maps, model animation fidelity, runtime bridge, debugger, alternate core and collaboration should wait for user demand and independent proof. An unsupported feature must be visibly unavailable rather than partially implemented under an optimistic compatibility claim.

## Research completion status
All requested research domains and deliverables are covered. The corrected junction passes read-only asset checks. Proposal contains architecture, data model/export contracts, ten workflows, six-phase roadmap, bounded MVP, source evidence and follow-up experiments. No editor implementation, live DB access, or client/server modification was performed.

