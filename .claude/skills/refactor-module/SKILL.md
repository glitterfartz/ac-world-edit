---
name: refactor-module
description: Refactor a module or oversized file in ACWorldEdit without changing behaviour. Use whenever the user asks to refactor, restructure, split, clean up, de-duplicate, rename classes, reduce a file's size, extract a class or seam, or calls a module too big or hard to maintain. Also use before a large feature when the target area needs a shape change first. The workflow builds a regression net, moves in gated phases, prefers readable domain names and real abstractions, commits each milestone, and keeps the structural ratchet green.
---

# Refactor a module

A refactor is a change of shape, not of behaviour. The safest refactor is one where the user can
see, after every commit, that the program still does exactly what it did before. This project has a
working example of that: the viewer refactor (R0–R7) moved ~4,000 lines, split the shell to 283
lines, extracted a lighting sampler, and kept six byte-identical goldens the whole way. Use the same
philosophy here.

Read `CLAUDE.md` first for the repository rules; architecture and test details live in `docs/`.
The worked example behind this skill, with exact milestones, measurements and gate output, is in
`references/viewer-case-study.md`.

## The non-negotiables

1. **Behaviour is proven, not asserted.** If the module has observable output, capture it before
   touching code and compare it after every change. Green unit tests are not enough for a renderer,
   UI tree, generated SQL or streamed tile set.
2. **One logical change per commit.** A rename commit contains only renames. A move commit contains
   only moves. A behaviour fix is its own commit, with its own proof.
3. **The gate runs after every commit.** If it fails, fix the commit; never edit a golden, a ratchet
   or a test to make a refactor pass.
4. **Readable names and honest abstractions come before line counts.** A 400-line file with four
   clear responsibilities is worse than a 600-line file with one. Split along reasons to change, not
   along line numbers.
5. **Deviations are reported, not hidden.** If a target is impossible or conflicts with another
   goal, show the measurement and ask. Never delete documentation or tests to hit an arbitrary
   number.

## Phase 0 — Understand the module

- Read the docs page for the area (`docs/architecture/*.md`, `docs/status/open-work.md`).
- List the files, their sizes, the public surface and the call sites. Use
  `Select-String`/`rg` for type and method references; do not assume a member is unused.
- Identify the invariants the code relies on: ordering, threading, precision, ownership, teardown
  order, `SetInputAsHandled`, frame budgets, SQL naming. Write them down before moving anything.
- Identify what is *not* in scope. In this project that includes selection-priority orders, the
  input swallow order, the streaming hysteresis and budget, and the SQL export file naming. Add
  area-specific exceptions to the list.

## Phase 1 — Build the regression net first

For Godot-facing modules the net is usually the existing gate:

```powershell
python Tools/refactor_gate.py --structural   # seconds: budgets, Godot-free layers, goldens manifest
python Tools/refactor_gate.py --fast         # structural + build + both test projects
python Tools/refactor_gate.py                # + six wrath/retail goldens (needs client data)
```

If the module has no stable capture yet, add one before refactoring:

- a `--state-dump`/`--ui-dump`-style deterministic flag that prints machine-readable state;
- a golden file under `Tests/golden/` with a hash manifest;
- a normaliser for live data (timestamps, DB row counts) so the golden is stable;
- a `Tools/*_golden.py` or gate step that runs and diffs it.

A UI golden that pins only the empty shell is not coverage. If the refactor touches rendered cards,
forms, pending rows or dialogs, capture those states first (a `--<area>-dump` that walks the live
control tree after real data loads), or the control tree can change without any golden noticing.

For pure logic (anything under `Foundation/`, `Scripts/Assets/`, `Scripts/Data/`), add xUnit tests
that pin the edges: ordering, ties, zero/negative input, wraparound, precision. These layers must
stay Godot-free; the xUnit project compiles them without a Godot reference.

Then measure the baseline and write it down:

```powershell
Get-ChildItem <folder> -Recurse -Filter *.cs |
  ForEach-Object { [pscustomobject]@{ Name=$_.Name; Lines=(Get-Content $_.FullName | Where-Object { $_.Trim() -ne '' }).Count } } |
  Sort-Object Lines -Descending
```

