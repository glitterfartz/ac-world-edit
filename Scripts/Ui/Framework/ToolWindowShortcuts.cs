using Godot;

namespace ACWorldEdit;

/// <summary>
/// Routes unhandled key events from inside a tool window back to the window script.
/// A Window is a Viewport, so its own node is not part of the node tree that receives its keys;
/// a control inside the window is.
/// </summary>
public partial class ToolWindowShortcuts : Control
{
    /// <summary>Handle one key event; return true when it was consumed.</summary>
    public Func<InputEvent, bool>? UnhandledKey { get; set; }

    public ToolWindowShortcuts()
    {
        SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        MouseFilter = Control.MouseFilterEnum.Ignore;
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (UnhandledKey?.Invoke(@event) == true)
        {
            GetViewport().SetInputAsHandled();
        }
    }
}
