# Tool windows

An editor that should float as its own OS window over the viewer is built as a `Window` subclass,
not as a panel on the UI `CanvasLayer`. [`ScriptEditorWindow`](../../Scripts/Ui/Scripting/ScriptEditorWindow.cs)
is the reference implementation: copy its shape when building or refactoring another window.

The older `ContentEditorWindow` is a `PanelContainer` pinned to a fixed target size inside the main
viewport. Use the `Window` pattern for new editor windows and when refactoring one of those.

## The shape

`ScriptEditorWindow` is one class with no separate frame class:

1. The constructor sets the window flags, then builds the whole tree in code.
2. The body is a `ScrollContainer` whose child is the editor body.
3. Dialogs are children of the window, so they stay inside it.
4. `Open()` centers the window over the main viewport once, then shows and focuses it.
5. `Close()` hides the window; the tree survives so reopening keeps state.

```csharp
public partial class MyEditorWindow : Window
{
    readonly VBoxContainer body;

    public MyEditorWindow()
    {
        Name = "MyEditorWindow";
        Title = "My editor";
        Visible = false;
        Theme = ClientStyle.Theme();

        // A tool window over the viewer that keeps its own dialogs inside itself.
        ForceNative = true;
        Transient = true;
        Borderless = true;
        GuiEmbedSubwindows = true;
        MinSize = new Vector2I(760, 480);
        Size = new Vector2I(1120, 700);

        var panel = new PanelContainer();
        panel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        panel.AddThemeStyleboxOverride("panel", ClientStyle.Panel());
        AddChild(panel);

        var root = new VBoxContainer();
        panel.AddChild(root);

        var header = new HBoxContainer();
        header.GuiInput += input =>
        {
            if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
            {
                StartDrag();
                header.AcceptEvent();
            }
        };
        root.AddChild(header);
        TitleLabel = ClientStyle.Text(Title, 19, ClientStyle.Header);
        TitleLabel.MouseFilter = Control.MouseFilterEnum.Ignore;
        TitleLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        header.AddChild(TitleLabel);
        CloseButton = new Button { Text = "✕", TooltipText = "Close (Escape)" };
        CloseButton.Pressed += AttemptClose;
        header.AddChild(CloseButton);
        root.AddChild(new HSeparator());

        var scroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        root.AddChild(scroll);
        body = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        scroll.AddChild(body);

        var grip = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        var handle = new Label { Text = "◢", TooltipText = "Drag to resize", MouseFilter = Control.MouseFilterEnum.Stop };
        handle.GuiInput += input =>
        {
            if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
            {
                StartResize(DisplayServer.WindowResizeEdge.BottomRight);
                handle.AcceptEvent();
            }
        };
        grip.AddChild(handle);
        root.AddChild(grip);

        // Key events are delivered to the window's viewport, not to the Window node itself.
        AddChild(new ScriptEditorShortcuts { UnhandledKey = HandleShortcut });

        CloseRequested += AttemptClose;
    }

    public VBoxContainer Body => body;

    public void Open()
    {
        if (!placed)
        {
            placed = true;
            var main = GetTree().Root;
            Position = main.Position + (main.Size - Size) / 2;
        }

        Visible = true;
        GrabFocus();
    }

    public void Close() => Visible = false;
}
```

The snippet abbreviates the class's own members (`TitleLabel`, `CloseButton`, `AttemptClose`,
`HandleShortcut`, `placed`, `ScriptEditorShortcuts`); see `ScriptEditorWindow` for their definitions.

The constructor's order matters: the body fills the `PanelContainer` only because the panel is
`FullRect` and the `ScrollContainer` sits between the fixed header/grip rows.

## Layout rules

