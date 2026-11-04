using Godot;
using ACWorldEdit.Assets;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// Owns terrain streaming: the wanted set around the camera, the worker tile read, the soft-budget
/// upload, the nearest-missing schedule and the loaded-tile bookkeeping. One worker at a time;
/// results from another generation are discarded. The shell injects the renderer, world scene,
/// camera and the three view decisions (database availability, overlays, current map).
/// </summary>
public sealed partial class TerrainStreamer
{
    readonly Node owner;
    readonly TerrainRenderer renderer;
    readonly WorldObjectScene objects;
    readonly Camera3D camera;
    readonly Func<bool> databaseEnabled;
    readonly Func<RegionOverlay[]> overlays;
    readonly Func<ushort?> currentMapId;
    readonly Func<bool> cameraFocused;
    readonly Action<string> setMessage;
    readonly Action<TileKey> tileScheduled;
    readonly Action<TileKey> tilePublished;

    // Three distinct ownership states: wanted keys, one worker/building result, and fully uploaded
    // tile roots. Generation guards prevent map/reload races.
    readonly Dictionary<TileKey, Node3D> loaded = [];
    // Per-tile MCNK AreaId grid (chunkY*16+chunkX), kept only for the current zone/continent label.
    readonly Dictionary<TileKey, uint[]> chunkAreas = [];
    // CPU height copies for ground snapping; same lifetime as the loaded tile node.
    readonly Dictionary<TileKey, TerrainHeights> terrainHeights = [];
    readonly HashSet<TileKey> failed = [];
    readonly HashSet<string> reportedNotices = [];
    readonly HashSet<TileKey> wanted = [];
    Task<PreparedTile>? pending;
    TileKey? pendingKey;
    CancellationTokenSource cancellation = new();
    PreparedTile? building;
    Node3D? buildingNode;
    int buildIndex, generation;
    // The SQL query region is stickier than the terrain load/release window; this remembers its
    // last recompute center/radius.
    TileKey? spawnRegionCenter;
    int spawnRegionLoadRadius = -1;

    public TerrainStreamer(Node owner, TerrainRenderer renderer, WorldObjectScene objects, Camera3D camera,
        Func<bool> databaseEnabled, Func<RegionOverlay[]> overlays, Func<ushort?> currentMapId,
        Func<bool> cameraFocused, Action<string> setMessage, Action<TileKey> tileScheduled, Action<TileKey> tilePublished)
    {
        this.owner = owner;
        this.renderer = renderer;
        this.objects = objects;
        this.camera = camera;
        this.databaseEnabled = databaseEnabled;
        this.overlays = overlays;
        this.currentMapId = currentMapId;
        this.cameraFocused = cameraFocused;
        this.setMessage = setMessage;
        this.tileScheduled = tileScheduled;
        this.tilePublished = tilePublished;
    }

    /// <summary>The map being streamed; the session sets it before each pump.</summary>
    public WorldMap? CurrentWorld { get; set; }

    /// <summary>Load radius in tiles, changed by the left panel.</summary>
    public int Radius { get; set; } = 1;

    public Dictionary<TileKey, Node3D> Loaded => loaded;
    public Dictionary<TileKey, uint[]> ChunkAreas => chunkAreas;
    public Dictionary<TileKey, TerrainHeights> TerrainHeights => terrainHeights;
    public HashSet<TileKey> Failed => failed;
    public HashSet<TileKey> Wanted => wanted;
    public Task<PreparedTile>? Pending => pending;
    public PreparedTile? Building => building;
    public int Generation => generation;

    /// <summary>Generation-tagged worker result containing CPU data/pixels, never Godot resources.</summary>
    public sealed record PreparedTile(int Generation, TerrainTile Tile, Dictionary<string, BlpTexture> Textures, List<string> Errors, List<string> Notices, TerrainRenderer.PreparedTerrain Terrain);

