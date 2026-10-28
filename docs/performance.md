# Performance

Measurements, findings and recommendations for world and database streaming. The breakdown below was measured on Forward+; the streaming results further down were taken on Compatibility (OpenGL 3.3) before the project switched. The benchmark is the retail client with the TrinityCore database around Stormwind City; the 3.3.5/AzerothCore path shares the streaming, model and overlay code, so the same changes apply to it. Open items also appear in `docs/tc-status.md` under "Performance".

## How to measure

`--perf-smoke` (`Scripts/Diagnostics/TerrainViewer.PerfSmoke.cs`) is the benchmark. Run it windowed (headless uses a dummy renderer, so uploads and draws cost nothing):

```powershell
$env:ACWORLD_CLIENT_PROFILE = "retail"
& "$env:GODOT_BIN".Replace(".exe", "_console.exe") --path . -- --perf-smoke
```

It opens Eastern Kingdoms (map 0), turns VSync off, puts the camera over the Trade District (tile 30,48) and records three phases:

- **ARRIVAL**: from the teleport until every wanted tile, model and overlay query has finished (the "lag" of arriving in the city).
- **FLIGHT**: a fixed path at 70 yd/s (Trade District, Cathedral Square, the Keep, the main gate; tiles 29–31, 47–48), positioned by wall time so runs are comparable.
- **SETTLE**: until streaming is idle again.

Each phase prints `PERF_<phase>` frame-time percentiles, `_SECTION` main-thread time per `_Process` step, `_RENDER` CPU and GPU render time (`RenderingServer.ViewportGetMeasuredRenderTime*`), `_TILES` tile latency (decode scheduled to tile published), `_DB` region-query latency per overlay, `_MODELS` decode/upload/bind/eviction counts, and `_GC` collections, node count, draw calls and video memory. It ends with `PERF_SMOKE_PASSED`. Discard the first run after a build (shader cache) and compare medians of three.

`--perf-breakdown` is the second benchmark: static poses, per-category cost. See "Where a settled Stormwind frame goes" below.

## Baseline (commit e4af540, retail + TrinityCore, windowed, 16 threads)

Median of runs 2–3; times in ms.

| Phase | Frame p50 | p95 | p99 | max | Render CPU p50 | GPU p50 | `_Process` sum p50 | Duration |
|---|---|---|---|---|---|---|---|---|
| Arrival | 21.8 | 37.4 | 41.6 | 253–303 | 18–20 | 15–17 | ~2.5 | 65–66 s |
| Flight | 21.9 | 37.1 | 44.0 | 81–100 | 17.4 | 14.6 | ~2.2 | 22.1 s |

- Arrival decodes 1416 distinct models (22.8 s of worker decode time), uploads 4290 surfaces, binds 2624 instances; the model cache reaches 1.9 GB (all live, so the 256 MiB soft limit never applies), video memory 1.6 GB, 10,300 nodes, 9,900 draw calls.
- Tiles take 380–420 ms from schedule to publish on arrival and 800–860 ms during flight.
- Region queries take 170–330 ms each. During the 22 s flight the creature and GameObject layers each start about 30 queries (one per 64-yard query-point cell).
- Main-thread spikes: the first creature model pass costs 215–221 ms (building the retail customization indexes on first humanoid lookup); GameObject publish/reconcile 6–15 ms; creature publish ~5 ms; model surface upload p99 4.5 ms, max 17 ms.

## Findings, ranked by impact

1. **Rendering dominates the frame.** `_Process` takes about 2.5 ms of a 22 ms frame; render CPU (culling and draw submission) is 17–20 ms and GPU 15–17 ms. Causes: one draw call per model surface per instance batch (every surface has its own `ShaderMaterial`), one `MeshInstance3D` and material per terrain chunk (256 per tile, up to 25 tiles loaded with the hysteresis ring), and shadow casting on every chunk, model and batch (each shadow cascade repeats the draws). Draw calls reach 9,900 on arrival.
2. **Model streaming throughput.** One decode worker that waits for the previous model to finish uploading, plus a frame of delay at each handoff, gives about 46 ms per model: 1416 models take 65 s, of which only 22.8 s is decode work.
3. **Unindexed spatial reads.** `creature` has only `idx_map`, so a Stormwind query visits all 54,508 map-0 rows (151 ms in `EXPLAIN ANALYZE`); `gameobject` has no map index and scans its primary key (77,000 rows read). This is 70–80 % of every region query.
4. **First humanoid lookup hitch.** `RetailCreatureCatalog` builds four lazy indexes over large DB2 tables on the main thread the first time a humanoid display is resolved (215–221 ms frame).
5. **Per-query schema read.** Every overlay query creates a new `WorldDatabase` through `DBSource.Open`, so its column cache is always empty: each query re-reads `information_schema.COLUMNS` (about 18 ms) and opens a new connection (about 20 ms, pooling is off).
6. **Tile pipeline gaps.** The next tile is not decoded until the current one has finished uploading (8 chunks per frame, about 32 frames per tile), so decode and upload never overlap.
7. **Small steady main-thread costs.** `ModelLibrary.PruneInstances` walks every instance with two interop calls each frame (0.5 ms); GameObject `Populate` and creature `PopulateList` rebuild their sidebar lists on every query result (about 5 ms); the creature overlay rewrites `Body.Visible` and `Label.Position` for every ready batch member each frame.

