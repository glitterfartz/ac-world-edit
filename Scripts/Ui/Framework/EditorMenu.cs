using Godot;

namespace ACWorldEdit;

/// <summary>
/// Top menu bar whose items are plain callbacks with optional enabled/checked state. Accelerators
/// are matched here in shortcut input (MenuBar did not dispatch them for a closed menu in Godot
/// 4.7.1; verified by the shortcut smoke). Shortcut input runs after focused controls such as text
/// fields have consumed their keys, so shortcuts never fire while typing. Disabled items ignore
/// their accelerators; state refreshes every frame.
/// </summary>
public partial class EditorMenu : MenuBar
{
    sealed record Item(PopupMenu Menu, int Index, Action Action, Func<bool>? Enabled, Func<bool>? Checked, Key Accelerator);
    readonly Dictionary<(PopupMenu, long), Item> items = [];
    readonly List<MenuEntry> entries = [];
    int nextId;

    public EditorMenu()
    {
        Name = "MenuBar";
        // A native global menu would bypass the in-window theme and shortcut path on some platforms.
        PreferGlobalMenu = false;
    }

    /// <summary>Every entry the sheet asked for, in display order, including submenu children.</summary>
    public IReadOnlyList<MenuEntry> Manifest => entries;

    /// <summary>
    /// Build the whole bar from a sheet. Called once: IDs come from a counter, so a second load would
    /// duplicate every entry. Two items may not share an accelerator.
    /// </summary>
    public void Load(IReadOnlyList<(string Title, IReadOnlyList<MenuEntry> Items)> sheet)
    {
        var accelerators = new Dictionary<Key, string>();
        foreach (var (title, items) in sheet)
        {
            Collect(items, accelerators);
        }

        foreach (var (title, items) in sheet)
        {
            Add(title, items);
        }
    }

    void Collect(IReadOnlyList<MenuEntry> items, Dictionary<Key, string> accelerators)
    {
        foreach (var entry in items)
        {
            if (entry is MenuSubmenu submenu)
            {
                Collect(submenu.Items, accelerators);
                continue;
            }

            Key accelerator = entry switch
            {
                MenuCommand command => command.Accelerator,
                MenuCheck check => check.Accelerator,
                _ => Key.None
            };
            if (accelerator == Key.None)
            {
                continue;
            }

            string label = entry is MenuCommand c ? c.Label : ((MenuCheck)entry).Label;
            if (accelerators.TryGetValue(accelerator, out string? other))
            {
                throw new InvalidOperationException($"Accelerator {accelerator} is used by both \"{other}\" and \"{label}\".");
            }

            accelerators[accelerator] = label;
        }
    }

    void Add(string title, IReadOnlyList<MenuEntry> items)
    {
        var menu = Menu(title);
        Add(menu, items);
    }

    void Add(PopupMenu menu, IReadOnlyList<MenuEntry> items)
    {
        foreach (var entry in items)
        {
            entries.Add(entry);
            switch (entry)
            {
                case MenuCommand command:
                    Command(menu, command.Label, command.Run, command.Enabled, command.Accelerator);
                    break;
                case MenuCheck check:
                    Check(menu, check.Label, check.Toggle, check.Checked, check.Accelerator);
                    break;
                case MenuSeparator:
                    menu.AddSeparator();
                    break;
                case MenuSubmenu submenu:
                    Add(Submenu(menu, submenu.Label), submenu.Items);
                    break;
            }
        }
    }

    /// <summary>
    /// Add a top-level menu; its title is the popup's node name.
    /// </summary>
    public PopupMenu Menu(string title)
    {
        var menu = new PopupMenu
        {
            Name = title
        };
        AddChild(menu);
        menu.IdPressed += id =>
        {
            if (items.TryGetValue((menu, id), out var item))
            {
                item.Action();
            }
        };
        return menu;
    }

    /// <summary>
    /// Add a submenu to a menu; fill it with Command/Check like a top-level menu.
    /// </summary>
    public PopupMenu Submenu(PopupMenu parent, string title)
    {
        var sub = new PopupMenu
        {
            Name = title
        };
        parent.AddChild(sub);
        parent.AddSubmenuNodeItem(title, sub);
        sub.IdPressed += id =>
        {
            if (items.TryGetValue((sub, id), out var item))
            {
                item.Action();
            }
        };
        return sub;
    }

    /// <summary>
    /// Add a command. Accelerator uses Godot key-with-modifier encoding, e.g.
    /// KeyModifierMask.MaskCtrl | Key.Z.
    /// </summary>
    public void Command(PopupMenu menu, string label, Action action, Func<bool>? enabled = null, Key accelerator = Key.None)
    {
        int id = nextId++;
        menu.AddItem(label, id, accelerator);
        items[(menu, id)] = new(menu, menu.GetItemIndex(id), action, enabled, null, accelerator);
    }

    /// <summary>
    /// Add a check item whose mark mirrors external state; toggling is the action's job.
    /// </summary>
    public void Check(PopupMenu menu, string label, Action action, Func<bool> isChecked, Key accelerator = Key.None)
    {
        int id = nextId++;
        menu.AddCheckItem(label, id, accelerator);
        items[(menu, id)] = new(menu, menu.GetItemIndex(id), action, null, isChecked, accelerator);
    }

    /// <summary>
    /// Run the enabled item whose accelerator matches this key press and consume the event so it
    /// cannot also reach Godot's own menu handling or the viewport.
    /// </summary>
    public override void _ShortcutInput(InputEvent input)
    {
        if (input is not InputEventKey { Pressed: true, Echo: false } key)
        {
            return;
        }

        var code = key.GetKeycodeWithModifiers();
        foreach (var item in items.Values)
        {
            if (item.Accelerator != Key.None && item.Accelerator == code && item.Enabled?.Invoke() != false)
            {
                GetViewport().SetInputAsHandled();
                item.Action();
                return;
            }
        }
    }

    public void Refresh()
    {
        foreach (var item in items.Values)
        {
            item.Menu.SetItemDisabled(item.Index, item.Enabled?.Invoke() == false);
            if (item.Checked != null)
            {
                item.Menu.SetItemChecked(item.Index, item.Checked());
            }
        }
    }
}

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
    public Button Mode(string text, string tooltip, ButtonGroup group, Action select, Func<bool> active, Shortcut? shortcut = null, Func<bool>? enabled = null)
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
        refresh.Add((button, enabled, active));
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

    /// <summary>A muted caption in the strip, before a control that needs naming.</summary>
    public Label Label(string text)
    {
        var label = new Label { Text = text, Modulate = new Color("aabac8") };
        AddChild(label);
        return label;
    }

    /// <summary>
    /// A compact option list with no keyboard focus, so a shortcut key still reaches the viewport.
    /// The callback runs only on a user pick, as with <see cref="OptionButton.ItemSelected"/>.
    /// </summary>
    public OptionButton Choice(string tooltip, IReadOnlyList<string> labels, int selected, Action<int> onChanged)
    {
        var option = new OptionButton
        {
            TooltipText = tooltip,
            FocusMode = FocusModeEnum.None
        };
        foreach (string label in labels)
        {
            option.AddItem(label);
        }

        option.Select(selected);
        option.ItemSelected += index => onChanged((int)index);
        AddChild(option);
        return option;
    }

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
