# Tool-window conversion implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Convert `ContentEditorWindow` and its six editor windows from a `PanelContainer` pinned in the main viewport into the `Window` tool-window template documented in `docs/architecture/tool-windows.md`.

**Architecture:** The base class becomes `Window` and owns the chrome (draggable header, scrolling body, action row, status row, resize grip, Escape shim, centered first open). Each derived editor overrides `BuildEditor()` and draws into `Body`, putting its actions in `Actions`. Diagnostic runs construct embedded windows so existing click/capture smokes keep working.

**Tech Stack:** Godot 4.7, C# (`net8.0`), gdUnit4, xUnit.

**Spec:** `docs/superpowers/specs/2026-11-18-tool-window-conversion-design.md`

## Global Constraints

- `Foundation/`, `Scripts/Assets/` and `Scripts/Data/` stay Godot-free; this change touches only `Scripts/Ui`, `Scripts/Viewer/Hud`, `Scripts/Diagnostics`, `Tests` and `docs`.
- 4-space indent, LF, file-scoped namespaces (`.editorconfig`).
- Status text says what happened; never write status into the Inspector.
- `ContentEditorWindow.ButtonIn`/`ButtonOf` stay static and public (called from `ContentPanel` and the TrinityCore views).
- Commit format `type(scope): summary`, imperative, ≤72 chars, no AI attribution.
- Do not run `Tools/refactor_gate.py` unless the user asks; the golden update in Task 7 is separately authorized.
- UI scale: tool windows render at 100%; do **not** add a scale hook in this change.
- Diagnostic runs (`*-smoke`, `--client-shot*`, `--ui-dump`, `--state-dump`, `--dump-target=*`) construct embedded windows (`ForceNative = false`).

## Review Focus

- **A window cannot be made smaller than its content.** Expected: `MinSize` is the floor and the body scrolls. Pinned by `ResizingBelowTheContentKeepsTheMinimumAndScrolls` (Task 2).
- **Escape does nothing, or closes a different window.** Expected: the focused window hides only itself. Pinned by `EscapeHidesTheFocusedWindowOnly` (Task 2).
- **The action row widens the window.** Expected: actions wrap inside the window width. Pinned by `ManyActionsWrapWithoutWideningTheWindow` (Task 2).
- **Status text vanishes or is posted twice.** Expected: `SetStatus` shows the window row and calls `Report` exactly once. Pinned by `SetStatusFillsTheRowAndReportsOnce` (Task 2).
- **A dialog opened from a window lands behind it.** Expected: dialogs are children of the window and embedded. Pinned by `DialogsAreChildrenOfTheirWindow` (Task 3).

---

### Task 1: Convert the base class and the mechanical subclass surface

