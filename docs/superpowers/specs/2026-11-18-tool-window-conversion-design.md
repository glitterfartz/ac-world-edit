# Tool-window conversion design

Date: 2026-11-18
Status: approved by the human partner; implementation not started.

## Intent and why

Every editor that behaves like a floating window in the viewer is a `PanelContainer` pinned
inside the main viewport (`ContentEditorWindow`). It has to draw its own title row, move itself by
mutating `Position`, keeps its size by re-pinning `Size` every frame, and shares the viewport with
the 3D world, so it cannot be moved off-monitor and it competes with world picking for input.
`tool-windows.md` already established the replacement: a `Window` subclass with a client-drawn
header, a scrolling body, embedded dialogs and a key shim (`ScriptEditorWindow`).

This change converts the base class once, so all five editor windows built on it become real
windows, rather than porting them one at a time and leaving two window models in the codebase.

## Goals

- `ContentEditorWindow : Window`; every derived editor (Behavior, Quest, Cinematics, Change
  History, Bookmarks, Creature Properties, GameObject Properties) opens as a real tool window
  using the `tool-windows.md` shape.
- The base owns the repeated chrome: draggable header, title/subtitle, close, scrolling body,
  action row, status row, resize grip, Escape shortcut, centered first open.
- Existing public APIs of the derived windows keep working (the viewer, the menus, the smokes and
  the tests call `Open(...)`, `CloseWindow()`, `Panel`, `Status`, `SmokeTitle`, and so on).

## Non-goals

- Converting docked HUD panels (`ContentBrowser`, `SceneSidebar`, `LeftPanel`, `StatusBar`) into
  windows. They are deliberately part of the viewer shell.
- Restyling native dialogs (`AcceptDialog`, `ConfirmationDialog`, `FileDialog`, `PopupMenu`,
  `RecordPicker`, tooltips). They are already `Window` subclasses.
- Moving status lines out of the embedded panels (`QuestPanel`, `BehaviorPanel`,
  `CinematicsPanel`), which own their own status row today.
- Changing draft, history, export or database behavior.

## Anti-goals

- A second window mechanism beside `ScriptEditorWindow`, or per-window copies of the chrome.
- Windows that grow to their content or refuse to shrink (the old `_Process` pin).
- Any window where Escape or the OS close button silently does nothing.

## Constraints

- `Window` is a `Viewport`: key events never reach the `Window` node. A full-rect `Control` child
  must route them (`tool-windows.md`, "Input and dialogs").
- The scroll container only stretches its child on axes with `SIZE_EXPAND`; the body needs both
  flags and `MinSize` must remain the floor.
- `ContentEditorWindow.ButtonIn`/`ButtonOf` are static helpers called by `ContentPanel` and by the
  TrinityCore views; they must survive the conversion.
- Godot-free layers are untouched; this is all under `Scripts/Ui` and `Scripts/Viewer/Hud`.

## Approaches considered

1. **Convert only `CinematicsEditorWindow`** (the original request). Small, but leaves the
   duplicated chrome and the frame-size hack for the other five windows.
2. **Extract the shared hooks into a helper and keep `ContentEditorWindow` a `PanelContainer` for
   the others.** Cleaner than a one-off but keeps two window models and two sets of chrome.
3. **Convert `ContentEditorWindow` itself to a `Window`.** Chosen. One model, one chrome, one set
   of rules; the five editors change shape together and `tool-windows.md` becomes true for all of
   them.

## The design

### Base class

`ContentEditorWindow : Window` is constructed as `(string name, string title)` and sets
`ForceNative`, `Transient`, `Borderless`, `GuiEmbedSubwindows`, `Theme`, `Visible = false`. In
`_Ready` it builds, before calling the new virtual `BuildEditor()`:

1. a `FullRect` `PanelContainer` with the client panel style;
2. a root `VBoxContainer`;
3. a header `HBoxContainer` whose left-click calls `StartDrag()`: a stacked titles box
   (`TitleLabel`, `SubtitleLabel`, both mouse-ignoring, horizontal `ExpandFill`) and a `✕` button;
4. an `HSeparator`;
5. a `ScrollContainer` (`ExpandFill` vertically) whose child is `Body`, a `VBoxContainer` with
   `ExpandFill` on **both** axes;
6. `Actions`, an `HFlowContainer` inside `Body`, then `StatusLabel` (`AutowrapMode.Off`, own row);
7. a bottom `HBoxContainer` aligned end with the `◢` handle calling
   `StartResize(BottomRight)`;
8. a full-rect `ToolWindowShortcuts`-style control (the renamed `ScriptEditorShortcuts`) whose
   `UnhandledKey` closes on Escape;
9. `CloseRequested += Close`.

`Size` and `MinSize` are set in `_Ready` from `TargetSize` and `MinimumSize` (virtual, default
`640×420`) so the subclass override is live. `Open()` centers once over the main viewport, then
shows and grabs focus. `Close()` hides; `CloseWindow()` is an alias kept for the viewer and the
smoke. `SetHeading(title, subtitle)` updates the labels and the OS `Title`.

