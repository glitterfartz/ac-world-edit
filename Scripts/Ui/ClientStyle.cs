using Godot;
using ACWorldEdit.Assets;

namespace ACWorldEdit;

/// <summary>
/// Look shared by the draft-backed editor windows (matching the dark editor panels), plus the
/// client-flavoured bits their previews need: WoW item quality colors, gold/silver/copper money and
/// client icons. Icons are read from the mounted client (Interface/*.blp) and cached; decoding runs
/// on a worker, textures are created on the main thread.
/// </summary>
public static class ClientStyle
{
    public static readonly Color Body = new("e7eef4"), Header = new("ffffff"), Gold = new("ffd100"), Faded = InspectorUi.Muted, Edited = new("6fe08a"), Error = new("ff7a7a"), Warning = new("f0b44c");

    /// <summary>Background of the highlighted (hovered or selected) list entry; text on it must stay readable.</summary>
    public static readonly Color Highlight = new("28544f");

    /// <summary>
    /// Item quality colors (0 poor .. 7 heirloom), as the client draws names, brightened for a dark background.
    /// </summary>
    static readonly Color[] Qualities = [new("9d9d9d"), new("ffffff"), new("1eff00"), new("4aa3ff"), new("c06bff"), new("ff8000"), new("e6cc80"), new("e6cc80")];
    public static Color Quality(int quality) => Qualities[Math.Clamp(quality, 0, Qualities.Length - 1)];

    public static Theme Theme()
    {
        var theme = new Theme
        {
            DefaultFontSize = 15
        };
        StyleBoxFlat Box(string background, int border = 0, string? borderColor = null) => new()
        {
            BgColor = new(background),
            BorderColor = new(borderColor ?? "3a5668"),
            BorderWidthBottom = border,
            BorderWidthTop = border,
            BorderWidthLeft = border,
            BorderWidthRight = border,
            CornerRadiusTopLeft = 5,
            CornerRadiusTopRight = 5,
            CornerRadiusBottomLeft = 5,
            CornerRadiusBottomRight = 5,
            ContentMarginLeft = 8,
            ContentMarginRight = 8,
            ContentMarginTop = 4,
            ContentMarginBottom = 4
        };
        foreach (string control in new[] { "Label", "Button", "CheckBox", "LineEdit", "TextEdit", "OptionButton", "ItemList" })
        {
            theme.SetColor("font_color", control, Body);
        }

        theme.SetColor("font_placeholder_color", "LineEdit", Faded);
        theme.SetColor("font_placeholder_color", "TextEdit", Faded);
        theme.SetColor("font_disabled_color", "Button", new("637a89"));
        theme.SetStylebox("normal", "Button", Box("1d303e"));
        theme.SetStylebox("hover", "Button", Box("294657"));
        theme.SetStylebox("pressed", "Button", Box("28544f"));
        theme.SetStylebox("disabled", "Button", Box("17252e"));
        theme.SetStylebox("focus", "Button", new StyleBoxEmpty());
        foreach (string control in new[] { "LineEdit", "TextEdit", "SpinBox" })
        {
            theme.SetStylebox("normal", control, Box("111e29", 1));
            theme.SetStylebox("focus", control, Box("111e29", 1, "5bd8c2"));
            theme.SetStylebox("read_only", control, Box("17252e", 1));
        }

        theme.SetStylebox("panel", "ItemList", Box("111e29", 1));
        theme.SetStylebox("focus", "ItemList", new StyleBoxEmpty());
        foreach (string state in new[] { "normal", "hover", "pressed", "hover_pressed" })
        {
            theme.SetStylebox(state, "OptionButton", Box(state == "normal" ? "1d303e" : "294657", 1));
        }

        theme.SetStylebox("focus", "OptionButton", Box("1d303e", 1, "5bd8c2"));
        foreach (string state in new[] { "font_hover_color", "font_pressed_color", "font_focus_color", "font_hover_pressed_color" })
        {
            theme.SetColor(state, "OptionButton", Body);
        }

        foreach (string state in new[] { "font_selected_color", "font_hovered_color", "font_hovered_selected_color" })
        {
            theme.SetColor(state, "ItemList", Header);
        }

        theme.SetStylebox("selected", "ItemList", Box("28544f"));
        theme.SetStylebox("selected_focus", "ItemList", Box("28544f"));
        theme.SetStylebox("hovered", "ItemList", Box("1d303e"));
        theme.SetStylebox("hovered_selected", "ItemList", Box("28544f"));
        theme.SetStylebox("hovered_selected_focus", "ItemList", Box("28544f"));
        // The dropdown list of an OptionButton is a PopupMenu.
        theme.SetStylebox("panel", "PopupMenu", Box("111e29", 1));
        theme.SetStylebox("hover", "PopupMenu", Box("28544f"));
        foreach (string state in new[] { "font_color", "font_hover_color", "font_accent_color", "font_separator_color" })
        {
            theme.SetColor(state, "PopupMenu", Body);
        }

        theme.SetColor("font_disabled_color", "PopupMenu", Faded);
        return theme;
    }

