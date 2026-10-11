using Godot;
using ACWorldEdit.Assets;
namespace ACWorldEdit;

public partial class TerrainViewer : Node3D
{
    AssetStore? store;
    WorldMap? world;
    TerrainRenderer renderer = null!;
    Camera3D camera = null!;
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
    OptionButton maps = null!;
    SpinBox tileX = null!, tileY = null!, distance = null!;
    LineEdit dataPath = null!;
    Button open = null!, reload = null!;
    Label status = null!, telemetry = null!;
    List<MapInfo> mapList = [];
    TerrainMinimap minimap = null!;
    string message = "Opening archives…";
    bool smoke;
    ACWorldEdit.Experiments.ViewportBenchmark? benchmark;
    Node3D? benchmarkProxies;
    bool foundationShell;
    CreatureOverlay creatures = null!;
    ushort? currentMapId;
    bool creatureSmoke;
    double creatureSmokeTime;
    int creatureSmokeStage;
    double creatureSelectedAt;
    int smokeStage;
    double smokeTime;
    TileKey smokeOriginal;
    int smokeSettle;
    sealed record PreparedTile(int Generation, TerrainTile Tile, Dictionary<string, BlpTexture> Textures, List<string> Errors);
    public override async void _Ready()
    {
        foundationShell = OS.GetCmdlineUserArgs().Contains("--foundation-shell");
        if (foundationShell)
        {
            var trial = new ACWorldEdit.Experiments.FoundationShellTrial { Position = new(30, 30) }; AddChild(trial);
            if (OS.GetCmdlineUserArgs().Contains("--foundation-shell-smoke"))
            { trial.RunSmoke(); GD.Print("Foundation standalone shell instantiated and fixture workflow passed."); GetTree().Quit(); }
            return;
        }
        renderer = new TerrainRenderer();
        camera = new Camera3D { Name = "Camera", Far = 8000, Near = 0.2f, Fov = 70, Current = true }; AddChild(camera);
        var light = new DirectionalLight3D { RotationDegrees = new Vector3(-50, -30, 0), LightEnergy = 1.25f }; AddChild(light);
        AddChild(new WorldEnvironment
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
        });
        creatures = new CreatureOverlay(); AddChild(creatures);
        creatures.FocusRequested += FocusCreature;
        BuildUi(); smoke = OS.GetCmdlineUserArgs().Contains("--terrain-smoke");
        creatureSmoke = OS.GetCmdlineUserArgs().Contains("--creature-smoke");
        if (OS.GetCmdlineUserArgs().Contains("--terrain-benchmark")) benchmark = new();
        await LoadArchives();
    }
    void BuildUi()
    {
        var layer = new CanvasLayer(); AddChild(layer);
        var ui = new Control { Name = "Interface", MouseFilter = Control.MouseFilterEnum.Ignore }; ui.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect); layer.AddChild(ui);
        var theme = new Theme(); theme.DefaultFontSize = 15;
        theme.SetColor("font_color", "Label", new Color("e7eef4"));
        theme.SetColor("font_color", "Button", new Color("e7eef4"));
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.055f, 0.08f, 0.11f, 0.94f),
            CornerRadiusTopLeft = 12,
            CornerRadiusTopRight = 12,
            CornerRadiusBottomLeft = 12,
            CornerRadiusBottomRight = 12,
            ContentMarginLeft = 18,
            ContentMarginRight = 18,
            ContentMarginTop = 16,
            ContentMarginBottom = 16
        };
        theme.SetStylebox("panel", "PanelContainer", style); ui.Theme = theme;
        var panel = new PanelContainer { Position = new Vector2(20, 20), CustomMinimumSize = new Vector2(330, 0) }; ui.AddChild(panel);
        var box = new VBoxContainer(); box.AddThemeConstantOverride("separation", 10); panel.AddChild(box);
        var title = new Label { Text = "AC WORLD EDIT" }; title.AddThemeFontSizeOverride("font_size", 24); box.AddChild(title);
        var subtitle = new Label { Text = "TERRAIN EXPLORER  /  WRATH 3.3.5" }; subtitle.AddThemeColorOverride("font_color", new Color("5bd8c2")); subtitle.AddThemeFontSizeOverride("font_size", 12); box.AddChild(subtitle);
        box.AddChild(new HSeparator());
        box.AddChild(new Label { Text = "MPQ directory" });
        dataPath = new LineEdit { Text = ProjectSettings.GlobalizePath("res://wow-data"), CustomMinimumSize = new Vector2(294, 0) }; box.AddChild(dataPath);
        reload = new Button { Text = "Load archives" }; reload.Pressed += async () => await LoadArchives(); box.AddChild(reload);
        box.AddChild(new Label { Text = "World" }); maps = new OptionButton(); box.AddChild(maps);
        maps.ItemSelected += id => { if ((int)id < mapList.Count) { var m = mapList[(int)id]; tileX.Value = m.Directory == "Azeroth" ? 34 : 32; tileY.Value = m.Directory == "Azeroth" ? 49 : 32; } };
        var row = new HBoxContainer(); box.AddChild(row);
        row.AddChild(new Label { Text = "Tile X" }); tileX = new SpinBox { MinValue = 0, MaxValue = 63, Value = 34 }; row.AddChild(tileX);
        row.AddChild(new Label { Text = "Y" }); tileY = new SpinBox { MinValue = 0, MaxValue = 63, Value = 49 }; row.AddChild(tileY);
        open = new Button { Text = "Open terrain", Disabled = true }; open.Pressed += async () => await OpenMap(); box.AddChild(open);
        var radiusRow = new HBoxContainer(); box.AddChild(radiusRow); radiusRow.AddChild(new Label { Text = "Nearby tile radius" });
        distance = new SpinBox { MinValue = 0, MaxValue = 3, Value = 1 }; radiusRow.AddChild(distance); distance.ValueChanged += value => { radius = (int)value; UpdateWanted(); };
        var grid = new CheckButton { Text = "Show terrain cell borders" }; grid.Toggled += value => renderer.SetGrid(value); box.AddChild(grid);
        box.AddChild(new HSeparator());
        box.AddChild(new Label { Text = "Right mouse  ·  look / fly\nW A S D  ·  move     Q / E  ·  down / up\nShift  ·  faster     Wheel  ·  flight speed\nEsc  ·  release mouse", Modulate = new Color("aabac8") });
        status = new Label { Text = message, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(294, 55) }; box.AddChild(status);
        var bottom = new PanelContainer { AnchorTop = 1, AnchorBottom = 1, AnchorRight = 1, OffsetLeft = 20, OffsetRight = -20, OffsetTop = -62, OffsetBottom = -18 }; ui.AddChild(bottom);
        telemetry = new Label { Text = "Waiting for terrain" }; bottom.AddChild(telemetry);
        minimap = new TerrainMinimap { Position = new Vector2(370, 20), CustomMinimumSize = new Vector2(180, 180), MouseFilter = Control.MouseFilterEnum.Stop }; ui.AddChild(minimap);
        minimap.Jump += key => { tileX.Value = key.X; tileY.Value = key.Y; Teleport(key); };
        ui.AddChild(creatures.BuildPanel());
    }
    async Task LoadArchives()
    {
        if (busy) return; busy = true; open.Disabled = true; reload.Disabled = true; ResetTerrain();
        var old = store; store = null;
        if (pending != null) { try { await pending; } catch { } pending = null; }
        old?.Dispose(); message = "Reading MPQ tables and map list…";
        string path = dataPath.Text;
        try
        {
            var result = await Task.Run(() => { var s = new AssetStore(path); try { return (Store: s, Maps: s.Maps()); } catch { s.Dispose(); throw; } });
            store = result.Store; mapList = result.Maps; maps.Clear(); int selected = 0;
            for (int i = 0; i < mapList.Count; i++) { maps.AddItem($"{mapList[i].Name} ({mapList[i].Id})"); if (mapList[i].Directory == "Azeroth") selected = i; }
            maps.Select(selected); message = $"{store.ArchiveCount} archives · {mapList.Count} maps"; open.Disabled = mapList.Count == 0;
        }
        catch (Exception e) { message = "Archive error: " + e.Message; GD.PushError(message); }
        finally { busy = false; reload.Disabled = false; }
        if (store != null && mapList.Count > 0) await OpenMap();
    }
    async Task OpenMap()
    {
        if (busy || store == null || maps.Selected < 0) return;
        busy = true; open.Disabled = true; ResetTerrain(); message = "Reading terrain index…";
        var assets = store; string name = mapList[maps.Selected].Directory;
        try
        {
            world = await Task.Run(() => new WorldMap(name, assets.Read($"World/Maps/{name}/{name}.wdt")));
            currentMapId = checked((ushort)mapList[maps.Selected].Id);
            var requested = new TileKey((int)tileX.Value, (int)tileY.Value);
            if (!world.Tiles.Contains(requested)) requested = world.Tiles.OrderBy(k => Math.Abs(k.X - requested.X) + Math.Abs(k.Y - requested.Y)).First();
            tileX.Value = requested.X; tileY.Value = requested.Y; Teleport(requested);
            message = $"{name} · {world.Tiles.Count} terrain tiles";
            if (smoke) { smokeOriginal = requested; smokeStage = 0; smokeTime = 0; }
        }
        catch (Exception e) { message = "Map error: " + e.Message; GD.PushWarning(message); }
        finally { busy = false; open.Disabled = false; }
    }
    void ResetTerrain()
    {
        generation++; cancellation.Cancel(); cancellation.Dispose(); cancellation = new();
        foreach (var n in loaded.Values) renderer.Release(n); loaded.Clear();
        if (buildingNode != null) renderer.Release(buildingNode);
        buildingNode = null; building = null; buildIndex = 0; failed.Clear(); wanted.Clear(); world = null;
        currentMapId = null; creatures.SetRegion(null);
    }
    void Teleport(TileKey key)
    {
        if (world == null || !world.Tiles.Contains(key)) return;
        camera.Position = new Vector3((key.X - 32 + 0.5f) * TerrainTile.Size, 250, (key.Y - 32 + 0.5f) * TerrainTile.Size);
        yaw = 0; pitch = -0.55f; camera.Rotation = new Vector3(pitch, yaw, 0); UpdateWanted();
        if (benchmark != null)
        {
            benchmarkProxies?.QueueFree(); benchmarkProxies = new Node3D(); AddChild(benchmarkProxies);
            benchmark.AddProxies(benchmarkProxies, camera.Position + new Vector3(0, -80, 0));
        }
        // A loaded tile can supply elevation immediately; otherwise adjust when it finishes.
        if (loaded.TryGetValue(key, out var node)) RaiseAboveTerrain(node);
    }
    TileKey CameraTile() => new((int)Math.Floor(camera.Position.X / TerrainTile.Size + 32), (int)Math.Floor(camera.Position.Z / TerrainTile.Size + 32));
    void FocusCreature(ACWorldEdit.Foundation.CreatureSpawn spawn)
    {
        var point = ACWorldEdit.Foundation.WorldCoordinates.ToView(new(spawn.Pose.X, spawn.Pose.Y, spawn.Pose.Z));
        var position = new Vector3((float)point.X, (float)point.Y, (float)point.Z);
        camera.Position = position + new Vector3(0, 8, 15); camera.LookAt(position + Vector3.Up);
        yaw = camera.Rotation.Y; pitch = camera.Rotation.X; UpdateWanted();
    }
    void UpdateWanted()
    {
        if (world == null) return; var center = CameraTile(); wanted.Clear();
        for (int y = -radius; y <= radius; y++) for (int x = -radius; x <= radius; x++)
        { var key = new TileKey(center.X + x, center.Y + y); if (world.Tiles.Contains(key)) wanted.Add(key); }
        foreach (var key in loaded.Keys.Where(k => !wanted.Contains(k)).ToArray()) { renderer.Release(loaded[key]); loaded.Remove(key); }
        if (building != null && !wanted.Contains(building.Tile.Key)) { if (buildingNode != null) renderer.Release(buildingNode); building = null; buildingNode = null; }
        if (currentMapId.HasValue && wanted.Count > 0)
            creatures.SetRegion(new(currentMapId.Value, wanted.Min(k => k.X), wanted.Max(k => k.X), wanted.Min(k => k.Y), wanted.Max(k => k.Y)));
    }
    PreparedTile Prepare(AssetStore assets, WorldMap map, TileKey key, int version, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); var tile = new TerrainTile(key, assets.Read(map.TilePath(key)), map.BigAlpha);
        var decoded = new Dictionary<string, BlpTexture>(StringComparer.OrdinalIgnoreCase); List<string> errors = [];
        foreach (string path in tile.Chunks.SelectMany(c => c.Layers).Select(l => l.Texture).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            token.ThrowIfCancellationRequested();
            try { decoded[path] = BlpTexture.Decode(assets.Read(path)); }
            catch (Exception e) { errors.Add(path + ": " + e.Message); }
        }
        return new(version, tile, decoded, errors);
    }
    public override void _Process(double delta)
    {
        if (foundationShell) return;
        double uploadMs = 0;
        if (capture && !busy)
        {
            Vector3 move = Vector3.Zero;
            if (Input.IsPhysicalKeyPressed(Key.W)) move -= camera.Basis.Z;
            if (Input.IsPhysicalKeyPressed(Key.S)) move += camera.Basis.Z;
            if (Input.IsPhysicalKeyPressed(Key.A)) move -= camera.Basis.X;
            if (Input.IsPhysicalKeyPressed(Key.D)) move += camera.Basis.X;
            if (Input.IsPhysicalKeyPressed(Key.Q)) move -= Vector3.Up;
            if (Input.IsPhysicalKeyPressed(Key.E)) move += Vector3.Up;
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
                        building = result; buildIndex = 0; buildingNode = renderer.BeginTile(result.Tile.Key); AddChild(buildingNode);
                        if (result.Errors.Count > 0) { message = $"{result.Errors.Count} missing/unsupported textures (magenta). See log."; foreach (string e in result.Errors) GD.PushWarning(e); }
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception e) { if (pendingKey.HasValue) failed.Add(pendingKey.Value); pendingKey = null; message = "Tile error: " + e.Message; GD.PushError(message); }
                if (pendingKey.HasValue) failed.Remove(pendingKey.Value); pendingKey = null;
                pending = null;
            }
            if (building != null && buildingNode != null)
            {
                var uploadStarted = System.Diagnostics.Stopwatch.GetTimestamp();
                try
                {
                    // Leave 0.5 ms headroom within the proposed 2 ms upload target.
                    // A single indivisible resource upload can still exceed the soft deadline.
                    for (int budget = 0; budget < 8 && buildIndex < building.Tile.Chunks.Count &&
                        (budget == 0 || System.Diagnostics.Stopwatch.GetElapsedTime(uploadStarted).TotalMilliseconds < 1.5); budget++, buildIndex++)
                        renderer.AddChunk(buildingNode, building.Tile.Chunks[buildIndex], building.Textures, world.BigAlpha);
                    if (buildIndex == building.Tile.Chunks.Count)
                    {
                        loaded[building.Tile.Key] = buildingNode;
                        if (CameraTile() == building.Tile.Key) RaiseAboveTerrain(buildingNode);
                        building = null; buildingNode = null;
                    }
                }
                catch (Exception e) { if (building != null) failed.Add(building.Tile.Key); if (buildingNode != null) renderer.Release(buildingNode); building = null; buildingNode = null; message = "Render error: " + e.Message; GD.PushError(message); }
                uploadMs = System.Diagnostics.Stopwatch.GetElapsedTime(uploadStarted).TotalMilliseconds;
            }
            if (pending == null && building == null)
            {
                var center = CameraTile();
                var next = wanted.Where(k => !loaded.ContainsKey(k) && !failed.Contains(k)).OrderBy(k => (k.X - center.X) * (k.X - center.X) + (k.Y - center.Y) * (k.Y - center.Y)).Select(k => (TileKey?)k).FirstOrDefault();
                if (next.HasValue)
                {
                    var key = next.Value; var assets = store; var map = world; int version = generation; var token = cancellation.Token;
                    // Failed tiles are tracked separately; obsolete requests can be retried when revisited.
                    pendingKey = key; pending = Task.Run(() => Prepare(assets, map, key, version, token));
                }
            }
        }
        status.Text = message;
        var current = CameraTile(); int cells = loaded.Values.Sum(n => n.GetChildCount()) + (buildingNode?.GetChildCount() ?? 0);
        telemetry.Text = $"{world?.Name ?? "No map"}    |    Tile {current.X}, {current.Y}    |    {loaded.Count}/{wanted.Count} tiles · {cells:N0} cells    |    {renderer.TextureCount} textures    |    {speed:0} yd/s    |    {Engine.GetFramesPerSecond()} FPS";
        minimap.World = world; minimap.Loaded = loaded.Keys.ToHashSet(); minimap.Current = current; minimap.CameraPosition = camera.Position; minimap.QueueRedraw();
        if (smoke) Smoke(delta);
        creatures.Update(camera);
        if (creatureSmoke) CreatureSmoke(delta);
        benchmark?.Sample(delta, uploadMs, world != null && !busy && loaded.Count == wanted.Count && loaded.Count > 0 && pending == null && building == null,
            loaded.Count, wanted.Count, () =>
            {
                if (world == null) return;
                var currentTile = CameraTile();
                var destination = world.Tiles.Where(k => Math.Abs(k.X - currentTile.X) >= 4)
                    .OrderBy(k => Math.Abs(k.X - currentTile.X) + Math.Abs(k.Y - currentTile.Y)).First();
                Teleport(destination);
            }, this);
    }
    void RaiseAboveTerrain(Node3D tile)
    {
        // Pick nearest mesh vertex to prevent spawning inside mountains.
        float best = float.MaxValue, height = camera.Position.Y;
        foreach (var mesh in tile.GetChildren().OfType<MeshInstance3D>())
        {
            var arrays = mesh.Mesh.SurfaceGetArrays(0); var vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            foreach (var v in vertices)
            { var pos = tile.Position + v; float d = new Vector2(pos.X - camera.Position.X, pos.Z - camera.Position.Z).LengthSquared(); if (d < best) { best = d; height = pos.Y; } }
        }
        if (camera.Position.Y < height + 35) camera.Position = new Vector3(camera.Position.X, height + 80, camera.Position.Z);
    }
    public override void _UnhandledInput(InputEvent input)
    {
        if (foundationShell) return;
        if (input is InputEventMouseButton mouse)
        {
            if (mouse.Pressed && mouse.ButtonIndex == MouseButton.Left && !capture)
            { creatures.Pick(camera, mouse.Position); return; }
            if (mouse.ButtonIndex == MouseButton.Right) { capture = mouse.Pressed; Input.MouseMode = capture ? Input.MouseModeEnum.Captured : Input.MouseModeEnum.Visible; }
            if (mouse.Pressed && mouse.ButtonIndex == MouseButton.WheelUp) speed = Math.Min(1500, speed * 1.25f);
            if (mouse.Pressed && mouse.ButtonIndex == MouseButton.WheelDown) speed = Math.Max(5, speed / 1.25f);
        }
        if (input is InputEventMouseMotion motion && capture)
        { yaw -= motion.Relative.X * 0.0025f; pitch = Mathf.Clamp(pitch - motion.Relative.Y * 0.0025f, -1.55f, 1.55f); camera.Rotation = new Vector3(pitch, yaw, 0); }
        if (input is InputEventKey key && key.Pressed && key.Keycode == Key.Escape) { capture = false; Input.MouseMode = Input.MouseModeEnum.Visible; }
    }
    void Smoke(double delta)
    {
        smokeTime += delta;
        if (smokeTime > 120) { GD.PushError("TERRAIN_SMOKE_TIMEOUT " + message); GetTree().Quit(1); return; }
        if (world == null || busy) return;
        if (loaded.Count != wanted.Count || loaded.Count == 0 || pending != null || building != null) { smokeSettle = 0; return; }
        if (++smokeSettle < 3) return;
        if (smokeStage == 0)
        {
            System.IO.Directory.CreateDirectory(ProjectSettings.GlobalizePath("res://artifacts"));
            if (DisplayServer.GetName() != "headless") GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath("res://artifacts/terrain-preview.png"));
            GD.Print($"TERRAIN_SMOKE_INITIAL tiles={loaded.Count} textures={renderer.TextureCount}");
            var destination = world.Tiles.Where(k => Math.Abs(k.X - smokeOriginal.X) >= 4).OrderBy(k => Math.Abs(k.X - smokeOriginal.X) + Math.Abs(k.Y - smokeOriginal.Y)).First();
            Teleport(destination); smokeStage = 1; smokeSettle = 0;
        }
        else if (smokeStage == 1 && loaded.Count == wanted.Count && loaded.Count > 0 && pending == null && building == null)
        {
            if (loaded.Keys.Any(k => !wanted.Contains(k)) || loaded.ContainsKey(smokeOriginal)) { GD.PushError("Stale streamed tiles"); GetTree().Quit(1); return; }
            GD.Print($"TERRAIN_SMOKE_MOVED position={CameraTile()} tiles={loaded.Count} textures={renderer.TextureCount}");
            Teleport(smokeOriginal); smokeStage = 2; smokeSettle = 0;
        }
        else if (smokeStage == 2)
        {
            GD.Print($"TERRAIN_SMOKE_PASSED returned={CameraTile()} tiles={loaded.Count} textures={renderer.TextureCount}"); GetTree().Quit(); smokeStage = 3;
        }
    }
    void CreatureSmoke(double delta)
    {
        creatureSmokeTime += delta;
        if (creatureSmokeTime > 60)
        { GD.PushError("CREATURE_SMOKE_TIMEOUT " + creatures.QueryStatus); GetTree().Quit(1); return; }
        if (busy || world == null || creatures.IsLoading || creatures.Records.Count == 0) return;
        if (creatures.Records.Any(s => s.Pose.Map != currentMapId))
        { GD.PushError("CREATURE_SMOKE_STALE_MAP"); GetTree().Quit(1); return; }
        if (creatureSmokeStage == 0)
        {
            if (loaded.Count != wanted.Count || pending != null || building != null) return;
            var centerTile = CameraTile();
            var spawn = creatures.Records.Where(s => ACWorldEdit.Foundation.WorldCoordinates.Tile(new(s.Pose.X, s.Pose.Y, s.Pose.Z)) == (centerTile.X, centerTile.Y))
                .OrderBy(s => creatures.PositionOf(s.Guid).DistanceSquaredTo(camera.Position)).First();
            var position = creatures.PositionOf(spawn.Guid);
            camera.Position = position + new Vector3(0, 8, 15); camera.LookAt(position + Vector3.Up);
            if (!creatures.Pick(camera, camera.UnprojectPosition(position + Vector3.Up)) || creatures.SelectedGuid != spawn.Guid || creatures.SelectedListGuid != spawn.Guid)
            { GD.PushError("CREATURE_SMOKE_PICK_FAILED"); GetTree().Quit(1); return; }
            creatures.Select(spawn.Guid);
            GD.Print($"CREATURE_SMOKE_SELECTED count={creatures.Records.Count} guid={spawn.Guid} map={spawn.Pose.Map}");
            creatureSelectedAt = creatureSmokeTime; creatureSmokeStage = 1;
        }
        else if (creatureSmokeStage == 1)
        {
            if (creatureSmokeTime - creatureSelectedAt < 1) return;
            if (DisplayServer.GetName() != "headless")
            {
                System.IO.Directory.CreateDirectory(ProjectSettings.GlobalizePath("res://artifacts"));
                GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath("res://artifacts/creature-preview.png"));
            }
            creatures.Refresh(); creatures.Update(camera); // Start a query, then replace its map immediately.
            int nextMap = mapList.FindIndex(m => m.Directory == "Kalimdor");
            if (nextMap < 0) { GetTree().Quit(1); return; }
            maps.Select(nextMap); tileX.Value = 32; tileY.Value = 32;
            creatureSmokeStage = 2; _ = OpenMap();
        }
        else
        {
            if (currentMapId != 1 || creatures.Records.Any(s => s.Pose.Map != 1))
            { GD.PushError("CREATURE_SMOKE_MAP_SWITCH_FAILED"); GetTree().Quit(1); return; }
            GD.Print($"CREATURE_SMOKE_PASSED map-switch/cancel count={creatures.Records.Count}"); GetTree().Quit();
        }
    }
    public override void _Notification(int what)
    {
        if (what == NotificationWMWindowFocusOut) { capture = false; Input.MouseMode = Input.MouseModeEnum.Visible; }
    }
    public override void _ExitTree()
    {
        cancellation.Cancel(); var assets = store;
        if (pending != null) _ = pending.ContinueWith(_ => assets?.Dispose()); else assets?.Dispose();
        cancellation.Dispose();
    }
}
