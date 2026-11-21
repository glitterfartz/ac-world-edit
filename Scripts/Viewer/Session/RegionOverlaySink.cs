using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>Adapts the editor's database-backed overlays to the streamer's optional region sink.</summary>
public sealed class RegionOverlaySink(Func<bool> databaseEnabled, Func<ushort?> mapId, Func<RegionOverlay[]> overlays) : ITileRegionSink
{
    public ushort? MapId => mapId();

    public bool Enabled => databaseEnabled();

    public void SetRegion(TileRegion? region)
    {
        var query = region is { } r ? new CreatureRegion(r.MapId, r.MinTileX, r.MaxTileX, r.MinTileY, r.MaxTileY) : null;
        foreach (var overlay in overlays())
        {
            overlay.SetRegion(query);
        }
    }
}
