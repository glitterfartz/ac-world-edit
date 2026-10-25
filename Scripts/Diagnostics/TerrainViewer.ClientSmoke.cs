using Godot;
using ACWorldEdit.Assets;

namespace ACWorldEdit;

/// <summary>
/// Opt-in check that the selected client profile opens: it waits for the client services, the
/// default map's WDT and, for a terrain map, its first streamed tile, prints one summary line and
/// exits. It needs no database, so it runs headless: <c>--headless -- --client-smoke</c> with
/// ACWORLD_CLIENT_PROFILE and the profile's folder variable set. It proves loading, not visual output.
/// </summary>
public partial class TerrainViewer
{
    bool clientSmoke, clientShot, clientShotMoved, clientShotFramed, clientShotWater, clientShotOverlays, clientShotOverlaysFocused;
    int clientShotFrames;
    TileKey? clientShotTile;
    double clientSmokeTime;

    /// <summary>
    /// World-space center of the vertices of the liquid mesh with the most vertices among the loaded
    /// tiles (--client-shot-water), or null when none is loaded. Averaging vertices rather than taking
    /// bounds keeps the target over the water when the batch spans the whole tile.
    /// </summary>
    Vector3? LargestLiquid()
    {
        Vector3? best = null;
        int bestCount = 0;
        foreach (var tile in loaded.Values)
        {
            if (tile.GetNodeOrNull<Node3D>("Liquids") is not { } container)
            {
                continue;
            }

            foreach (var mesh in container.GetChildren().OfType<MeshInstance3D>())
            {
                var vertices = mesh.Mesh.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                if (vertices.Length > bestCount)
                {
                    bestCount = vertices.Length;
                    best = mesh.GlobalTransform * (vertices.Aggregate(Vector3.Zero, (a, v) => a + v) / vertices.Length);
                }
            }
        }

        return best;
    }

