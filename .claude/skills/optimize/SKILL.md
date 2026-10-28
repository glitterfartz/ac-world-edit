---
name: optimize
description: Measure and improve ACWorldEdit's runtime performance in one area - frame rate and frame-time consistency, tile and model streaming, overlay and game-object loading, database query cost. Measures with --perf-smoke, Godot monitors or Stopwatch probes before and after every change, commits each proven win, and reports overhaul-sized work instead of starting it.
argument-hint: "<area, e.g. tile streaming | creature overlay | TC waypoint query>"
disable-model-invocation: true
---

# Optimize

Make the viewer faster and smoother without changing what it does: a high, steady frame rate while the camera flies, short non-blocking loading of terrain, models and overlays, and database reads that stay cheap and never stall the main thread. Judge frame time by p95/p99 and the worst hitch, not mean FPS.

Scope: `$ARGUMENTS`. If it is empty, run the benchmark, pick the area with the worst numbers (or the top open finding in `docs/performance.md`), and say why.

**Measure before you change anything, and again after.** An optimization without a before/after number is not committed.

Read `.claude/skills/_shared/working-rules.md` first and follow it. Also read `docs/performance.md` (benchmark, baseline, ranked findings, open work), `docs/ARCHITECTURE.md` (threading) and `docs/architecture/viewer-runtime.md` (streaming, per-frame order, smokes).

## Know the structure before changing it

Check the current values in the code and `viewer-runtime.md`, don't trust these from memory:

- Terrain and models each run one decode task at a time, with per-frame upload and bind budgets (item count and milliseconds) in `TerrainViewer` and `Rendering/ModelLibrary.cs`; models use LRU eviction with a soft memory limit, ref-counted textures and `MultiMeshInstance3D` placements.
- Overlays: `RegionQuery<T>` runs one cancellable query at a time per overlay; reads are bounded (`DbLimits`, the per-source limits), use a command timeout and never write.
- Godot objects are touched on the main thread only; workers produce plain CPU data; a `generation` counter plus a `CancellationTokenSource` discards stale results.

## Baseline

Record numbers, machine state (windowed or headless, map, tile, camera path) and the commit.

- **Benchmark:** `--perf-smoke` (`Scripts/Diagnostics/TerrainViewer.PerfSmoke.cs`), run windowed as `docs/performance.md` shows. It prints `PERF_<phase>` percentiles plus `_SECTION`, `_RENDER`, `_TILES`, `_DB`, `_MODELS` and `_GC` lines. Discard the first run after a build and compare medians of three. Extend it rather than writing a parallel probe.
- **Other smokes with timings:** `--water-smoke` and `--visual-smoke` print frame percentiles; run them with the `run-ac-world-edit` skill.
- **Godot monitors:** `Performance.GetMonitor(...)` (`TimeFps`, `TimeProcess`, draw calls, objects, primitives, video and texture memory, object and orphan node counts); `RenderingServer.ViewportSetMeasureRenderTime` for CPU/GPU render time (windowed only; headless uses a dummy renderer); `ViewportSetDebugDraw` (overdraw, wireframe) with a screenshot. The editor's Profiler and Visual Profiler can't be driven from the shell; suggest them as a manual follow-up when a GPU question needs them.
- **Own probes**, only where the above doesn't reach: `Stopwatch.GetTimestamp` around the section, reporting count, total, mean, p95 and max. `GC.CollectionCount(0..2)` and `GC.GetAllocatedBytesForCurrentThread()` for allocation-driven hitches. Probes in `_Process` must not allocate, and a disabled probe must cost nothing. Remove temporary probes before committing; a permanent one is a feature and is documented in the Diagnostics flag table of `viewer-runtime.md` and in `docs/performance.md`. Parsers in `Scripts/Assets/` are engine-free and can be timed from the xUnit project.
- **Database:** time each query as the code runs it (open, execute, read, map) and count rows. Use `EXPLAIN` / `EXPLAIN ANALYZE` read-only on the predicates the sources issue (map plus position range on `creature`/`gameobject`, path lookups, `IN (...)` lists). Look for N+1 reads, repeated identical reads a session cache could serve, over-wide `SELECT *`, per-row round trips one `IN (...)` read replaces, and results re-filtered in C#. Keep row limits and parameterization (`WorldReadSession.InList`, `@k0` parameters); never concatenate input into SQL. The connection is read-only with pooling and local infile off by design: don't change the connection string without asking. Schema or index changes go in the report as SQL for the user.

