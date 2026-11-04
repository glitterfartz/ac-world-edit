using Godot;
using ACWorldEdit.Assets;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// Terrain streaming: the wanted set around the camera, the worker tile read, the soft-budget
/// upload and the nearest-missing-tile schedule. One worker at a time; results for another
/// generation are discarded.
/// </summary>
public partial class ViewerShell
{
    void UpdateWanted()
    {
        if (world == null)
        {
            return;
        }

        var center = CameraTile();
        UpdateWantedTiles(center);
        UpdateSpawnRegion(center);
    }

    /// <summary>Fill the wanted set and release anything outside the hysteresis ring.</summary>
    void UpdateWantedTiles(TileKey center)
    {
        wanted.Clear();
        wanted.UnionWith(TileWindow.Wanted(world!.Tiles, center, radius));

        // Release with one extra ring of hysteresis beyond the load radius: turning or wobbling
        // right at the boundary must not repeatedly tear down and reload the same tile, which is
        // what made streamed terrain/scenery appear to flicker in and out while flying.
        foreach (var key in loaded.Keys.Where(k => !TileWindow.KeepLoaded(k, center, radius)).ToArray())
        {
            renderer.Release(loaded[key]);
            loaded.Remove(key);
            chunkAreas.Remove(key);
            terrainHeights.Remove(key);
            objects.RemoveTile(key);
        }

        if (building != null && !TileWindow.KeepLoaded(building.Tile.Key, center, radius))
        {
            if (buildingNode != null)
            {
                renderer.Release(buildingNode);
            }

            building = null;
            buildingNode = null;
        }
    }

    /// <summary>The sticky database query window, then one region fan-out to every overlay.</summary>
    void UpdateSpawnRegion(TileKey center)
    {
        ACWorldEdit.Foundation.CreatureRegion? spawnRegion = null;
        if (currentMapId.HasValue && wanted.Count > 0)
        {
            // Sticky with generous padding: recomputing this tightly around `center` every time the
            // camera crosses a tile boundary forced CreatureOverlay/GameObjectOverlay to fully clear
            // and re-query (visibly "Loading nearby creatures…") every ~533 yards of ordinary flight,
            // even though the SQL query already re-centers its own nearest-radius filter far more
            // cheaply via the quantized camera point. Keep the same padded region until the camera
            // actually nears its edge, or the radius setting itself changes.
            const int pad = 3;
            if (spawnRegionCenter == null || radius != spawnRegionLoadRadius ||
                Math.Abs(center.X - spawnRegionCenter.Value.X) > pad || Math.Abs(center.Y - spawnRegionCenter.Value.Y) > pad)
            {
                spawnRegionCenter = center;
                spawnRegionLoadRadius = radius;
            }

            var regionCenter = spawnRegionCenter.Value;
            int r = radius + pad;
            spawnRegion = new(currentMapId.Value, regionCenter.X - r, regionCenter.X + r, regionCenter.Y - r, regionCenter.Y + r);
        }
        else if (currentMapId.HasValue && world!.Tiles.Count == 0)
        {
            spawnRegionCenter = null;
            spawnRegion = new(currentMapId.Value, Math.Clamp(center.X - radius, 0, 63), Math.Clamp(center.X + radius, 0, 63), Math.Clamp(center.Y - radius, 0, 63), Math.Clamp(center.Y + radius, 0, 63));
        }
        else
        {
            spawnRegionCenter = null;
        }

        // Without a matching database no layer may query, whatever its toggle says.
        var queryRegion = databaseEnabled ? spawnRegion : null;
        foreach (var overlay in overlays)
        {
            overlay.SetRegion(queryRegion);
        }
    }

    /// <summary>
    /// Worker-only tile/texture decode with cancellation checks. Carry decode errors to the main
    /// thread for explicit warnings.
    /// </summary>
    static PreparedTile Prepare(IClientFiles assets, WorldMap map, TileKey key, int version, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var tile = map.ReadTile(assets, key);
        var decoded = new Dictionary<string, BlpTexture>(StringComparer.OrdinalIgnoreCase);
        List<string> errors = [];
        List<string> notices = [];
        DecodeLayers(assets, tile, decoded, errors, notices, token);
        DecodeLiquids(assets, tile, decoded, errors, token);
        token.ThrowIfCancellationRequested();
        var terrain = TerrainRenderer.PrepareTile(tile, WorkerOptions(token));
        return new(version, tile, decoded, errors, notices, terrain);
    }

