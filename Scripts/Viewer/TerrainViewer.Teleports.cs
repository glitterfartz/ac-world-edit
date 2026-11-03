using Godot;
using ACWorldEdit.Assets;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// game_tele bookmarks and same-map server XYZ navigation. Bookmarks are read once at startup
/// (and again whenever the database connection changes)
/// on a worker, shown in the left panel, and used to move the camera to a server position,
/// opening the target map first when it differs from the current one. Read-only; stale loads are dropped.
/// </summary>
public partial class TerrainViewer
{
    OptionButton teleports = null!;
    LineEdit telePosition = null!;
    TeleportLocation[] teleportList = [];
    int teleportGeneration;

    void BuildTeleportUi(VBoxContainer box)
    {
        box.AddChild(new Label { Text = "Teleport" });
        teleports = new OptionButton { Name = "TeleportLocations", TooltipText = "Jump to a location from the game_tele table", Disabled = true };
        teleports.AddItem("Loading…");
        teleports.ItemSelected += async index => await TeleportTo((int)index);
        box.AddChild(teleports);
        box.AddChild(new Label { Text = "Teleport to XYZ (current map)" });
        var positionRow = new HBoxContainer();
        box.AddChild(positionRow);
        telePosition = new LineEdit
        {
            Name = "TeleportPosition",
            PlaceholderText = "X, Y, Z",
            TooltipText = "Server coordinates on the current map. Use decimal points; press Enter or Go.",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        telePosition.TextSubmitted += TeleportToPosition;
        positionRow.AddChild(telePosition);
        var go = new Button { Name = "TeleportPositionGo", Text = "Go" };
        go.Pressed += () => TeleportToPosition(telePosition.Text);
        positionRow.AddChild(go);
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

        var settings = DatabaseConnection();
        TeleportLocation[] rows;
        string failure = "";
        try
        {
            rows = await Task.Run(() => DBSource.Open(settings).ReadTeleportsAsync(CancellationToken.None));
        }
        catch (Exception error)
        {
            rows = [];
            failure = DbErrors.Safe(error);
        }

        if (current != teleportGeneration || !IsInstanceValid(teleports))
        {
            return;
        }

        teleportList = rows;
        teleports.Clear();
        teleports.AddItem(rows.Length > 0 ? $"Select a location (game_tele) ({rows.Length})" : failure.Length > 0 ? "Unavailable" : "No teleports");
        foreach (var row in rows)
        {
            teleports.AddItem(row.Name);
        }

        teleports.Select(0);
        teleports.Disabled = rows.Length == 0;
        teleports.TooltipText = failure.Length > 0 ? failure : "Jump to a location from the game_tele table";
    }

    /// <summary>
    /// A random game_tele location on a continent the client has (the Wrath continents, or the retail open-world maps), to open instead of the default tile.
    /// Null without a usable database connection, so the default location is used.
    /// </summary>
    async Task<TeleportLocation?> PickStartLocation()
    {
        if (!databaseEnabled)
        {
            return null;
        }

        var settings = DatabaseConnection();
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var rows = await Task.Run(() => DBSource.Open(settings).ReadTeleportsAsync(timeout.Token), timeout.Token);
            // Wrath has four known continents; the retail Map table says which maps are open world.
            var available = profile.Kind == ClientKind.Retail
                ? mapList.Where(m => m.Continent).Select(m => (ushort)m.Id).ToHashSet()
                : mapList.Select(m => (ushort)m.Id).Where(TeleportLocations.Continents.Contains).ToHashSet();
            return TeleportLocations.Pick(rows, available, Random.Shared);
        }
        catch (Exception error)
        {
            GD.Print("Start location: game_tele unavailable, using the default (" + DbErrors.Safe(error) + ")");
            return null;
        }
    }

    /// <summary>Move to exact server coordinates without consulting bookmarks or changing maps.</summary>
    void TeleportToPosition(string text)
    {
        var parts = text.Split(',');
        static bool Coordinate(string value, out float coordinate) =>
            float.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out coordinate)
            && float.IsFinite(coordinate);
        if (parts.Length != 3 || !Coordinate(parts[0], out float x) || !Coordinate(parts[1], out float y) || !Coordinate(parts[2], out float z))
        {
            message = "Enter three finite server coordinates: X, Y, Z (use decimal points).";
            return;
        }

        if (busy)
        {
            message = "The viewer is busy loading; try again in a moment.";
            return;
        }

        if (world == null || store == null || !currentMapId.HasValue)
        {
            message = "Open a map before teleporting to coordinates.";
            return;
        }

        // Coordinate entry is an eye position, unlike FocusPose's offset view of an object.
        // Keep the current heading and stop later terrain uploads lifting the requested Z.
        cameraFocused = true;
        camera.Position = ViewSpace.ToView(x, y, z);
        UpdateWanted();
        message = FormattableString.Invariant($"Teleported to {x}, {y}, {z}.");
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
