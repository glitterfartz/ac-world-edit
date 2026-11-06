using Godot;
using ACWorldEdit.Assets;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// Standalone camera/input/archive/streaming coordinator. UI lives in a partial file and opt-in
/// smoke workflows under Diagnostics; GPU work stays on the Godot thread.
/// </summary>
public partial class ViewerShell : Node3D
{
    IClientFiles? store;
    // The client's star model, drawn like a skybox model; both clients have it under this name.
    const string StarsModelPath = "Environments/Stars/stars.m2";
    WorldMap? world;
    TerrainRenderer renderer = null!;
    ModelLibrary models = null!;
    WorldObjectScene objects = null!;
    Camera3D camera = null!;
    SkyLighting sky = null!;
    ClientLighting? lighting;
    AreaLookup? areas;
    // Terrain ownership (wanted/loaded/failed/building) lives in the streamer; these forward for the
    // existing diagnostics and HUD readers without copying state.
    TerrainStreamer streamer = null!;
    Dictionary<TileKey, Node3D> loaded => streamer.Loaded;
    Dictionary<TileKey, uint[]> chunkAreas => streamer.ChunkAreas;
    Dictionary<TileKey, TerrainHeights> terrainHeights => streamer.TerrainHeights;
    HashSet<TileKey> failed => streamer.Failed;
    HashSet<TileKey> wanted => streamer.Wanted;
    Task<TerrainStreamer.PreparedTile>? pending => streamer.Pending;
    TerrainStreamer.PreparedTile? building => streamer.Building;
    int generation => streamer.Generation;
    PlacementTool placement = null!;
    ViewportDropSurface dropSurface = null!;
    ViewerInput input = null!;
    bool capture { get => input.Capture; set => input.Capture = value; }
    // One session state drives both the busy guards and the Open world button.
    SessionState session = SessionState.Unavailable;
    bool busy => session == SessionState.Loading;

    void SetSession(SessionState value)
    {
        session = value;
        if (open != null)
        {
            open.Disabled = value != SessionState.Ready;
        }
    }
    float yaw { get => input.Yaw; set => input.Yaw = value; }
    float pitch { get => input.Pitch; set => input.Pitch = value; }
    float speed { get => input.Speed; set => input.Speed = value; }
    int radius { get => streamer.Radius; set => streamer.Radius = value; }
    OptionButton maps = null!;
    SpinBox tileX = null!, tileY = null!, distance = null!;
    // Active client profile and one remembered folder per profile (chosen in File → Load client…); empty until chosen.
    ClientProfile profile = ClientProfile.For(ClientKind.Wrath335);
    string wrathDirectory = "", retailDirectory = "";
    // False when the open client cannot be described by the world database (Retail): database layers,
    // teleports and the content editors stay off instead of querying a mismatched server.
    bool databaseEnabled = true;
    IClientLookup? clientLookup;
    string dataDirectory
    {
        get => profile.Kind == ClientKind.Retail ? retailDirectory : wrathDirectory;
        set
        {
            if (profile.Kind == ClientKind.Retail)
            {
                retailDirectory = value;
            }
            else
            {
                wrathDirectory = value;
            }
        }
    }

    Button open = null!;
    Label status = null!, zoneLabel = null!, continentLabel = null!;
    Slider timeOfDay = null!;
    Label timeOfDayLabel = null!;
    Button playTime = null!;
    List<MapInfo> mapList = [];
    TerrainMinimap minimap = null!;
    string message = "Opening client…";
    readonly StatusLog statusLog = new();
    StatusBar statusBar = null!;
    // The "Started at" note clears itself after a few seconds unless something else replaced it.
    string startMessage = "";
    ulong startMessageShown;
    const ulong StartMessageMs = 6000;
    /// <summary>
    /// User-entered world database connection shared by every database-backed layer and editor.
    /// </summary>
    readonly ConnectionForm connection = new();
    /// <summary>The dialect follows the client kind; the form holds the settings saved for that profile.</summary>
    WorldConnection DatabaseConnection() => connection.Connection;
    CreatureOverlay creatures = null!;
    GameObjectOverlay gameObjects = null!;
    GraveyardOverlay graveyards = null!;
    WaypointOverlay waypoints = null!;
    AreaTriggerOverlay areaTriggers = null!;
    /// <summary>The database-backed layers, in draw order, so a region step fans out from one list.</summary>
    RegionOverlay[] overlays = [];
    SceneSidebar sidebar = null!;
    NavigationOverlay navigation = null!;
    ushort? currentMapId;
    /// <summary>
    /// Create systems/UI on the Godot thread, configure opt-in checks and asynchronously open
    /// archives.
    /// </summary>
    public override async void _Ready()
    {
        ContentDraftFiles.UseProfile(UserSettings.LoadProfile());
        BuildSceneSystems();
        BuildUi();
        navigation.Configure(profile.Kind, null);
        WarnMissingDependencies(UserSettings.LoadProfile());
        connection.Applied += () => _ = LoadTeleports();
        _ = LoadTeleports();
        ConfigureSmokeChecks();
        _ = StartupPrompts();
        await LoadClient();
    }