Record file count, largest file, shell size, long methods, test counts and the folder total. If a
ratchet file exists (`Tools/viewer-budget.json`, `Tools/viewer-long-methods.json`), it is the
contract; lower it in the same commit that earns the reduction.

## Phase 2 — Mechanical moves first

Do the boring, reviewable changes before rewriting anything:

- rename a type once, across every partial, scene reference, `.cs.uid`, test and doc page;
- move files into responsibility folders with their `.uid` companions;
- keep namespaces flat unless the repository already uses nested namespaces;
- run the gate and confirm the diff is renames/moves only.

A reviewer should be able to skip this commit's diff. Do not combine it with logic.

## Phase 3 — Extract pure decisions

Move decisions out of Godot nodes and UI code into plain, testable functions:

- geometry and tile arithmetic (`TileGrid`, `TileWindow`, `RayBox`);
- parsers and records that do not need Godot (`ClientLighting`, `LightingSampler`,
  `LightingBlend`);
- SQL/draft grouping (`SqlExportBatch`);
- selection/scheduling rules that take values and return values.

Rules for the extracted code:

- no `using Godot`, no `Color`, `Vector3`, `Node`, `GD` in `Foundation/`, `Scripts/Assets/` or
  `Scripts/Data/`;
- pass plain floats/tuples or the project's own `Point3`/`RgbBand`-style records at the boundary;
- the node keeps *publication* only (converting floats to Godot values and setting shader/window
  state); the sampler keeps *arithmetic*;
- keep `Color.Lerp` and `Mathf.Lerp` distinct where the last ULPs matter; do not widen floats to
  doubles to make an extraction convenient;
- a null result from a sampler means "keep the last frame" when the original early return skipped
  application;
- add a test per branch the old code had: default/fallback, nearest/tie, zero span, interior,
  missing table.

Prefer injecting seams (`Func<...>`, a small host interface) over static singletons. A seam is worth
it when it lets a flow be tested without a client install or a database; it is not worth it when it
only moves fields around.

## Phase 4 — Shared construction, not copy-paste

When the same control tree is built by hand in several places, add a small builder
(`UiForm`, `MenuSheet`, `Dialogs`, a header/HistoryRow/PoseCard helper) *additive first*:

- land the helper and its gdUnit4 test;
- convert one consumer per commit;
- keep the built control tree identical (the UI golden proves it), including creation order,
  auto-generated node names, literals, `MouseFilter`, tab order and "configure before `AddChild`";
- builders construct synchronously and never own state or defer work.

If converting a consumer requires inserting a wrapper node, change the helper to wrap an existing
container instead. The manifest/golden is the contract.

## Phase 5 — Split the coordinator

A composition root that knows too much should become a thin coordinator. Move methods to their own
class only when the class has a real seam:

- **state-owning collaborator** (e.g. `TerrainStreamer`): owns the sets and the worker lifecycle,
  receives the renderer/world/camera/overlay seams through its constructor, exposes small
  read-only properties for the UI and diagnostics;
- **flow controller** (e.g. `ViewerSession`): owns the order of operations behind a host interface,
  with the mutable state and UI side effects on the host;
- **router** (e.g. `ViewerInput`): owns camera angles and capture, receives the overlays and
  selection hook through a host interface, and forwards Godot's callbacks from the Node.

When extracting, forward the old member names on the shell for call sites you are not touching yet;
that keeps a 40-file diff out of a 3-file change. Remove the forwarding layer in a later commit if
it is not pulling its weight.

If a class extraction would require a 30-member host and add more lines than it removes, say so and
ask whether the class boundary is worth it. Do not create a class that only forwards to the shell.

## Phase 6 — De-duplicate behaviour, carefully

Extract a shared path only when the copies are *the same operation*, not merely similar text:

