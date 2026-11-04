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
    // Three distinct ownership states: wanted keys, one worker/building result, and
    // fully uploaded tile roots. Generation guards prevent map/reload races.
    readonly Dictionary<TileKey, Node3D> loaded = [];
    // Per-tile MCNK AreaId grid (chunkY*16+chunkX), kept only for the current zone/continent label;
    // CPU terrain data is otherwise discarded once a tile finishes uploading.
    readonly Dictionary<TileKey, uint[]> chunkAreas = [];
    // CPU height copies for ground snapping; same lifetime as the loaded tile node.
    readonly Dictionary<TileKey, TerrainHeights> terrainHeights = [];
    PlacementTool placement = null!;
    readonly HashSet<TileKey> failed = [];
    readonly HashSet<string> reportedNotices = [];
    readonly HashSet<TileKey> wanted = [];
    Task<PreparedTile>? pending;
    TileKey? pendingKey;
    CancellationTokenSource cancellation = new();
    PreparedTile? building;
    Node3D? buildingNode;
    int buildIndex, generation;
    bool capture;
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
    float yaw, pitch = -0.4f, speed = 70;
    int radius = 1;
    // The SQL query region is intentionally stickier than the terrain load/release window (see
    // UpdateWanted); these remember its last recompute center/radius.
    TileKey? spawnRegionCenter;
    int spawnRegionLoadRadius = -1;
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
    /// Generation-tagged worker result containing CPU data/pixels, never Godot resources. Obsolete
    /// generations cannot be published.
    /// </summary>
    sealed record PreparedTile(int Generation, TerrainTile Tile, Dictionary<string, BlpTexture> Textures, List<string> Errors, List<string> Notices, TerrainRenderer.PreparedTerrain Terrain);
    /// <summary>
    /// Create systems/UI on the Godot thread, configure opt-in checks and asynchronously open
    /// archives.
    /// </summary>
    public override async void _Ready()
    {
        ContentDraftFiles.UseProfile(UserSettings.LoadProfile());
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
        creatures = new CreatureOverlay
        {
            Models = models,
            Connection = () => DatabaseConnection(),
            Report = statusLog.Post
        };
        AddChild(creatures);
        creatures.FocusRequested += FocusPose;
        creatures.GroundAt = GroundAt;
        gameObjects = new GameObjectOverlay { Models = models, Connection = () => DatabaseConnection(), GroundAt = GroundAt, Report = statusLog.Post };
        AddChild(gameObjects);
        gameObjects.FocusRequested += FocusPose;
        graveyards = new GraveyardOverlay { Connection = () => DatabaseConnection(), Areas = () => areas, GroundAt = GroundAt, Report = statusLog.Post };
        AddChild(graveyards);
        graveyards.FocusRequested += FocusPose;
        waypoints = new WaypointOverlay { Connection = () => DatabaseConnection(), GroundAt = GroundAt, Report = statusLog.Post, FocusedCreature = () => creatures.SelectedGuid };
        AddChild(waypoints);
        waypoints.FocusRequested += FocusPose;
        areaTriggers = new AreaTriggerOverlay { Connection = () => DatabaseConnection(), Lookup = () => clientLookup };
        AddChild(areaTriggers);
        areaTriggers.FocusRequested += FocusPose;
        overlays = [creatures, gameObjects, graveyards, waypoints, areaTriggers];
        placement = new PlacementTool { Camera = camera, TerrainAt = TerrainHeightAt, GroundAt = GroundAt, Map = () => (ushort)currentMapId, Place = PlaceTemplateAsync };
        AddChild(placement);
        creatures.PlaceRequested += (entry, name) => placement.Arm(new(SpawnKind.Creature, entry, name));
        gameObjects.PlaceRequested += (entry, name) => placement.Arm(new(SpawnKind.GameObject, entry, name));
        placement.Changed += template =>
        {
            creatures.PlaceCard.SetPlacing(template?.Kind == SpawnKind.Creature ? template.Name : null);
            gameObjects.PlaceCard.SetPlacing(template?.Kind == SpawnKind.GameObject ? template.Name : null);
            if (template != null)
            {
                statusLog.Post($"Placing {template.Name}. Esc stops.");
            }
        };
        navigation = new NavigationOverlay { Report = statusLog.Post, OcclusionDistance = NavigationOcclusion };
        AddChild(navigation);
        BuildUi();
        navigation.Configure(profile.Kind, null);
        WarnMissingDependencies(UserSettings.LoadProfile());
        connection.Applied += () => _ = LoadTeleports();
        _ = LoadTeleports();
        ConfigureSmokeChecks();
        _ = StartupPrompts();
        await LoadClient();
    }

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
        var old = store;
        store = null;
        if (pending != null)
        {
            try
            {
                await pending;
            }
            catch
            {
            }

            pending = null;
        }

        sky.SetStarsModel(null);
        await models.SetStore(null);
        old?.Dispose();
        message = profile.LoadingMessage;
        string path = dataDirectory;
        var chosen = profile;
        var kind = chosen.Kind;
        string listfile = UserSettings.ListfilePath();
        try
        {
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
            int selected = 0;
            await models.SetStore(store, result.Creatures, result.GameObjects);
            sky.SetStarsModel(store.Exists(StarsModelPath) ? StarsModelPath : null);
            thumbnails?.Reset(ThumbnailDirectory(kind));
            for (int i = 0; i < mapList.Count; i++)
            {
                maps.AddItem($"{mapList[i].Name} ({mapList[i].Id})");
                if (mapList[i].Directory.Equals(waterInterior ? "Blackfathom" : "Azeroth", StringComparison.OrdinalIgnoreCase))
                {
                    selected = i;
                }
            }

            maps.Select(selected);
            message = result.Description;
            SetSession(mapList.Count == 0 ? SessionState.Unavailable : SessionState.Ready);
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
            var requested = waterSmoke && waterStage == 0 ? new TileKey(32, 49) : new TileKey((int)tileX.Value, (int)tileY.Value);
            if (world.Tiles.Count > 0)
            {
                if (!world.Tiles.Contains(requested))
                {
                    requested = world.Tiles.OrderBy(k => Math.Abs(k.X - requested.X) + Math.Abs(k.Y - requested.Y)).First();
                }

                tileX.Value = requested.X;
                tileY.Value = requested.Y;
                Teleport(requested);
            }
            else
            {
                var placement = world.GlobalObjects[0];
                camera.Position = new(placement.Position.X, placement.Position.Y + 2.5f, placement.Position.Z + 10);
                camera.Rotation = new(-0.12f, 0, 0);
                yaw = 0;
                pitch = -0.12f;
                UpdateWanted();
            }

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

    /// <summary>
    /// Invalidate pending results, release complete/partial tiles and clear transient
    /// objects/creatures. Scene nodes do not own source assets or durable drafts.
    /// </summary>
    void ResetTerrain()
    {
        navigation?.SetMap(null);
        generation++;
        cancellation.Cancel();
        cancellation.Dispose();
        cancellation = new();
        foreach (var n in loaded.Values)
        {
            renderer.Release(n);
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
        UpdateWanted();
        // A loaded tile can supply elevation immediately; otherwise adjust when it finishes.
        if (loaded.TryGetValue(key, out var node))
        {
            RaiseAboveTerrain(node);
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
    bool cameraFocused;

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
        UpdateWanted();
    }



    /// <summary>
    /// Move the camera, publish current-generation results and incrementally upload/update systems.
    /// One worker and soft upload budgets limit frame stalls.
    /// </summary>
    public override void _Process(double delta)
    {
        navigation.UpdateRegion(CameraTile(), radius);
        PerfMark(0);
        if (capture && !busy)
        {
            MoveCamera(delta);
        }

        if (!busy && world != null && store != null)
        {
            UpdateTerrainStreaming(world, store);
        }

        PerfMark(1);
        UpdatePendingLocate(delta);

        if (startMessage.Length > 0 && Time.GetTicksMsec() - startMessageShown > StartMessageMs)
        {
            if (message == startMessage)
            {
                message = "";
            }

            startMessage = "";
        }

        status.Text = message;
        menu.Refresh();
        toolbar.Refresh();
        sky.Update(camera, delta);
        timeOfDay.SetValueNoSignal(sky.TimeOfDayHours);
        int hh = (int)sky.TimeOfDayHours, mm = (int)((sky.TimeOfDayHours - hh) * 60);
        timeOfDayLabel.Text = $"{hh:00}:{mm:00}";
        var (zone, continent) = CurrentLocation();
        zoneLabel.Text = zone;
        zoneLabel.Visible = zone.Length > 0;
        continentLabel.Text = continent;
        var current = CameraTile();
        int cells = renderer.CellCount;
        statusBar.Telemetry = $"{world?.Name ?? "No map"}    |    Tile {current.X}, {current.Y}    |    Position {CameraServer().X:0.0}, {CameraServer().Y:0.0}, {CameraServer().Z:0.0}    |    {loaded.Count}/{wanted.Count} tiles · {cells:N0} cells    |    {objects.ObjectCount:N0} objects · {creatures.ModelCreatureCount} creatures · {gameObjects.ModelObjectCount} GameObjects{(models.Busy ? " (loading)" : "")}    |    {speed:0} yd/s    |    {Engine.GetFramesPerSecond()} FPS";
        minimap.Sync(world, loaded.Keys, current, camera.Position);
        PerfMark(2);
        creatures.Update(camera);
        PerfMark(3);
        gameObjects.Update(camera);
        PerfMark(4);
        graveyards.Update(camera);
        waypoints.Update(camera);
        areaTriggers.Update(camera);
        creatureProps.Track(databaseEnabled ? creatures.SelectedSpawn : null);
        PerfMark(5);
        models.Update(camera);
        PerfMark(6);
        sidebar.UpdateCounts();
        PollPending(delta);
        PerfMark(7);
        RunSmokeChecks(delta);
    }


    /// <summary>
    /// Handle navigation/clicks after UI consumption. Gizmo handles take priority over creature
    /// selection.
    /// </summary>
    public override void _UnhandledInput(InputEvent input)
    {
        if (!capture && navigation.Inspect && navigation.LayerEnabled && input is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } navClick)
        {
            navigation.Pick(camera, navClick.Position);
            GetViewport().SetInputAsHandled();
            return;
        }
        if (placement.Current == null && HandleSelectionGesture(input))
        {
            return;
        }

        if (placement.Current != null && !capture)
        {
            if (input is InputEventMouseMotion hover)
            {
                placement.Hover(camera, hover.Position);
            }
            else if (input is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click)
            {
                _ = placement.ClickAsync(camera, click.Position, click.ShiftPressed, statusLog.Post);
                GetViewport().SetInputAsHandled();
                return;
            }
            else if (input is InputEventKey { Pressed: true, Keycode: Key.Escape })
            {
                placement.Disarm();
                statusLog.Post("Stopped placing.");
                return;
            }
        }

        if (input is InputEventMouseButton mouse)
        {
            if (mouse.Pressed && mouse.ButtonIndex == MouseButton.Left && !capture)
            {
                if (!ActiveEditor.BeginDrag(camera, mouse.Position))
                {
                    // Nearest ray hit wins across creatures, GameObjects, graveyard icons and
                    // AreaTrigger volumes; a volume loses ties so models inside it stay clickable.
                    var creatureHit = creatures.FindHit(camera, mouse.Position);
                    var objectHit = gameObjects.FindHit(camera, mouse.Position);
                    var graveyardHit = graveyards.FindHit(camera, mouse.Position);
                    var triggerHit = areaTriggers.FindHit(camera, mouse.Position);
                    var waypointHit = waypoints.FindHit(camera, mouse.Position);
                    float creatureDistance = creatureHit?.Distance ?? float.MaxValue;
                    float objectDistance = objectHit?.Distance ?? float.MaxValue;
                    float graveyardDistance = graveyardHit?.Distance ?? float.MaxValue;
                    float triggerDistance = triggerHit?.Distance ?? float.MaxValue;
                    float waypointDistance = waypointHit?.Distance ?? float.MaxValue;
                    if (waypointHit.HasValue && waypointDistance <= creatureDistance && waypointDistance <= objectDistance && waypointDistance <= graveyardDistance && waypointDistance <= triggerDistance)
                    {
                        waypoints.Select(waypointHit.Value.Path, waypointHit.Value.Point);
                    }
                    else if (triggerHit.HasValue && triggerDistance < creatureDistance && triggerDistance < objectDistance && triggerDistance < graveyardDistance)
                    {
                        areaTriggers.Select(triggerHit.Value.Key);
                    }
                    else if (graveyardHit.HasValue && graveyardDistance <= creatureDistance && graveyardDistance <= objectDistance)
                    {
                        graveyards.Select(graveyardHit.Value.Id);
                    }
                    else if (creatureHit.HasValue && creatureDistance <= objectDistance)
                    {
                        creatures.Select(creatureHit.Value.Guid);
                    }
                    else if (objectHit.HasValue)
                    {
                        gameObjects.Select(objectHit.Value.Guid);
                    }
                }

                return;
            }

            if (mouse.ButtonIndex == MouseButton.Right)
            {
                if (mouse.Pressed)
                {
                    rightPress = (mouse.Position, Time.GetTicksMsec(), yaw, pitch);
                    rightMotion = 0;
                }
                else if (rightPress is { } press && rightMotion < 6 && Time.GetTicksMsec() - press.Ticks < 350)
                {
                    // A short right-click without dragging interacts, like the client; a drag looks.
                    yaw = press.Yaw;
                    pitch = press.Pitch;
                    camera.Rotation = new Vector3(pitch, yaw, 0);
                    if (creatures.FindHit(camera, press.Position) is { } hit)
                    {
                        creatures.Select(hit.Guid);
                        if (databaseEnabled && creatures.SelectedSpawn is { } spawn)
                        {
                            creatureProps.Open(spawn, "gossip");
                        }
                    }
                }

                capture = mouse.Pressed;
                Input.MouseMode = capture ? Input.MouseModeEnum.Captured : Input.MouseModeEnum.Visible;
            }

            if (mouse.Pressed && mouse.ButtonIndex == MouseButton.WheelUp)
            {
                speed = Math.Min(1500, speed * 1.25f);
            }

            if (mouse.Pressed && mouse.ButtonIndex == MouseButton.WheelDown)
            {
                speed = Math.Max(5, speed / 1.25f);
            }
        }

        if (input is InputEventMouseMotion motion && capture)
        {
            rightMotion += motion.Relative.Length();
            yaw -= motion.Relative.X * 0.0025f;
            pitch = Mathf.Clamp(pitch - motion.Relative.Y * 0.0025f, -1.55f, 1.55f);
            camera.Rotation = new Vector3(pitch, yaw, 0);
        }

        if (input is InputEventKey key && key.Pressed && key.Keycode == Key.Escape)
        {
            capture = false;
            Input.MouseMode = Input.MouseModeEnum.Visible;
        }
    }

    /// <summary>
    /// Release camera capture and cancel an active draft drag when window focus is lost.
    /// </summary>
    public override void _Notification(int what)
    {
        if (what == NotificationWMWindowFocusOut)
        {
            capture = false;
            Input.MouseMode = Input.MouseModeEnum.Visible;
            creatures?.EndDrag(false);
            gameObjects?.EndDrag(false);
            graveyards?.EndDrag(false);
            waypoints?.EndDrag(false);
        }
    }

    /// <summary>
    /// The draft editor that owns the current selection; creatures when nothing editable is
    /// selected. Menus, toolbar and viewport drags route through it.
    /// </summary>
    (Vector2 Position, ulong Ticks, float Yaw, float Pitch)? rightPress;
    float rightMotion;

    IPlacementEditor ActiveEditor => waypoints != null && waypoints.HasSelection ? waypoints : graveyards != null && graveyards.HasSelection ? graveyards : gameObjects != null && gameObjects.HasSelection ? gameObjects : creatures;

    /// <summary>
    /// Keep flight keys away from shortcuts, and own an active drag through release/Escape even
    /// across UI. Completion saves locally; cancellation restores the starting pose.
    /// </summary>
    public override void _Input(InputEvent input)
    {
        // F12 works while flying too, so it sits before the flight-key swallow.
        if (input is InputEventKey { Keycode: Key.F12, Pressed: true, Echo: false })
        {
            CaptureDiagnostics();
            GetViewport().SetInputAsHandled();
            return;
        }

        // While right-mouse flight is active, W/A/S/D/Q/E drive the camera by polling. Consume key
        // events here (before shortcut input) so they cannot also switch tools or run menu
        // commands. Esc still reaches _UnhandledInput to release the mouse.
        if (capture && input is InputEventKey { Keycode: not Key.Escape })
        {
            GetViewport().SetInputAsHandled();
            return;
        }

        if (creatures == null || gameObjects == null || graveyards == null || waypoints == null || !ActiveEditor.IsDragging)
        {
            return;
        }

        var editor = ActiveEditor;
        if (input is InputEventMouseMotion motion)
        {
            editor.Drag(camera, motion.Position);
        }
        else if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false })
        {
            editor.EndDrag(true);
        }
        else if (input is InputEventKey { Keycode: Key.Escape, Pressed: true })
        {
            editor.EndDrag(false);
        }

        GetViewport().SetInputAsHandled();
    }

    /// <summary>
    /// Cancel streaming and delay store disposal until worker reads complete. Closing the viewer
    /// never mutates client/server data.
    /// </summary>
    public override void _ExitTree()
    {
        cancellation.Cancel();
        var assets = store;
        var modelTask = models?.Stop() ?? Task.CompletedTask;
        if (pending != null || !modelTask.IsCompleted)
        {
            _ = Task.WhenAll(pending ?? Task.CompletedTask, modelTask).ContinueWith(_ => assets?.Dispose());
        }
        else
        {
            assets?.Dispose();
        }

        cancellation.Dispose();
        drafts?.Dispose();
    }

    /// <summary>
    /// Translate captured keyboard input along camera/world axes. Normalize combined movement so
    /// diagonal flight has the same base speed.
    /// </summary>
    void MoveCamera(double delta)
    {
        Vector3 move = Vector3.Zero;
        if (Input.IsPhysicalKeyPressed(Key.W))
        {
            move -= camera.Basis.Z;
        }

        if (Input.IsPhysicalKeyPressed(Key.S))
        {
            move += camera.Basis.Z;
        }

        if (Input.IsPhysicalKeyPressed(Key.A))
        {
            move -= camera.Basis.X;
        }

        if (Input.IsPhysicalKeyPressed(Key.D))
        {
            move += camera.Basis.X;
        }

        if (Input.IsPhysicalKeyPressed(Key.Q))
        {
            move -= Vector3.Up;
        }

        if (Input.IsPhysicalKeyPressed(Key.E))
        {
            move += Vector3.Up;
        }

        if (move != Vector3.Zero)
        {
            cameraFocused = false;
        }

        camera.Position += move.Normalized() * speed * (Input.IsPhysicalKeyPressed(Key.Shift) ? 4 : 1) * (float)delta;
    }

}
