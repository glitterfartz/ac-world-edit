using Godot;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// Draws a <see cref="Tooltip"/> in the look of the game's tooltips. It knows nothing about items or
/// spells, so any builder that produces a <see cref="Tooltip"/> can be shown with it (item and spell
/// editors included). Use <see cref="Create"/> for a standalone control, or
/// <see cref="TooltipButton"/> to attach one to a button.
/// </summary>
public static class GameTooltip
{
    const int Width = 300;

    static Color ToneColor(TooltipLine line) => line.Tone switch
    {
        TooltipTone.Title => ClientStyle.Quality(line.Quality),
        TooltipTone.Muted => ClientStyle.Faded,
        TooltipTone.Good => new("1eff00"),
        TooltipTone.Bad => ClientStyle.Error,
        TooltipTone.Flavor => ClientStyle.Gold,
        TooltipTone.Gold => new("e6cc80"),
        _ => ClientStyle.Header
    };

    public static Control Create(Tooltip tooltip)
    {
        var panel = new PanelContainer();
        var style = new StyleBoxFlat
        {
            BgColor = new(0.04f, 0.05f, 0.09f, 0.96f),
            BorderColor = new("6b7280"),
            ContentMarginLeft = 10,
            ContentMarginRight = 10,
            ContentMarginTop = 8,
            ContentMarginBottom = 8,
            CornerRadiusTopLeft = 4,
            CornerRadiusTopRight = 4,
            CornerRadiusBottomLeft = 4,
            CornerRadiusBottomRight = 4,
            BorderWidthLeft = 1,
            BorderWidthRight = 1,
            BorderWidthTop = 1,
            BorderWidthBottom = 1
        };
        panel.AddThemeStyleboxOverride("panel", style);
        var rows = new VBoxContainer
        {
            CustomMinimumSize = new(Width, 0)
        };
        rows.AddThemeConstantOverride("separation", 1);
        panel.AddChild(rows);
        foreach (var line in tooltip.Lines)
        {
            rows.AddChild(Row(line));
        }

        return panel;
    }

    /// <summary>
    /// Plain labels at a fixed width: a wrapping label with no width to wrap against collapses to one
    /// word per line, which is what made earlier tooltips tall.
    /// </summary>
    static Control Row(TooltipLine line)
    {
        var color = ToneColor(line);
        int size = line.Tone == TooltipTone.Title ? 16 : 13;
        Label Make(string text, HorizontalAlignment align = HorizontalAlignment.Left)
        {
            var label = new Label
            {
                Text = text,
                HorizontalAlignment = align
            };
            label.AddThemeFontSizeOverride("font_size", size);
            label.AddThemeColorOverride("font_color", color);
            return label;
        }

        if (line.Right.Length == 0)
        {
            var text = Make(line.Left);
            text.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            text.CustomMinimumSize = new(Width, 0);
            return text;
        }

        var row = new HBoxContainer();
        var left = Make(line.Left);
        left.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.AddChild(left);
        row.AddChild(Make(line.Right, HorizontalAlignment.Right));
        return row;
    }
}

/// <summary>
/// A button whose hover tooltip is drawn by <see cref="GameTooltip"/> from whatever
/// <see cref="Source"/> returns. Godot only asks for a custom tooltip while the control has tooltip
/// text, so set <c>TooltipText</c> to a non-empty fallback.
/// </summary>
public partial class TooltipButton : Button
{
    /// <summary>Builds the tooltip on hover; null falls back to the plain tooltip text.</summary>
    public Func<Tooltip?>? Source { get; set; }

    public override GodotObject _MakeCustomTooltip(string forText) => Source?.Invoke() is { } tooltip ? GameTooltip.Create(tooltip) : null!;
}

/// <summary>
/// A row of controls (for example an icon and its text) that shows a <see cref="GameTooltip"/> when
/// hovered anywhere on it. Same contract as <see cref="TooltipButton"/>.
/// </summary>
public partial class TooltipRow : HBoxContainer
{
    public Func<Tooltip?>? Source { get; set; }

    public override GodotObject _MakeCustomTooltip(string forText) => Source?.Invoke() is { } tooltip ? GameTooltip.Create(tooltip) : null!;
}
