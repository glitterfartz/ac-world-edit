using Godot;

namespace ACWorldEdit;

/// <summary>
/// The per-frame pump: streaming, HUD text, overlay updates and diagnostics, in the order the
/// overlays and models expect. Perf marks keep their indices; the perf smoke reads the histograms.
/// </summary>
public partial class ViewerShell
{
    /// <summary>
    /// Move the camera, publish current-generation results and incrementally upload/update systems.
    /// One worker and soft upload budgets limit frame stalls.
    /// </summary>
    public override void _Process(double delta)
    {
        navigation.UpdateRegion(CameraTile(), radius);
        PerfMark(0);
        if (capture && !busy)
        {
            input.Move(delta);
        }

        if (!busy && world != null && store != null)
        {
            streamer.UpdateTerrainStreaming(world, store);
        }

        PerfMark(1);
        UpdatePendingLocate(delta);
        ExpireStartMessage();
        UpdateHud(delta);
        PerfMark(2);
        UpdateOverlays();
        models.Update(camera);
        PerfMark(6);
        sidebar.UpdateCounts();
        PollPending(delta);
        PerfMark(7);
        RunSmokeChecks(delta);
    }

    /// <summary>Drop the transient "started at" line once its display window has passed.</summary>
    void ExpireStartMessage()
    {
        if (startMessage.Length > 0 && Time.GetTicksMsec() - startMessageShown > StartMessageMs)
        {
            if (message == startMessage)
            {
                message = "";
            }

            startMessage = "";
        }
    }

    /// <summary>Status line, menu/toolbar state, the day-night readout and the minimap.</summary>
    void UpdateHud(double delta)
    {
        status.Text = message;
        menu.Refresh();
        toolbar.Refresh();
        sky.Update(camera, delta);
        timeOfDay.SetValueNoSignal(sky.TimeOfDayHours);
        int hh = (int)sky.TimeOfDayHours, mm = (int)((sky.TimeOfDayHours - hh) * 60);
        timeOfDayLabel.Text = $"{hh:00}:{mm:00}";
        var (zone, continent) = CurrentLocation();
        zoneLabel.Text = zone;
        zoneLabel.Visible = zone.Length > 0;
        continentLabel.Text = continent;
        var current = CameraTile();
        int cells = renderer.CellCount;
        statusBar.Telemetry = $"{world?.Name ?? "No map"}    |    Tile {current.X}, {current.Y}    |    Position {CameraServer().X:0.0}, {CameraServer().Y:0.0}, {CameraServer().Z:0.0}    |    {loaded.Count}/{wanted.Count} tiles · {cells:N0} cells    |    {objects.ObjectCount:N0} objects · {creatures.ModelCreatureCount} creatures · {gameObjects.ModelObjectCount} GameObjects{(models.Busy ? " (loading)" : "")}    |    {speed:0} yd/s    |    {Engine.GetFramesPerSecond()} FPS";
        minimap.Sync(world, loaded.Keys, current, camera.Position);
    }

    /// <summary>Overlay repaint and the properties window's selection tracking.</summary>
    void UpdateOverlays()
    {
        creatures.Update(camera);
        PerfMark(3);
        gameObjects.Update(camera);
        PerfMark(4);
        graveyards.Update(camera);
        waypoints.Update(camera);
        areaTriggers.Update(camera);
        creatureProps.Track(databaseEnabled ? creatures.SelectedSpawn : null);
        gameObjectProps.Track(databaseEnabled ? gameObjects.SelectedSpawn : null);
        PerfMark(5);
    }
}
