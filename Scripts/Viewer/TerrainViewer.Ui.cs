using Godot;
using ACWorldEdit.Assets;

namespace ACWorldEdit;

/// <summary>
/// UI composition for the viewer. Streaming, archive ownership and camera movement remain in the main partial; controls never write source data.
/// </summary>
public partial class TerrainViewer
{
    /// <summary>
    /// Build viewer controls with the minimap inside the left panel. Creature controls come from the
    /// overlay; actions only reload sources or change previews.
    /// </summary>
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
        // Anchored to the window bottom (matching the telemetry bar and Scene explorer below) and
        // scrollable so added controls never clip off-screen on a shorter window.
        var panel = new PanelContainer
        {
            AnchorBottom = 1,
            OffsetLeft = 20,
            OffsetTop = 20,
            OffsetBottom = -82,
            CustomMinimumSize = new Vector2(330, 0)
        };
        ui.AddChild(panel);
        var scroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
        };
        panel.AddChild(scroll);
        var box = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        box.AddThemeConstantOverride("separation", 10);
        scroll.AddChild(box);
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
        box.AddChild(new Label { Text = "Time of day" });
        var timeRow = new HBoxContainer();
        box.AddChild(timeRow);
        playTime = new Button
        {
            Text = "⏸",
            CustomMinimumSize = new Vector2(32, 0),
            TooltipText = "Pause/resume the day-night cycle"
        };
        playTime.Pressed += () =>
        {
            sky.Playing = !sky.Playing;
            playTime.Text = sky.Playing ? "⏸" : "▶";
        };
        timeRow.AddChild(playTime);
        timeOfDay = new HSlider
        {
            MinValue = 0,
            MaxValue = 24,
            Step = 1.0 / 60,
            Value = sky.TimeOfDayHours,
            CustomMinimumSize = new Vector2(190, 0),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            TooltipText = "Scrub the time of day"
        };
        timeOfDay.ValueChanged += value => sky.SetTimeOfDay(value);
        timeRow.AddChild(timeOfDay);
        timeOfDayLabel = new Label
        {
            CustomMinimumSize = new Vector2(48, 0)
        };
        timeRow.AddChild(timeOfDayLabel);
        var speedRow = new HBoxContainer();
        box.AddChild(speedRow);
        speedRow.AddChild(new Label { Text = "Cycle speed" });
        var speed = new OptionButton();
        string[] speedLabels = ["0.5x", "1x", "2x", "5x", "20x"];
        float[] speedValues = [0.5f, 1f, 2f, 5f, 20f];
        foreach (string label in speedLabels)
        {
            speed.AddItem(label);
        }

        speed.Select(1);
        speed.ItemSelected += id => sky.Speed = speedValues[id];
        speedRow.AddChild(speed);
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
        zoneLabel = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        zoneLabel.AddThemeColorOverride("font_color", new Color("5bd8c2"));
        zoneLabel.AddThemeFontSizeOverride("font_size", 17);
        box.AddChild(zoneLabel);
        continentLabel = new Label();
        continentLabel.AddThemeColorOverride("font_color", new Color("aabac8"));
        continentLabel.AddThemeFontSizeOverride("font_size", 12);
        box.AddChild(continentLabel);
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
        sidebar = new SceneSidebar(creatures, gameObjects, objects);
        ui.AddChild(sidebar);
    }
}