## What to look for

- **Main-thread hitches:** work over the per-frame budget; synchronous file or database waits; resources (meshes, materials, textures) that could be cached or shared; per-frame `GetNode`/string lookups; node-tree churn from adding and freeing many nodes at once.
- **Streaming:** scheduling order, idle gaps between one-at-a-time tasks, eviction thrash, hysteresis when the camera turns, duplicate decodes, cancelled work that still finishes expensive steps.
- **Rendering:** draw calls and material variants, `MultiMesh` use, per-chunk materials that could be shared, visibility ranges and culling, shadow cost, transparent overdraw, shader work, texture formats and mips, far-tile vertex counts.
- **Overlays:** queries re-issued when the region barely changed, regions too large or re-centered too eagerly, nodes built for far or hidden objects, per-object node and label cost, per-frame loops over all spawns that a grid or dirty flag removes.
- **Allocations and data structures:** LINQ, closures, string building, copies and boxing in per-frame or per-spawn loops; `Span<T>`, `ArrayPool<T>`, reused buffers in parsers; linear scans an index replaces; duplicate or unbounded caches.
- **Consistency:** spikes at tile boundaries, model batch completion, `WorldObjectScene.Reconcile`, overlay result landing, reload or map switch, GC. Report p50, p95, p99 and max; flatten the tail even if the mean doesn't move.

## Keep the balance

- Behavior stays the same: same pixels, same SQL results, same UI text, same draft and settings formats. For rendering changes, compare windowed `--client-shot` screenshots before and after and look at them.
- Keep the `generation`/cancellation guards, main-thread-only Godot access, row limits, the read-only guarantee and the eviction rules.
- A change under the noise floor is reverted. Prefer the simplest change that moves the number; explain a non-obvious one in a comment that says why.
- A changed pure function or data path gets a fast xUnit test asserting behavior (and, if useful, row or allocation counts), never wall-clock timing.

## Local wins vs overhauls

Do every optimization that is local and safe: a cache, a batched query, a reused buffer, a better schedule order, an upload spread across frames, a culling check, a shared material.

For a finding that needs an overhaul (a different streaming architecture, several decode workers, a persistent overlay cache, a shared spatial index, replacing node-per-spawn with `MultiMesh` or `RenderingServer` calls, schema or index changes, connection pooling), don't start the rewrite. Do the local, measurable part (a step toward it or an instrument that sizes it), commit it if it's a proven win, and describe the rest: what you measured, expected gain, design, touch points, risks and order. Add it to the findings in `docs/performance.md` and the "Performance" open work in `docs/tc-status.md`.

If a result is surprising or inconclusive, say so; don't claim a win you can't reproduce. Timings from parallel runs aren't comparable, so measure only in this session.

## Per step (in addition to the shared loop)

After the suite passes, re-run the measured scenario the same number of times and compare medians and tails; keep the change only if the gain is real and repeatable. Update the baseline table in `docs/performance.md` when a commit moves it.

Commit type: `perf(scope): summary`, with before/after numbers in the body.

## Report

Besides the shared report items: the scenarios measured and how; a before/after table per commit (p50, p95, p99, max frame or load time, draw calls, memory, query time and rows, as relevant); probes added and where they're documented; unfixed findings ranked by expected gain, overhaul-sized ones described as above; and any SQL index or schema change for the user to run.