Texture mipmap generation on the main thread was measured and is small (terrain 0.1 ms per texture, model textures p95 0.7 ms, 2 ms for 1024² textures); it is not worth moving.

## Recommendations

Local changes (in order):

1. Model pipeline: start the next decode as soon as a decoded model enters the upload slot, and let the worker decode a short nearest-first queue back to back, so decode never waits on frames. Still one worker. Expected: arrival bounded by decode time (about 25–30 s instead of 65 s).
2. Build the retail creature catalog's customization indexes on the client-open worker. Expected: removes the 215 ms hitch.
3. Cache the schema column list per connection identity, so overlay queries skip `information_schema`. Expected: about 18 ms less per query (tried; no measurable change, see Results).
4. Terrain: decode the next tile while the current one uploads. Expected: tile latency down by the decode time (about 200–400 ms per tile). Tried; not proven, see Results.

5. Fewer draw calls (done, see Results). Terrain draws one mesh per distinct layer list per tile (about 20–40 instead of 256 chunk meshes) over a per-tile RGBA splat atlas, sampled so it matches the old per-chunk maps; the decode worker merges a model's opaque batches that share every material input (`SurfaceMerge`); unskinned, unanimated batches share one `ShaderMaterial` per material key. The planned per-tile texture array was not built: it would copy every ground texture into each of up to 25 loaded tiles and force mixed-size textures to one size, while grouping by layer list keeps textures shared and pixels unchanged.

Larger work (not started; each needs its own run):