    public void UpdateWanted()
    {
        if (CurrentWorld == null)
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
        wanted.UnionWith(TileWindow.Wanted(CurrentWorld!.Tiles, center, Radius));

        // Release with one extra ring of hysteresis beyond the load radius: turning or wobbling
        // right at the boundary must not repeatedly tear down and reload the same tile, which is
        // what made streamed terrain/scenery appear to flicker in and out while flying.
        foreach (var key in loaded.Keys.Where(k => !TileWindow.KeepLoaded(k, center, Radius)).ToArray())
        {
            renderer.Release(loaded[key]);
            loaded.Remove(key);
            chunkAreas.Remove(key);
            terrainHeights.Remove(key);
            objects.RemoveTile(key);
        }

        if (building != null && !TileWindow.KeepLoaded(building.Tile.Key, center, Radius))
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
        CreatureRegion? spawnRegion = null;
        if (currentMapId().HasValue && wanted.Count > 0)
        {
            // Sticky with generous padding: recomputing this tightly around `center` every time the
            // camera crosses a tile boundary forced CreatureOverlay/GameObjectOverlay to fully clear
            // and re-query (visibly "Loading nearby creatures…") every ~533 yards of ordinary flight,
            // even though the SQL query already re-centers its own nearest-radius filter far more
            // cheaply via the quantized camera point. Keep the same padded region until the camera
            // actually nears its edge, or the radius setting itself changes.
            const int pad = 3;
            if (spawnRegionCenter == null || Radius != spawnRegionLoadRadius ||
                Math.Abs(center.X - spawnRegionCenter.Value.X) > pad || Math.Abs(center.Y - spawnRegionCenter.Value.Y) > pad)
            {
                spawnRegionCenter = center;
                spawnRegionLoadRadius = Radius;
            }

            var regionCenter = spawnRegionCenter.Value;
            int r = Radius + pad;
            spawnRegion = new(currentMapId()!.Value, regionCenter.X - r, regionCenter.X + r, regionCenter.Y - r, regionCenter.Y + r);
        }
        else if (currentMapId().HasValue && CurrentWorld!.Tiles.Count == 0)
        {
            spawnRegionCenter = null;
            spawnRegion = new(currentMapId()!.Value, Math.Clamp(center.X - Radius, 0, 63), Math.Clamp(center.X + Radius, 0, 63), Math.Clamp(center.Y - Radius, 0, 63), Math.Clamp(center.Y + Radius, 0, 63));
        }
        else
        {
            spawnRegionCenter = null;
        }

        // Without a matching database no layer may query, whatever its toggle says.
        var queryRegion = databaseEnabled() ? spawnRegion : null;
        foreach (var overlay in overlays())
        {
            overlay.SetRegion(queryRegion);
        }
    }

    /// <summary>Invalidate the generation, release every loaded/building node and clear the window.</summary>
    public void Reset()
    {
        generation++;
        cancellation.Cancel();
        cancellation.Dispose();
        cancellation = new();
        foreach (var node in loaded.Values)
        {
            renderer.Release(node);
        }

        loaded.Clear();
        chunkAreas.Clear();
        terrainHeights.Clear();
        spawnRegionCenter = null;
        if (buildingNode != null)
        {
            renderer.Release(buildingNode);
        }

        buildingNode = null;
        building = null;
        buildIndex = 0;
        failed.Clear();
        reportedNotices.Clear();
        wanted.Clear();
    }

    /// <summary>Wait out the worker read, then forget it; used before the store is disposed.</summary>
    public async Task WaitForPendingAsync()
    {
        if (pending == null)
        {
            return;
        }

        try
        {
            await pending;
        }
        catch
        {
        }

        pending = null;
    }

    /// <summary>Cancel streaming for shutdown; the token source is not replaced.</summary>
    public void Shutdown()
    {
        cancellation.Cancel();
        cancellation.Dispose();
    }

    /// <summary>Release one loaded tile so it can stream back in (the content smoke's reload case).</summary>
    public void UnloadTile(TileKey key)
    {
        if (loaded.Remove(key, out var node))
        {
            renderer.Release(node);
            chunkAreas.Remove(key);
            terrainHeights.Remove(key);
        }
    }

    TileKey CameraTile() => TileGrid.FromView(camera.Position.X, camera.Position.Z);
}
