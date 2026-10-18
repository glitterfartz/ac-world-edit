using Godot;
using ACWorldEdit.Assets;

namespace ACWorldEdit;

/// <summary>
/// Client-style look for interaction and quest windows: parchment body, dark-brown frame and text,
/// WoW quality colors and gold/silver/copper money. Icons are read from the mounted client
/// (Interface/*.blp) and cached; decoding runs on a worker, textures are created on the main thread.
/// </summary>
public static class ClientStyle
{
    public static readonly Color Parchment = new("efe0b9"), Ink = new("2b1a0a"), Frame = new("4a3018"), Header = new("1f140a"), Gold = new("ffd100"), Faded = new("7a6650"), Edited = new("1e6b2e"), Error = new("a01818"), Warning = new("8a5a00");

    /// <summary>
    /// Item quality colors (0 poor .. 7 heirloom), as the client draws names.
    /// </summary>
    static readonly Color[] Qualities = [new("6b6b6b"), new("2b1a0a"), new("1e8a00"), new("0060c8"), new("8a2be2"), new("c86400"), new("b8a060"), new("b8a060")];
    public static Color Quality(int quality) => Qualities[Math.Clamp(quality, 0, Qualities.Length - 1)];

    public static Theme Theme()
    {
        var theme = new Theme
        {
            DefaultFontSize = 15
        };
        theme.SetColor("font_color", "Label", Ink);
        theme.SetColor("font_color", "Button", Ink);
        theme.SetColor("font_hover_color", "Button", new("6a2c00"));
        theme.SetColor("font_pressed_color", "Button", new("6a2c00"));
        theme.SetColor("font_disabled_color", "Button", Faded);
        theme.SetColor("font_color", "CheckBox", Ink);
        theme.SetColor("font_color", "LineEdit", Ink);
        theme.SetColor("font_color", "TextEdit", Ink);
        theme.SetColor("font_placeholder_color", "LineEdit", Faded);
        theme.SetColor("font_placeholder_color", "TextEdit", Faded);
        theme.SetColor("font_color", "OptionButton", Ink);
        theme.SetColor("font_color", "ItemList", Ink);
        StyleBoxFlat Box(Color background, int border = 0) => new()
        {
            BgColor = background,
            BorderColor = Frame,
            BorderWidthBottom = border,
            BorderWidthTop = border,
            BorderWidthLeft = border,
            BorderWidthRight = border,
            CornerRadiusTopLeft = 3,
            CornerRadiusTopRight = 3,
            CornerRadiusBottomLeft = 3,
            CornerRadiusBottomRight = 3,
            ContentMarginLeft = 6,
            ContentMarginRight = 6,
            ContentMarginTop = 3,
            ContentMarginBottom = 3
        };
        // Same padding in every state, or toggled buttons draw wider than their layout slot.
        theme.SetStylebox("normal", "Button", new StyleBoxEmpty { ContentMarginLeft = 6, ContentMarginRight = 6, ContentMarginTop = 3, ContentMarginBottom = 3 });
        theme.SetStylebox("hover", "Button", Box(new("e2cb93")));
        theme.SetStylebox("pressed", "Button", Box(new("d6bb7a")));
        theme.SetStylebox("disabled", "Button", new StyleBoxEmpty { ContentMarginLeft = 6, ContentMarginRight = 6, ContentMarginTop = 3, ContentMarginBottom = 3 });
        theme.SetStylebox("focus", "Button", new StyleBoxEmpty());
        foreach (string control in new[] { "LineEdit", "TextEdit", "OptionButton", "ItemList", "SpinBox" })
        {
            theme.SetStylebox("normal", control, Box(new("fbf3dc"), 1));
            theme.SetStylebox("panel", control, Box(new("fbf3dc"), 1));
            theme.SetStylebox("focus", control, Box(new("fffaea"), 1));
            theme.SetStylebox("read_only", control, Box(new("e9dab0"), 1));
        }

        theme.SetStylebox("hover", "OptionButton", Box(new("fffaea"), 1));
        theme.SetStylebox("pressed", "OptionButton", Box(new("fffaea"), 1));
        return theme;
    }

    /// <summary>
    /// Parchment panel with a dark frame, as the client's gossip/merchant frames.
    /// </summary>
    public static StyleBoxFlat Panel() => new()
    {
        BgColor = Parchment,
        BorderColor = Frame,
        BorderWidthBottom = 4,
        BorderWidthTop = 4,
        BorderWidthLeft = 4,
        BorderWidthRight = 4,
        CornerRadiusTopLeft = 6,
        CornerRadiusTopRight = 6,
        CornerRadiusBottomLeft = 6,
        CornerRadiusBottomRight = 6,
        ContentMarginLeft = 14,
        ContentMarginRight = 14,
        ContentMarginTop = 10,
        ContentMarginBottom = 12,
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
        label.AddThemeColorOverride("font_color", color ?? Ink);
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