    /// <summary>Renderer, camera, sky, overlays, placement tool and navigation, before any UI exists.</summary>
    void BuildSceneSystems()
    {
        BuildWorldNodes();
        BuildOverlays();
        input = new ViewerInput(this);
        streamer = new TerrainStreamer(this, renderer, objects, camera, () => databaseEnabled, () => overlays, () => currentMapId,
            () => cameraFocused, text => message = text, PerfTileScheduled, PerfTilePublished);
        viewerSession = new ViewerSession(this);
        BuildTools();
    }

    /// <summary>The renderer, the placement root, the camera and the sky.</summary>
    void BuildWorldNodes()
    {
        renderer = new TerrainRenderer();
        models = new ModelLibrary();
        // Placements hang under their own node so the world scene is not the shell's direct child.
        var objectRoot = new Node3D { Name = "Objects" };
        AddChild(objectRoot);
        objects = new WorldObjectScene(objectRoot, models);
        camera = new Camera3D
        {
            Name = "Camera",
            Far = 8000,
            Near = 0.2f,
            Fov = 70,
            Current = true
        };
        AddChild(camera);
        sky = new SkyLighting(models);
        AddChild(sky);
    }

    /// <summary>The five database-backed layers, in draw order.</summary>
    void BuildOverlays()
    {
        creatures = new CreatureOverlay
        {
            Models = models,
            Connection = () => DatabaseConnection(),
            Lookup = () => clientLookup,
            Store = () => store,
            Thumbnails = () => thumbnails,
            Report = statusLog.Post,
            PerfProbe = creaturePerf
        };
        AddChild(creatures);
        creatures.FocusRequested += FocusPose;
        creatures.GroundAt = GroundAt;
        creatures.TerrainHeightAt = TerrainHeightAt;
        gameObjects = new GameObjectOverlay { Models = models, Connection = () => DatabaseConnection(), GroundAt = GroundAt, Report = statusLog.Post };
        AddChild(gameObjects);
        gameObjects.FocusRequested += FocusPose;
        graveyards = new GraveyardOverlay { Connection = () => DatabaseConnection(), Areas = () => areas, GroundAt = GroundAt, Report = statusLog.Post };
        AddChild(graveyards);
        graveyards.FocusRequested += FocusPose;
        waypoints = new WaypointOverlay { Connection = () => DatabaseConnection(), GroundAt = GroundAt, Report = statusLog.Post, FocusedCreature = () => creatures.SelectedGuid };
        AddChild(waypoints);
        creatures.WaypointSelection = () => waypoints.SelectedPath;
        creatures.WaypointPointSelection = () => waypoints.SelectedPath is { } path && waypoints.SelectedPoint is { } point ? (path, point) : null;
        creatures.PathCreated += id =>
        {
            // The new path lives only in the draft until exported, so select it from the
            // staged rows and bring the Waypoints tab up ready to extend it.
            waypoints.SelectPath(id);
            waypoints.AddingPoints = true;
            sidebar.ShowWaypointsTab();
        };
        waypoints.FocusRequested += FocusPose;
        areaTriggers = new AreaTriggerOverlay { Connection = () => DatabaseConnection(), Lookup = () => clientLookup };
        AddChild(areaTriggers);
        areaTriggers.FocusRequested += FocusPose;
        overlays = [creatures, gameObjects, graveyards, waypoints, areaTriggers];
    }

