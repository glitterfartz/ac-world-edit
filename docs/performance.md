# Performance

Measurements, findings and recommendations for world and database streaming. The benchmark is the retail client with the TrinityCore database around Stormwind City; the 3.3.5/AzerothCore path shares the streaming, model and overlay code, so the same changes apply to it. Open items also appear in `docs/tc-status.md` under "Performance".

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
3. Cache the schema column list per connection identity, so overlay queries skip `information_schema`. Expected: about 18 ms less per query.
4. Terrain: decode the next tile while the current one uploads. Expected: tile latency down by the decode time (about 200–400 ms per tile).

Larger work (not started; each needs its own run):

- **Fewer draw calls (largest frame-time gain).** Merge a model's opaque surfaces that share an identical material into one surface at decode time (worker, pixel-identical); share one `ShaderMaterial` per material key instead of one per surface; build terrain as one mesh per tile with a texture array and a per-tile splat atlas instead of 256 chunk meshes. Expected: draw calls several times lower and render CPU roughly halved. Touches `ModelLibrary.AddSurface`, `MaterialPalette`, `TerrainRenderer` and `terrain.gdshader`. Risk: transparent surface order, per-surface UV/colour animation columns, picking and ground probes that read surfaces.
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

## Results

Filled in per change below.
