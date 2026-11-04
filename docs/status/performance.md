# Performance

How the renderer and the database streaming perform now, and how to measure them. Every number here
was taken windowed on one machine; treat it as a reference point, not a promise. Older runs, the changes
each one bought, and experiments that were reverted are in
[performance-history.md](performance-history.md). Open items also appear in
[open-work.md](open-work.md#performance).

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

## Open items

- **Re-baseline `--perf-smoke` on Forward+.** The streaming tables in the history page were measured on
  Compatibility; the settled-frame breakdown above is the only Forward+ measurement. New streaming
  numbers need their own run before comparing changes.
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
- **Shadow cost.** Measure with shadows off for small doodads and far terrain; any change alters pixels
  and needs the user's decision.
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
