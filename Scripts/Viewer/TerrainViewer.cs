using Godot;
using ACWorldEdit.Assets;

namespace ACWorldEdit;

/// <summary>
/// Standalone camera/input/archive/streaming coordinator. UI lives in a partial file and opt-in
/// smoke workflows under Diagnostics; GPU work stays on the Godot thread.
/// </summary>
public partial class TerrainViewer : Node3D
{
    AssetStore? store;
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
    readonly HashSet<TileKey> failed = [];
    readonly HashSet<TileKey> wanted = [];
    Task<PreparedTile>? pending;
    TileKey? pendingKey;
    CancellationTokenSource cancellation = new();
    PreparedTile? building;
    Node3D? buildingNode;
    int buildIndex, generation;
    bool busy, capture;
    float yaw, pitch = -0.4f, speed = 70;
    int radius = 1;
    // The SQL query region is intentionally stickier than the terrain load/release window (see
    // UpdateWanted); these remember its last recompute center/radius.
    TileKey? spawnRegionCenter;
    int spawnRegionLoadRadius = -1;
    OptionButton maps = null!;
    SpinBox tileX = null!, tileY = null!, distance = null!;
    // MPQ directory chosen through File → Load archives…; defaults to the project's wow-data junction.
    string dataDirectory = "";
    Button open = null!;
    Label status = null!, telemetry = null!, zoneLabel = null!, continentLabel = null!;
    Slider timeOfDay = null!;
    Label timeOfDayLabel = null!;
    Button playTime = null!;
    List<MapInfo> mapList = [];
    TerrainMinimap minimap = null!;
    string message = "Opening archives…";
    /// <summary>
    /// External worldserver.conf path shared by every database-backed layer and editor; credentials
    /// stay in that file.
    /// </summary>
    readonly LineEdit connection = new()
    {
        Text = System.Environment.GetEnvironmentVariable("ACWORLD_SERVER_CONFIG") ?? ACWorldEdit.Data.CreatureSource.DefaultConfigPath,
        PlaceholderText = "External worldserver.conf path"
    };
    CreatureOverlay creatures = null!;
    GameObjectOverlay gameObjects = null!;
    GraveyardOverlay graveyards = null!;
    AreaTriggerOverlay areaTriggers = null!;
    SceneSidebar sidebar = null!;
    ushort? currentMapId;
    /// <summary>
    /// Generation-tagged worker result containing CPU data/pixels, never Godot resources. Obsolete
    /// generations cannot be published.
    /// </summary>
    sealed record PreparedTile(int Generation, TerrainTile Tile, Dictionary<string, BlpTexture> Textures, List<string> Errors);
    /// <summary>
    /// Create systems/UI on the Godot thread, configure opt-in checks and asynchronously open
    /// archives.
    /// </summary>
    public override async void _Ready()
    {
        renderer = new TerrainRenderer();
        models = new ModelLibrary();
        objects = new WorldObjectScene(this, models);
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
            Models = models
        };
        AddChild(creatures);
        creatures.FocusRequested += FocusPose;
        creatures.GroundAt = GroundAt;
        gameObjects = new GameObjectOverlay { Models = models, ConnectionPath = () => connection.Text };
        AddChild(gameObjects);
        gameObjects.FocusRequested += FocusPose;
        graveyards = new GraveyardOverlay { ConnectionPath = () => connection.Text, Areas = () => areas, GroundAt = GroundAt };
        AddChild(graveyards);
        graveyards.FocusRequested += FocusPose;
        areaTriggers = new AreaTriggerOverlay { ConnectionPath = () => connection.Text };
        AddChild(areaTriggers);
        areaTriggers.FocusRequested += FocusPose;
        BuildUi();
        ConfigureSmokeChecks();
        await LoadArchives();
    }

    /// <summary>
    /// Cancel/await terrain and model readers before replacing the client store. Dispose old MPQs
    /// only when no worker can still read them.
    /// </summary>
    async Task LoadArchives()
    {
        if (busy)
        {
            return;
        }

        busy = true;
        open.Disabled = true;
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

        await models.SetStore(null);
        old?.Dispose();
        message = "Reading MPQ tables and map list…";
        string path = dataDirectory;
        try
        {
            var result = await Task.Run(() =>
            {
                var s = new AssetStore(path);
                try
                {
                    return (Store: s, Maps: s.Maps(), Lighting: ClientLighting.TryLoad(s), Areas: AreaLookup.TryLoad(s));
                }
                catch
                {
                    s.Dispose();
                    throw;
                }
            });
            store = result.Store;
            mapList = result.Maps;
            lighting = result.Lighting;
            areas = result.Areas;
            maps.Clear();
            int selected = 0;
            await models.SetStore(store);
            for (int i = 0; i < mapList.Count; i++)
            {
                maps.AddItem($"{mapList[i].Name} ({mapList[i].Id})");
                if (mapList[i].Directory == (waterInterior ? "Blackfathom" : "Azeroth"))
                {
                    selected = i;
                }
            }

            maps.Select(selected);
            message = $"{store.ArchiveCount} archives · {mapList.Count} maps";
            open.Disabled = mapList.Count == 0;
        }
        catch (Exception e)
        {
            message = "Archive error: " + e.Message;
            GD.PushError(message);
        }
        finally
        {
            busy = false;
        }

        if (store != null && mapList.Count > 0)
        {
            await OpenMap();
        }
    }

    /// <summary>
    /// Read WDT on a worker and choose outdoor navigation or an interior camera start. Map changes
    /// invalidate terrain and creature generations.
    /// </summary>
    async Task OpenMap()
    {
        if (busy || store == null || maps.Selected < 0)
        {
            return;
        }

        busy = true;
        open.Disabled = true;
        ResetTerrain();
        message = "Reading terrain index…";
        var assets = store;
        string name = mapList[maps.Selected].Directory;
        try
        {
            world = await Task.Run(() => new WorldMap(name, assets.Read($"World/Maps/{name}/{name}.wdt")));
            currentMapId = checked((ushort)mapList[maps.Selected].Id);
            sky.SetMap(lighting, currentMapId, world.Tiles.Count == 0);
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
            busy = false;
            open.Disabled = false;
        }
    }

    /// <summary>
    /// Invalidate pending results, release complete/partial tiles and clear transient
    /// objects/creatures. Scene nodes do not own source assets or durable drafts.
    /// </summary>
    void ResetTerrain()
    {
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
        wanted.Clear();
        world = null;
        currentMapId = null;
        creatures.SetRegion(null);
        gameObjects.SetRegion(null);
        graveyards.SetRegion(null);
        areaTriggers.SetRegion(null);
        interaction?.Reset();
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
    TileKey CameraTile() => new((int)Math.Floor(camera.Position.X / TerrainTile.Size + 32), (int)Math.Floor(camera.Position.Z / TerrainTile.Size + 32));
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
        var point = ACWorldEdit.Foundation.WorldCoordinates.ToView(new(pose.X, pose.Y, pose.Z));
        var position = new Vector3((float)point.X, (float)point.Y, (float)point.Z);
        camera.Position = position + new Vector3(0, 8, 15);
        camera.LookAt(position + Vector3.Up);
        yaw = camera.Rotation.Y;
        pitch = camera.Rotation.X;
        UpdateWanted();
    }

    /// <summary>
    /// Compute nearby available tiles, release unwanted full/partial nodes and bound creature
    /// queries. Interior maps use camera neighborhoods without inventing ADTs.
    /// </summary>
    void UpdateWanted()
    {
        if (world == null)
        {
            return;
        }

        var center = CameraTile();
        wanted.Clear();
        for (int y = -radius; y <= radius; y++)
        {
            for (int x = -radius; x <= radius; x++)
            {
                var key = new TileKey(center.X + x, center.Y + y);
                if (world.Tiles.Contains(key))
                {
                    wanted.Add(key);
                }
            }
        }

        // Release with one extra ring of hysteresis beyond the load radius: turning or wobbling
        // right at the boundary must not repeatedly tear down and reload the same tile, which is
        // what made streamed terrain/scenery appear to flicker in and out while flying.
        bool KeepLoaded(TileKey key) => Math.Abs(key.X - center.X) <= radius + 1 && Math.Abs(key.Y - center.Y) <= radius + 1;
        foreach (var key in loaded.Keys.Where(k => !KeepLoaded(k)).ToArray())
        {
            renderer.Release(loaded[key]);
            loaded.Remove(key);
            chunkAreas.Remove(key);
            terrainHeights.Remove(key);
            objects.RemoveTile(key);
        }

        if (building != null && !KeepLoaded(building.Tile.Key))
        {
            if (buildingNode != null)
            {
                renderer.Release(buildingNode);
            }

            building = null;
            buildingNode = null;
        }

        ACWorldEdit.Foundation.CreatureRegion? spawnRegion = null;
        if (currentMapId.HasValue && wanted.Count > 0)
        {
            // Sticky with generous padding: recomputing this tightly around `center` every time the
            // camera crosses a tile boundary forced CreatureOverlay/GameObjectOverlay to fully clear
            // and re-query (visibly "Loading nearby creatures…") every ~533 yards of ordinary flight,
            // even though the SQL query already re-centers its own nearest-radius filter far more
            // cheaply via the quantized camera point. Keep the same padded region until the camera
            // actually nears its edge, or the radius setting itself changes.
            const int pad = 3;
            if (spawnRegionCenter == null || radius != spawnRegionLoadRadius ||
                Math.Abs(center.X - spawnRegionCenter.Value.X) > pad || Math.Abs(center.Y - spawnRegionCenter.Value.Y) > pad)
            {
                spawnRegionCenter = center;
                spawnRegionLoadRadius = radius;
            }

            var regionCenter = spawnRegionCenter.Value;
            int r = radius + pad;
            spawnRegion = new(currentMapId.Value, regionCenter.X - r, regionCenter.X + r, regionCenter.Y - r, regionCenter.Y + r);
        }
        else if (currentMapId.HasValue && world.Tiles.Count == 0)
        {
            spawnRegionCenter = null;
            spawnRegion = new(currentMapId.Value, Math.Clamp(center.X - radius, 0, 63), Math.Clamp(center.X + radius, 0, 63), Math.Clamp(center.Y - radius, 0, 63), Math.Clamp(center.Y + radius, 0, 63));
        }
        else
        {
            spawnRegionCenter = null;
        }

        creatures.SetRegion(spawnRegion);
        gameObjects.SetRegion(spawnRegion);
        graveyards.SetRegion(spawnRegion);
        areaTriggers.SetRegion(spawnRegion);
    }

    /// <summary>
    /// Worker-only tile/texture decode with cancellation checks. Carry decode errors to the main
    /// thread for explicit warnings.
    /// </summary>
    PreparedTile Prepare(AssetStore assets, WorldMap map, TileKey key, int version, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var tile = new TerrainTile(key, assets.Read(map.TilePath(key)), map.BigAlpha);
        var decoded = new Dictionary<string, BlpTexture>(StringComparer.OrdinalIgnoreCase);
        List<string> errors = [];
        foreach (string path in tile.Chunks.SelectMany(c => c.Layers).Select(l => l.Texture).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            token.ThrowIfCancellationRequested();
            try
            {
                decoded[path] = BlpTexture.Decode(assets.Read(path));
            }
            catch (Exception e)
            {
                errors.Add(path + ": " + e.Message);
            }
        }

        for (int i = 0; i < tile.Liquids.Count; i++)
        {
            var surface = tile.Liquids[i];
            uint type = surface.LiquidType!.Value;
            token.ThrowIfCancellationRequested();
            try
            {
                decoded[surface.Texture] = LiquidTextures.For(assets).Decode(type);
                tile.Liquids[i] = surface with
                {
                    LiquidKind = LiquidTextures.For(assets).Kind(type)
                };
            }
            catch (Exception e)
            {
                errors.Add(surface.Texture + ": " + e.Message);
            }
        }

        return new(version, tile, decoded, errors);
    }

    /// <summary>
    /// Move the camera, publish current-generation results and incrementally upload/update systems.
    /// One worker and soft upload budgets limit frame stalls.
    /// </summary>
    public override void _Process(double delta)
    {
        if (capture && !busy)
        {
            MoveCamera(delta);
        }

        if (!busy && world != null && store != null)
        {
            UpdateTerrainStreaming(world, store);
        }

        UpdatePendingLocate(delta);

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
        int cells = loaded.Values.Sum(n => n.GetChildCount()) + (buildingNode?.GetChildCount() ?? 0);
        telemetry.Text = $"{world?.Name ?? "No map"}    |    Tile {current.X}, {current.Y}    |    {loaded.Count}/{wanted.Count} tiles · {cells:N0} cells    |    {objects.ObjectCount:N0} objects · {creatures.ModelCreatureCount} creatures · {gameObjects.ModelObjectCount} GameObjects{(models.Busy ? " (loading)" : "")}    |    {speed:0} yd/s    |    {Engine.GetFramesPerSecond()} FPS";
        minimap.World = world;
        minimap.Loaded = loaded.Keys.ToHashSet();
        minimap.Current = current;
        minimap.CameraPosition = camera.Position;
        minimap.QueueRedraw();
        creatures.Update(camera);
        gameObjects.Update(camera);
        graveyards.Update(camera);
        areaTriggers.Update(camera);
        interaction.Track(creatures.SelectedSpawn);
        models.Update(camera);
        sidebar.UpdateCounts();
        RunSmokeChecks(delta);
    }

    /// <summary>
    /// Lift the camera above the nearest terrain sample after loading/teleport. Navigation
    /// assistance is not collision or movement snapping.
    /// </summary>
    void RaiseAboveTerrain(Node3D tile)
    {
        // Pick nearest mesh vertex to prevent spawning inside mountains.
        float best = float.MaxValue, height = camera.Position.Y;
        foreach (var mesh in tile.GetChildren().OfType<MeshInstance3D>())
        {
            var arrays = mesh.Mesh.SurfaceGetArrays(0);
            var vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            foreach (var v in vertices)
            {
                var pos = tile.Position + v;
                float d = new Vector2(pos.X - camera.Position.X, pos.Z - camera.Position.Z).LengthSquared();
                if (d < best)
                {
                    best = d;
                    height = pos.Y;
                }
            }
        }

        if (camera.Position.Y < height + 35)
        {
            camera.Position = new Vector3(camera.Position.X, height + 80, camera.Position.Z);
        }
    }

    /// <summary>
    /// Handle navigation/clicks after UI consumption. Gizmo handles take priority over creature
    /// selection.
    /// </summary>
    public override void _UnhandledInput(InputEvent input)
    {
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
                    float creatureDistance = creatureHit?.Distance ?? float.MaxValue;
                    float objectDistance = objectHit?.Distance ?? float.MaxValue;
                    float graveyardDistance = graveyardHit?.Distance ?? float.MaxValue;
                    float triggerDistance = triggerHit?.Distance ?? float.MaxValue;
                    if (triggerHit.HasValue && triggerDistance < creatureDistance && triggerDistance < objectDistance && triggerDistance < graveyardDistance)
                    {
                        areaTriggers.Select(triggerHit.Value.Entry);
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
                        if (creatures.SelectedSpawn is { } spawn)
                        {
                            interaction.Open(spawn);
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
            graveyards?.EndDrag(false);
        }
    }

    /// <summary>
    /// The draft editor that owns the current selection; creatures when nothing editable is
    /// selected. Menus, toolbar and viewport drags route through it.
    /// </summary>
    (Vector2 Position, ulong Ticks, float Yaw, float Pitch)? rightPress;
    float rightMotion;

    IPlacementEditor ActiveEditor => graveyards != null && graveyards.HasSelection ? graveyards : creatures;

    /// <summary>
    /// Keep flight keys away from shortcuts, and own an active drag through release/Escape even
    /// across UI. Completion saves locally; cancellation restores the starting pose.
    /// </summary>
    public override void _Input(InputEvent input)
    {
        // While right-mouse flight is active, W/A/S/D/Q/E drive the camera by polling. Consume key
        // events here (before shortcut input) so they cannot also switch tools or run menu
        // commands. Esc still reaches _UnhandledInput to release the mouse.
        if (capture && input is InputEventKey { Keycode: not Key.Escape })
        {
            GetViewport().SetInputAsHandled();
            return;
        }

        if (creatures == null || graveyards == null || !ActiveEditor.IsDragging)
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

    /// <summary>
    /// Consume completed tile reads, reject obsolete generations, build within a soft per-frame
    /// budget and schedule the nearest missing tile. Explicit map/store arguments preserve a stable
    /// non-null worker source.
    /// </summary>
    void UpdateTerrainStreaming(WorldMap activeWorld, AssetStore activeAssets)
    {
        UpdateWanted();
        if (pending is { IsCompleted: true })
        {
            try
            {
                var result = pending.GetAwaiter().GetResult();
                // Cancellation is cooperative: a finished read may still belong to the
                // previous map or camera region. Check both identity and current interest.
                if (result.Generation == generation && wanted.Contains(result.Tile.Key))
                {
                    building = result;
                    buildIndex = 0;
                    buildingNode = renderer.BeginTile(result.Tile.Key);
                    AddChild(buildingNode);
                    if (result.Errors.Count > 0)
                    {
                        message = $"{result.Errors.Count} missing/unsupported textures. See log.";
                        foreach (string e in result.Errors)
                        {
                            GD.PushWarning(e);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception e)
            {
                if (pendingKey.HasValue)
                {
                    failed.Add(pendingKey.Value);
                }

                pendingKey = null;
                message = "Tile error: " + e.Message;
                GD.PushError(message);
            }

            if (pendingKey.HasValue)
            {
                failed.Remove(pendingKey.Value);
            }

            pendingKey = null;
            pending = null;
        }

        if (building != null && buildingNode != null)
        {
            var uploadStarted = System.Diagnostics.Stopwatch.GetTimestamp();
            try
            {
                // Leave 0.5 ms headroom within the proposed 2 ms upload target.
                // A single indivisible resource upload can still exceed the soft deadline.
                for (int budget = 0; budget < 8 && buildIndex < building.Tile.Chunks.Count + building.Tile.Liquids.Count && (budget == 0 || System.Diagnostics.Stopwatch.GetElapsedTime(uploadStarted).TotalMilliseconds < 1.5); budget++, buildIndex++)
                {
                    if (buildIndex < building.Tile.Chunks.Count)
                    {
                        renderer.AddChunk(buildingNode, building.Tile.Chunks[buildIndex], building.Textures, activeWorld.BigAlpha);
                    }
                    else
                    {
                        renderer.AddLiquid(buildingNode, building.Tile.Liquids[buildIndex - building.Tile.Chunks.Count], building.Textures);
                    }
                }

                if (buildIndex == building.Tile.Chunks.Count + building.Tile.Liquids.Count)
                {
                    loaded[building.Tile.Key] = buildingNode;
                    var ids = new uint[256];
                    foreach (var c in building.Tile.Chunks)
                    {
                        ids[c.Y * 16 + c.X] = c.AreaId;
                    }

                    chunkAreas[building.Tile.Key] = ids;
                    terrainHeights[building.Tile.Key] = new TerrainHeights(building.Tile);
                    objects.SetTile(building.Tile.Key, building.Tile.Objects);
                    if (CameraTile() == building.Tile.Key && !cameraFocused)
                    {
                        RaiseAboveTerrain(buildingNode);
                    }

                    building = null;
                    buildingNode = null;
                }
            }
            catch (Exception e)
            {
                if (building != null)
                {
                    failed.Add(building.Tile.Key);
                }

                if (buildingNode != null)
                {
                    renderer.Release(buildingNode);
                }

                building = null;
                buildingNode = null;
                message = "Render error: " + e.Message;
                GD.PushError(message);
            }
        }

        if (pending == null && building == null)
        {
            var center = CameraTile();
            var next = wanted.Where(k => !loaded.ContainsKey(k) && !failed.Contains(k)).OrderBy(k => (k.X - center.X) * (k.X - center.X) + (k.Y - center.Y) * (k.Y - center.Y)).Select(k => (TileKey?)k).FirstOrDefault();
            if (next.HasValue)
            {
                var key = next.Value;
                var assets = activeAssets;
                var map = activeWorld;
                int version = generation;
                var token = cancellation.Token;
                // Failed tiles are tracked separately; obsolete requests can be retried when revisited.
                pendingKey = key;
                pending = Task.Run(() => Prepare(assets, map, key, version, token));
            }
        }
    }
}
