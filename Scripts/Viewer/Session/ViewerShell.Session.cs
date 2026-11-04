using Godot;
using ACWorldEdit.Assets;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// The client/terrain session: open a client, release the previous store safely, open a map and
/// clear terrain. The store is only disposed once no worker can still read it.
/// </summary>
public partial class ViewerShell
{
    /// <summary>
    /// Cancel/await terrain and model readers before replacing the client store. Dispose the old
    /// files only when no worker can still read them. The client services (files, map list, lighting,
    /// areas) are built on a worker for the selected profile.
    /// </summary>
    async Task LoadClient()
    {
        if (dataDirectory.Length == 0)
        {
            message = "Choose your client folder (File → Load client…).";
            return;
        }

        if (busy)
        {
            return;
        }

        SetSession(SessionState.Loading);
        ResetTerrain();
        var chosen = profile;
        await ReleaseClientStore();
        message = chosen.LoadingMessage;
        try
        {
            await LoadClientStore(chosen);
        }
        catch (Exception e)
        {
            message = "Client error: " + e.Message;
            GD.PushError(message);
        }
        finally
        {
            if (session == SessionState.Loading)
            {
                SetSession(store != null && mapList.Count > 0 ? SessionState.Ready : SessionState.Unavailable);
            }
        }

        if (store != null && chosen.OffersListfile)
        {
            OfferListfile();
        }

        if (store != null && mapList.Count > 0)
        {
            await StartAtLoadedClient();
        }
    }

    /// <summary>Wait out the current tile read, then drop the old store and model cache.</summary>
    async Task ReleaseClientStore()
    {
        var old = store;
        store = null;
        await streamer.WaitForPendingAsync();
        sky.SetStarsModel(null);
        await models.SetStore(null);
        old?.Dispose();
    }

    /// <summary>Open the selected profile on a worker and adopt everything it publishes.</summary>
    async Task LoadClientStore(ClientProfile chosen)
    {
        string path = dataDirectory;
        var kind = chosen.Kind;
        string listfile = UserSettings.ListfilePath();
        var result = await Task.Run(() => ClientServices.Open(kind, path, listfile, GD.PushWarning));
        store = result.Files;
        mapList = result.Maps;
        navigation.Configure(kind, result.NavigationParents);
        lighting = result.Lighting;
        sky.SetCelestialTextures(CelestialTextures.Load(store, "Textures/sunCenter.blp"), CelestialTextures.Load(store, "Textures/moon.blp"), CelestialTextures.Load(store, "Textures/moon02.blp"), CelestialTextures.Load(store, "Textures/sunGlare.blp"), CelestialTextures.Load(store, "Textures/moonGlare.blp"));
        areas = result.Areas;
        databaseEnabled = result.DatabaseEnabled;
        clientLookup = result.Lookup;
        ApplyClientProfile(result.Description);
        if (databaseEnabled && result.Creatures is RetailCreatureCatalog retailCreatures)
        {
            await ApplyDisplayHotfixes(retailCreatures);
        }

        maps.Clear();
        await models.SetStore(store, result.Creatures, result.GameObjects);
        sky.SetStarsModel(store.Exists(StarsModelPath) ? StarsModelPath : null);
        thumbnails?.Reset(ThumbnailDirectory(kind));
        int selected = FillMapList();
        maps.Select(selected);
        message = result.Description;
        SetSession(mapList.Count == 0 ? SessionState.Unavailable : SessionState.Ready);
    }