**Files:**
- Modify: `Scripts/Ui/Framework/ContentEditorWindow.cs` (whole class)
- Create: `Scripts/Ui/Framework/ToolWindowShortcuts.cs`
- Delete: `Scripts/Ui/Scripting/ScriptEditorShortcuts.cs` (and move the class; keep the `ScriptEditorWindow` behavior)
- Modify: `Scripts/Ui/Scripting/ScriptEditorWindow.cs:89`
- Modify: `Scripts/Ui/Behavior/BehaviorEditorWindow.cs`
- Modify: `Scripts/Ui/Quest/QuestEditorWindow.cs`
- Modify: `Scripts/Ui/Cinematics/CinematicsEditorWindow.cs`
- Modify: `Scripts/Ui/Scene/PendingChangesWindow.cs`
- Modify: `Scripts/Viewer/Hud/BookmarkWindow.cs`
- Modify: `Scripts/Ui/Properties/PropertiesWindow.cs`
- Modify: `Scripts/Ui/CreatureProperties/CreaturePropertiesWindow.cs`
- Modify: `Scripts/Ui/GameObjectProperties/GameObjectPropertiesWindow.cs`
- Modify: `Tests/ACWorldEdit.GdTests/UiPartsTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `protected ContentEditorWindow(string name, string title)`
  - `public VBoxContainer Body { get; }`, `public HFlowContainer Actions { get; }`
  - `public Label TitleLabel { get; }`, `public Label SubtitleLabel { get; }`, `public Button CloseButton { get; }`, `public Label StatusLabel { get; }`
  - `protected virtual void BuildEditor()`
  - `public void Open()`, `public void Close()`, `public void CloseWindow()`
  - `public void SetHeading(string title, string? subtitle = null)`
  - `public void SetStatus(string message)`, `public Action<string>? Report { get; set; }`
  - `protected virtual Vector2 TargetSize => new(1000, 700);`
  - `protected virtual Vector2I MinimumSize => new(640, 420);`
  - `public static bool DiagnosticRun { get; }` (on `ContentEditorWindow`; true for headless runs, `*-smoke`, `--client-shot*`, `--ui-dump`, `--state-dump`, `--dump-target=*`)
  - `ToolWindowShortcuts : Control` with `Func<InputEvent, bool>? UnhandledKey`

- [ ] **Step 1: Write the base frame.**

  Replace `ContentEditorWindow : PanelContainer` with `Window`. Constructor signature `protected ContentEditorWindow(string name, string title)` sets `Name`, `Title`, `Visible = false`, `Theme = ClientStyle.Theme()`, `ForceNative = !DiagnosticRun`, `Transient = true`, `Borderless = true`, `GuiEmbedSubwindows = true`. `DiagnosticRun` is a static property that is true when `DisplayServer.GetName() == "headless"` or `OS.GetCmdlineUserArgs()` holds a `-smoke` suffix or a `--client-shot`, `--ui-dump`, `--state-dump`, `--dump-target` prefix.

  `_Ready()` builds, in this order: `FullRect` `PanelContainer` with `ClientStyle.Panel()`; root `VBoxContainer`; header `HBoxContainer` with `StartDrag()` on left press, a stacked titles `VBoxContainer` (`TitleLabel`, `SubtitleLabel`, both `MouseFilter = Ignore`, `SizeFlagsHorizontal = ExpandFill`) and `CloseButton` (`✕`, tooltip `Close (Escape)`, `Pressed += Close`); `HSeparator`; `ScrollContainer { SizeFlagsVertical = ExpandFill }`; `Body = new VBoxContainer { SizeFlagsHorizontal = ExpandFill, SizeFlagsVertical = ExpandFill }`; `Actions = new HFlowContainer()` and `StatusLabel = ClientStyle.Text("", 12, ClientStyle.Faded)` with `AutowrapMode.Off`, both added to `Body`; a bottom `HBoxContainer { Alignment = End }` with the `◢` label calling `StartResize(BottomRight)`; `AddChild(new ToolWindowShortcuts { UnhandledKey = HandleShortcut })`; `CloseRequested += Close`; then `MinSize = MinimumSize; Size = (Vector2I)TargetSize; BuildEditor();`.

  `HandleShortcut` returns true for `Key.Escape` when `Visible` and calls `Close()`.

- [ ] **Step 2: Add the base methods.**

  `Open()`: on first call set `Position = GetTree().Root.Position + (GetTree().Root.Size - Size) / 2`; then `Visible = true; GrabFocus();`. `Close() => Visible = false;`. `CloseWindow() => Close();`. `SetHeading(title, subtitle)`: set both labels, hide `SubtitleLabel` when subtitle is empty or null, set `Title`. `SetStatus(message)`: `status = message; StatusLabel.Text = message; StatusLabel.Visible = message.Length > 0; Report?.Invoke(message);`.

  Remove `dragOffset`, `DragHeader`, `EndDrag`, `_Process`, `PlaceOnFirstOpen`, `AddHeader` and the `WindowHeader` record. Keep `Connection`, `Store`, `Drafts`, `status`, `LoadCatalog`, `EnsureCatalog`, `ButtonOf`, `ButtonIn`, `CommitEdits`, `ApplyHistory`, `ReloadOnForeignChange`, `SyncHistoryButtons`.

- [ ] **Step 3: Move the key shim.**

  Create `Scripts/Ui/Framework/ToolWindowShortcuts.cs` with the current `ScriptEditorShortcuts` body, renamed `ToolWindowShortcuts`. Delete `ScriptEditorShortcuts.cs` and its `.uid`; update `ScriptEditorWindow.cs:89` to `new ToolWindowShortcuts`. Do not hand-edit `.cs.uid` files.

- [ ] **Step 4: Mechanically convert each subclass.**

  For every subclass: constructor becomes `base(name, title)`; `public override void _Ready()` becomes `protected override void BuildEditor()`; the local `root` is `Body`; `AddHeader(root, title)` / `AddHeader(root, title, subtitle)` becomes `SetHeading(title, subtitle)`; `PlaceOnFirstOpen(x, y)` becomes `Open()`; remove `Size = TargetSize`; remove the local `✕` button and its tooltip (the base owns it).

  Specifics:
  - `BehaviorEditorWindow`: `base("BehaviorEditor", "Behavior")`; `Sync()` calls `SetHeading(Panel.Title)`.
  - `QuestEditorWindow`: `base("QuestEditor", "Quest Editor")`; keep `Panel = new AcQuestPanel()`.
  - `CinematicsEditorWindow`: `base("CinematicsEditor", "Cinematics")`; `Attach(CinematicsDialects.For(Connection().Dialect))` stays in `BuildEditor` (the object initializer has run before `_Ready`).
  - `PendingChangesWindow`: `base("PendingChanges", "Change History")`; declare `public new void Open()` and have it call `base.Open()` instead of `MoveToFront()`; delete the local `SetStatus` so the base one is inherited.
  - `BookmarkWindow`: `base("Bookmarks", "Bookmarks")`; declare `public new void Open()` and have it call `base.Open()` then `Rebuild()`; `SyncStatus()` calls `SetStatus(status)`.
  - `PropertiesWindow`: `base(name, "")`; `BuildFrame` uses `var root = Body;` (no `AddChild(root)`), no longer calls `AddHeader`, and drops its own `HSeparator` (the base frame has one); delete the local `title`, `subtitle` and `statusLabel` fields and the `root.AddChild(statusLabel)` call; delete `_UnhandledInput`; `_Process` no longer calls `base._Process`; `Show(...)` calls `Open()`; delete `CloseWindow()`; `UpdateChrome()` calls `SetHeading(title, subtitle)` and `SetStatus(Shell.Status)` using the base labels.
  - `CreaturePropertiesWindow` / `GameObjectPropertiesWindow`: `_Ready` → `BuildEditor`; delete `CustomMinimumSize`, `MouseFilter` and `Size = TargetSize`; override `MinimumSize => new Vector2I(760, 520)`.

- [ ] **Step 5: Update `UiPartsTests`.**

  Replace `TestWindow.Header(...)` with a test subclass that overrides `BuildEditor` and exposes the built labels; rewrite `HeaderKeepsTheTitleAndOptionalSubtitleInTheDraggableRow` to spawn the window into the tree, call `SetHeading("Quest Editor")` and assert `TitleLabel.Text`, `SubtitleLabel.Visible == false`, and `Title`; then `SetHeading("", "")` and assert both labels are set and the subtitle row is visible.

- [ ] **Step 6: Build and run the existing UI suite.**

  Run: `dotnet build ACWorldEdit.csproj -p:NuGetAudit=false`
  Expected: 0 errors.

  Run: `dotnet test Tests/ACWorldEdit.GdTests -c Debug -p:NuGetAudit=false`
  Expected: all suites pass (no new cases yet).

- [ ] **Step 7: Commit.**

```bash
git add -A Scripts/Ui Scripts/Viewer/Hud Tests/ACWorldEdit.GdTests/UiPartsTests.cs
git commit -m "refactor(ui): make editor windows real Window subclasses"
```

---

### Task 2: Pin the base window contract

**Files:**
- Create: `Tests/ACWorldEdit.GdTests/ToolWindowTests.cs`
- Modify: `Scripts/Ui/Framework/ContentEditorWindow.cs` only if a test exposes a layout defect.

**Interfaces:**
- Consumes: Task 1's `Open`, `Close`, `Body`, `Actions`, `StatusLabel`, `Report`, `SetStatus`, `MinimumSize`.
- Produces: `ToolWindowTests` fixtures used by later tasks' regression checks.

- [ ] **Step 1: Write the failing tests.**

```csharp
[TestSuite]
[RequireGodotRuntime]
public partial class ToolWindowTests
{
    sealed partial class Probe : ContentEditorWindow
    {
        readonly int actionCount;

