using Godot;
using ACWorldEdit.Assets;

namespace ACWorldEdit;
public partial class TerrainViewer
{
    void BuildUi()
    {
        var layer = new CanvasLayer();
        AddChild(layer);
        var ui = new Control
        {
            Name = "Interface",
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        ui.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(ui);
        var theme = new Theme();
        theme.DefaultFontSize = 15;
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
        ui.Theme = theme;
        var panel = new PanelContainer
        {
            Position = new Vector2(20, 20),
            CustomMinimumSize = new Vector2(330, 0)
        };
        ui.AddChild(panel);
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 10);
        panel.AddChild(box);
        var title = new Label
        {
            Text = "AC WORLD EDIT"
        };
        title.AddThemeFontSizeOverride("font_size", 24);
        box.AddChild(title);
        var subtitle = new Label
        {
            Text = "WORLD VIEWER  /  WRATH 3.3.5"
        };
        subtitle.AddThemeColorOverride("font_color", new Color("5bd8c2"));
        subtitle.AddThemeFontSizeOverride("font_size", 12);
        box.AddChild(subtitle);
        box.AddChild(new HSeparator());
        box.AddChild(new Label { Text = "MPQ directory" });
        dataPath = new LineEdit
        {
            Text = ProjectSettings.GlobalizePath("res://wow-data"),
            CustomMinimumSize = new Vector2(294, 0)
        };
        box.AddChild(dataPath);
        reload = new Button
        {
            Text = "Load archives"
        };
        reload.Pressed += async () => await LoadArchives();
        box.AddChild(reload);
        box.AddChild(new Label { Text = "World" });
        maps = new OptionButton();
        box.AddChild(maps);
        maps.ItemSelected += id =>
        {
            if ((int)id < mapList.Count)
            {
                var m = mapList[(int)id];
                tileX.Value = m.Directory == "Azeroth" ? 34 : 32;
                tileY.Value = m.Directory == "Azeroth" ? 49 : 32;
            }
        };
        var row = new HBoxContainer();
        box.AddChild(row);
        row.AddChild(new Label { Text = "Tile X" });
        tileX = new SpinBox
        {
            MinValue = 0,
            MaxValue = 63,
            Value = 34
        };
        row.AddChild(tileX);
        row.AddChild(new Label { Text = "Y" });
        tileY = new SpinBox
        {
            MinValue = 0,
            MaxValue = 63,
            Value = 49
        };
        row.AddChild(tileY);
        open = new Button
        {
            Text = "Open world",
            Disabled = true
        };
        open.Pressed += async () => await OpenMap();
        box.AddChild(open);
        var radiusRow = new HBoxContainer();
        box.AddChild(radiusRow);
        radiusRow.AddChild(new Label { Text = "Nearby tile radius" });
        distance = new SpinBox
        {
            MinValue = 0,
            MaxValue = 3,
            Value = 1
        };
        radiusRow.AddChild(distance);
        distance.ValueChanged += value =>
        {
            radius = (int)value;
            UpdateWanted();
        };
        var grid = new CheckButton
        {
            Text = "Show terrain cell borders"
        };
        grid.Toggled += value => renderer.SetGrid(value);
        box.AddChild(grid);
        box.AddChild(new HSeparator());
        box.AddChild(new Label { Text = "Right mouse  ·  look / fly\nW A S D  ·  move     Q / E  ·  down / up\nShift  ·  faster     Wheel  ·  flight speed\nEsc  ·  release mouse", Modulate = new Color("aabac8") });
        status = new Label
        {
            Text = message,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(294, 55)
        };
        box.AddChild(status);
        var bottom = new PanelContainer
        {
            AnchorTop = 1,
            AnchorBottom = 1,
            AnchorRight = 1,
            OffsetLeft = 20,
            OffsetRight = -20,
            OffsetTop = -62,
            OffsetBottom = -18
        };
        ui.AddChild(bottom);
        telemetry = new Label
        {
            Text = "Waiting for terrain"
        };
        bottom.AddChild(telemetry);
        minimap = new TerrainMinimap
        {
            CustomMinimumSize = new Vector2(180, 180),
            MouseFilter = Control.MouseFilterEnum.Stop
        };
        box.AddChild(minimap);
        minimap.Jump += key =>
        {
            tileX.Value = key.X;
            tileY.Value = key.Y;
            Teleport(key);
        };
        ui.AddChild(creatures.BuildPanel());
    }
}
