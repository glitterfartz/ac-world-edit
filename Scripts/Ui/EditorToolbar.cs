using Godot;

namespace ACWorldEdit;

/// <summary>
/// Horizontal tool strip of callback buttons. Button shortcuts run in shortcut input, after focused
/// text fields, and disabled buttons ignore them; state refreshes every frame from its sources.
/// </summary>
public partial class EditorToolbar : HBoxContainer
{
    readonly List<(BaseButton Button, Func<bool>? Enabled, Func<bool>? Pressed)> refresh = [];

    public EditorToolbar()
    {
        Name = "Toolbar";
        AddThemeConstantOverride("separation", 6);
    }

    /// <summary>
    /// Single-key or modified shortcut for a button, e.g. Shortcut(Key.W) or
    /// Shortcut(Key.Z, ctrl: true, shift: true).
    /// </summary>
    public static Shortcut Shortcut(Key key, bool ctrl = false, bool shift = false) => new()
    {
        Events = [new InputEventKey { Keycode = key, CtrlPressed = ctrl, ShiftPressed = shift }]
    };

    public Button Action(string text, string tooltip, Action action, Func<bool>? enabled = null, Shortcut? shortcut = null)
    {
        var button = new Button
        {
            Text = text,
            TooltipText = tooltip,
            Shortcut = shortcut,
            ShortcutInTooltip = shortcut != null,
            FocusMode = FocusModeEnum.None
        };
        button.Pressed += action;
        AddChild(button);
        refresh.Add((button, enabled, null));
        return button;
    }

    /// <summary>
    /// Mutually exclusive mode button. The group keeps exactly one pressed; Pressed mirrors the
    /// source of truth so changes from menus or code show here too.
    /// </summary>
    public Button Mode(string text, string tooltip, ButtonGroup group, Action select, Func<bool> active, Shortcut? shortcut = null)
    {
        var button = new Button
        {
            Text = text,
            TooltipText = tooltip,
            ToggleMode = true,
            ButtonGroup = group,
            Shortcut = shortcut,
            ShortcutInTooltip = shortcut != null,
            FocusMode = FocusModeEnum.None
        };
        button.Pressed += select;
        AddChild(button);
        refresh.Add((button, null, active));
        return button;
    }

    public CheckButton Toggle(string text, string tooltip, Action<bool> changed, Func<bool> isOn)
    {
        var toggle = new CheckButton
        {
            Text = text,
            TooltipText = tooltip,
            FocusMode = FocusModeEnum.None
        };
        toggle.Toggled += value => changed(value);
        AddChild(toggle);
        refresh.Add((toggle, null, isOn));
        return toggle;
    }

    public void Separator() => AddChild(new VSeparator());

    public void Refresh()
    {
        foreach (var (button, enabled, pressed) in refresh)
        {
            button.Disabled = enabled?.Invoke() == false;
            if (pressed != null && button.ButtonPressed != pressed())
            {
                // Mirror state without emitting Toggled/Pressed back into the source.
                button.SetPressedNoSignal(pressed());
            }
        }
    }
}
