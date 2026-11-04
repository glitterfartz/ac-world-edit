using Godot;
using ACWorldEdit.Assets;
using ACWorldEdit.Data;

namespace ACWorldEdit;

/// <summary>
/// UI composition for the viewer: the canvas and theme, then one builder per region. Streaming,
/// archive ownership and camera movement remain in the main partial; controls never write source data.
/// The build order is the draw order, so the calls stay in this order.
/// </summary>
public partial class ViewerShell
{
    /// <summary>
    /// Build the menu/tool strip above a body holding the navigation panel, Scene explorer and
    /// telemetry. The strip sits in a VBox so panels never overlap it. Actions only reload sources,
    /// change previews or record local drafts.
    /// </summary>
    void BuildUi()
    {
        var layer = new CanvasLayer();
        AddChild(layer);
        var frame = new VBoxContainer
        {
            Name = "Frame",
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        frame.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        frame.AddThemeConstantOverride("separation", 0);
        layer.AddChild(frame);
        // Ignore filters let clicks on empty viewport areas fall through to world picking.
        var ui = new Control
        {
            Name = "Interface",
            MouseFilter = Control.MouseFilterEnum.Ignore,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        LoadProfileUi();
        var theme = BuildTheme();
        ui.Theme = theme;
        frame.AddChild(BuildStrip(theme));
        frame.AddChild(ui);
        BuildLeftPanel(ui);
        BuildEditors(layer);
        BuildContentBrowser(ui);
        BuildMenus();
        BuildToolbar();
    }

    /// <summary>Read the saved window profile into the fields the builders read.</summary>
    void LoadProfileUi()
    {
        SetUiScale(UserSettings.LoadUiScale(), false);
        profile = ClientProfile.For(UserSettings.LoadProfile());
        wrathDirectory = UserSettings.LoadDataDirectory();
        retailDirectory = UserSettings.LoadRetailDirectory();
        databaseEnabled = profile.SupportsDatabase;
        connection.Dialect = profile.Dialect;
    }

    /// <summary>The panels' shared theme: light text on the translucent dark surface.</summary>
    static Theme BuildTheme()
    {
        var theme = new Theme
        {
            DefaultFontSize = 15
        };
        theme.SetColor("font_color", "Label", new Color("e7eef4"));
        theme.SetColor("font_color", "Button", new Color("e7eef4"));
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.055f, 0.08f, 0.11f, 0.94f),
            CornerRadiusTopLeft = 12,
            CornerRadiusTopRight = 12,
            CornerRadiusBottomLeft = 12,
            CornerRadiusBottomRight = 12,
            ContentMarginLeft = 18,
            ContentMarginRight = 18,
            ContentMarginTop = 16,
            ContentMarginBottom = 16
        };
        theme.SetStylebox("panel", "PanelContainer", style);
        return theme;
    }
}
