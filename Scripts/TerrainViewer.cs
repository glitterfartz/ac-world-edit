using Godot;
using ACWorldEdit.Assets;

namespace ACWorldEdit;
public partial class TerrainViewer : Node3D
{
    AssetStore? store;
    WorldMap? world;
    TerrainRenderer renderer = null !;
    ModelLibrary models = null !;
    WorldObjectScene objects = null !;
    Camera3D camera = null !;
    WorldEnvironment environment = null !;
    readonly Dictionary<TileKey, Node3D> loaded = [];
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
    OptionButton maps = null !;
    SpinBox tileX = null !, tileY = null !, distance = null !;
    LineEdit dataPath = null !;
    Button open = null !, reload = null !;
    Label status = null !, telemetry = null !;
    List<MapInfo> mapList = [];
    TerrainMinimap minimap = null !;
    string message = "Opening archives…";
    CreatureOverlay creatures = null !;
    ushort? currentMapId;
    sealed record PreparedTile(int Generation, TerrainTile Tile, Dictionary<string, BlpTexture> Textures, List<string> Errors);
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
        var light = new DirectionalLight3D
        {
            RotationDegrees = new Vector3(-50, -30, 0),
            LightEnergy = 1.25f
        };
        AddChild(light);
        environment = new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new Color("91b5c9"),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color("c8d7e5"),
                AmbientLightEnergy = 0.65f,
                TonemapMode = Godot.Environment.ToneMapper.Filmic
            }
        };
        AddChild(environment);
        creatures = new CreatureOverlay
        {
            Models = models
        };
        AddChild(creatures);
        creatures.FocusRequested += FocusCreature;
        BuildUi();
        waterInterior = OS.GetCmdlineUserArgs().Contains("--water-interior-smoke");
        waterSmoke = OS.GetCmdlineUserArgs().Contains("--water-smoke") || waterInterior;
        smoke = OS.GetCmdlineUserArgs().Contains("--terrain-smoke");
        creatureSmoke = OS.GetCmdlineUserArgs().Contains("--creature-smoke");
        visualSmoke = OS.GetCmdlineUserArgs().Contains("--visual-smoke");
        creatureSmoke |= visualSmoke;
        gizmoSmoke = OS.GetCmdlineUserArgs().Contains("--gizmo-smoke");
        creatureSmoke |= gizmoSmoke;
        await LoadArchives();
    }

    async Task LoadArchives()
    {
        if (busy)
            return;
        busy = true;
        open.Disabled = true;
        reload.Disabled = true;
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
        string path = dataPath.Text;
        try
        {
            var result = await Task.Run(() =>
            {
                var s = new AssetStore(path);
                try
                {
                    return (Store: s, Maps: s.Maps());
                }
                catch
                {
                    s.Dispose();
                    throw;
                }
            });
            store = result.Store;
            mapList = result.Maps;
            maps.Clear();
            int selected = 0;
            await models.SetStore(store);
            for (int i = 0; i < mapList.Count; i++)
            {
                maps.AddItem($"{mapList[i].Name} ({mapList[i].Id})");
                if (mapList[i].Directory == (waterInterior ? "Blackfathom" : "Azeroth"))
                    selected = i;
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
            reload.Disabled = false;
        }

        if (store != null && mapList.Count > 0)
            await OpenMap();
    }

    async Task OpenMap()
    {
        if (busy || store == null || maps.Selected < 0)
            return;
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
            environment.Environment.BackgroundColor = world.Tiles.Count == 0 ? new Color("080a0d") : new Color("91b5c9");
            tileX.Editable = tileY.Editable = world.Tiles.Count > 0;
            objects.SetGlobals(world.GlobalObjects);
            var requested = waterSmoke && waterStage == 0 ? new TileKey(32, 49) : new TileKey((int)tileX.Value, (int)tileY.Value);
            if (world.Tiles.Count > 0)
            {
                if (!world.Tiles.Contains(requested))
                    requested = world.Tiles.OrderBy(k => Math.Abs(k.X - requested.X) + Math.Abs(k.Y - requested.Y)).First();
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

    void ResetTerrain()
    {
        generation++;
        cancellation.Cancel();
        cancellation.Dispose();
        cancellation = new();
        foreach (var n in loaded.Values)
            renderer.Release(n);
        loaded.Clear();
        if (buildingNode != null)
            renderer.Release(buildingNode);
        buildingNode = null;
        building = null;
        buildIndex = 0;
        failed.Clear();
        wanted.Clear();
        world = null;
        currentMapId = null;
        creatures.SetRegion(null);
        objects.Clear();
    }

    void Teleport(TileKey key)
    {
        if (world == null || !world.Tiles.Contains(key))
            return;
        camera.Position = new Vector3((key.X - 32 + 0.5f) * TerrainTile.Size, 250, (key.Y - 32 + 0.5f) * TerrainTile.Size);
        yaw = 0;
        pitch = -0.55f;
        camera.Rotation = new Vector3(pitch, yaw, 0);
        UpdateWanted();
        // A loaded tile can supply elevation immediately; otherwise adjust when it finishes.
        if (loaded.TryGetValue(key, out var node))
            RaiseAboveTerrain(node);
    }

    TileKey CameraTile() => new((int)Math.Floor(camera.Position.X / TerrainTile.Size + 32), (int)Math.Floor(camera.Position.Z / TerrainTile.Size + 32));
    void FocusCreature(ACWorldEdit.Foundation.CreatureSpawn spawn)
    {
        var point = ACWorldEdit.Foundation.WorldCoordinates.ToView(new(spawn.Pose.X, spawn.Pose.Y, spawn.Pose.Z));
        var position = new Vector3((float)point.X, (float)point.Y, (float)point.Z);
        camera.Position = position + new Vector3(0, 8, 15);
        camera.LookAt(position + Vector3.Up);
        yaw = camera.Rotation.Y;
        pitch = camera.Rotation.X;
        UpdateWanted();
    }

    void UpdateWanted()
    {
        if (world == null)
            return;
        var center = CameraTile();
        wanted.Clear();
        for (int y = -radius; y <= radius; y++)
            for (int x = -radius; x <= radius; x++)
            {
                var key = new TileKey(center.X + x, center.Y + y);
                if (world.Tiles.Contains(key))
                    wanted.Add(key);
            }

        foreach (var key in loaded.Keys.Where(k => !wanted.Contains(k)).ToArray())
        {
            renderer.Release(loaded[key]);
            loaded.Remove(key);
            objects.RemoveTile(key);
        }

        if (building != null && !wanted.Contains(building.Tile.Key))
        {
            if (buildingNode != null)
                renderer.Release(buildingNode);
            building = null;
            buildingNode = null;
        }

        if (currentMapId.HasValue && wanted.Count > 0)
            creatures.SetRegion(new(currentMapId.Value, wanted.Min(k => k.X), wanted.Max(k => k.X), wanted.Min(k => k.Y), wanted.Max(k => k.Y)));
        else if (currentMapId.HasValue && world.Tiles.Count == 0)
            creatures.SetRegion(new(currentMapId.Value, Math.Clamp(center.X - radius, 0, 63), Math.Clamp(center.X + radius, 0, 63), Math.Clamp(center.Y - radius, 0, 63), Math.Clamp(center.Y + radius, 0, 63)));
    }

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

    public override void _Process(double delta)
    {
        if (capture && !busy)
        {
            Vector3 move = Vector3.Zero;
            if (Input.IsPhysicalKeyPressed(Key.W))
                move -= camera.Basis.Z;
            if (Input.IsPhysicalKeyPressed(Key.S))
                move += camera.Basis.Z;
            if (Input.IsPhysicalKeyPressed(Key.A))
                move -= camera.Basis.X;
            if (Input.IsPhysicalKeyPressed(Key.D))
                move += camera.Basis.X;
            if (Input.IsPhysicalKeyPressed(Key.Q))
                move -= Vector3.Up;
            if (Input.IsPhysicalKeyPressed(Key.E))
                move += Vector3.Up;
            camera.Position += move.Normalized() * speed * (Input.IsPhysicalKeyPressed(Key.Shift) ? 4 : 1) * (float)delta;
        }

        if (!busy && world != null && store != null)
        {
            UpdateWanted();
            if (pending is { IsCompleted: true })
            {
                try
                {
                    var result = pending.GetAwaiter().GetResult();
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
                                GD.PushWarning(e);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception e)
                {
                    if (pendingKey.HasValue)
                        failed.Add(pendingKey.Value);
                    pendingKey = null;
                    message = "Tile error: " + e.Message;
                    GD.PushError(message);
                }

                if (pendingKey.HasValue)
                    failed.Remove(pendingKey.Value);
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
                        if (buildIndex < building.Tile.Chunks.Count)
                            renderer.AddChunk(buildingNode, building.Tile.Chunks[buildIndex], building.Textures, world.BigAlpha);
                        else
                            renderer.AddLiquid(buildingNode, building.Tile.Liquids[buildIndex - building.Tile.Chunks.Count], building.Textures);
                    if (buildIndex == building.Tile.Chunks.Count + building.Tile.Liquids.Count)
                    {
                        loaded[building.Tile.Key] = buildingNode;
                        objects.SetTile(building.Tile.Key, building.Tile.Objects);
                        if (CameraTile() == building.Tile.Key)
                            RaiseAboveTerrain(buildingNode);
                        building = null;
                        buildingNode = null;
                    }
                }
                catch (Exception e)
                {
                    if (building != null)
                        failed.Add(building.Tile.Key);
                    if (buildingNode != null)
                        renderer.Release(buildingNode);
                    building = null;
                    buildingNode = null;
                    message = "Render error: " + e.Message;
                    GD.PushError(message);
                }
            }

            if (pending == null && building == null)
            {
                var center = CameraTile();
                var next = wanted.Where(k => !loaded.ContainsKey(k) && !failed.Contains(k)).OrderBy(k => (k.X - center.X) * (k.X - center.X) + (k.Y - center.Y) * (k.Y - center.Y)).Select(k => (TileKey? )k).FirstOrDefault();
                if (next.HasValue)
                {
                    var key = next.Value;
                    var assets = store;
                    var map = world;
                    int version = generation;
                    var token = cancellation.Token;
                    // Failed tiles are tracked separately; obsolete requests can be retried when revisited.
                    pendingKey = key;
                    pending = Task.Run(() => Prepare(assets, map, key, version, token));
                }
            }
        }

        status.Text = message;
        var current = CameraTile();
        int cells = loaded.Values.Sum(n => n.GetChildCount()) + (buildingNode?.GetChildCount() ?? 0);
        telemetry.Text = $"{world?.Name ?? "No map"}    |    Tile {current.X}, {current.Y}    |    {loaded.Count}/{wanted.Count} tiles · {cells:N0} cells    |    {objects.ObjectCount:N0} objects · {creatures.ModelCreatureCount} creature models{(models.Busy ? " (loading)" : "")}    |    {speed:0} yd/s    |    {Engine.GetFramesPerSecond()} FPS";
        minimap.World = world;
        minimap.Loaded = loaded.Keys.ToHashSet();
        minimap.Current = current;
        minimap.CameraPosition = camera.Position;
        minimap.QueueRedraw();
        if (smoke)
            Smoke(delta);
        if (waterSmoke)
            WaterSmoke(delta);
        creatures.Update(camera);
        models.Update(camera);
        if (creatureSmoke)
            CreatureSmoke(delta);
    }

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
            camera.Position = new Vector3(camera.Position.X, height + 80, camera.Position.Z);
    }

    public override void _UnhandledInput(InputEvent input)
    {
        if (input is InputEventMouseButton mouse)
        {
            if (mouse.Pressed && mouse.ButtonIndex == MouseButton.Left && !capture)
            {
                if (!creatures.BeginDrag(camera, mouse.Position))
                    creatures.Pick(camera, mouse.Position);
                return;
            }

            if (mouse.ButtonIndex == MouseButton.Right)
            {
                capture = mouse.Pressed;
                Input.MouseMode = capture ? Input.MouseModeEnum.Captured : Input.MouseModeEnum.Visible;
            }

            if (mouse.Pressed && mouse.ButtonIndex == MouseButton.WheelUp)
                speed = Math.Min(1500, speed * 1.25f);
            if (mouse.Pressed && mouse.ButtonIndex == MouseButton.WheelDown)
                speed = Math.Max(5, speed / 1.25f);
        }

        if (input is InputEventMouseMotion motion && capture)
        {
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

    public override void _Notification(int what)
    {
        if (what == NotificationWMWindowFocusOut)
        {
            capture = false;
            Input.MouseMode = Input.MouseModeEnum.Visible;
            creatures?.EndDrag(false);
        }
    }

    public override void _Input(InputEvent input)
    {
        if (creatures == null || !creatures.IsDragging)
            return;
        if (input is InputEventMouseMotion motion)
            creatures.Drag(camera, motion.Position);
        else if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false })
            creatures.EndDrag(true);
        else if (input is InputEventKey { Keycode: Key.Escape, Pressed: true })
            creatures.EndDrag(false);
        GetViewport().SetInputAsHandled();
    }

    public override void _ExitTree()
    {
        cancellation.Cancel();
        var assets = store;
        var modelTask = models?.Stop() ?? Task.CompletedTask;
        if (pending != null || !modelTask.IsCompleted)
            _ = Task.WhenAll(pending ?? Task.CompletedTask, modelTask).ContinueWith(_ => assets?.Dispose());
        else
            assets?.Dispose();
        cancellation.Dispose();
    }
}
