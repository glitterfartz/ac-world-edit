using Godot;
using ACWorldEdit.Data;
using ACWorldEdit.Assets;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// The shell side of the client session. The session controller owns the flow; the host supplies the
/// mutable client/map state and the few UI and diagnostics actions the flow performs. Keeping this a
/// seam lets the session be exercised with fakes and keeps the controller free of the shell's fields.
/// </summary>
public interface IViewerSessionHost
{
    IClientFiles? Store { get; set; }
    WorldMap? World { get; set; }
    ushort? CurrentMapId { get; set; }
    List<MapInfo> MapList { get; set; }
    ClientLighting? Lighting { get; set; }
    AreaLookup? Areas { get; set; }
    bool DatabaseEnabled { get; set; }
    IClientLookup? Lookup { get; set; }
    ClientProfile Profile { get; set; }
    string DataDirectory { get; set; }
    SessionState State { get; }
    bool Busy { get; }
    string Message { get; set; }
    string StartMessage { get; set; }
    ulong StartMessageShown { get; set; }
    float Yaw { get; set; }
    float Pitch { get; set; }
    bool AnySmoke { get; }
    bool Smoke { get; }
    TileKey SmokeOriginal { get; set; }
    int SmokeStage { get; set; }
    double SmokeTime { get; set; }
    bool WaterSmoke { get; }
    int WaterStage { get; }
    bool WaterInterior { get; }
    Camera3D Camera { get; }
    SkyLighting Sky { get; }
    ModelLibrary Models { get; }
    TerrainStreamer Streamer { get; }
    NavigationOverlay Navigation { get; }
    WorldObjectScene Objects { get; }
    RegionOverlay[] Overlays { get; }
    CreaturePropertiesWindow? CreatureProperties { get; }
    GameObjectPropertiesWindow? GameObjectProperties { get; }
    ContentDrafts Drafts { get; }
    OptionButton MapsSelect { get; }
    SpinBox TileX { get; }
    SpinBox TileY { get; }
    ModelThumbnails? Thumbnails { get; }
    Func<ClientKind, string, string?, Action<string>?, ClientServices> OpenClient { get; }

    void SetSession(SessionState state);
    void ResetTerrain();
    void ApplyClientProfile(string description);
    Task ApplyHotfixes(RetailCreatureCatalog catalog);
    void OfferListfile();
    Task<TeleportLocation?> PickStartLocation();
    void OpenMapAtServerPose(uint mapId, double x, double y, double z);
    void FocusPose(WorldPose pose);
    void Teleport(TileKey key);
    string ThumbnailDirectory(ClientKind kind);
}

/// <summary>
/// The client/terrain session: open a client, release the previous store safely, open a map, clear
/// terrain and dispose the store when no worker can still read it. The store and map state live on
/// the host; this class owns the order and the failure handling.
/// </summary>
public sealed class ViewerSession
{
    readonly IViewerSessionHost host;

    public ViewerSession(IViewerSessionHost host) => this.host = host;

    /// <summary>Open the host's chosen client: release the old store, load on a worker, then start at a pose.</summary>
    public async Task LoadClient()
    {
        if (host.DataDirectory.Length == 0)
        {
            host.Message = "Choose your client folder (File → Load client…).";
            return;
        }

        if (host.Busy)
        {
            return;
        }

        host.SetSession(SessionState.Loading);
        host.ResetTerrain();
        var chosen = host.Profile;
        await ReleaseClientStore();
        host.Message = chosen.LoadingMessage;
        try
        {
            await LoadClientStore(chosen);
        }
        catch (Exception e)
        {
            host.Message = "Client error: " + e.Message;
            GD.PushError(host.Message);
        }
        finally
        {
            if (host.State == SessionState.Loading)
            {
                host.SetSession(host.Store != null && host.MapList.Count > 0 ? SessionState.Ready : SessionState.Unavailable);
            }
        }

        if (host.Store != null && chosen.OffersListfile)
        {
            host.OfferListfile();
        }

        if (host.Store != null && host.MapList.Count > 0)
        {
            await StartAtLoadedClient();
        }
    }

    /// <summary>Wait out the current tile read, then drop the old store and model cache.</summary>
    async Task ReleaseClientStore()
    {
        var old = host.Store;
        host.Store = null;
        await host.Streamer.WaitForPendingAsync();
        host.Sky.SetStarsModel(null);
        await host.Models.SetStore(null);
        old?.Dispose();
    }