- **Renderer.** The project moved from Compatibility (OpenGL 3.3) to Forward+ and the frame improved (the change is uncommitted in `project.godot`). The `--perf-smoke` tables below predate it; re-baseline them on Forward+ before comparing new changes.
- **Building cost.** WMO shells, their embedded doodads and ADT doodads are most of the frame on Forward+, and shadows add about 30 %; the breakdown below lists the candidates.
- **Shadow cost.** Measure with shadows off for small doodads and far terrain; any change alters pixels and needs the user's decision.
- **Parallel model decode.** Two or three decode workers (CASC reads are serialized; BLP decode and palette baking are not). Expected: arrival another 1.5–2× faster. Risk: memory spikes, ordering, cancellation.
- **Model memory.** 1.9 GB of CPU-side model data stays alive because every entry keeps its `ModelData` and decoded BLP pixels after upload. Dropping the pixel copies after upload (keeping what ground probes and liquids need) would cut most of it.
- **Database:** add spatial indexes (below); consider a persistent read connection instead of a connection per query (pooling is off by design; needs the user's decision).

Indexes for the user to run on their own server (not applied by the app):

```sql
ALTER TABLE world.creature   ADD INDEX idx_map_pos (map, position_x, position_y);
ALTER TABLE world.gameobject ADD INDEX idx_map_pos (map, position_x, position_y);
-- AzerothCore equivalents
ALTER TABLE acore_world.creature   ADD INDEX idx_map_pos (map, position_x, position_y);
ALTER TABLE acore_world.gameobject ADD INDEX idx_map_pos (map, position_x, position_y);
```

## Where a settled Stormwind frame goes (Forward+)

`--perf-breakdown` (`Scripts/Diagnostics/TerrainViewer.PerfBreakdown.cs`) parks the camera at five fixed poses (Trade District, Cathedral Square, the Keep, the main gate, and a high overview) once streaming is idle. At each pose it measures 90 frames (after 20 warm-up frames) with every scene category on, then with one category hidden, with one category's shadow casting off, and with the sun's shadows off. A category's cost is its delta from the all-on frame. Deltas are not additive (hiding a building reveals terrain behind it); the "everything hidden" row is the floor. Run it windowed:

```powershell
$env:ACWORLD_CLIENT_PROFILE = "retail"
& "$env:GODOT_BIN".Replace(".exe", "_console.exe") --path . -- --perf-breakdown
```

It prints `PERFB_POSE` (tiles loaded), `PERFB_COUNT` (nodes, geometry nodes, surfaces and drawn copies per category), `PERFB_WMO` (the heaviest buildings) and one `PERFB` row per pose and configuration: median frame time, render CPU and GPU time, and draw calls, triangles and objects for the main pass (`Vis`) and the shadow passes (`Shadow`).

Measured on Godot 4.7.1 with **Forward+ (Vulkan)** on an RTX 5080, retail client, windowed, **one run** (the first after a build, so the shader cache was cold; a second run was cancelled). Frame time in ms; render CPU time is about 60 % of it. Differences under about 1.5 ms are noise: the two all-on rows at the Keep differ by 2.5 ms.

| Configuration | Trade District | Cathedral Sq. | Keep | Main gate | Overview |
|---|---|---|---|---|---|
| All on | 11.1 | 8.5 | 11.1 | 13.0 | 9.1 |
| All on (repeat) | 11.1 | 8.9 | 13.6 | 13.1 | 8.3 |
| Everything hidden (floor) | 1.1 | 1.1 | 1.1 | 1.1 | 1.0 |
| Hide terrain | 10.2 | 7.9 | 9.7 | 11.3 | 7.6 |
| Hide liquid | 11.3 | 8.7 | 10.6 | 13.3 | 9.0 |
| Hide WMO shells | 7.7 | 6.0 | 7.4 | 10.0 | 6.0 |
| Hide WMO embedded doodads | 7.3 | 5.8 | 6.4 | 8.2 | 5.7 |
| Hide ADT doodads | 8.3 | 6.8 | 7.9 | 10.3 | 6.4 |
| Hide creature models | 10.5 | 7.9 | 10.7 | 13.8 | 8.2 |
| Hide GameObject models | 11.2 | 8.5 | 11.0 | 14.2 | 8.4 |
| Hide creature/GameObject markers and labels | 11.2 | 8.8 | 11.6 | 13.6 | 8.2 |
| WMO shells stop casting shadows | 9.9 | 7.8 | 10.6 | 12.1 | 7.1 |
| WMO embedded doodads stop casting shadows | 10.0 | 7.8 | 10.3 | 12.2 | 8.1 |
| ADT doodads stop casting shadows | 10.8 | 8.2 | 10.5 | 12.5 | 7.9 |
| Terrain stops casting shadows | 11.1 | 8.7 | 11.3 | 13.2 | 8.4 |
| Sun shadows off | 7.4 | 5.2 | 8.2 | 9.7 | 6.2 |

Main-pass versus shadow-pass work at the all-on frame (medians):

| Pose | Main draws | Main triangles | Shadow draws | Shadow triangles | Render GPU |
|---|---|---|---|---|---|
| Trade District | 3,295 | 3.4 M | 4,658 | 7.4 M | 2.5 ms |
| Cathedral Square | 2,384 | 2.7 M | 4,314 | 6.6 M | 2.0 ms |
| Keep | 3,708 | 3.4 M | 3,015 | 4.2 M | 2.3 ms |
| Main gate | 4,439 | 4.3 M | 3,504 | 5.3 M | 2.6 ms |
| Overview | 2,805 | 2.8 M | 2,792 | 4.1 M | 1.9 ms |

What is loaded at the Trade District pose (9 tiles; the counts grow to 16 tiles by the main gate): 528 terrain group meshes, 53 WMO shell nodes (1,074 merged surfaces), 1,613 embedded-doodad nodes (2,415 surfaces, 8,420 placements), 836 ADT doodad nodes (1,545 surfaces, 7,996 placements), 90 creature models and 37 GameObject models. The heaviest buildings each carry 400-1,440 embedded doodad placements and 35-106 shell surfaces (for example `fdid:2320850` has 1,440 doodads and `fdid:321999` has 106 surfaces and 1,179 doodads).

Findings:

1. **On Forward+ the settled frame is 8.5-13 ms and CPU-bound.** Render GPU time is only 2-2.6 ms, so the GPU is nowhere near the limit; the cost is culling and draw submission for 2,400-4,400 main-pass draws plus 3,000-4,700 shadow-pass draws.
2. **Buildings dominate: shells, their embedded doodads and ADT doodads each cost 2.5-5 ms** (hiding any one of them removes 25-45 % of the frame). Together they are nearly all of the 10 ms above the floor. Terrain costs about 1 ms; liquid, creature and GameObject models, markers and labels are 0-1.5 ms (within noise, except creature models at the overview).
3. **Shadows are about 30 % of the frame.** Turning the sun's four-split shadows off saves 3-3.7 ms at four of five poses. The shadow passes draw as many objects as the main pass and 1.5-2x its triangles (7.4 M against 3.4 M at the Trade District). Of the casters, WMO shells (1-2.4 ms) and WMO embedded doodads (0.8-1.2 ms) matter most; terrain casting shows no measurable cost.
4. **Draws are not the whole story.** Triangle counts are high (2.7-4.3 M in the main pass), and hiding ADT doodads cuts main-pass triangles by 1.1 M at the Trade District, so part of the cost is geometry, not only the draw count. Whether a distance cut-off on small embedded doodads or lower-detail meshes pay off needs a separate run; both change what is drawn.
5. **Limits of this run.** One cold-cache run, 90 frames per configuration, a fixed pitch, one time of day. The skybox row measured nothing (no sky model was loaded at these poses). The `--perf-smoke` arrival and flight tables below were measured on Compatibility, so they are not comparable with this table.

Candidate next steps, none implemented: cull small embedded doodads at a shorter distance than the 1,600 yd used for every `MultiMesh` (and stop them casting shadows at range); reduce `DirectionalShadowMaxDistance` (400) or the four splits; stop WMO shells casting shadows beyond the nearest cascade; cull per building part, since a whole WMO (shell plus every embedded doodad) is drawn whenever any part is visible. Each changes pixels and needs the user's decision.

## Results

Medians of three windowed runs; baseline as above.

| Change | Measure | Before | After |
|---|---|---|---|
| Model decode queue (decode overlaps upload, batches of 4) | Arrival duration | 66.1 s | 47.3 s |
| | Arrival frame p50 / p95 / p99 | 21.8 / 37.4 / 41.6 ms | 23.7 / 37.8 / 42.4 ms |
| | Flight: models decoded in 22 s | 246 | 320 |
| | Flight frame p50 / p95 / p99 | 21.6 / 36.7 / 42.6 ms | 23.2 / 37.9 / 42.4 ms |
| | `ModelLibrary.Update` p50 (arrival) | 1.8 ms | 2.7 ms |

| Retail customization indexes built on the client-open worker | Arrival creature-overlay step max | 216–221 ms | 24–29 ms |
| | Arrival frame max | 253–303 ms | 70–101 ms |

The upload budget (8 surfaces, 1.5 ms per frame) is now the limit on arrival: the main thread spends its whole model budget most frames, which costs about 1 ms of frame time while content loads. Flight frames are slightly slower because 30 % more models finish during the same flight.

Tried and reverted: caching the schema column list per server and database (skipping the `information_schema` read, about 18 ms by a temporary probe) did not move the measured query latency (creature median 206 → 205 ms, GameObject 183 → 173 ms, both within run-to-run spread), so it was not kept.

Tried and reverted: decoding the next terrain tile while the current one uploads. Arrival time did not change (47.8 s; models are the limit) and per-tile latency from schedule to publish rose (arrival 377 → 624 ms, flight 806 → 1157 ms) because tiles are now scheduled before the upload slot is free. A fair test needs a "terrain complete" time in the probe; until then the change is unproven.

Fewer draw calls (terrain layer groups and splat atlas, model batch merge, shared materials). Baseline: `HEAD` 583a6a7 measured in the same session, median of two runs (a third froze and was closed). After: one complete run (the others were stopped). Treat the after column as indicative until repeated.

| Measure | Before | After |
|---|---|---|
| Arrival duration | 57.7 s | 40.9 s |
| Arrival frame p50 / p95 / p99 | 25.8 / 50.1 / 59.9 ms | 16.8 / 38.8 / 50.2 ms |
| Arrival render CPU / GPU p50 | 20.5 / 16.2 ms | 12.9 / 9.6 ms |
| Arrival draw calls / nodes (last frame) | 9,877 / 10,343 | 8,005 / 8,567 |
| Arrival surfaces uploaded | 4,290 | 3,472 |
| Arrival tile latency p50 | 622–668 ms | 136 ms |
| Flight frame p50 / p95 / p99 | 32.3 / 56.0 / 67.4 ms | 17.6 / 34.2 / 46.0 ms |
| Flight render CPU / GPU p50 | 26.2 / 22.2 ms | 13.8 / 10.8 ms |
| Flight draw calls (last frame) | 3,865 | 3,159 |
| Flight tile latency p50 | 1,068–1,247 ms | 199 ms |
| Flight models decoded in 22 s | 257–270 | 346 |
| Video memory (arrival / flight) | 1,645 / 1,510–1,519 MiB | 1,648 / 1,527 MiB |

Render CPU and GPU time roughly halved and video memory did not change. The draw-call monitor samples one frame at the end of each phase and fell less than frame time did. Tiles publish 4–6 times sooner because a tile uploads its atlas and 20–40 group meshes instead of 512 small textures and 256 meshes. The `HEAD` baseline is slower than the e4af540 baseline above (same code path), so compare rows within this table only. A before/after `--client-shot-overlays` capture of Goldshire shows identical terrain and models.