        public Probe(int actions = 0) : base("Probe", "Probe") => actionCount = actions;

        protected override void BuildEditor()
        {
            // Actions and Body exist by the time BuildEditor runs.
            for (int i = 0; i < actionCount; i++)
            {
                Actions.AddChild(new Button { Text = "Action " + i });
            }
        }
    }

    static Probe Spawn(int actions = 0)
    {
        var window = AutoFree(new Probe(actions))!;
        ((SceneTree)Engine.GetMainLoop()).Root.AddChild(window);
        window.Open();
        return window;
    }

    static async Task Frames(int count = 2)
    {
        var tree = (SceneTree)Engine.GetMainLoop();
        for (int i = 0; i < count; i++)
        {
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        }
    }
}
```

  Tests:
  - `OpeningCentersShowsAndFocuses`: assert `Visible`, `Position == main.Position + (main.Size - Size) / 2` (or within 1 px), `HasFocus()`.
  - `CloseRequestedAndEscapeHideTheWindow`: `EmitSignal(Window.SignalName.CloseRequested)`, assert hidden; reopen, `window.PushInput(new InputEventKey { Keycode = Key.Escape, Pressed = true })`, `await Frames()`, assert hidden. (`Window` inherits `Viewport.PushInput`, which reaches the shim's `_UnhandledKeyInput`.)
  - `EscapeHidesTheFocusedWindowOnly`: spawn two probes, focus the second, send Escape through its viewport, assert second hidden and first still visible.
  - `GrowingTheWindowExpandsTheBody`: record `Body.Size.Y`, set `window.Size = new Vector2I(1400, 1000)`, `await Frames()`, assert `Body.Size.Y > before + 100`.
  - `ResizingBelowTheContentKeepsTheMinimumAndScrolls`: set `window.Size = new Vector2I(320, 240)`, `await Frames()`, assert `window.Size.X >= window.MinSize.X` and `window.Size.Y >= window.MinSize.Y`.
  - `ManyActionsWrapWithoutWideningTheWindow`: spawn with 10 actions, await frames, assert `Actions.GetCombinedMinimumSize().X < 1200` and `window.Size.X <= window.MinSize.X + 600`.
  - `SetStatusFillsTheRowAndReportsOnce`: `int reported = 0; window.Report = _ => reported++; window.SetStatus("Saved.")`; assert `StatusLabel.Text == "Saved."`, visible, `reported == 1`; `SetStatus("")` hides it.

- [ ] **Step 2: Run them to verify they fail for the expected reason.**

  Run: `dotnet test Tests/ACWorldEdit.GdTests -c Debug -p:NuGetAudit=false --filter "FullyQualifiedName~ToolWindowTests"`
  Expected: compile (Task 1 API exists) then failures that expose real chrome defects, or all pass if Task 1 is correct.

- [ ] **Step 3: Fix only the defects the tests expose** in `ContentEditorWindow` (layout flags, grip wiring, shim routing). Do not add per-window code here.

- [ ] **Step 4: Re-run.**

  Run the same command; expected: PASS.

- [ ] **Step 5: Commit.**

```bash
git add Tests/ACWorldEdit.GdTests/ToolWindowTests.cs Scripts/Ui/Framework/ContentEditorWindow.cs
git commit -m "test(ui): pin the tool-window chrome contract"
```

---

### Task 3: Toolbars and status for Behavior and Bookmarks

**Files:**
- Modify: `Scripts/Ui/Behavior/BehaviorEditorWindow.cs`
- Modify: `Scripts/Viewer/Hud/BookmarkWindow.cs`
- Modify: `Tests/ACWorldEdit.GdTests/BookmarkWindowTests.cs`

**Interfaces:**
- Consumes: Task 1–2 base API.
- Produces: `BehaviorEditorWindow` actions `Undo`, `Redo` in `Actions`; `BookmarkWindow` `BookmarkSave` in `Actions` and a status row fed by `SetStatus`.

- [ ] **Step 1: Move the actions.**

  `BehaviorEditorWindow.BuildEditor`: after wiring `Panel`, add `Undo` and `Redo` buttons to `Actions`, then `Body.AddChild(Panel)`. `BookmarkWindow.BuildEditor`: add `BookmarkSave` to `Actions`, then `Body.AddChild(body)`.

- [ ] **Step 2: Route status.**

  `BookmarkWindow.Rebuild()`/`OpenAt` call `SetStatus(status)`; delete `statusLabel` and `SyncStatus`.

- [ ] **Step 3: Add the layout assertion.**

  In `BookmarkWindowTests`, after `window.Open()`: await two frames; assert `window.Actions.GetCombinedMinimumSize().X > 0`, `window.StatusLabel.Visible` is true when `SmokeStatus` is non-empty, and the window's `MinSize` is the floor. Add `DialogsAreChildrenOfTheirWindow` to `ToolWindowTests`: `Dialogs.Info(window, "T", "X")`; assert the dialog's parent is the window.

- [ ] **Step 4: Run.**

  Run: `dotnet test Tests/ACWorldEdit.GdTests -c Debug -p:NuGetAudit=false --filter "FullyQualifiedName~BookmarkWindowTests|FullyQualifiedName~ToolWindowTests"`
  Expected: PASS.

- [ ] **Step 5: Commit.**

```bash
git add Scripts/Ui/Behavior/BehaviorEditorWindow.cs Scripts/Viewer/Hud/BookmarkWindow.cs Tests/ACWorldEdit.GdTests
git commit -m "refactor(ui): put behavior and bookmark actions in the toolbar"
```

---

### Task 4: Toolbars and bodies for Quest and Cinematics

**Files:**
- Modify: `Scripts/Ui/Quest/QuestEditorWindow.cs`
- Modify: `Scripts/Ui/Cinematics/CinematicsEditorWindow.cs`
- Modify: `Tests/ACWorldEdit.GdTests/CinematicsWindowTests.cs`

**Interfaces:**
- Consumes: Tasks 1–2.
- Produces: both windows keep `Panel`, `Open(uint)`, `Open(SmartParamKind,uint)`, `ShowTab`, `Load`, `NewItem`, `Search`, `ResultCount`, `CurrentId`; actions in `Actions`; the search/panel split is the expanding `Body` child.

- [ ] **Step 1: Move the actions.**

  Quest: `Undo`, `Redo`, `+ New quest`, `Duplicate` into `Actions`; keep `duplicate` as a field for `Sync()`. Cinematics: `Undo`, `Redo` into `Actions`.

- [ ] **Step 2: Make the split the expanding pane.**

  In both `BuildEditor`s, the `split` `HBoxContainer` gets `SizeFlagsVertical = ExpandFill` and is added to `Body`; the search column keeps `CustomMinimumSize = new(220 or 230, 0)`; the panel keeps its own expansion.

- [ ] **Step 3: Add layout assertions.**

  In `CinematicsWindowTests`, add `GrowingTheWindowExpandsTheSearchAndPanel`: `window.Open(0); await frames; float before = window.Panel.Size.Y; window.Size = new Vector2I(1400, 1000); await frames; assert window.Panel.Size.Y > before + 100; assert window.Body.Size.X > 0`. Add the same shape for Quest to the quest suite if it exists; otherwise extend `ToolWindowTests` with a `QuestEditorWindow` spawn and the same assertion.

- [ ] **Step 4: Run.**

  Run: `dotnet test Tests/ACWorldEdit.GdTests -c Debug -p:NuGetAudit=false --filter "FullyQualifiedName~CinematicsWindowTests|FullyQualifiedName~Quest|FullyQualifiedName~ToolWindowTests"`
  Expected: PASS.

- [ ] **Step 5: Commit.**

```bash
git add Scripts/Ui/Quest/QuestEditorWindow.cs Scripts/Ui/Cinematics/CinematicsEditorWindow.cs Tests/ACWorldEdit.GdTests
git commit -m "refactor(ui): put quest and cinematics actions in the toolbar"
```

---

### Task 5: Toolbars, status and bodies for Change History and Properties

**Files:**
- Modify: `Scripts/Ui/Scene/PendingChangesWindow.cs`
- Modify: `Scripts/Ui/Properties/PropertiesWindow.cs`
- Modify: `Tests/ACWorldEdit.GdTests/ChangeHistoryWindowInteractionTests.cs`
- Modify: `Tests/ACWorldEdit.GdTests/CreaturePropertiesWindowTests.cs`
- Modify: `Tests/ACWorldEdit.GdTests/GameObjectPropertiesWindowTests.cs`

**Interfaces:**
- Consumes: Tasks 1–2.
- Produces: `PendingChangesWindow.SetStatus` is the inherited base method; `PropertiesWindow` heading/status come from the base; the section split expands.

- [ ] **Step 1: Move the Change History actions.**

  `Actions`: `Apply selected`, `Revert selected`, `Preview…`, `Export selected…`, `Discard staged…`, `Refresh`, `Delete`, `Show all worlds`, `exportKind`. `Body`: `summary`, `selectAll`, the scrolling list. Delete the local `footer` and `options` containers.

- [ ] **Step 2: Move the Properties actions.**

  `Actions`: `Undo`, `Redo`. `Body`: the section picker + content split; the content VBox keeps `ExpandFill` both axes. `UpdateChrome()` calls `SetHeading(title, subtitle)` and `SetStatus(Shell.Status)` (the base labels replace the deleted local fields from Task 1).

- [ ] **Step 3: Add layout/status assertions.**

  Add to `ChangeHistoryWindowInteractionTests`: after `Spawn()`, `window.SetStatus("Applied.")`, assert `window.StatusLabel.Text == "Applied."`. Add `PropertiesWindow` growth: spawn `CreaturePropertiesWindow`, `window.Open(null)`, await frames, record the section picker height, set `Size` larger, await frames, assert the picker grew. In `GameObjectPropertiesWindowTests`, assert the same for its split.

- [ ] **Step 4: Run.**

  Run: `dotnet test Tests/ACWorldEdit.GdTests -c Debug -p:NuGetAudit=false --filter "FullyQualifiedName~ChangeHistoryWindowInteractionTests|FullyQualifiedName~CreaturePropertiesWindowTests|FullyQualifiedName~GameObjectPropertiesWindowTests"`
  Expected: PASS.

- [ ] **Step 5: Commit.**

```bash
git add Scripts/Ui/Scene/PendingChangesWindow.cs Scripts/Ui/Properties/PropertiesWindow.cs Tests/ACWorldEdit.GdTests
git commit -m "refactor(ui): put history and properties actions in the toolbar"
```

---

### Task 6: Wire the shell and verify diagnostics embedding

**Files:**
- Modify: `Scripts/Viewer/Hud/EditorsWiring.cs`
- Verify (no edit expected): `Scripts/Diagnostics/ViewerShell.BehaviorSmoke.cs`, `Scripts/Diagnostics/ViewerShell.ClientSmoke.cs`
- Modify if needed: `Scripts/Ui/Framework/ContentEditorWindow.cs` (the `Diagnostic` predicate)

**Interfaces:**
- Consumes: `Report` from Task 1.
- Produces: every converted window has `Report = statusLog.Post`; diagnostic runs use embedded windows.

- [ ] **Step 1: Wire `Report`.**

  In `BuildEditors`, add `Report = statusLog.Post` to the initializers for `creatureProps`, `gameObjectProps`, `bookmarkWindow`, `areaBehavior`, `questEditor` and `cinematicsEditor`; `pendingWindow` is created without an initializer, so set `pendingWindow.Report = statusLog.Post` after construction.

- [ ] **Step 2: Verify the diagnostic predicate.**

  Run the build. Then run a diagnostic smoke that opens an editor, for example `python launch_viewer.py --behavior-smoke` when a database/client are configured. Expected: `BEHAVIOR_SMOKE_PASSED` and `artifacts/client-shot-*` editor PNGs still show the editor content. If clicks or captures miss the editor, the `DiagnosticRun` predicate in `ContentEditorWindow` is not matching the run; extend it with the exact flag the run uses (print `OS.GetCmdlineUserArgs()` if unsure).

- [ ] **Step 3: Run the default suites.**

  Run: `dotnet test Tests/ACWorldEdit.GdTests -c Debug -p:NuGetAudit=false`
  Expected: PASS.

  Run: `dotnet test Tests/ACWorldEdit.Tests -p:NuGetAudit=false`
  Expected: PASS (Godot-free layers unaffected).

- [ ] **Step 4: Commit.**

```bash
git add Scripts/Viewer/Hud/EditorsWiring.cs
git commit -m "refactor(ui): report editor status through the viewer status bar"
```

---

### Task 7: Docs and goldens

**Files:**
- Modify: `docs/architecture/tool-windows.md`
- Modify: `docs/architecture/viewer-runtime.md`
- Modify: `docs/status/completed-work.md`
- Modify: `docs/plans/scripting-plan.md` (historical notes only)
- Modify: `Tests/golden/ui-behavior-295-wrath.json`, `Tests/golden/ui-creature-15192-gossip-wrath.json`, `Tests/golden/ui-quest-1-overview-wrath.json`, `Tests/golden/.sha256` (via the tool)

**Interfaces:**
- Consumes: the finished code.
- Produces: docs that describe the new base and no stale "pinned `PanelContainer`" statements.

- [ ] **Step 1: Rewrite the stale statements.**

  `tool-windows.md`: replace the opening paragraph that calls `ContentEditorWindow` "the older ... `PanelContainer`" with a statement that `ContentEditorWindow` is the converted base and `ScriptEditorWindow` is its largest client; rename every `ScriptEditorShortcuts` mention to `ToolWindowShortcuts` (snippet, prose at the "Input and dialogs" and "Regressions" sections). `docs/architecture/scripting.md:164`: rename the shim. `viewer-runtime.md:226`: the editor base is a `Window` (chrome, scroll body, embedded dialogs), not "drag, size pin"; rename the shim at line 233. `completed-work.md` rows that describe the shell/behaviour windows: note they are native tool windows. In `scripting-plan.md`, correct the three notes at lines 160, 683 and 1053 that say `ContentEditorWindow` pins `TargetSize`/is SQL-draft-specific, marking them as superseded. In `Scripts/AGENTS.md`, rename the shim in the `Window`/`Viewport` bullet.

- [ ] **Step 2: Update the goldens (authorized by the user).**

  Run, with the database/client environment configured:

```bash
python Tools/ui_golden.py --target behavior:295 --profile wrath --update
python Tools/ui_golden.py --target creature:15192,gossip --profile wrath --update
python Tools/ui_golden.py --target quest:1,overview --profile wrath --update
```

  Expected: each writes the golden and the manifest. Read the diffs; the only expected change is that the editor window node rows become absent from the Control list while their descendants remain. The static snapshots `ui-wrath.json`, `ui-retail.json`, `state-wrath.json`, `state-retail.json` and `ui-creature-15192-usedby-wrath.json` are not checked by the refactor gate; leave them unchanged in this change.

- [ ] **Step 3: Verify no stale names remain.**

  Run: `grep -rn "PlaceOnFirstOpen\|AddHeader\|ScriptEditorShortcuts\|size pin\|PanelContainer pinned" Scripts Tests docs --include='*.cs' --include='*.md'`
  Expected: no hits outside historical notes that explicitly say they are superseded.

- [ ] **Step 4: Build and run the full UI suite once more.**

  Run: `dotnet build ACWorldEdit.csproj -p:NuGetAudit=false`
  Run: `dotnet test Tests/ACWorldEdit.GdTests -c Debug -p:NuGetAudit=false`
  Expected: PASS.

- [ ] **Step 5: Commit.**

```bash
git add docs Tests/golden Scripts
git commit -m "docs(ui): describe the converted tool windows"
```
