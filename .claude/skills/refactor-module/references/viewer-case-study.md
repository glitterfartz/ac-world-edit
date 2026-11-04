# Case study: the viewer refactor (R0–R7)

This is the run the `refactor-module` skill is modeled on. It is useful as a scale reference: how
much can move while six goldens stay byte-identical, and where the honest limits are.

## Starting point

```
Scripts/Viewer: 4,033 non-blank lines, 21 files
ViewerShell.cs: 1,096 lines
xUnit: 1,177    gdUnit4: 23
No deterministic capture of terrain/UI/lighting/export.
```

## Milestones and what each one proved

| Phase | Commit | Change | Proof |
|---|---|---|---|
| R0 | `test(viewer): add the refactor regression net` | `--state-dump`, `--ui-dump`, `Tools/goldens.py`, `smoke_golden.py`, `state_golden.py`, `refactor_gate.py`, six goldens, hash manifest, ratchets | gate green on the untouched tree |
| R1.0 | `refactor(viewer): rename TerrainViewer to ViewerShell` | 34 partials, scene root, docs, UI goldens | renames only; UI tree identical |
| R1.1 | `refactor(viewer): group viewer files by responsibility` | Session/Streaming/Interaction/Lighting/Hud/Export/Geometry + `.uid` | moves only; all goldens |
| R1.2 | `TileGrid`, `SqlExportBatch`, static `Prepare`, `ClientLighting` split | pure logic to Assets/Foundation | 12 new xUnit; state/lighting goldens |
| R2 | `UiForm`, `MenuSheet`, `Dialogs`, headers, card parts | shared UI construction | 10 gdUnit4; UI tree identical |
| R3 | `BuildUi` → LeftPanel/EditorsWiring/Layout/Toolbar/ClientPrompts | `ViewerShell.Ui.cs` removed | UI tree identical |
| R4 | `OpenMapAtServerPose`, `ProbeTerrain`, overlays list, `SessionState` | one shared path; stale-button fix | state/ui goldens; reload assertion |
| R5 | `TerrainStreamer`, `ViewerInput`, `ViewerSession` | standalone classes + host seams | 6 goldens; gizmo/reload smokes |
| R6 | `LightingSampler`, `LightingBlend` | clock/light selection and all published bands in Assets | 18 xUnit; five-hour state goldens |
| R7 | docs, UID tracking, final gate, merge | `master`, tag `refactor-complete` | full gate |

## Final measurement

```
Scripts/Viewer: 4,081 non-blank (3,466 code, 615 comments), 38 files
ViewerShell.cs: 283
Largest file: 297
Long-method allowlist: []
xUnit: 1,214    gdUnit4: 33
Six goldens: unchanged at every phase
```

The original target was 3,200 non-blank lines. The class seams cost ~266 lines, so the target was
retired by agreement and the measured 4,081 recorded as the budget. This is the clearest lesson of
the run: **line targets are proxies; behaviour and named boundaries are the real goal.** When they
conflict, measure both and ask, do not delete comments.

## What made it safe

1. The goldens were captured before the first rename and hash-locked. `--update` was never run in
   CI; a changed golden failed the gate.
2. Every move kept `.uid` files with their scripts and changed no `.tscn` path except the one rename.
3. Pure decisions went to `Assets`/`Foundation` and gained xUnit tests *after* the behaviour was
   already pinned by goldens.
4. A behaviour fix (the empty-map button in `OpenMap`'s `finally`) was its own commit with a
   reload-smoke assertion, not folded into a refactor.
5. The final split was `advance → sample → publish`: arithmetic in `LightingSampler`/`LightingBlend`,
   Godot publication in `SkyLighting`.

## What not to copy

- Splitting code into partial files and calling it a class extraction. The reviewer will ask for
  the standalone class and its seam.
- Treating the gate as a substitute for reading the moved code. It catches behavior drift, not a
  confusing abstraction.
- Chasing a folder number after the shape is right.
