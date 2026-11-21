using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>Adapts the editor's database-backed overlays to the streamer's optional region sink.</summary>
public sealed class RegionOverlaySink(Func<bool> databaseEnabled, Func<ushort?> mapId, Func<RegionOverlay[]> overlays) : ITileRegionSink
{
    public ushort? MapId => mapId();

    public bool Enabled => databaseEnabled();

    public void SetRegion(CreatureRegion? region)
    {
        foreach (var overlay in overlays())
        {
            overlay.SetRegion(region);
        }
    }
}