    static ParallelOptions WorkerOptions(CancellationToken token) => new()
    {
        CancellationToken = token,
        MaxDegreeOfParallelism = Math.Max(1, Math.Min(4, System.Environment.ProcessorCount / 2))
    };

    static void DecodeLayers(IClientFiles assets, TerrainTile tile, Dictionary<string, BlpTexture> decoded,
        List<string> errors, List<string> notices, CancellationToken token)
    {
        int overLayered = tile.Chunks.Count(c => c.DroppedLayers > 0);
        if (overLayered > 0)
        {
            notices.Add("Some chunks define more than 4 texture layers; the extra layers are not drawn.");
        }

        var layerPaths = tile.Chunks.SelectMany(c => c.Layers).SelectMany(l => new[] { l.Texture, l.HeightTexture }).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (layerPaths.Contains(""))
        {
            notices.Add("Some terrain layers name FileDataID 0; they are drawn with the fallback texture.");
            layerPaths.Remove("");
        }

        // Each BLP is read and decoded independently; MPQ reads run concurrently, and CASC serializes
        // the read itself but still overlaps the decode.
        var results = new (BlpTexture? Texture, string? Error)[layerPaths.Count];
        Parallel.For(0, layerPaths.Count, WorkerOptions(token), i =>
        {
            try
            {
                results[i] = (BlpTexture.Decode(assets.Read(layerPaths[i])), null);
            }
            catch (Exception e)
            {
                results[i] = (null, layerPaths[i] + ": " + e.Message);
            }
        });
        for (int i = 0; i < layerPaths.Count; i++)
        {
            if (results[i].Texture != null)
            {
                decoded[layerPaths[i]] = results[i].Texture!;
            }
            else
            {
                errors.Add(results[i].Error!);
            }
        }
    }

    static void DecodeLiquids(IClientFiles assets, TerrainTile tile, Dictionary<string, BlpTexture> decoded, List<string> errors, CancellationToken token)
    {
        for (int i = 0; i < tile.Liquids.Count; i++)
        {
            var surface = tile.Liquids[i];
            uint type = surface.LiquidType!.Value;
            token.ThrowIfCancellationRequested();
            try
            {
                decoded[surface.Texture] = LiquidTextures.For(assets).Decode(type);
                tile.Liquids[i] = surface with
                {
                    LiquidKind = LiquidTextures.For(assets).Kind(type),
                    LiquidStyle = LiquidTextures.For(assets).Style(type)
                };
            }
            catch (Exception e)
            {
                errors.Add(surface.Texture + ": " + e.Message);
            }
        }
    }

    /// <summary>
    /// Lift the camera above the nearest terrain sample after loading/teleport. Navigation
    /// assistance is not collision or movement snapping.
    /// </summary>
    void RaiseAboveTerrain(Node3D tile)
    {
        // Pick nearest mesh vertex to prevent spawning inside mountains.
        float best = float.MaxValue, height = camera.Position.Y;
        foreach (var mesh in tile.GetChildren().OfType<MeshInstance3D>())
        {
            var arrays = mesh.Mesh.SurfaceGetArrays(0);
            var vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            foreach (var v in vertices)
            {
                var pos = tile.Position + v;
                float d = new Vector2(pos.X - camera.Position.X, pos.Z - camera.Position.Z).LengthSquared();
                if (d < best)
                {
                    best = d;
                    height = pos.Y;
                }
            }
        }

        if (camera.Position.Y < height + 35)
        {
            camera.Position = new Vector3(camera.Position.X, height + 80, camera.Position.Z);
        }
    }

    /// <summary>
    /// Consume completed tile reads, reject obsolete generations, build within a soft per-frame
    /// budget and schedule the nearest missing tile. Explicit map/store arguments preserve a stable
    /// non-null worker source.
    /// </summary>
    void UpdateTerrainStreaming(WorldMap activeWorld, IClientFiles activeAssets)
    {
        UpdateWanted();
        ConsumePendingRead();
        UploadBuilding(activeWorld);
        if (pending == null && building == null)
        {
            ScheduleNextTile(activeAssets, activeWorld);
        }
    }

