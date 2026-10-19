using Godot;

namespace ACWorldEdit;

/// <summary>
/// The strip along the bottom of the screen: viewer telemetry on the left, the latest status message
/// on the right. Clicking the message opens the message history.
/// </summary>
public sealed partial class StatusBar : PanelContainer
{
    readonly Label telemetry;
    readonly Button message;
    readonly StatusHistoryWindow history;

    public StatusBar(StatusLog log)
    {
        AnchorTop = 1;
        AnchorBottom = 1;
        AnchorRight = 1;
        OffsetLeft = 20;
        OffsetRight = -20;
        OffsetTop = -62;
        OffsetBottom = -18;
        history = new StatusHistoryWindow(log);
        AddChild(history);
        var row = new HBoxContainer();
        AddChild(row);
        telemetry = new Label
        {
            Text = "Waiting for terrain",
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        row.AddChild(telemetry);
        message = new Button
        {
            Alignment = HorizontalAlignment.Right,
            ClipText = true,
            CustomMinimumSize = new Vector2(420, 0),
            TooltipText = "Show message history",
            Text = log.Latest
        };
        message.AddThemeStyleboxOverride("normal", BoxStyle(new Color(0.12f, 0.17f, 0.23f, 1f), new Color(1, 1, 1, 0.22f)));
        message.AddThemeStyleboxOverride("hover", BoxStyle(new Color(0.17f, 0.24f, 0.32f, 1f), new Color("5bd8c2")));
        message.AddThemeStyleboxOverride("pressed", BoxStyle(new Color(0.08f, 0.12f, 0.17f, 1f), new Color("5bd8c2")));
        message.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        message.Pressed += () => history.Open();
        row.AddChild(message);
        log.Posted += () => message.Text = log.Latest;
    }

    /// <summary>Raised box so the message reads as a button.</summary>
    static StyleBoxFlat BoxStyle(Color fill, Color border) => new()
    {
        BgColor = fill,
        BorderColor = border,
        BorderWidthLeft = 1,
        BorderWidthRight = 1,
        BorderWidthTop = 1,
        BorderWidthBottom = 1,
        CornerRadiusTopLeft = 6,
        CornerRadiusTopRight = 6,
        CornerRadiusBottomLeft = 6,
        CornerRadiusBottomRight = 6,
        ContentMarginLeft = 12,
        ContentMarginRight = 12,
        ContentMarginTop = 3,
        ContentMarginBottom = 3
    };

    public string Telemetry
    {
        set => telemetry.Text = value;
    }
}
