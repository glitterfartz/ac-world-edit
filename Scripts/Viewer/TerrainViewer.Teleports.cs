using Godot;
using ACWorldEdit.Assets;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// game_tele bookmarks: read once at startup (and again whenever the database connection changes)
/// on a worker, shown in the left panel, and used to move the camera to a server position,
/// opening the target map first when it differs from the current one. Read-only; stale loads are dropped.
/// </summary>
public partial class TerrainViewer
{
    OptionButton teleports = null!;
    TeleportLocation[] teleportList = [];
    int teleportGeneration;

    void BuildTeleportUi(VBoxContainer box)
    {
        box.AddChild(new Label { Text = "Teleport" });
        teleports = new OptionButton { TooltipText = "Jump to a location from the game_tele table", Disabled = true };
        teleports.AddItem("Loading…");
        teleports.ItemSelected += async index => await TeleportTo((int)index);
        box.AddChild(teleports);
    }

    async Task LoadTeleports()
    {
        int current = ++teleportGeneration;
        if (!databaseEnabled)
        {
            teleportList = [];
            teleports.Clear();
            teleports.AddItem("Unavailable");
            teleports.Select(0);
            teleports.Disabled = true;
            teleports.TooltipText = "Teleports come from the world database, which is not used with this client.";
            return;
        }

        var settings = connection.Settings;
        TeleportLocation[] rows;
        string failure = "";
        try
        {
            rows = await Task.Run(() => new TeleportSource(settings).ReadAsync(CancellationToken.None));
        }
        catch (Exception error)
        {
            rows = [];
            failure = CreatureSource.SafeError(error);
        }

        if (current != teleportGeneration || !IsInstanceValid(teleports))
        {
            return;
        }

        teleportList = rows;
        teleports.Clear();
        teleports.AddItem(rows.Length > 0 ? $"Select a location ({rows.Length})" : failure.Length > 0 ? "Unavailable" : "No teleports");
        foreach (var row in rows)
        {
            teleports.AddItem(row.Name);
        }

        teleports.Select(0);
        teleports.Disabled = rows.Length == 0;
        teleports.TooltipText = failure.Length > 0 ? failure : "Jump to a location from the game_tele table";
    }

    /// <summary>
    /// A random game_tele location on Eastern Kingdoms (map 0) to open instead of the default tile.
    /// Null without a usable database connection, so the default location is used.
    /// </summary>
    async Task<TeleportLocation?> PickStartLocation()
    {
        if (!databaseEnabled)
        {
            return null;
        }

        var settings = connection.Settings;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var rows = await Task.Run(() => new TeleportSource(settings).ReadAsync(timeout.Token), timeout.Token);
            return TeleportSource.PickOnMap(rows, 0, Random.Shared);
        }
        catch (Exception error)
        {
            GD.Print("Start location: game_tele unavailable, using the default (" + CreatureSource.SafeError(error) + ")");
            return null;
        }
    }

    async Task TeleportTo(int index)
    {
        teleports.Select(0);
        if (index < 1 || index > teleportList.Length)
        {
            return;
        }

        var target = teleportList[index - 1];
        int mapIndex = mapList.FindIndex(m => m.Id == target.Map);
        if (store == null || mapIndex < 0)
        {
            message = $"{target.Name} is on map {target.Map}, which this client does not have.";
            return;
        }

        if (busy)
        {
            message = "The viewer is busy loading; try again in a moment.";
            return;
        }

        if (target.Map != currentMapId)
        {
            // OpenMap falls back to the nearest available tile when this one is missing.
            var view = WorldCoordinates.ToView(new(target.X, target.Y, target.Z));
            maps.Select(mapIndex);
            tileX.Value = Math.Clamp((int)Math.Floor(view.X / TerrainTile.Size + 32), 0, 63);
            tileY.Value = Math.Clamp((int)Math.Floor(view.Z / TerrainTile.Size + 32), 0, 63);
            await OpenMap();
            if (currentMapId != target.Map)
            {
                return;
            }
        }

        FocusPose(new WorldPose(target.Map, target.X, target.Y, target.Z, target.Orientation));
        message = $"Teleported to {target.Name}.";
    }
}
