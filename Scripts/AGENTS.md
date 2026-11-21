# Scripts (the Godot app)

`ACWorldEdit.csproj` builds this tree as the Godot app. The reusable halves live in `src/`: `ACWorldEdit.Core` and `ACWorldEdit.ClientData` are Godot-free (the xUnit project references them), and `ACWorldEdit.ClientRender` uses Godot but must not declare Godot script classes (`partial` types deriving from `Node`). `Scripts/Data` is Godot-free and source-linked into the xUnit project; everything else under `Scripts/` may use Godot.

## Layout and namespaces

| Folder | Namespace | Godot types |
|---|---|---|
| `Data/` | `ACWorldEdit.Data` | not allowed |
| `Viewer/`, `Rendering/` (thumbnails), `Overlays/`, `Ui/`, `Diagnostics/` | `ACWorldEdit` (flat — subfolders add no segment) | allowed |
| `src/ACWorldEdit.Core/` | `ACWorldEdit` | not allowed |
| `src/ACWorldEdit.ClientData/` | `ACWorldEdit.Assets`, `ACWorldEdit.ClientData` | not allowed |
| `src/ACWorldEdit.ClientRender/` | `ACWorldEdit` (flat) | allowed, no script classes |

The Godot views of the pure lighting model are extension methods in `src/ACWorldEdit.ClientRender/Lighting/LightVisuals.cs` (`LightBand.At`, `LightRegion.ViewPosition`); the model itself stays in `src/ACWorldEdit.ClientData/Assets/World`.

`Scripts/Data` must not reference `Scripts/Viewer` or `Scripts/Ui`; the libraries must never reference the app. `ClientData` and `Core` are the Godot-free halves the xUnit project references, and `Scripts/Data` is the Godot-free source it compiles.

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
