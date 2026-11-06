using Godot;

namespace ACWorldEdit;

/// <summary>
/// A <see cref="MenuButton"/> that looks like an input box: MenuButton's own theme type is bare text,
/// so it borrows the OptionButton's panel styleboxes and arrow icon instead. The popup is a checklist
/// where every row carries its description as a tooltip, which <see cref="PopupMenu"/> does not give
/// items for free (each row's tooltip must be set explicitly).
/// The popup stays open while ticking (a checklist, not a one-shot menu) and each tick flips its
/// checkmark at once; checkable rows have no built-in toggle behavior and must be flipped manually.
/// </summary>
public sealed partial class CheckMenuButton : MenuButton
{
    readonly PopupMenu popup;

    public CheckMenuButton()
    {
        popup = GetPopup();
        popup.HideOnCheckableItemSelection = false;
        popup.HideOnItemSelection = false;
        Flat = false;
        popup.IdPressed += id =>
        {
            popup.ToggleItemChecked(popup.GetItemIndex((int)id));
            ItemToggled?.Invoke((int)id);
        };
        ThemeChanged += ApplyInputBoxLook;
    }

    public override void _Ready() => ApplyInputBoxLook();

    /// <summary>Add one check row. The tooltip is the row's description; an empty one falls back to the name.</summary>
    public void AddCheck(string name, string tip, int id)
    {
        popup.AddCheckItem(name, id);
        popup.SetItemTooltip(popup.GetItemIndex(id), tip.Length > 0 ? tip : name);
    }

    /// <summary>Tick or untick the row with the given id without reading the popup.</summary>
    public void SetChecked(int id, bool on) => popup.SetItemChecked(popup.GetItemIndex(id), on);

    /// <summary>Raised after the user ticks or unticks a row; the argument is the row's id.</summary>
    public event Action<int>? ItemToggled;

    bool applying;

    /// <summary>
    /// Copy the OptionButton look (panel, font colors and arrow) from the theme, duplicated so the
    /// shared styles stay untouched. Theme lookup needs the control in the tree, so this runs on
    /// ready and whenever the theme changes; in the constructor the lookup would miss the theme.
    /// Guarded: adding an override emits ThemeChanged, which would otherwise recurse forever
    /// (hang, then a stack-overflow crash) the moment the control enters the tree.
    /// </summary>
    void ApplyInputBoxLook()
    {
        if (applying)
        {
            return;
        }

        applying = true;
        try
        {
            foreach (string state in new[] { "normal", "hover", "pressed", "focus", "disabled" })
            {
                if (GetThemeStylebox(state, "OptionButton") is { } box)
                {
                    AddThemeStyleboxOverride(state, (StyleBox)box.Duplicate());
                }
            }

            foreach (string color in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_disabled_color", "font_focus_color" })
            {
                if (HasThemeColor(color, "OptionButton"))
                {
                    AddThemeColorOverride(color, GetThemeColor(color, "OptionButton"));
                }
            }

            if (GetThemeIcon("arrow", "OptionButton") is { } arrow)
            {
                Icon = arrow;
                IconAlignment = HorizontalAlignment.Right;
            }
        }
        finally
        {
            applying = false;
        }
    }
}
