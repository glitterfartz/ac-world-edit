# Performance

How the renderer and the database streaming perform now, and how to measure them. Every number here
was taken windowed on one machine; treat it as a reference point, not a promise. Older runs, the changes
each one bought, and experiments that were reverted are in
[performance-history.md](performance-history.md). Open items also appear in
[editor-plan.md](../plans/editor-plan.md#performance).

## How to measure

`--perf-smoke` (`Scripts/Diagnostics/ViewerShell.PerfSmoke.cs`) is the streaming benchmark. Run it
windowed (headless uses a dummy renderer, so uploads and draws cost nothing):

```powershell
$env:ACWORLD_CLIENT_PROFILE = "retail"
& "$env:GODOT_BIN".Replace(".exe", "_console.exe") --path . -- --perf-smoke
```

It opens Eastern Kingdoms (map 0), turns VSync off, puts the camera over the Trade District (tile 30,48)
and records three phases:

- **ARRIVAL**: from the teleport until every wanted tile, model and overlay query has finished (the
  "lag" of arriving in the city).
- **FLIGHT**: a fixed path at 70 yd/s (Trade District, Cathedral Square, the Keep, the main gate; tiles
  29-31, 47-48), positioned by wall time so runs are comparable.
- **SETTLE**: until streaming is idle again.

Each phase prints `PERF_<phase>` frame-time percentiles, `_SECTION` main-thread time per `_Process` step,
`_RENDER` CPU and GPU render time (`RenderingServer.ViewportGetMeasuredRenderTime*`), `_TILES` tile
latency (decode scheduled to tile published), `_DB` region-query latency per overlay, `_MODELS`
decode/upload/bind/eviction counts, and `_GC` collections, node count, draw calls and video memory.
`_MODELS` also prints `dormantMiB` (estimated size of models no instance uses) and `cascLockMs(total)`,
the time all threads have held the CASC read lock since the process started (it includes tile decodes,
so it bounds the serialized share of model decode from above). It ends with `PERF_SMOKE_PASSED`.
Discard the first run after a build (shader cache) and compare medians of three.

`--perf-breakdown` (`Scripts/Diagnostics/ViewerShell.PerfBreakdown.cs`) is the static benchmark: parks
the camera at five Stormwind poses once streaming is idle and measures what each scene category costs.
It is described under [Current frame budget](#current-frame-budget-forward).

## Current frame budget (Forward+)

The desktop renderer is Forward+ (Godot's default; `project.godot` only overrides the mobile renderer to
Compatibility). The settled Stormwind frame is 8.5-13 ms and CPU-bound. The numbers below come from one
`--perf-breakdown` run on Godot 4.7.1 (Vulkan), an RTX 5080, retail client, windowed, with a cold shader
cache; differences under about 1.5 ms are noise. The two all-on rows at the Keep differ by 2.5 ms.

At each pose it measures 90 frames (after 20 warm-up frames) with every scene category on, then with one
category hidden, with one category's shadow casting off, and with the sun's shadows off. A category's
cost is its delta from the all-on frame. Deltas are not additive (hiding a building reveals terrain
behind it); the "everything hidden" row is the floor. Run it windowed:

```powershell
$env:ACWORLD_CLIENT_PROFILE = "retail"
& "$env:GODOT_BIN".Replace(".exe", "_console.exe") --path . -- --perf-breakdown
```

It prints `PERFB_POSE` (tiles loaded), `PERFB_COUNT` (nodes, geometry nodes, surfaces and drawn copies per
category), `PERFB_WMO` (the heaviest buildings) and one `PERFB` row per pose and configuration: median
frame time, render CPU and GPU time, and draw calls, triangles and objects for the main pass (`Vis`) and
the shadow passes (`Shadow`).

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

What is loaded at the Trade District pose (9 tiles; the counts grow to 16 tiles by the main gate): 528
terrain group meshes, 53 WMO shell nodes (1,074 merged surfaces), 1,613 embedded-doodad nodes (2,415
surfaces, 8,420 placements), 836 ADT doodad nodes (1,545 surfaces, 7,996 placements), 90 creature models
and 37 GameObject models. The heaviest buildings each carry 400-1,440 embedded doodad placements and
35-106 shell surfaces (for example `fdid:2320850` has 1,440 doodads and `fdid:321999` has 106 surfaces
and 1,179 doodads).

Findings:

1. **On Forward+ the settled frame is 8.5-13 ms and CPU-bound.** Render GPU time is only 2-2.6 ms, so the
   GPU is nowhere near the limit; the cost is culling and draw submission for 2,400-4,400 main-pass
   draws plus 3,000-4,700 shadow-pass draws.
2. **Buildings dominate: shells, their embedded doodads and ADT doodads each cost 2.5-5 ms** (hiding any
   one of them removes 25-45 % of the frame). Together they are nearly all of the 10 ms above the floor.
   Terrain costs about 1 ms; liquid, creature and GameObject models, markers and labels are 0-1.5 ms
   (within noise, except creature models at the overview).
3. **Shadows are about 30 % of the frame.** Turning the sun's four-split shadows off saves 3-3.7 ms at
   four of five poses. The shadow passes draw as many objects as the main pass and 1.5-2× its triangles
   (7.4 M against 3.4 M at the Trade District). Of the casters, WMO shells (1-2.4 ms) and WMO embedded
   doodads (0.8-1.2 ms) matter most; terrain casting shows no measurable cost.
4. **Draws are not the whole story.** Triangle counts are high (2.7-4.3 M in the main pass), and hiding
   ADT doodads cuts main-pass triangles by 1.1 M at the Trade District, so part of the cost is geometry,
   not only the draw count.
5. **Limits of this run.** One cold-cache run, 90 frames per configuration, a fixed pitch, one time of
   day. The skybox row measured nothing (no sky model was loaded at these poses).

## Dynamic sun shadows

The custom cascade pass (see [rendering.md](../architecture/rendering.md#lighting-and-shadows))
adds `shadow:<quality>` rows (receiver only; the camera is still, so the maps are already rendered)
and `shadow:<quality>(caster)` rows that force a map redraw every frame. The second is the
moving-camera cost; the first is what a settled frame pays. Measured with the retail client on Godot
4.7.2 / Vulkan / RTX 5080, windowed, cold shader cache, same five poses and 90 measured frames:

| Pose | Off | Low (caster) | Medium (caster) | High (caster) |
|---|---|---|---|---|
| Trade District | 5.96 | 7.28 (+1.32) | 8.62 (+2.66) | 9.31 (+3.35) |
| Cathedral Square | 4.48 | 5.53 (+1.05) | 6.78 (+2.30) | 7.61 (+3.13) |
| Keep | 7.07 | 7.85 (+0.78) | 9.03 (+1.96) | 9.73 (+2.66) |
| Main gate | 8.27 | 9.86 (+1.59) | 10.65 (+2.38) | 11.92 (+3.65) |
| Overview | 5.41 | 6.53 (+1.12) | 7.57 (+2.16) | 8.31 (+2.90) |

The still rows stay within noise (the largest difference is 0.55 ms at the Keep's Medium row), so the
per-fragment receiver is effectively free. Per-cascade caster CPU is 0.3-0.6 ms and GPU 0.1-1.0 ms;
the frame delta above is mostly the four viewports' culling and submission. The first version set
`SubViewport.Size` and rebound the map global on every redraw and cost about 25 ms per forced frame;
guarding those assignments (the size changes only on a quality change, the `ViewportTexture` binding
once per viewport) brought it down to the numbers above.

**Shipped default: Low.** It adds 0.8-1.6 ms while moving, about 9-14 % of the recorded 8.5-13 ms
retail frame, and its single 1024 map over 100 yards is enough for nearby buildings. Medium adds about
2-2.7 ms (18-27 %) and High about 2.7-3.7 ms (24-37 %), so both stay menu choices; Off is the other
extreme. The throughput lever, if a future default wants more coverage without more redraws, is to
redraw one cascade per frame in round-robin while moving.

**Detail casters (creatures and M2 GameObjects).** `--perf-breakdown` prints `PERFB_PROXIES` per pose
(total, animated, creature and GameObject proxies) and adds `noShadow:creatureModels`,
`noShadow:gameObjectModels` (hide that category's proxies and force a redraw) and `shadow:Low(creature)`
(force a redraw every frame; skipped with `PERFB_SKIP` when no detail proxies exist). The
`PERFB_COUNT` detail-mesh counts rise because a detailed entry uploads separate opaque, skinned and
alpha-tested bucket meshes instead of one chain. Measured windowed on Godot 4.7.2 / Vulkan / RTX 5080
with `--shadow-quality=High`; the main-viewport frame times are lower than the earlier table because the
custom pass draws in the cascade `SubViewport`s, which the main viewport's render time does not include
(the same reason the `noShadow:*` rows show little change: they measure the main viewport, not the
cascade viewports):

| Pose | Proxies (animated/creature/GameObject) | Low still | Low forced redraw | Δ per redraw | `shadow:Low(creature)` |
|---|---|---|---|---|---|
| Trade District | 473 (167/126/41) | 2.98 | 4.29 | +1.31 | 4.31 |
| Cathedral Square | 398 (75/54/21) | 2.27 | 2.68 | +0.41 | 2.63 |
| Keep | 486 (86/71/15) | 3.49 | 3.90 | +0.41 | 3.96 |
| Main gate | 463 (24/22/2) | 3.19 | 3.68 | +0.49 | 3.67 |
| Overview | 439 (0/0/0) | 2.69 | 3.03 | +0.34 | skipped |

At Low's 20 Hz the animated refresh costs 20 × Δ, about 7-26 ms per second (0.7-2.6 % of a second); at
Medium/High's 15 Hz it is about 5-20 ms per second. The `shadow:Low(creature)` row matches the
forced-caster row within noise, so the detail casters add no separate redraw cost beyond the cascade
pass at these poses. The animated rate is a cap, not a guarantee: when the frame rate is below
`AnimatedRefreshHz`, the maps redraw every frame.

## Open items

- **Re-run `--perf-smoke` after the shadow changes.** The main streaming tables in the history page were
  measured on Compatibility; the model-memory and parallel-decode table is Forward+, but a fresh streaming
  run is still needed before comparing changes.
- **Upload budget.** With three decode workers the workers are idle about 40 % of arrival (34 s of
  decode work over 19 s of arrival), so the 8-surface, 1.5 ms per-frame upload cap or the decoded-queue
  limit (three entries, 256 MiB) now gates arrival. Not measured yet. Moving the numerics-to-Godot array
  conversion onto the decode worker is the first candidate.
- **Building cost.** WMO shells, their embedded doodads and ADT doodads are most of the frame. Candidate
  steps, none implemented: cull small embedded doodads at a shorter distance than the 1,600 yd used for
  every `MultiMesh` (and stop them casting shadows at range); reduce `DirectionalShadowMaxDistance`
  (400) or the four splits; stop WMO shells casting shadows beyond the nearest cascade; cull per
  building part, since a whole WMO (shell plus every embedded doodad) is drawn whenever any part is
  visible. Each changes pixels and needs the user's decision.
- **Sun-shadow tuning.** The custom pass costs what the table above shows. Open levers: redraw one
  cascade per frame while moving, tighten the caster distance per cascade, and measure `shadow:Low`
  and `shadow:Medium` in an ordinary flight rather than the forced-single-view loop. Each changes
  pixels or smoothness and needs the user's decision.
- **Spawn table indexes.** `creature` (map index only) and `gameobject` (no map index) are scanned for
  every region query (150 ms of about 200 ms). The app cannot add indexes; the SQL is below for the
  operator to run.
- **Connection per query.** Pooling is off by design; each overlay query opens a connection (about 20
  ms). A persistent read connection would need the user's decision.

Indexes for the operator to run on their own server (not applied by the app):

```sql
ALTER TABLE world.creature   ADD INDEX idx_map_pos (map, position_x, position_y);
ALTER TABLE world.gameobject ADD INDEX idx_map_pos (map, position_x, position_y);
-- AzerothCore equivalents
ALTER TABLE acore_world.creature   ADD INDEX idx_map_pos (map, position_x, position_y);
ALTER TABLE acore_world.gameobject ADD INDEX idx_map_pos (map, position_x, position_y);
```