    /// <summary>WCAG contrast ratio (1..21) between two colors.</summary>
    public static double Contrast(Color a, Color b)
    {
        static double Lum(Color c)
        {
            static double Lin(float v) => v <= 0.04045f ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
            return 0.2126 * Lin(c.R) + 0.7152 * Lin(c.G) + 0.0722 * Lin(c.B);
        }

        double la = Lum(a), lb = Lum(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    /// <summary>
    /// Floating editor window panel: the same dark rounded box as the main UI panels.
    /// </summary>
    public static StyleBoxFlat Panel() => new()
    {
        BgColor = new(0.055f, 0.08f, 0.11f, 0.97f),
        BorderColor = new("27404f"),
        BorderWidthBottom = 1,
        BorderWidthTop = 1,
        BorderWidthLeft = 1,
        BorderWidthRight = 1,
        CornerRadiusTopLeft = 12,
        CornerRadiusTopRight = 12,
        CornerRadiusBottomLeft = 12,
        CornerRadiusBottomRight = 12,
        ContentMarginLeft = 16,
        ContentMarginRight = 16,
        ContentMarginTop = 12,
        ContentMarginBottom = 14,
        ShadowColor = new(0, 0, 0, 0.45f),
        ShadowSize = 10
    };

    public static Label Text(string text, int size = 15, Color? color = null)
    {
        var label = new Label
        {
            Text = text,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color ?? Body);
        return label;
    }

    /// <summary>
    /// Short non-wrapping caption for inline editor rows (wrapping labels collapse in HBoxes).
    /// </summary>
    public static Label Tag(string text)
    {
        var label = new Label
        {
            Text = text,
            VerticalAlignment = VerticalAlignment.Center
        };
        label.AddThemeFontSizeOverride("font_size", 11);
        label.AddThemeColorOverride("font_color", Faded);
        return label;
    }

    /// <summary>
    /// Copper amount as the client shows it: "1g 20s 5c".
    /// </summary>
    public static string Money(long copper)
    {
        if (copper <= 0)
        {
            return "free";
        }

        long gold = copper / 10000, silver = copper / 100 % 100, rest = copper % 100;
        return string.Join(' ', new[] { gold > 0 ? $"{gold}g" : "", silver > 0 ? $"{silver}s" : "", rest > 0 ? $"{rest}c" : "" }.Where(s => s.Length > 0));
    }

    /// <summary>
    /// Client gossip icon file per GOSSIP_ICON_* value (GossipDef.h); unknown values use the chat icon.
    /// </summary>
    public static string GossipIcon(uint icon) => "Interface/GossipFrame/" + (icon switch
    {
        1 => "Vendor",
        2 => "Taxi",
        3 => "Trainer",
        4 or 5 => "Binder",
        6 => "Banker",
        7 => "Petition",
        8 => "Tabard",
        9 => "BattleMaster",
        _ => "Gossip"
    }) + "GossipIcon.blp";

    public const string AvailableQuestIcon = "Interface/GossipFrame/AvailableQuestIcon.blp", ActiveQuestIcon = "Interface/GossipFrame/ActiveQuestIcon.blp", MissingIcon = "Interface/Icons/INV_Misc_QuestionMark.blp";
}

/// <summary>
/// Client BLP icon textures keyed by path. Requests decode on a worker; finished images become
/// textures on the main thread. Cleared when the asset store changes.
/// </summary>
public sealed class ClientIcons(Func<IClientFiles?> store)
{
    readonly Dictionary<string, Texture2D?> ready = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, Task<BlpTexture?>> pending = new(StringComparer.OrdinalIgnoreCase);
    IClientFiles? owner;

    /// <summary>
    /// Texture for a client path, or null while loading or when missing. Call again after
    /// Changed fires.
    /// </summary>
    public Texture2D? Get(string path)
    {
        var current = store();
        if (!ReferenceEquals(current, owner))
        {
            ready.Clear();
            pending.Clear();
            owner = current;
        }

        if (ready.TryGetValue(path, out var texture))
        {
            return texture;
        }

        if (current != null && !pending.ContainsKey(path))
        {
            pending[path] = Task.Run(() =>
            {
                try
                {
                    return BlpTexture.Decode(current.Read(path), 64);
                }
                catch (Exception)
                {
                    return (BlpTexture?)null;
                }
            });
        }

        return null;
    }

    /// <summary>
    /// Publish finished decodes (main thread). Returns true when any icon became available.
    /// </summary>
    public bool Poll()
    {
        bool changed = false;
        foreach (var (path, task) in pending.Where(p => p.Value.IsCompleted).ToArray())
        {
            pending.Remove(path);
            var blp = task.Result;
            if (blp == null)
            {
                ready[path] = null;
                continue;
            }

            using var image = Image.CreateFromData(blp.Width, blp.Height, false, Image.Format.Rgba8, blp.Rgba);
            ready[path] = ImageTexture.CreateFromImage(image);
            changed = true;
        }

        return changed;
    }
}
