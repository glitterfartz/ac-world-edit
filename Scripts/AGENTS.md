# Scripts (the Godot app)

`ACWorldEdit.csproj` builds this tree as one Godot assembly. `Scripts/Assets` and `Scripts/Data` are *also* compiled into the xUnit project, so they must stay free of Godot types; everything else may use Godot.

## Layout and namespaces

| Folder | Namespace | Godot types |
|---|---|---|
| `Assets/` | `ACWorldEdit.Assets` | not allowed |
| `Data/` | `ACWorldEdit.Data` | not allowed |
| `Viewer/`, `Rendering/`, `Overlays/`, `Ui/`, `Diagnostics/` | `ACWorldEdit` (flat — subfolders add no segment) | allowed |

Two files under `Viewer/` keep `namespace ACWorldEdit.Assets` on purpose: `Session/AreaTable.cs` (`AreaLookup`) and `Lighting/ClientLightingView.cs` (`ClientLighting`) are the Godot halves of partial classes whose engine-free halves sit in `Assets/World`.

`Assets` must not reference `Data` or `Viewer`; `Data` must not reference `Viewer`. `Assets` and `Data` are the two Godot-free halves that the xUnit project compiles.

## Data access

- Reads go through `DBSource.Open(...)` → `IDBSource`, schema facts through `IDBSource.Schema` (`IWorldSchema`). `Ac*Source`/`Tc*Source` are constructed in `Data/` only.
- Dialect code lives in `Data/AzerothCore`, `Data/TrinityCore`, `Ui/AzerothCore`, `Ui/TrinityCore`: one folder per dialect per layer, not a copy under each feature.

## Ui

- Status and feedback go to `StatusBar` via `StatusLog`; overlays raise their `Report` (`Action<string>`) callback. Never print into the Inspector panel.
- An editor that should float as its own OS window is a `Window` subclass. The draft-backed editors share `Ui/Framework/ContentEditorWindow.cs`; `Ui/Scripting/ScriptEditorWindow.cs` is the reference for a standalone one: [`docs/architecture/tool-windows.md`](../docs/architecture/tool-windows.md) has the full shape, the pitfalls and the regressions.
  - The body lives in a `ScrollContainer` and needs `SizeFlagsVertical` **and** `SizeFlagsHorizontal` set to `ExpandFill`; the scroll container only stretches its child on axes the child expands on, so a horizontal-only body stays at its minimum and the canvas stops growing while the bottom pane floats mid-window.
  - Keep the scroll mode `AUTO` and the body scrolled rather than letting content drive the size; set the window floor with `MinSize` and never let a content minimum raise it.
  - Toolbar actions go in an `HFlowContainer`; labels stay `AutowrapMode.Off`; the status line gets its own row.
  - A `Window` is a `Viewport`: key events never reach the `Window` node, so a full-rect `Control` child (`ToolWindowShortcuts`) overrides `_UnhandledKeyInput`. Dialogs are children of the window (`GuiEmbedSubwindows = true`); a headless run refuses instead of prompting.
  - Layout tests set `Size`, await two `ProcessFrame`s, and assert the canvas grew and the bottom pane stayed at the bottom.
- Windows are built in code through `Ui/Framework` (`UiForm`, `MenuSheet`, dialogs, inspector card parts). Control creation order is pinned by `Tests/golden/ui-*.json`, so a builder that reorders nodes breaks a golden: rewrite one only with `python Tools/ui_golden.py --update`, and only when the change is intended.
- `Tests/ACWorldEdit.GdTests` must cover new UI flows (see `Tests/AGENTS.md`).

## Threading

Godot objects belong to the main thread. Tile decode, model decode and overlay queries run on workers that return plain data; results from an older map or client are discarded with a generation counter plus `CancellationTokenSource`. Never create a `Node` or `Resource` on a worker.

## Flags

`--terrain-smoke`, `--state-dump`, `--ui-dump`, `--dump-target=…`, `--client-shot*` and the rest are parsed in `Diagnostics/`; `docs/architecture/viewer-runtime.md` lists what each one prints.
