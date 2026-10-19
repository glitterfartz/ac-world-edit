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
    int nextId;

    public EditorMenu()
    {
        Name = "MenuBar";
        // A native global menu would bypass the in-window theme and shortcut path on some platforms.
        PreferGlobalMenu = false;
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