    /// <summary>Open the selected profile on a worker and adopt everything it publishes.</summary>
    async Task LoadClientStore(ClientProfile chosen)
    {
        string path = host.DataDirectory;
        var kind = chosen.Kind;
        string listfile = UserSettings.ListfilePath();
        var result = await Task.Run(() => host.OpenClient(kind, path, listfile, GD.PushWarning));
        host.Store = result.Files;
        host.MapList = result.Maps;
        host.Navigation.Configure(kind, result.NavigationParents);
        host.Lighting = result.Lighting;
        host.Sky.SetCelestialTextures(CelestialTextures.Load(host.Store, "Textures/sunCenter.blp"), CelestialTextures.Load(host.Store, "Textures/moon.blp"), CelestialTextures.Load(host.Store, "Textures/moon02.blp"), CelestialTextures.Load(host.Store, "Textures/sunGlare.blp"), CelestialTextures.Load(host.Store, "Textures/moonGlare.blp"));
        host.Areas = result.Areas;
        host.DatabaseEnabled = result.DatabaseEnabled;
        host.Lookup = result.Lookup;
        host.ApplyClientProfile(result.Description);
        if (host.DatabaseEnabled && result.Creatures is RetailCreatureCatalog retailCreatures)
        {
            await host.ApplyHotfixes(retailCreatures);
        }

        host.MapsSelect.Clear();
        await host.Models.SetStore(host.Store, result.Creatures, result.GameObjects);
        host.Sky.SetStarsModel(host.Store.Exists(StarsModelPath) ? StarsModelPath : null);
        host.Thumbnails?.Reset(host.ThumbnailDirectory(kind));
        int selected = FillMapList();
        host.MapsSelect.Select(selected);
        host.Message = result.Description;
        host.SetSession(host.MapList.Count == 0 ? SessionState.Unavailable : SessionState.Ready);
    }

    /// <summary>Refill the map dropdown and return the row to select (Azeroth, or Blackfathom for the water smoke).</summary>
    int FillMapList()
    {
        int selected = 0;
        for (int i = 0; i < host.MapList.Count; i++)
        {
            host.MapsSelect.AddItem($"{host.MapList[i].Name} ({host.MapList[i].Id})");
            if (host.MapList[i].Directory.Equals(host.WaterInterior ? "Blackfathom" : "Azeroth", StringComparison.OrdinalIgnoreCase))
            {
                selected = i;
            }
        }

        return selected;
    }

    /// <summary>Open the remembered/default tile, or a random game_tele spot when interactive.</summary>
    async Task StartAtLoadedClient()
    {
        // Opens at a random game_tele spot (any map) when the database is reachable.
        TeleportLocation? start = null;
        if (!host.AnySmoke)
        {
            start = await host.PickStartLocation();
            if (start != null)
            {
                host.OpenMapAtServerPose(start.Map, start.X, start.Y, start.Z);
            }
        }

        await OpenMap();
        if (start != null && host.CurrentMapId == start.Map)
        {
            host.FocusPose(new WorldPose(start.Map, start.X, start.Y, start.Z, start.Orientation));
            host.Message = host.StartMessage = $"Started at {start.Name}.";
            host.StartMessageShown = Time.GetTicksMsec();
        }
    }

    /// <summary>
    /// Read WDT on a worker and choose outdoor navigation or an interior camera start. Map changes
    /// invalidate terrain and creature generations.
    /// </summary>
    public async Task OpenMap()
    {
        if (host.State != SessionState.Ready || host.Store == null || host.MapsSelect.Selected < 0)
        {
            return;
        }

        host.SetSession(SessionState.Loading);
        host.ResetTerrain();
        host.Message = "Reading terrain index…";
        var assets = host.Store;
        string name = host.MapList[host.MapsSelect.Selected].Directory;
        try
        {
            var requested = await ReadMapInfo(assets, name);
            requested = host.World!.Tiles.Count > 0 ? EnterTerrain(requested) : EnterInterior(requested);
            host.Message = host.World.Tiles.Count > 0 ? $"{name} · {host.World.Tiles.Count} terrain tiles" : $"{name} · interior WMO";
            if (host.Smoke)
            {
                host.SmokeOriginal = requested;
                host.SmokeStage = 0;
                host.SmokeTime = 0;
            }
        }
        catch (Exception e)
        {
            host.Message = "Map error: " + e.Message;
            GD.PushWarning(host.Message);
        }
        finally
        {
            host.SetSession(host.Store != null && host.MapList.Count > 0 ? SessionState.Ready : SessionState.Unavailable);
        }
    }

