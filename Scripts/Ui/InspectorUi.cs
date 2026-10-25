using Godot;

namespace ACWorldEdit;

/// <summary>Controls of an entity list section: search box, result list, status line and the fly-to button.</summary>
public sealed record ListParts(LineEdit Search, ItemList List, Label Status, Button Fly);

/// <summary>The "Selection" card: its container plus the title and identity lines.</summary>
public sealed record SelectionParts(VBoxContainer Box, Label Title, Label Identity);

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
    public static void SelectRow(ItemList list, ulong id, bool reveal = true)
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

    /// <summary>
    /// Heading, search box, entity list (rows carry the ID as string metadata), status line and the
    /// Refresh / Fly to selection row, shared by every overlay page.
    /// </summary>
    public static ListParts ListSection(VBoxContainer page, string heading, string subtitle, string placeholder, string initialStatus,
        Action<ulong> select, Action refresh, Action fly, Action searchChanged, int listHeight,
        int? listFontSize = null, string? listName = null, string? searchName = null, bool flyDisabled = false, bool clipList = false)
    {
        page.AddChild(Text(heading, 20));
        page.AddChild(Text(subtitle, 12, true));
        var search = new LineEdit
        {
            PlaceholderText = placeholder,
            ClearButtonEnabled = true,
            MaxLength = 80
        };
        if (searchName != null)
        {
            search.Name = searchName;
        }

        page.AddChild(search);
        search.TextChanged += _ => searchChanged();
        var list = new ItemList
        {
            CustomMinimumSize = new(0, listHeight),
            ClipContents = clipList,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis
        };
        if (listName != null)
        {
            list.Name = listName;
        }

        if (listFontSize is int size)
        {
            list.AddThemeFontSizeOverride("font_size", size);
        }

        page.AddChild(list);
        list.ItemSelected += index => select(ulong.Parse(list.GetItemMetadata((int)index).AsString(), System.Globalization.CultureInfo.InvariantCulture));
        var status = Text(initialStatus, 12, true);
        page.AddChild(status);
        var row = new HBoxContainer();
        page.AddChild(row);
        row.AddChild(Button("Refresh", refresh));
        var flyButton = Button("Fly to selection", fly);
        flyButton.Disabled = flyDisabled;
        row.AddChild(flyButton);
        return new(search, list, status, flyButton);
    }

    /// <summary>The "Selection" card with its title line and a muted identity/hint line.</summary>
    public static SelectionParts SelectionCard(VBoxContainer page, string emptyTitle, string emptyIdentity)
    {
        var box = Card(page, "Selection");
        var title = Text(emptyTitle, 19);
        box.AddChild(title);
        var identity = Text(emptyIdentity, 12, true);
        box.AddChild(identity);
        return new(box, title, identity);
    }

    /// <summary>
    /// The "Drop to ground / Reset to source / Copy .go" row for a placement editor.
    /// </summary>
    public static (Button Drop, Button Reset, Button CopyGo) PlacementButtons(Container card, IPlacementEditor editor)
    {
        var row = new HBoxContainer();
        card.AddChild(row);
        var drop = Button("Drop to ground", editor.DropToGround);
        var reset = Button("Reset to source", editor.ResetToSource);
        var copyGo = Button("Copy .go", editor.CopyGoCommand);
        row.AddChild(drop);
        row.AddChild(reset);
        row.AddChild(copyGo);
        return (drop, reset, copyGo);
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