- `OpenMapAtServerPose`: the map handler overwrites the tile fields, so the order is now explicit;
- `ProbeTerrain`: one probe with a `Loaded` flag so callers can still explain "not loaded yet";
- one `overlays` list for region resets;
- one `SessionState` enum for `busy` + button state, with the stale-button bug fixed in its own
  commit and a smoke assertion.

Before unifying selection priority, input ordering or SQL naming, stop: those are documented
behaviour, and there is no click/smoke matrix that proves a change is harmless.

## Structural budgets

Default targets, unless the area's docs say otherwise:

| Measure | Target |
|---|---|
| Any `.cs` file | ≤ 400 non-blank lines |
| Composition root | ≤ 300 non-blank lines |
| Any method | ≤ 60 lines |
| Folder | ratchet, only lowered |

A method's size is a smell, not a crime: a 65-line switch with no decisions is fine; a 50-line method
with three nested flows is not. Split by extracting a named step, not by adding `#region`.

When a target conflicts with a real seam (class constructors, host interfaces), measure both
readings (code vs non-blank/comments), show the numbers and ask. Never delete comments or tests to
hit a number.

## When the user asks for a plan, not code

Sometimes the deliverable is a hand-off document for another agent. Produce the same thinking in
writing, in this order, and make it executable:

1. **Measured baseline** — folder/file counts using the gate's own counter, shell size, largest
   file, long methods, ratchet values, test counts, existing goldens.
2. **The honest finding** — is the module actually over budget, or is the pain tangle/naming? Say
   which. Do not invent a size emergency.
3. **What is missing from the net** — the states or paths no golden/smoke covers, and the capture to
   add before the first move.
4. **Ordered, gated commits** — net → mechanical moves → pure extractions → seams → docs. One
   logical change per commit, with the gate command that proves each one.
5. **Named units and seams** — concrete class/file names and what each owns, not "split into
   helpers". State which extractions you reject and why (e.g. a Godot-typed prepare step).
6. **Invariants and do-not-touch** — ordering, budgets, hysteresis, precision, smoke contracts.
7. **Expected end state and non-goals** — including line movement out of the folder, not just a
   smaller number, and what this pass deliberately leaves undone.

A plan that only lists file moves is a file split. A plan that names responsibilities, proofs and
seams is a refactor plan.

## Commit and reporting protocol

```
type(scope): imperative summary (≤72 chars)
```

- `refactor(viewer): ...` for shape changes, `feat(ui): ...` for a new builder, `fix(viewer): ...`
  for a behaviour fix found on the way, `test(...)` for the regression net, `docs(...)` for docs.
- One logical change; docs updated in the same commit; no AI attribution trailers.
- After every commit, run the gate and quote the result in the reply:
  - structural result and measured lines;
  - test counts (`xUnit`, `gdUnit4`);
  - golden results (`ok` for each);
  - any smoke that exercises the moved path (`terrain`, `gizmo`, `reload`).
- End with a short table of commits, what moved, the measured before/after, and an honest list of
  deviations or deferred items. If something remains, say what and why, with a concrete next step.

## Definition of done

- The gate is green on the final commit, and at least one observable golden/smoke covers each moved
  path.
- No file or method exceeds the agreed budget; the ratchet files match the measurement.
- Names describe the domain (`TerrainStreamer`, `LightingSampler`), not the pattern
  (`Manager`, `Helper`, `Util`, `Service`).
- The composition root is small enough to read in one sitting and mostly delegates.
- Docs describe the new files/seams; no stale names remain (`Select-String` across `docs/`).
- The user can see the evidence without taking anything on trust.

## Anti-patterns seen in this codebase

- Moving code into a partial file and calling it an extraction. A partial split is a good first
  step, but the plan's target may be a standalone class with a seam; say which one you did.
- A builder that inserts a wrapper node, shifting auto-generated names and breaking the UI golden.
- A "sampler" that still holds Godot `Color`/`Vector3` arithmetic.
- Sorting or reordering during a move; the golden records creation order.
- Changing `SetInputAsHandled`, `PerfMark` indices or the streaming budget to make a split neater.
- Making a behaviour fix inside a refactor commit. Fix it separately, with its own smoke.
