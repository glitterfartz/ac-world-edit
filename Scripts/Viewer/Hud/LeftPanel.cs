using Godot;

namespace ACWorldEdit;

/// <summary>
/// The left navigation panel: world settings, teleports, the day-night scrubber, the status line
/// and the terrain minimap, then the Scene explorer and the selection box.
/// </summary>
public partial class ViewerShell
{
    PanelContainer leftPanel = null!;

    void BuildLeftPanel(Control ui)
    {
        // Anchored to the window bottom (matching the telemetry bar and Scene explorer below) and
        // scrollable so added controls never clip off-screen on a shorter window.
        var panel = leftPanel = new PanelContainer
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
        var form = new UiForm(box);
        BuildWorldSettings(form, box);
        BuildDayNight(form, ui);
        sidebar = new SceneSidebar(creatures, gameObjects, graveyards, waypoints, areaTriggers, objects, connection, navigation);
        ui.AddChild(sidebar);
        BuildSelectionBox(ui);
    }

    /// <summary>Title, client name, map/tile entry, the open action and teleports.</summary>
    void BuildWorldSettings(UiForm form, VBoxContainer box)
    {
        form.Heading("AC WORLD EDIT");
        clientLabel = form.Caption("WORLD VIEWER  /  " + profile.Title, new Color("5bd8c2"));
        form.Add(new HSeparator());
        form.Add(InspectorUi.Text("World settings", 24));
        form.Add(new Label { Text = "Map" });
        // Map names can be long; clip them instead of letting the button and its dropdown grow.
        maps = form.Add(form.Options([], -1, id =>
        {
            if (id < mapList.Count)
            {
                var m = mapList[id];
                tileX.Value = m.Directory == "Azeroth" ? 34 : 32;
                tileY.Value = m.Directory == "Azeroth" ? 49 : 32;
            }
        }, clip: true, minWidth: 220));
        var tileRow = form.Row();
        tileRow.AddChild(new Label { Text = "Tile X" });
        tileX = form.Spin(0, 63, 34);
        tileRow.AddChild(tileX);
        tileRow.AddChild(new Label { Text = "Y" });
        tileY = form.Spin(0, 63, 49);
        tileRow.AddChild(tileY);
        open = form.Action("Open world", () => _ = OpenMap(), disabled: true);
        BuildTeleportUi(box);
        var radiusRow = form.Row();
        radiusRow.AddChild(new Label { Text = "Tile view distance" });
        distance = form.Spin(0, 3, 1, value =>
        {
            radius = (int)value;
            streamer.UpdateWanted();
        });
        radiusRow.AddChild(distance);
    }

    /// <summary>Day-night scrubber, cycle speed, status line, zone/country labels and the minimap.</summary>
    void BuildDayNight(UiForm form, Control ui)
    {
        form.Add(new HSeparator());
        form.Add(new Label { Text = "Time of day" });
        var timeRow = form.Row();
        playTime = form.Button(sky.Playing ? "⏸" : "▶", () =>
        {
            sky.Playing = !sky.Playing;
            playTime.Text = sky.Playing ? "⏸" : "▶";
        }, "Pause/resume the day-night cycle");
        playTime.CustomMinimumSize = new Vector2(32, 0);
        timeRow.AddChild(playTime);
        timeOfDay = form.Slider(0, 24, sky.TimeOfDayHours, 1.0 / 60, value => sky.SetTimeOfDay(value),
            "Scrub the time of day", 190);
        timeRow.AddChild(timeOfDay);
        timeOfDayLabel = new Label
        {
            CustomMinimumSize = new Vector2(48, 0)
        };
        timeRow.AddChild(timeOfDayLabel);
        var speedRow = form.Row();
        speedRow.AddChild(new Label { Text = "Cycle speed" });
        string[] speedLabels = ["0.5x", "1x", "2x", "5x", "20x"];
        float[] speedValues = [0.5f, 1f, 2f, 5f, 20f];
        var speed = form.Options(speedLabels, 1, id => sky.Speed = speedValues[id]);
        speedRow.AddChild(speed);
        form.Add(new HSeparator());
        status = form.Add(new Label
        {
            Text = message,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(294, 55)
        });
        ui.AddChild(statusBar = new StatusBar(statusLog));
        zoneLabel = form.Text("", 17, new Color("5bd8c2"));
        zoneLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        continentLabel = form.Text("", 12, new Color("aabac8"));
        minimap = form.Add(new TerrainMinimap
        {
            CustomMinimumSize = new Vector2(180, 180),
            MouseFilter = Control.MouseFilterEnum.Stop
        });
        minimap.Jump += key =>
        {
            tileX.Value = key.X;
            tileY.Value = key.Y;
            Teleport(key);
        };
    }
}
