namespace ACWorldEdit;

/// <summary>A tile rectangle a database layer should query, in viewer tile axes.</summary>
public readonly record struct TileRegion(ushort MapId, int MinTileX, int MaxTileX, int MinTileY, int MaxTileY);

/// <summary>
/// Optional consumer of the streamer's sticky database query window. The render library knows nothing
/// about database layers; the editor implements this over its overlays and a game client leaves it
/// null.
/// </summary>
public interface ITileRegionSink
{
    /// <summary>The open map, or null when no map has terrain.</summary>
    ushort? MapId { get; }

    /// <summary>Whether any layer may query (a database is configured and the layer is on).</summary>
    bool Enabled { get; }

    /// <summary>Publish the current query window; null clears the layers.</summary>
    void SetRegion(TileRegion? region);
}
