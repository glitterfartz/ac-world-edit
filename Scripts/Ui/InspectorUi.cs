using Godot;

namespace ACWorldEdit;
/// <summary>Small shared UI primitives keep scene/entity pages consistent without a framework.</summary>
public static class InspectorUi
{
    public static readonly Color Muted = new("91a4b5");
    public static readonly Color Accent = new("5bd8c2");
    /// <summary>Restrained shared colors and clear active/hover states for the inspector controls.</summary>
    public static Theme ExplorerTheme()
    {
        var theme = new Theme
        {
            DefaultFontSize = 14
        };
        StyleBoxFlat Surface(string color) => new()
        {
            BgColor = new(color),
            CornerRadiusTopLeft = 5,
            CornerRadiusTopRight = 5,
            CornerRadiusBottomLeft = 5,
            CornerRadiusBottomRight = 5,
            ContentMarginLeft = 10,
            ContentMarginRight = 10,
            ContentMarginTop = 7,
            ContentMarginBottom = 7
        };
        theme.SetStylebox("normal", "Button", Surface("1d303e"));
        theme.SetStylebox("hover", "Button", Surface("294657"));
        theme.SetStylebox("pressed", "Button", Surface("28544f"));
        theme.SetStylebox("disabled", "Button", Surface("17252e"));
        theme.SetColor("font_disabled_color", "Button", new("637a89"));
        theme.SetStylebox("panel", "ItemList", Surface("111e29"));
        theme.SetStylebox("selected", "ItemList", Surface("28544f"));
        theme.SetStylebox("selected_focus", "ItemList", Surface("28544f"));
        var active = Surface("203b43");
        active.BorderWidthBottom = 2;
        active.BorderColor = Accent;
        theme.SetStylebox("tab_selected", "TabContainer", active);
        theme.SetStylebox("tab_unselected", "TabContainer", Surface("14212b"));
        theme.SetStylebox("tab_hovered", "TabContainer", Surface("294657"));
        theme.SetStylebox("panel", "TabContainer", new StyleBoxEmpty { ContentMarginTop = 12 });
        theme.SetColor("font_selected_color", "TabContainer", Accent);
        theme.SetColor("font_unselected_color", "TabContainer", Muted);
        return theme;
    }

    public static Label Text(string text, int size = 14, bool muted = false)
    {
        var label = new Label
        {
            Text = text,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Modulate = muted ? Muted : Colors.White
        };
        label.AddThemeFontSizeOverride("font_size", size);
        return label;
    }

    public static VBoxContainer Card(VBoxContainer parent, string title)
    {
        var panel = new PanelContainer();
        var style = new StyleBoxFlat
        {
            BgColor = new("111e29"),
            CornerRadiusTopLeft = 8,
            CornerRadiusTopRight = 8,
            CornerRadiusBottomLeft = 8,
            CornerRadiusBottomRight = 8,
            ContentMarginLeft = 12,
            ContentMarginRight = 12,
            ContentMarginTop = 12,
            ContentMarginBottom = 12
        };
        panel.AddThemeStyleboxOverride("panel", style);
        parent.AddChild(panel);
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 9);
        panel.AddChild(box);
        if (title.Length > 0)
        {
            box.AddChild(Text(title.ToUpperInvariant(), 11, true));
        }

        return box;
    }

    public static VBoxContainer Page(string name, out ScrollContainer scroll)
    {
        scroll = new ScrollContainer
        {
            Name = name,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
        };
        var page = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        page.AddThemeConstantOverride("separation", 12);
        scroll.AddChild(page);
        return page;
    }

    /// <summary>
    /// Select the list row whose string metadata is this ID (entity lists store IDs that way), and
    /// optionally scroll it into view. Does nothing when the row is not listed.
    /// </summary>
    public static void SelectRow(ItemList list, uint id, bool reveal = true)
    {
        string key = id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        for (int i = 0; i < list.ItemCount; i++)
        {
            if (list.GetItemMetadata(i).AsString() == key)
            {
                list.Select(i);
                if (reveal)
                {
                    list.CallDeferred(ItemList.MethodName.EnsureCurrentIsVisible);
                }

                return;
            }
        }
    }

    public static Button Button(string text, Action action)
    {
        var button = new Button
        {
            Text = text,
            CustomMinimumSize = new(0, 34),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        button.Pressed += action;
        return button;
    }
}