    /// <summary>Refill the map dropdown and return the row to select (Azeroth, or Blackfathom for the water smoke).</summary>
    int FillMapList()
    {
        int selected = 0;
        for (int i = 0; i < mapList.Count; i++)
        {
            maps.AddItem($"{mapList[i].Name} ({mapList[i].Id})");
            if (mapList[i].Directory.Equals(waterInterior ? "Blackfathom" : "Azeroth", StringComparison.OrdinalIgnoreCase))
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
        if (!anySmoke)
        {
            start = await PickStartLocation();
            if (start != null)
            {
                OpenMapAtServerPose(start.Map, start.X, start.Y, start.Z);
            }
        }

        await OpenMap();
        if (start != null && currentMapId == start.Map)
        {
            FocusPose(new WorldPose(start.Map, start.X, start.Y, start.Z, start.Orientation));
            message = startMessage = $"Started at {start.Name}.";
            startMessageShown = Time.GetTicksMsec();
        }
    }

    /// <summary>
    /// Read WDT on a worker and choose outdoor navigation or an interior camera start. Map changes
    /// invalidate terrain and creature generations.
    /// </summary>
    async Task OpenMap()
    {
        if (session != SessionState.Ready || store == null || maps.Selected < 0)
        {
            return;
        }

        SetSession(SessionState.Loading);
        ResetTerrain();
        message = "Reading terrain index…";
        var assets = store;
        string name = mapList[maps.Selected].Directory;
        try
        {
            var requested = await ReadMapInfo(assets, name);
            requested = world!.Tiles.Count > 0 ? EnterTerrain(requested) : EnterInterior(requested);
            message = world.Tiles.Count > 0 ? $"{name} · {world.Tiles.Count} terrain tiles" : $"{name} · interior WMO";
            if (smoke)
            {
                smokeOriginal = requested;
                smokeStage = 0;
                smokeTime = 0;
            }
        }
        catch (Exception e)
        {
            message = "Map error: " + e.Message;
            GD.PushWarning(message);
        }
        finally
        {
            SetSession(store != null && mapList.Count > 0 ? SessionState.Ready : SessionState.Unavailable);
        }
    }

    /// <summary>Read the WDT, publish map-level systems, and return the requested start tile.</summary>
    async Task<TileKey> ReadMapInfo(IClientFiles assets, string name)
    {
        var info = mapList[maps.Selected];
        // Retail names the WDT by FileDataID; MPQ clients by path.
        string wdtPath = info.WdtFileId != 0 ? AssetRef.FromFileId(info.WdtFileId) : $"World/Maps/{name}/{name}.wdt";
        world = await Task.Run(() => new WorldMap(name, assets.Read(wdtPath)));
        currentMapId = checked((ushort)mapList[maps.Selected].Id);
        navigation.SetMap(currentMapId);
        sky.Retail = profile.Kind == ClientKind.Retail;
        sky.SetMap(lighting, currentMapId, world.Tiles.Count == 0);
        // Retail maps may carry placed fog volumes in a separate _fogs.wdt.
        sky.SetFogVolumes(profile.Kind == ClientKind.Retail ? await Task.Run(() => FogVolumes.ApplyConditions(FogVolumes.Load(assets, name), lighting?.FogVolumePresence)) : []);
        tileX.Editable = tileY.Editable = world.Tiles.Count > 0;
        objects.SetGlobals(world.GlobalObjects);
        return waterSmoke && waterStage == 0 ? new TileKey(32, 49) : new TileKey((int)tileX.Value, (int)tileY.Value);
    }

    /// <summary>Teleport into the requested tile, falling back to the nearest one the map has.</summary>
    TileKey EnterTerrain(TileKey requested)
    {
        if (!world!.Tiles.Contains(requested))
        {
            requested = world.Tiles.OrderBy(k => Math.Abs(k.X - requested.X) + Math.Abs(k.Y - requested.Y)).First();
        }

        tileX.Value = requested.X;
        tileY.Value = requested.Y;
        Teleport(requested);
        return requested;
    }

    /// <summary>Interior WMO: place the camera at the first global object instead of a terrain tile.</summary>
    TileKey EnterInterior(TileKey requested)
    {
        var placement = world!.GlobalObjects[0];
        camera.Position = new(placement.Position.X, placement.Position.Y + 2.5f, placement.Position.Z + 10);
        camera.Rotation = new(-0.12f, 0, 0);
        yaw = 0;
        pitch = -0.12f;
        streamer.UpdateWanted();
        return requested;
    }

    /// <summary>
    /// Invalidate pending results, release complete/partial tiles and clear transient
    /// objects/creatures. Scene nodes do not own source assets or durable drafts.
    /// </summary>
    void ResetTerrain()
    {
        navigation?.SetMap(null);
        streamer.Reset();
        world = null;
        currentMapId = null;
        foreach (var overlay in overlays)
        {
            overlay.SetRegion(null);
        }

        creatureProps?.Reset();
        objects.Clear();
    }

    /// <summary>
    /// Cancel streaming and delay store disposal until worker reads complete. Closing the viewer
    /// never mutates client/server data.
    /// </summary>
    public override void _ExitTree()
    {
        streamer.Shutdown();
        var assets = store;
        var modelTask = models?.Stop() ?? Task.CompletedTask;
        if (streamer.Pending != null || !modelTask.IsCompleted)
        {
            _ = Task.WhenAll(streamer.Pending ?? Task.CompletedTask, modelTask).ContinueWith(_ => assets?.Dispose());
        }
        else
        {
            assets?.Dispose();
        }

        drafts?.Dispose();
    }
}