    void ClientSmoke(double delta)
    {
        clientSmokeTime += delta;
        if (clientSmokeTime > 180)
        {
            SmokeFail($"CLIENT_SMOKE_TIMEOUT settled={TerrainSettled} loaded={loaded.Count}/{wanted.Count} modelsBusy={models.Busy} {message}");
            return;
        }

        if (busy)
        {
            return;
        }

        if (store == null || mapList.Count == 0)
        {
            // LoadClient reports its failure through the status message and then goes idle.
            if (clientSmokeTime > 5 && !message.StartsWith("Opening", StringComparison.Ordinal) && !message.StartsWith("Reading", StringComparison.Ordinal))
            {
                SmokeFail("CLIENT_SMOKE_NO_CLIENT " + message);
            }

            return;
        }

        if (world == null)
        {
            if (message.StartsWith("Map error", StringComparison.Ordinal))
            {
                SmokeFail("CLIENT_SMOKE_MAP_FAILED " + message);
            }

            return;
        }

        // A terrain map must also stream its first tile (worker decode plus main-thread upload); the
        // dummy headless renderer accepts the uploads, so this exercises the whole path but not the output.
        if (world.Tiles.Count > 0 && loaded.Count == 0)
        {
            if (message.StartsWith("Tile error", StringComparison.Ordinal) || message.StartsWith("Render error", StringComparison.Ordinal))
            {
                SmokeFail("CLIENT_SMOKE_TILE_FAILED " + message);
            }

            return;
        }

        // --client-shot additionally waits for the visible terrain to finish, lets a few frames render and
        // saves artifacts/client-shot.png for inspection. It needs a real window (not --headless).
        if (clientShot && DisplayServer.GetName() != "headless")
        {
            // --client-shot-tile=X,Y moves the camera over that tile first, then waits for it to finish.
            if (!clientShotMoved)
            {
                clientShotMoved = true;
                if (clientShotTile is { } target)
                {
                    Teleport(target);
                    return;
                }
            }

            if (pending != null || building != null || !wanted.All(loaded.ContainsKey) || models.Busy || clientShotFrames++ < 120)
            {
                return;
            }

            // --client-shot-overlays keeps the interface and shows the database layers over Goldshire:
            // creatures, GameObjects, graveyards and AreaTriggers, all read from the world database.
            if (clientShotOverlays)
            {
                if (!clientShotOverlaysFocused)
                {
                    // The poses below are Eastern Kingdoms coordinates; open that map first if another is showing.
                    if (currentMapId != 0)
                    {
                        int azeroth = mapList.FindIndex(m => m.Directory == "Azeroth");
                        if (azeroth >= 0 && maps.Selected != azeroth)
                        {
                            maps.Select(azeroth);
                            tileX.Value = 31;
                            tileY.Value = 49;
                            _ = OpenMap();
                        }

                        return;
                    }

                    clientShotOverlaysFocused = true;
                    areaTriggers.SetLayerEnabled(true);
                    FocusPose(new(0, -9465.5, 74, 56.8, 0));
                    clientShotFrames = 0;
                    return;
                }

                bool ready = creatures.Records.Count > 0 && gameObjects.Records.Count > 0 && graveyards.Sites.Count > 0 && areaTriggers.Triggers.Count > 0;
                if (!ready || creatures.IsLoading || gameObjects.IsLoading || areaTriggers.IsLoading || models.Busy || clientShotFrames++ < 240)
                {
                    return;
                }

                GD.Print($"CLIENT_SHOT_OVERLAYS creatures={creatures.Records.Count} models={creatures.ModelCreatureCount} gameobjects={gameObjects.Records.Count} models={gameObjects.ModelObjectCount} graveyards={graveyards.Sites.Count} triggers={areaTriggers.Triggers.Count}");
                GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath("res://artifacts/client-shot-overlays.png"));
                // TrinityCore graveyards export world_safe_locs and team-condition statements, so they are editable.
                graveyards.Select(graveyards.Sites[0].Record.Id);
                if (!graveyards.CanEdit)
                {
                    SmokeFail("CLIENT_SHOT_GRAVEYARD_READONLY a TrinityCore graveyard refused edits");
                    return;
                }

                graveyards.Deselect();
                GD.Print($"CLIENT_SMOKE_OK kind={store.Kind} database={databaseEnabled} overlays=True graveyardsEditable=True");
                GetTree().Quit();
                return;
            }

            // Frame the largest decoded building close up, and hide the interface so it does not cover the scene.
            if (!clientShotFramed)
            {
                clientShotFramed = true;
                if (clientShotWater && LargestLiquid() is { } lake)
                {
                    GD.Print($"CLIENT_SHOT_WATER center={lake}");
                    camera.Position = lake + new Vector3(0, 90, 140);
                    camera.LookAt(lake);
                    yaw = camera.Rotation.Y;
                    pitch = camera.Rotation.X;
                    clientShotFrames = 0;
                }
                else if (objects.ReadyWmoPlacements().Where(w => !w.BoundsMinimum.HasValue || !w.BoundsMaximum.HasValue || (w.BoundsMaximum.Value - w.BoundsMinimum.Value).Length() < 150).OrderByDescending(w => w.BoundsMinimum.HasValue && w.BoundsMaximum.HasValue ? (w.BoundsMaximum.Value - w.BoundsMinimum.Value).Length() : 0).FirstOrDefault() is { } building)
                {
                    var at = new Vector3(building.Position.X, building.Position.Y, building.Position.Z);
                    var size = building.BoundsMinimum.HasValue && building.BoundsMaximum.HasValue ? (building.BoundsMaximum.Value - building.BoundsMinimum.Value).Length() : 60;
                    var center = building.BoundsMinimum.HasValue && building.BoundsMaximum.HasValue ? (building.BoundsMaximum.Value + building.BoundsMinimum.Value) / 2 : building.Position;
                    var target = new Vector3(center.X, center.Y, center.Z);
                    GD.Print($"CLIENT_SHOT_BUILDING {building.Path} id={building.Id} size={size:F0} at={at}");
                    camera.Position = target + new Vector3(0, size * 0.5f, size * 1.1f);
                    camera.LookAt(target);
                    yaw = camera.Rotation.Y;
                    pitch = camera.Rotation.X;
                    clientShotFrames = 0;
                }

                foreach (var item in GetChildren().OfType<CanvasItem>())
                {
                    item.Visible = false;
                }

                foreach (var layer in GetChildren().OfType<CanvasLayer>())
                {
                    layer.Visible = false;
                }

                return;
            }

            GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath("res://artifacts/client-shot.png"));
        }

        GD.Print($"CLIENT_SMOKE_OK kind={store.Kind} build={store.Build} maps={mapList.Count} map={mapList[Math.Max(maps.Selected, 0)].Directory} tiles={world.Tiles.Count} streamed={loaded.Count} textures={renderer.TextureCount} globalWmos={world.GlobalObjects.Count} database={databaseEnabled} catalogs={models.Catalog != null} message=\"{message}\"");
        GetTree().Quit();
    }
}