    /// <summary>The placement tool and the navigation overlay.</summary>
    void BuildTools()
    {
        // The drop surface is parented by BuildUi, but the placement tool and its callbacks need it now.
        dropSurface = new ViewportDropSurface();
        placement = new PlacementTool { Camera = camera, TerrainAt = TerrainHeightAt, GroundAt = GroundAt, Map = () => (ushort)currentMapId, Place = PlaceTemplateAsync };
        AddChild(placement);
        placement.OpenWorld = dropSurface;
        dropSurface.Hover = HoverTemplateDrag;
        dropSurface.Dropped = DropTemplate;
        dropSurface.Ended = EndTemplateDrag;
        navigation = new NavigationOverlay { Report = statusLog.Post, OcclusionDistance = NavigationOcclusion };
        AddChild(navigation);
    }

    /// <summary>
    /// Move to an available tile and refresh streaming. Adjust elevation from cached geometry now or
    /// when its pending upload completes.
    /// </summary>
    void Teleport(TileKey key)
    {
        if (world == null || !world.Tiles.Contains(key))
        {
            return;
        }

        cameraFocused = false;
        camera.Position = new Vector3((key.X - 32 + 0.5f) * TerrainTile.Size, 250, (key.Y - 32 + 0.5f) * TerrainTile.Size);
        yaw = 0;
        pitch = -0.55f;
        camera.Rotation = new Vector3(pitch, yaw, 0);
        streamer.UpdateWanted();
        // A loaded tile can supply elevation immediately; otherwise adjust when it finishes.
        if (loaded.TryGetValue(key, out var node))
        {
            streamer.RaiseAboveTerrain(node);
        }
    }

    /// <summary>
    /// Convert camera horizontal coordinates to floor-based tile indices. Interiors may have camera
    /// indices outside the terrain grid.
    /// </summary>
    TileKey CameraTile() => TileGrid.FromView(camera.Position.X, camera.Position.Z);


    /// <summary>
    /// Convert the camera's view-space position to server coordinates. The Y is the camera's eye height, not the ground.
    /// </summary>
    Vector3 CameraServer()
    {
        var position = ViewSpace.ToServer(camera.Position);
        return new Vector3((float)position.X, (float)position.Y, (float)position.Z);
    }

    /// <summary>
    /// Resolve the camera's current zone/subzone and continent for the UI title. Outdoor maps use the
    /// nearest loaded chunk's MCNK AreaId; interiors have no per-chunk area, so only the map's own
    /// display name is shown.
    /// </summary>
    (string Zone, string Continent) CurrentLocation()
    {
        string continent = maps.Selected >= 0 && maps.Selected < mapList.Count ? mapList[maps.Selected].Name : "";
        if (world == null)
        {
            return ("", "");
        }

        if (world.Tiles.Count == 0)
        {
            return (continent, "");
        }

        var tile = CameraTile();
        if (areas == null || !chunkAreas.TryGetValue(tile, out var ids) || !loaded.TryGetValue(tile, out var root))
        {
            return ("", continent);
        }

        int cx = Math.Clamp((int)((camera.Position.X - root.Position.X) / TerrainTile.ChunkSize), 0, 15);
        int cy = Math.Clamp((int)((camera.Position.Z - root.Position.Z) / TerrainTile.ChunkSize), 0, 15);
        var (zone, parent) = areas.Describe(ids[cy * 16 + cx]);
        return (parent != null ? $"{parent} — {zone}" : zone, continent);
    }
    /// <summary>
    /// True after a deliberate focus (fly to a creature, object or graveyard): the camera sits just
    /// above that target on purpose, so a tile finishing underneath must not lift it away. Cleared
    /// by keyboard movement and teleports, which keep the lift-above-terrain assistance.
    /// </summary>
    bool cameraFocused { get => input.CameraFocused; set => input.CameraFocused = value; }

    /// <summary>
    /// Move near a server pose and synchronize mouse-look angles before refreshing the region.
    /// </summary>
    void FocusPose(ACWorldEdit.Foundation.WorldPose pose)
    {
        cameraFocused = true;
        var position = ViewSpace.ToView(pose);
        camera.Position = position + new Vector3(0, 8, 15);
        camera.LookAt(position + Vector3.Up);
        yaw = camera.Rotation.Y;
        pitch = camera.Rotation.X;
        streamer.UpdateWanted();
    }






    public override void _UnhandledInput(InputEvent e) => input?.HandleUnhandled(e);
    public override void _Input(InputEvent e) => input?.HandleInput(e);
    public override void _Notification(int what) => input?.HandleNotification(what);
}