Removed from the base: `_Process` size pinning, `DragHeader`/`EndDrag`/`dragOffset`,
`PlaceOnFirstOpen`, `AddHeader` and the `WindowHeader` record. Kept unchanged: `Connection`,
`Store`, `Drafts`, `status`, `LoadCatalog`/`EnsureCatalog`, `CommitEdits`, `ApplyHistory`,
`ReloadOnForeignChange`, `SyncHistoryButtons`, `ButtonOf`/`ButtonIn`.

`Report` is added to the base (`Action<string>?`), and `SetStatus(string)` writes the base
`StatusLabel`, shows or hides it, and calls `Report`. `EditorsWiring` sets
`Report = statusLog.Post` on the converted windows, so their status also reaches the viewer's
status bar. The embedded panels that own a status row (`QuestPanel`, `BehaviorPanel`,
`CinematicsPanel`) keep it and are not double-posted.

### Derived windows

Each `_Ready` override becomes `protected override void BuildEditor()`, drawing into `Body`.
Header buttons move into `Actions`; the hand-built `✕` is dropped; `PlaceOnFirstOpen(x, y)`
becomes `Open()`.

| Window | Content | Actions |
|---|---|---|
| `BehaviorEditorWindow` | panel fills the body | Undo, Redo |
| `QuestEditorWindow` | search column + panel split, expanding | Undo, Redo, New, Duplicate |
| `CinematicsEditorWindow` | search column + panel split, expanding | Undo, Redo |
| `PendingChangesWindow` | summary, select all, scrolling list | Apply, Revert, Preview, Export, Discard, Refresh, Delete old |
| `BookmarkWindow` | bookmark rows + status | Save location |
| `PropertiesWindow` | section picker + section host split, expanding | Undo, Redo |

`PropertiesWindow` keeps its abstract `Header()` tuple and calls `SetHeading(...)` from
`UpdateChrome()`; its `_UnhandledInput` Escape override is deleted (the base shim handles it) and
`_Process` no longer calls `base._Process`. The concrete properties windows drop
`CustomMinimumSize`/`MouseFilter` (not `Control`s) and set per-window `MinimumSize` instead.

`StatusLabel` is the base status row; `BookmarkWindow`, `PendingChangesWindow` and
`PropertiesWindow` bind their existing status text to it instead of keeping private labels.

### Decisions taken after the builder check

- **UI scale: accepted at 100%.** `SetUiScale` sets only the root window's `ContentScaleFactor`;
  a `Window` is its own viewport, so converted editors render at 100% just as
  `ScriptEditorWindow` does today. Scaling tool windows (and their embedded dialogs) is a
  follow-up, recorded in the docs; this change does not add a scale hook.
- **Diagnostics embed the windows.** A diagnostic run (`*-smoke` or `--client-shot*`, the
  convention `BookmarkStore` and `UserSettings` already use) constructs the editors with
  `ForceNative = false`, so `--behavior-smoke`'s `Input.ParseInputEvent` clicks and
  `--client-shot-*`'s `GetViewport().GetTexture()` captures keep working. Interactive runs still
  get native OS windows.
- **Goldens are updated in this change.** The human authorized `Tools/ui_golden.py --update` for
  `behavior:295`, `creature:15192,gossip` and `quest:1,overview` (plus the rewritten manifest).
  The plan runs those three after the code is green and reports the diffs.
- Headless gdUnit runs and the `--dump-target` UI dump also take the embedded path (headless has
  no native windows).

### Tests and docs

- Update `UiPartsTests` (its `TestWindow` and header test) to the new base chrome.
- Existing suites (`BookmarkWindowTests`, `ChangeHistoryWindowInteractionTests`,
  `CreaturePropertiesWindowTests`, `GameObjectPropertiesWindowTests`, `CinematicsWindowTests`)
  keep their flows; add layout regressions per `tool-windows.md`: open centers and shows, window
  growth expands the main pane, the action row wraps without widening the window, Escape and
  `CloseRequested` hide it.
- Update `tool-windows.md` (the older `ContentEditorWindow` paragraph is now false),
  `viewer-runtime.md` where it describes the editors, `Scripts/AGENTS.md` if it names the old
  base, and `docs/status/completed-work.md`.

## Left to the builder

- Exact per-window toolbar ordering and whether a low-value action moves into an overflow menu.
- Whether `PendingChangesWindow`'s seven actions stay a single flow row or split into two rows.
- Splitting the work into commits (base first, then each window) and the order of the tests.

## Appendix: floating UI audit (2026-11-18)

| Thing | Type | Verdict |
|---|---|---|
| `ScriptEditorWindow` | `Window` | template |
| `ContentEditorWindow` family | `PanelContainer` | converted by this design |
| `ContentBrowser` | `PanelContainer` | docked HUD, stays |
| `SceneSidebar`, `LeftPanel`, `StatusBar` | `PanelContainer` | HUD docks, stay |
| `SpawnInspector`, `VisibilityPreviewPanel`, overlay pages | `VBoxContainer` | inspector content |
| `TerrainMinimap` | `Control` | HUD widget |
| `selectionBox`, `ViewportDropSurface` | `Panel`/`Control` | drag rectangle and drop target |
| `RecordPicker` | `PopupPanel` (a `Window`) | already windowed, frameless |
| `AcceptDialog`/`ConfirmationDialog`/`FileDialog`/`PopupMenu`/tooltip | native `Window`s | already windowed |
