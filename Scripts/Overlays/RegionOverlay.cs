using Godot;
using ACWorldEdit.Assets;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// State and plumbing every database-backed region layer shares: the connection, the layer switch,
/// the streamed region and query center, and the status line. Each layer decides in
/// <see cref="OnRegionChanged"/> how a region step restarts it, and in <see cref="Restart"/> what a
/// hard or soft restart clears.
/// </summary>
public abstract partial class RegionOverlay : Node3D
{
    public Func<WorldConnection> Connection { get; set; } = () => WorldConnection.Empty;
    /// <summary>The open client's reference data (retail only); layers that need it ask for it when a query starts.</summary>
    public Func<IClientLookup?> Lookup { get; set; } = () => null;
    public bool LayerEnabled { get; private set; }
    public string QueryStatus => queryStatus;

    protected CreatureRegion? region;
    protected SpawnQueryPoint? point;
    protected string queryStatus;

    protected RegionOverlay(bool layerEnabled, string initialStatus)
    {
        LayerEnabled = layerEnabled;
        queryStatus = initialStatus;
    }

    /// <summary>Whether Refresh() wipes the display (true) or keeps it until the new result arrives.</summary>
    protected virtual bool RefreshIsHard => true;

    public void SetRegion(CreatureRegion? value)
    {
        if (region == value)
        {
            return;
        }

        bool mapChanged = region?.Map != value?.Map;
        region = value;
        OnRegionChanged(mapChanged);
    }

    public void SetLayerEnabled(bool value)
    {
        if (LayerEnabled != value)
        {
            LayerEnabled = value;
            Restart(true);
        }
    }

    /// <summary>Invalidate the snapshot and request fresh data through normal cancellation.</summary>
    public void Refresh() => Restart(RefreshIsHard);

    /// <summary>The region was replaced (already stored in <c>region</c>); reset the query point and restart as the layer needs.</summary>
    protected abstract void OnRegionChanged(bool mapChanged);

    /// <summary>
    /// Cancel and reissue the query. A hard restart (map, layer or filter change) frees every marker
    /// and the selection; a soft one keeps the display until the new result arrives.
    /// </summary>
    protected abstract void Restart(bool hard);
}
