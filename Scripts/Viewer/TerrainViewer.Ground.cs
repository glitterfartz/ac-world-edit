using Godot;
using ACWorldEdit.Assets;

namespace ACWorldEdit;

/// <summary>
/// On-demand ground probes for creature placement. Runs on the main thread only when a command or
/// drag release asks, never per frame, and fails closed rather than guessing a surface.
/// </summary>
public partial class TerrainViewer
{
    /// <summary>
    /// Combine loaded terrain and ready WMO floors under a viewer-space point, then choose the
    /// highest surface at or below point.Y + GroundProbe.StepUp. Hidden building layers or WMOs
    /// still loading over the point refuse to snap, because the terrain beneath a building would be
    /// a wrong answer.
    /// </summary>
    GroundHit GroundAt(Vector3 point)
    {
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        double Elapsed() => System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        if (!sidebar.WmosEnabled)
        {
            return new(null, "buildings layer is hidden; show it to snap.", Elapsed());
        }

        List<float> heights = [];
        var key = new TileKey((int)Math.Floor(point.X / TerrainTile.Size + 32), (int)Math.Floor(point.Z / TerrainTile.Size + 32));
        bool terrainLoaded = terrainHeights.TryGetValue(key, out var tile);
        if (tile != null && tile.Probe(point.X - (key.X - 32) * TerrainTile.Size, point.Z - (key.Y - 32) * TerrainTile.Size) is float terrain)
        {
            heights.Add(terrain);
        }

        var (floors, pending) = objects.FloorHeights(point);
        if (pending > 0)
        {
            return new(null, "building geometry is still loading here; try again shortly.", Elapsed());
        }

        heights.AddRange(floors);
        if (GroundProbe.Choose(heights, point.Y) is float height)
        {
            return new(height, floors.Count > 0 ? "building floor or terrain" : "terrain", Elapsed());
        }

        // A WMO-only map has no terrain tiles, so only report an unloaded tile where one exists.
        string reason = !terrainLoaded && world != null && world.Tiles.Contains(key)
            ? "terrain tile is not loaded yet."
            : $"no ground within {GroundProbe.StepUp:0} yd above or anywhere below.";
        return new(null, reason, Elapsed());
    }
}