    void ConsumePendingRead()
    {
        if (pending is not { IsCompleted: true })
        {
            return;
        }

        try
        {
            var result = pending.GetAwaiter().GetResult();
            // Cancellation is cooperative: a finished read may still belong to the
            // previous map or camera region. Check both identity and current interest.
            if (result.Generation == generation && wanted.Contains(result.Tile.Key))
            {
                building = result;
                buildIndex = 0;
                buildingNode = renderer.BeginTile(result.Tile.Key);
                AddChild(buildingNode);
                if (result.Errors.Count > 0)
                {
                    message = $"{result.Errors.Count} missing/unsupported textures. See log.";
                    foreach (string e in result.Errors)
                    {
                        GD.PushWarning(e);
                    }
                }

                // Per-map limitations repeat on every tile, so each is reported once per map.
                foreach (string notice in result.Notices)
                {
                    if (reportedNotices.Add(notice))
                    {
                        GD.PushWarning(notice);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e)
        {
            if (pendingKey.HasValue)
            {
                failed.Add(pendingKey.Value);
            }

            pendingKey = null;
            message = "Tile error: " + e.Message;
            GD.PushError(message);
        }

        if (pendingKey.HasValue)
        {
            failed.Remove(pendingKey.Value);
        }

        pendingKey = null;
        pending = null;
    }

    void UploadBuilding(WorldMap activeWorld)
    {
        if (building == null || buildingNode == null)
        {
            return;
        }

        var uploadStarted = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            // Steps: the splat atlas, one per layer group, one per liquid. Leave 0.5 ms headroom
            // within the 2 ms upload target; one indivisible upload can still exceed it.
            var groups = building.Terrain.Groups;
            int steps = 1 + groups.Length + building.Tile.Liquids.Count;
            for (int budget = 0; budget < 8 && buildIndex < steps && (budget == 0 || System.Diagnostics.Stopwatch.GetElapsedTime(uploadStarted).TotalMilliseconds < 1.5); budget++, buildIndex++)
            {
                if (buildIndex == 0)
                {
                    renderer.BeginTerrain(buildingNode, building.Terrain);
                }
                else if (buildIndex <= groups.Length)
                {
                    renderer.AddGroup(buildingNode, groups[buildIndex - 1], building.Textures, activeWorld.BigAlpha);
                }
                else
                {
                    renderer.AddLiquid(buildingNode, building.Tile.Liquids[buildIndex - 1 - groups.Length], building.Textures);
                }
            }

            if (buildIndex == steps)
            {
                PublishBuilding(building, buildingNode);
                building = null;
                buildingNode = null;
            }
        }
        catch (Exception e)
        {
            if (building != null)
            {
                failed.Add(building.Tile.Key);
            }

            if (buildingNode != null)
            {
                renderer.Release(buildingNode);
            }

            building = null;
            buildingNode = null;
            message = "Render error: " + e.Message;
            GD.PushError(message);
        }
    }

    /// <summary>A finished upload becomes the loaded tile for this key.</summary>
    void PublishBuilding(PreparedTile tile, Node3D node)
    {
        loaded[tile.Tile.Key] = node;
        PerfTilePublished(tile.Tile.Key);
        var ids = new uint[256];
        foreach (var c in tile.Tile.Chunks)
        {
            ids[c.Y * 16 + c.X] = c.AreaId;
        }

        chunkAreas[tile.Tile.Key] = ids;
        terrainHeights[tile.Tile.Key] = new TerrainHeights(tile.Tile);
        objects.SetTile(tile.Tile.Key, tile.Tile.Objects);
        if (CameraTile() == tile.Tile.Key && !cameraFocused)
        {
            RaiseAboveTerrain(node);
        }
    }

    void ScheduleNextTile(IClientFiles activeAssets, WorldMap activeWorld)
    {
        var next = TileWindow.NextMissing(wanted, loaded.ContainsKey, failed.Contains, CameraTile());
        if (next.HasValue)
        {
            var key = next.Value;
            var assets = activeAssets;
            var map = activeWorld;
            int version = generation;
            var token = cancellation.Token;
            // Failed tiles are tracked separately; obsolete requests can be retried when revisited.
            pendingKey = key;
            PerfTileScheduled(key);
            pending = Task.Run(() => Prepare(assets, map, key, version, token));
        }
    }
}