    /// <summary>Read the WDT, publish map-level systems, and return the requested start tile.</summary>
    async Task<TileKey> ReadMapInfo(IClientFiles assets, string name)
    {
        var info = host.MapList[host.MapsSelect.Selected];
        // Retail names the WDT by FileDataID; MPQ clients by path.
        string wdtPath = info.WdtFileId != 0 ? AssetRef.FromFileId(info.WdtFileId) : $"World/Maps/{name}/{name}.wdt";
        host.World = await Task.Run(() => new WorldMap(name, assets.Read(wdtPath)));
        host.CurrentMapId = checked((ushort)host.MapList[host.MapsSelect.Selected].Id);
        host.Navigation.SetMap(host.CurrentMapId);
        host.Sky.Retail = host.Profile.Kind == ClientKind.Retail;
        host.Sky.SetMap(host.Lighting, host.CurrentMapId, host.World.Tiles.Count == 0);
        // Retail maps may carry placed fog volumes in a separate _fogs.wdt.
        host.Sky.SetFogVolumes(host.Profile.Kind == ClientKind.Retail ? await Task.Run(() => FogVolumes.ApplyConditions(FogVolumes.Load(assets, name), host.Lighting?.FogVolumePresence)) : []);
        host.TileX.Editable = host.TileY.Editable = host.World.Tiles.Count > 0;
        host.Objects.SetGlobals(host.World.GlobalObjects);
        return host.WaterSmoke && host.WaterStage == 0 ? new TileKey(32, 49) : new TileKey((int)host.TileX.Value, (int)host.TileY.Value);
    }

    /// <summary>Teleport into the requested tile, falling back to the nearest one the map has.</summary>
    TileKey EnterTerrain(TileKey requested)
    {
        if (!host.World!.Tiles.Contains(requested))
        {
            requested = host.World.Tiles.OrderBy(k => Math.Abs(k.X - requested.X) + Math.Abs(k.Y - requested.Y)).First();
        }

        host.TileX.Value = requested.X;
        host.TileY.Value = requested.Y;
        host.Teleport(requested);
        return requested;
    }

    /// <summary>Interior WMO: place the camera at the first global object instead of a terrain tile.</summary>
    TileKey EnterInterior(TileKey requested)
    {
        var placement = host.World!.GlobalObjects[0];
        host.Camera.Position = new(placement.Position.X, placement.Position.Y + 2.5f, placement.Position.Z + 10);
        host.Camera.Rotation = new(-0.12f, 0, 0);
        host.Yaw = 0;
        host.Pitch = -0.12f;
        host.Streamer.UpdateWanted();
        return requested;
    }

    /// <summary>Invalidate the terrain window and clear transient objects/creatures.</summary>
    public void ResetTerrain()
    {
        host.Navigation?.SetMap(null);
        host.Streamer.Reset();
        host.World = null;
        host.CurrentMapId = null;
        foreach (var overlay in host.Overlays)
        {
            overlay.SetRegion(null);
        }

        host.CreatureProperties?.Reset();
        host.GameObjectProperties?.Reset();
        host.Objects.Clear();
    }

    /// <summary>Cancel streaming and delay store disposal until worker reads complete.</summary>
    public void ExitTree()
    {
        host.Streamer.Shutdown();
        var assets = host.Store;
        var modelTask = host.Models?.Stop() ?? Task.CompletedTask;
        if (host.Streamer.Pending != null || !modelTask.IsCompleted)
        {
            _ = Task.WhenAll(host.Streamer.Pending ?? Task.CompletedTask, modelTask).ContinueWith(_ => assets?.Dispose());
        }
        else
        {
            assets?.Dispose();
        }

        host.Drafts?.Dispose();
    }

    // The client's star model, drawn like a skybox model; both clients have it under this name.
    const string StarsModelPath = "Environments/Stars/stars.m2";
}
