using Godot;
using ACWorldEdit.Assets;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// The shell's side of <see cref="IViewerSessionHost"/>: it exposes the mutable client/map state and
/// the UI/diagnostics actions the session flow calls, without the controller touching fields.
/// </summary>
public partial class ViewerShell : IViewerSessionHost
{
    ViewerSession viewerSession = null!;

    IClientFiles? IViewerSessionHost.Store { get => store; set => store = value; }
    WorldMap? IViewerSessionHost.World { get => world; set => world = value; }
    ushort? IViewerSessionHost.CurrentMapId { get => currentMapId; set => currentMapId = value; }
    List<MapInfo> IViewerSessionHost.MapList { get => mapList; set => mapList = value; }
    ClientLighting? IViewerSessionHost.Lighting { get => lighting; set => lighting = value; }
    AreaLookup? IViewerSessionHost.Areas { get => areas; set => areas = value; }
    bool IViewerSessionHost.DatabaseEnabled { get => databaseEnabled; set => databaseEnabled = value; }
    IClientLookup? IViewerSessionHost.Lookup { get => clientLookup; set => clientLookup = value; }
    ClientProfile IViewerSessionHost.Profile { get => profile; set => profile = value; }
    string IViewerSessionHost.DataDirectory { get => dataDirectory; set => dataDirectory = value; }
    SessionState IViewerSessionHost.State => session;
    bool IViewerSessionHost.Busy => busy;
    string IViewerSessionHost.Message { get => message; set => message = value; }
    string IViewerSessionHost.StartMessage { get => startMessage; set => startMessage = value; }
    ulong IViewerSessionHost.StartMessageShown { get => startMessageShown; set => startMessageShown = value; }
    float IViewerSessionHost.Yaw { get => yaw; set => yaw = value; }
    float IViewerSessionHost.Pitch { get => pitch; set => pitch = value; }
    bool IViewerSessionHost.AnySmoke => anySmoke;
    bool IViewerSessionHost.Smoke => smoke;
    TileKey IViewerSessionHost.SmokeOriginal { get => smokeOriginal; set => smokeOriginal = value; }
    int IViewerSessionHost.SmokeStage { get => smokeStage; set => smokeStage = value; }
    double IViewerSessionHost.SmokeTime { get => smokeTime; set => smokeTime = value; }
    bool IViewerSessionHost.WaterSmoke => waterSmoke;
    int IViewerSessionHost.WaterStage => waterStage;
    bool IViewerSessionHost.WaterInterior => waterInterior;
    Camera3D IViewerSessionHost.Camera => camera;
    SkyLighting IViewerSessionHost.Sky => sky;
    ModelLibrary IViewerSessionHost.Models => models;
    TerrainStreamer IViewerSessionHost.Streamer => streamer;
    NavigationOverlay IViewerSessionHost.Navigation => navigation;
    WorldObjectScene IViewerSessionHost.Objects => objects;
    RegionOverlay[] IViewerSessionHost.Overlays => overlays;
    CreaturePropertiesWindow? IViewerSessionHost.CreatureProperties => creatureProps;
    ContentDrafts IViewerSessionHost.Drafts => drafts;
    OptionButton IViewerSessionHost.MapsSelect => maps;
    SpinBox IViewerSessionHost.TileX => tileX;
    SpinBox IViewerSessionHost.TileY => tileY;
    ModelThumbnails? IViewerSessionHost.Thumbnails => thumbnails;
    Func<ClientKind, string, string?, Action<string>?, ClientServices> IViewerSessionHost.OpenClient => ClientServices.Open;

    void IViewerSessionHost.SetSession(SessionState state) => SetSession(state);
    void IViewerSessionHost.ResetTerrain() => ResetTerrain();
    void IViewerSessionHost.ApplyClientProfile(string description) => ApplyClientProfile(description);
    Task IViewerSessionHost.ApplyHotfixes(RetailCreatureCatalog catalog) => ApplyDisplayHotfixes(catalog);
    void IViewerSessionHost.OfferListfile() => OfferListfile();
    Task<TeleportLocation?> IViewerSessionHost.PickStartLocation() => PickStartLocation();
    void IViewerSessionHost.OpenMapAtServerPose(uint mapId, double x, double y, double z) => OpenMapAtServerPose(mapId, x, y, z);
    void IViewerSessionHost.FocusPose(WorldPose pose) => FocusPose(pose);
    void IViewerSessionHost.Teleport(TileKey key) => Teleport(key);
    string IViewerSessionHost.ThumbnailDirectory(ClientKind kind) => ThumbnailDirectory(kind);

    public override void _ExitTree() => viewerSession?.ExitTree();
}
