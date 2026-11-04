# Performance history

Measured runs, what each change bought, and experiments that were reverted. The current method, frame
budget and open items are in [performance.md](performance.md); open work also appears in
[open-work.md](open-work.md#performance).

Every number here was taken windowed under the conditions named in its section. Compare rows within one
table, not across tables: the renderer, the renderer mode (Compatibility then Forward+) and the benchmark
changed between them.

## Baseline (commit e4af540, retail + TrinityCore, windowed, 16 threads)

Median of runs 2–3; times in ms. Measured on Compatibility (OpenGL 3.3), before the project moved to
Forward+.

| Phase | Frame p50 | p95 | p99 | max | Render CPU p50 | GPU p50 | `_Process` sum p50 | Duration |
|---|---|---|---|---|---|---|---|---|
| Arrival | 21.8 | 37.4 | 41.6 | 253-303 | 18-20 | 15-17 | ~2.5 | 65-66 s |
| Flight | 21.9 | 37.1 | 44.0 | 81-100 | 17.4 | 14.6 | ~2.2 | 22.1 s |

- Arrival decodes 1416 distinct models (22.8 s of worker decode time), uploads 4290 surfaces, binds 2624
  instances; the model cache reaches 1.9 GB (all live, so the 256 MiB soft limit never applies), video
  memory 1.6 GB, 10,300 nodes, 9,900 draw calls.
- Tiles take 380-420 ms from schedule to publish on arrival and 800-860 ms during flight.
- Region queries take 170-330 ms each. During the 22 s flight the creature and GameObject layers each
  start about 30 queries (one per 64-yard query-point cell).
- Main-thread spikes: the first creature model pass costs 215-221 ms (building the retail customization
  indexes on first humanoid lookup); GameObject publish/reconcile 6-15 ms; creature publish ~5 ms; model
  surface upload p99 4.5 ms, max 17 ms.

## Findings at that baseline, ranked by impact

These motivated the changes below. Several are fixed now; the current state is in
[performance.md](performance.md).

1. **Rendering dominates the frame.** `_Process` takes about 2.5 ms of a 22 ms frame; render CPU
   (culling and draw submission) is 17-20 ms and GPU 15-17 ms. Causes: one draw call per model surface
   per instance batch (every surface has its own `ShaderMaterial`), one `MeshInstance3D` and material per
   terrain chunk (256 per tile, up to 25 tiles loaded with the hysteresis ring), and shadow casting on
   every chunk, model and batch (each shadow cascade repeats the draws). Draw calls reach 9,900 on
   arrival.
2. **Model streaming throughput.** One decode worker that waits for the previous model to finish
   uploading, plus a frame of delay at each handoff, gives about 46 ms per model: 1416 models take 65 s,
   of which only 22.8 s is decode work.
3. **Unindexed spatial reads.** `creature` has only `idx_map`, so a Stormwind query visits all 54,508
   map-0 rows (151 ms in `EXPLAIN ANALYZE`); `gameobject` has no map index and scans its primary key
   (77,000 rows read). This is 70-80 % of every region query.
4. **First humanoid lookup hitch.** `RetailCreatureCatalog` builds four lazy indexes over large DB2
   tables on the main thread the first time a humanoid display is resolved (215-221 ms frame).
5. **Per-query schema read.** Every overlay query creates a new `WorldDatabase` through `DBSource.Open`,
   so its column cache is always empty: each query re-reads `information_schema.COLUMNS` (about 18 ms)
   and opens a new connection (about 20 ms, pooling is off).
6. **Tile pipeline gaps.** The next tile is not decoded until the current one has finished uploading (8
   chunks per frame, about 32 frames per tile), so decode and upload never overlap.
7. **Small steady main-thread costs.** `ModelLibrary.PruneInstances` walks every instance with two
   interop calls each frame (0.5 ms); GameObject `Populate` and creature `PopulateList` rebuild their
   sidebar lists on every query result (about 5 ms); the creature overlay rewrites `Body.Visible` and
   `Label.Position` for every ready batch member each frame.

Texture mipmap generation on the main thread was measured and is small (terrain 0.1 ms per texture,
model textures p95 0.7 ms, 2 ms for 1024² textures); it is not worth moving.

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

The upload budget (8 surfaces, 1.5 ms per frame) became the limit on arrival: the main thread spends
its whole model budget most frames, which costs about 1 ms of frame time while content loads. Flight
frames are slightly slower because 30 % more models finish during the same flight.

Tried and reverted: caching the schema column list per server and database (skipping the
`information_schema` read, about 18 ms by a temporary probe) did not move the measured query latency
(creature median 206 → 205 ms, GameObject 183 → 173 ms, both within run-to-run spread), so it was not
kept.

Tried and reverted: decoding the next terrain tile while the current one uploads. Arrival time did not
change (47.8 s; models are the limit) and per-tile latency from schedule to publish rose (arrival 377 →
624 ms, flight 806 → 1157 ms) because tiles are now scheduled before the upload slot is free. A fair
test needs a "terrain complete" time in the probe; until then the change is unproven.

Fewer draw calls (terrain layer groups and splat atlas, model batch merge, shared materials). Baseline:
`HEAD` 583a6a7 measured in the same session, median of two runs (a third froze and was closed). After:
one complete run (the others were stopped). Treat the after column as indicative until repeated.

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

Render CPU and GPU time roughly halved and video memory did not change. The draw-call monitor samples
one frame at the end of each phase and fell less than frame time did. Tiles publish 4–6 times sooner
because a tile uploads its atlas and 20–40 group meshes instead of 512 small textures and 256 meshes.
The `HEAD` baseline is slower than the e4af540 baseline above (same code path), so compare rows within
this table only. A before/after `--client-shot-overlays` capture of Goldshire shows identical terrain
and models.

Model memory and parallel model decode (Forward+, retail, windowed, same machine and session). "Before"
is the code at the start of the change (two runs; flight frame times from the second). "Memory only" is
one run with the memory change and one decode worker. The two- and three-worker columns are two runs
each, taken with nothing else running. A first set of three-worker runs was discarded because a game was
running during the benchmark.

| Measure | Before | Memory only | 2 workers | 3 workers |
|---|---|---|---|---|
| Arrival duration | 30.3 s | 30.3 s | 21.1–21.9 s | 19.1–19.3 s |
| Arrival decode work (summed over workers) | 23.0–23.2 s | 23.3 s | 28.2–29.0 s | 33.6–34.0 s |
| Model cache after arrival | 1903 MiB | 318 MiB | 318 MiB | 318 MiB |
| Flight frame p50 / p95 / p99 | 9.7 / 15.8 / 19.7 ms | 9.5 / 16.8 / 21.5 ms | 10.0–10.1 / 16.5–17.1 / 20.7–21.4 ms | 9.8 / 16.1–16.5 / 20.2–21.4 ms |
| Flight models decoded in 22 s | 373–375 | 360 | 390–395 | 396 |
| Flight evictions | 388–392 | 159 | 200–208 | 204–212 |
| Model cache after flight | 1674 MiB | 420 MiB | 427–433 MiB | 428 MiB |
| Video memory after flight | 1,790–1,900 MiB | 2,197 MiB | 2,206–2,211 MiB | 2,212–2,255 MiB |

- A published model now keeps only its doodads, animation IDs and (for a WMO) batch positions, indices
  and liquid types (`ModelData.ForProbes`); decoded BLP pixels, palette pixels and vertex channels are
  dropped after upload. The cache fell by 83 %, and the 256 MiB limit now covers only models no instance
  uses, so it applies (`dormantMiB` reaches 255 during a flight).
- Flight evictions halved but did not vanish: the dormant limit is full while flying, so models behind
  the camera still leave and come back. A larger limit trades memory for fewer rebuilds.
- Video memory in flight rose by 300–400 MiB because dormant meshes now stay on the GPU until evicted.
- Three decode workers cut arrival by 37 % against one. Each decode runs about 45 % slower while three
  run together (the CASC read lock, allocation and memory bandwidth are shared), so the gain is 1.6×
  rather than 3×. The workers are idle about 40 % of arrival, which points at the upload cap and the
  decoded-queue limit as the next constraints.
- Frame times during the flight stay within run-to-run spread of the baseline with two or three workers.
- `ParallelModelDecodeCorpusTests` decodes about 60 retail creature models serially and on four threads
  and compares hashes of the output (textures, palettes, merged geometry).