- **The body must expand on both axes.** A `ScrollContainer` stretches its child only on an axis
  where the child has the `SIZE_EXPAND` flag (`Scene::ScrollContainer::_reposition_children` uses the
  child's minimum size otherwise). Give the body `SizeFlagsHorizontal` and `SizeFlagsVertical` both
  `ExpandFill`. Within the body, only the pane that should absorb height (here the graph split) is
  `ExpandFill`; the bottom pane keeps its `CustomMinimumSize` so the Lua preview stays pinned to the
  bottom.
- **Keep the body scrollable, not sized by content.** In `SCROLL_MODE_AUTO` the `ScrollContainer`
  reports no minimum of its own, so the editor's tall content cannot raise the window's minimum.
  `MinSize` stays what the constructor set. Do not switch the scroll mode to `DISABLED` and do not
  put the body directly under the window: the content minimum, which is taller than a comfortable
  tool window, would then become the window's minimum and it could no longer be made smaller.
- **Set the minimum explicitly.** A `Window` does not grow to its content unless `WrapControls` is
  on. Use `MinSize` for the floor and let the scroll body handle the overflow.
- **The panel is `FullRect`.** A `Window` is a `Viewport`; a `Control` child anchors against the
  window's visible rect, so `SetAnchorsAndOffsetsPreset(FullRect)` makes it track every resize.
- **Set the window title from the document.** `Title` is the OS window title (taskbar, window list);
  keep it in step with the header label, including the dirty marker.

## Toolbar rules

- Put the action buttons in an `HFlowContainer`, not an `HBoxContainer`: the flow container wraps
  the row at the window width, while an `HBoxContainer` demands the sum of every button and widens
  the window instead.
- Do not autowrap labels in the toolbar. An autowrapping `Label` reports a very small minimum width,
  but the header and caption labels in the row can still force a huge width; use `AutowrapMode.Off`
  with a tooltip.
- Give the status line its own row under the actions. It reports what happened
  ("Saved.", "Exported 1 file."); keep routing it to the viewer's status bar through the window's
  `Report` callback as well, and never write status into the Inspector pane.

## Input and dialogs

- **Key events do not reach the `Window` node.** A `Window` is a `Viewport`, so its own node is not
  in the tree that receives its input. Add a full-rect `Control` child that overrides
  `_UnhandledKeyInput` and calls `GetViewport().SetInputAsHandled()` when it consumes the event;
  `ScriptEditorShortcuts` is that shim. Overriding `_UnhandledKeyInput` on the window class itself
  silently never fires.
- **Dialogs belong to the window.** `AddChild(dialog)` then `PopupCentered()`; with
  `GuiEmbedSubwindows = true` they stay inside the tool window and cannot fall behind it. Keep
  dialogs owned by the window so closing the editor closes them.
- **Headless runs must not prompt.** A modal in a headless test hangs the run. Refuse the action
  instead when `DisplayServer.GetName() == "headless"`, as `GuardDiscard` and `AttemptClose` do.
- **Confirm discarding unsaved edits** through `Dialogs.Confirm(this, ...)` before replacing or
  closing a dirty document.

## Native versus embedded

`ForceNative = true` asks for a real OS window. On platforms where multi-window is unavailable
(native Wayland is the known case), the window can end up embedded instead; the layout code is the
same either way because everything is a `Viewport`. `Transient = true` keeps the window above
the viewer, and `Borderless = true` means the client-drawn header is the only title bar, so dragging
(`StartDrag`) and resizing (`StartResize`) must stay wired.

## Testing

UI tests live in `Tests/ACWorldEdit.GdTests` and drive the real window:

```csharp
var window = new ScriptEditorWindow { WorkspaceDirectory = workshop, ExportDirectory = export, Report = _ => { } };
((SceneTree)Engine.GetMainLoop()).Root.AddChild(window);
AutoFree(window);
window.OpenEditor();
```

- Spawn via the root, `AutoFree`, and use `OpenEditor()` so the same code path as the app runs.
- Assert **layout after two frames**: container layout settles across frames, so set `Size` and then
  `await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame)` twice before reading `Size` or
  `GetGlobalRect()`.
- Cover the flows, not just construction: opening, resizing (canvas grows, bottom pane stays at the
  bottom), toolbar wrapping without widening the window, close/hide, and unsaved-change refusal.
  `TheToolbarWrapsWithoutStretchingTheWindow` and
  `GrowingTheWindowExpandsTheCanvasAndKeepsThePreviewAtTheBottom` are the layout regressions to
  keep.

## Regressions to avoid

These are real failures this window has had; the rules above are the fixes.

| Symptom | Cause | Fix |
|---|---|---|
| The toolbar buttons eat almost the whole width, or the window refuses to be narrow | Action buttons in an `HBoxContainer`, plus an autowrapping caption label that reported a huge minimum width | `HFlowContainer` for the actions, `AutowrapMode.Off` on labels, status line on its own row |
| After enlarging the window the canvas stops growing and the Lua preview floats mid-window, with empty panel below | Body added to the `ScrollContainer` with only `SizeFlagsHorizontal = ExpandFill`; the scroll container kept it at its minimum height | `SizeFlagsVertical = ExpandFill` on the body |
| The window suddenly adopts a large size and never shrinks back | A `Control`-derived frame (`ScriptWindowFrame`) sized itself from its content and adopted a transient large minimum reported while children laid out | Make the editor a `Window` with an explicit `MinSize` and a scrolling body; never let content drive the window size |
| Shortcuts stop working after the window becomes a `Window` | `_UnhandledKeyInput` overridden on the `Window` node, which never receives its own keys | The `ScriptEditorShortcuts` child control routes them |
| Tests hang or the suite stops responding | A confirmation dialog popped in a headless run | Refuse instead of prompting when `DisplayServer.GetName() == "headless"` |
