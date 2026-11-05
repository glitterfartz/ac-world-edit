using Godot;
using ACWorldEdit.Assets;
using NVector = System.Numerics.Vector3;

namespace ACWorldEdit;

/// <summary>Owns an independent, generation-guarded file worker and bounded main-thread uploads.</summary>
public partial class NavigationOverlay : Node3D
{
    public Action<string> Report { get; set; } = _ => { };
    public bool LayerEnabled { get; private set; }
    public Func<PickRay, float?>? OcclusionDistance { get; set; }
    public bool Inspect { get; private set; }
    public int TileCount => tiles.Count;
    INavigationReader reader = new AcNavigationReader();
    NavigationSource? source;
    NavigationParameters? mapParameters;
    int? map;
    int generation;
    CancellationTokenSource cancellation = new();
    Task<Result>? pending;
    readonly Dictionary<TileKey, Tile> tiles = [];
    readonly HashSet<TileKey> attempted = [];
    readonly HashSet<byte> hidden = [];
    readonly HashSet<string> notices = [];
    TileKey center;
    int radius;
    bool surfaces = true, edges = true, bounds, connections = true, xray;
    float opacity = .4f;
    StandardMaterial3D fill = null!, line = null!, highlight = null!;
    Label detail = null!, folder = null!;
    VBoxContainer filters = null!;
    ScrollContainer panelScroll = null!;
    CheckBox enabled = null!;
    ClientKind kind;
    string directory = "";
    IReadOnlyDictionary<int, int>? parents;
    MeshInstance3D? selected;
    Tile? selectedTile;
    int selectedPolygon = -1;
    sealed record Result(int Generation, TileKey Key, NavigationTile? Data, NavigationParameters? Parameters, string? Error, bool Missing);
    sealed class Tile(NavigationTile data, Node3D root, NavigationParameters? parameters)
    {
        public NavigationTile Data = data;
        public Node3D Root = root;
        public NavigationParameters? Parameters = parameters;
        public int Triangle, Polygon;
        public bool Finished;
        public List<(Node3D Node, byte? Area, int Kind)> Parts = [];
    }
    public override void _Ready()
    {
        fill = Material(true); line = Material(false); highlight = Material(true);
        fill.AlbedoColor = new Color(1, 1, 1, opacity);
        highlight.AlbedoColor = new Color(1, .85f, .15f, .85f);
    }
    StandardMaterial3D Material(bool transparent) => new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        VertexColorUseAsAlbedo = true,
        Transparency = transparent ? BaseMaterial3D.TransparencyEnum.Alpha : BaseMaterial3D.TransparencyEnum.Disabled,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        NoDepthTest = xray
    };
    public void Configure(ClientKind value, IReadOnlyDictionary<int, int>? mapParents)
    {
        kind = value; parents = mapParents; reader = value == ClientKind.Retail ? new TcNavigationReader() : new AcNavigationReader();
        directory = System.Environment.GetEnvironmentVariable(kind == ClientKind.Retail ? "ACWORLD_TC_MMAP_DIR" : "ACWORLD_AC_MMAP_DIR") ?? UserSettings.LoadNavigationDirectory(kind);
        if (folder != null) folder.Text = directory.Length == 0 ? "Choose an MMaps folder." : directory;
        Reset(); BuildFilters();
    }
    public void SetMap(int? value) { map = value; Reset(); }
    public void SetLayerEnabled(bool value)
    {
        LayerEnabled = value;
        if (enabled != null) enabled.SetPressedNoSignal(value);
        Reset();
        if (value && directory.Length == 0) Report("Choose an MMaps folder in Scene explorer → MMaps.");
    }
    public void Reset()
    {
        generation++; cancellation.Cancel(); cancellation.Dispose(); cancellation = new();
        foreach (var tile in tiles.Values) tile.Root.QueueFree();
        tiles.Clear(); attempted.Clear(); notices.Clear(); ClearSelection();
        mapParameters = null;
        source = directory.Length == 0 ? null : new NavigationSource(directory, reader, kind == ClientKind.Retail ? parents : null);
    }
    void ClearSelection()
    {
        selected?.QueueFree(); selected = null; selectedTile = null; selectedPolygon = -1;
        if (detail != null) detail.Text = "Enable Inspect navmesh, then click a navigation surface.";
    }
    public void UpdateRegion(TileKey value, int loadRadius)
    {
        center = value; radius = loadRadius;
        foreach (var pair in tiles.ToArray())
        {
            if (Math.Abs(pair.Key.X - center.X) <= radius + 1 && Math.Abs(pair.Key.Y - center.Y) <= radius + 1) continue;
            if (selectedTile == pair.Value) ClearSelection();
            pair.Value.Root.QueueFree(); tiles.Remove(pair.Key); attempted.Remove(pair.Key);
        }
    }
    public override void _Process(double delta)
    {
        if (pending is { IsCompleted: true })
        {
            var result = pending.GetAwaiter().GetResult(); pending = null;
            if (result.Generation == generation && LayerEnabled)
            {
                if (result.Error != null) { if (notices.Add(result.Error)) Report(result.Error); }
                else if (result.Data != null && Math.Abs(result.Key.X - center.X) <= radius + 1 && Math.Abs(result.Key.Y - center.Y) <= radius + 1)
                {
                    mapParameters = result.Parameters;
                    var root = new Node3D(); AddChild(root);
                    tiles[result.Key] = new Tile(result.Data, root, result.Parameters);
                }
                else if (result.Data != null) attempted.Remove(result.Key);
            }
        }
        if (!LayerEnabled || map == null || source == null) return;
        // At most one bounded geometry batch per frame, even while files decode.
        var building = tiles.Values.FirstOrDefault(t => !t.Finished);
        if (building != null) Upload(building);
        if (pending != null) return;
        var wanted = new List<TileKey>();
        for (int y = center.Y - radius; y <= center.Y + radius; y++)
            for (int x = center.X - radius; x <= center.X + radius; x++)
                if (x >= 0 && x < 64 && y >= 0 && y < 64 && !attempted.Contains(new(x, y))) wanted.Add(new(x, y));
        if (wanted.Count == 0) return;
        var key = wanted.OrderBy(k => Math.Abs(k.X - center.X) + Math.Abs(k.Y - center.Y)).First(); attempted.Add(key);
        int version = generation, mapId = map.Value; var files = source; var tileReader = reader; var parametersSnapshot = mapParameters; var token = cancellation.Token;
        pending = Task.Run(() =>
        {
            try
            {
                token.ThrowIfCancellationRequested();
                // Filename coordinates are server grid X/Y, opposite to the viewer's horizontal tile axes.
                var data = files.Read(mapId, key.Y, key.X); token.ThrowIfCancellationRequested();
                var parameters = parametersSnapshot ?? files.Parameters(mapId); token.ThrowIfCancellationRequested();
                return new Result(version, key, data, parameters, null, false);
            }
            catch (OperationCanceledException) { return new Result(version, key, null, null, null, false); }
            catch (FileNotFoundException) { return new Result(version, key, null, null, "Navigation tiles or map parameters are missing for this region.", true); }
            catch (Exception e) { return new Result(version, key, null, null, $"Navigation {tileReader.TileName(mapId, key.Y, key.X)}: {e.Message}", false); }
        });
    }
    static Vector3 View(NVector point)
    {
        var v = NavigationTile.ToView(point); return new(v.X, v.Y + .08f, v.Z);
    }
    Color AreaColor(byte area) => reader.AreaName(area) switch
    {
        "Ground" => new(.2f, .95f, .45f),
        "Water" => new(.15f, .55f, 1),
        "Steep ground" => new(1, .8f, .2f),
        "Magma" or "Magma / slime" => new(1, .25f, .15f),
        "Slime" => new(.8f, .3f, 1),
        _ => new(.7f, .7f, .7f)
    };
    void AddPart(Tile tile, List<Vector3> vertices, List<Color> colors, byte? area, int type)
    {
        if (vertices.Count == 0) return;
        var arrays = new Godot.Collections.Array(); arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices.ToArray(); arrays[(int)Mesh.ArrayType.Color] = colors.ToArray();
        var mesh = new ArrayMesh(); mesh.AddSurfaceFromArrays(type == 0 ? Mesh.PrimitiveType.Triangles : Mesh.PrimitiveType.Lines, arrays);
        var node = new MeshInstance3D { Mesh = mesh, MaterialOverride = type == 0 ? fill : line, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        tile.Root.AddChild(node); tile.Parts.Add((node, area, type)); ApplyVisibility(tile);
    }
    void Upload(Tile tile)
    {
        var data = tile.Data;
        if (tile.Triangle < data.Triangles.Length)
        {
            var batch = data.Triangles.Skip(tile.Triangle).Take(512).ToArray(); tile.Triangle += batch.Length;
            foreach (var group in batch.GroupBy(t => data.Polygons[t.Polygon].Area))
            {
                var vertices = new List<Vector3>(); var colors = new List<Color>(); var color = AreaColor(group.Key);
                foreach (var t in group) { vertices.AddRange([View(t.A), View(t.B), View(t.C)]); colors.AddRange([color, color, color]); }
                AddPart(tile, vertices, colors, group.Key, 0);
            }
            return;
        }
        if (tile.Polygon < data.Polygons.Length)
        {
            var batch = data.Polygons.Skip(tile.Polygon).Take(512).Where(p => p.Type == 0).ToArray(); tile.Polygon += 512;
            foreach (var group in batch.GroupBy(p => p.Area))
            {
                var vertices = new List<Vector3>();
                foreach (var p in group) for (int i = 0; i < p.Vertices.Length; i++) vertices.AddRange([View(data.Vertices[p.Vertices[i]]), View(data.Vertices[p.Vertices[(i + 1) % p.Vertices.Length]])]);
                AddPart(tile, vertices, Enumerable.Repeat(AreaColor(group.Key).Lightened(.35f), vertices.Count).ToList(), group.Key, 1);
            }
            return;
        }
        var lo = data.Minimum; var hi = data.Maximum;
        NVector[] corners = [lo, new(hi.X, lo.Y, lo.Z), new(hi.X, lo.Y, hi.Z), new(lo.X, lo.Y, hi.Z)];
        var boundary = new List<Vector3>(); for (int i = 0; i < 4; i++) boundary.AddRange([View(corners[i]), View(corners[(i + 1) % 4])]);
        AddPart(tile, boundary, Enumerable.Repeat(new Color(1, .8f, .1f), boundary.Count).ToList(), null, 2);
        var links = data.Connections.Concat(tile.Parameters?.Connections.Where(c => c.TileX == data.FileX && c.TileY == data.FileY) ?? []);
        var points = links.SelectMany(c => new[] { View(c.From), View(c.To) }).ToList();
        AddPart(tile, points, Enumerable.Repeat(new Color(1, .3f, 1), points.Count).ToList(), null, 3);
        tile.Finished = true;
    }
    void ApplyVisibility(Tile tile)
    {
        foreach (var part in tile.Parts) part.Node.Visible = (!part.Area.HasValue || !hidden.Contains(part.Area.Value)) && (part.Kind switch { 0 => surfaces, 1 => edges, 2 => bounds, _ => connections });
    }
    void RefreshDisplay()
    {
        fill.AlbedoColor = new Color(1, 1, 1, opacity); fill.NoDepthTest = line.NoDepthTest = highlight.NoDepthTest = xray;
        foreach (var tile in tiles.Values) ApplyVisibility(tile);
        if (selectedTile != null && (hidden.Contains(selectedTile.Data.Polygons[selectedPolygon].Area) || !surfaces)) ClearSelection();
    }
    public void Pick(Camera3D camera, Vector2 screen)
    {
        ClearSelection(); if (!LayerEnabled || !surfaces || opacity <= 0) return;
        var origin = camera.ProjectRayOrigin(screen); var direction = camera.ProjectRayNormal(screen); float nearest = camera.Far;
        if (!xray && OcclusionDistance?.Invoke(PickRay.From(camera, screen)) is float blocker) nearest = Math.Min(nearest, blocker + .25f);
        foreach (var tile in tiles.Values) foreach (var triangle in tile.Data.Triangles.Take(tile.Triangle))
        {
            if (hidden.Contains(tile.Data.Polygons[triangle.Polygon].Area)) continue;
            var hit = Geometry3D.RayIntersectsTriangle(origin, direction, View(triangle.A), View(triangle.B), View(triangle.C));
            if (hit.VariantType != Variant.Type.Vector3) continue;
            float distance = origin.DistanceTo(hit.AsVector3());
            if (distance >= nearest) continue;
            nearest = distance; selectedTile = tile; selectedPolygon = triangle.Polygon;
        }
        if (selectedTile == null) return;
        var data = selectedTile.Data; var polygon = data.Polygons[selectedPolygon];
        var vertices = data.Triangles.Where(t => t.Polygon == selectedPolygon).SelectMany(t => new[] { View(t.A), View(t.B), View(t.C) }).ToArray();
        var arrays = new Godot.Collections.Array(); arrays.Resize((int)Mesh.ArrayType.Max); arrays[(int)Mesh.ArrayType.Vertex] = vertices;
        var mesh = new ArrayMesh(); mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        selected = new MeshInstance3D { Mesh = mesh, MaterialOverride = highlight }; AddChild(selected);
        detail.Text = $"{data.File}\nRequested map {data.RequestedMap} · source {data.SourceMap}\nFile tile {data.FileX}, {data.FileY} · Detour grid {data.GridX}, {data.GridY}, layer {data.Layer}\nPolygon {selectedPolygon} · {reader.AreaName(polygon.Area)} ({polygon.Area}) · flags 0x{polygon.Flags:X4}\nNeighbors (0=wall, 1-based internal, 0x8000=external): {string.Join(", ", polygon.Neighbors.Select(n => $"0x{n:X4}"))}\nDetour vertices: {string.Join("; ", polygon.Vertices.Select(v => FormatPoint(data.Vertices[v])))}\nAgent height {data.AgentHeight:0.###}, radius {data.AgentRadius:0.###}, climb {data.AgentClimb:0.###}\nUses liquids: {data.UsesLiquids}\n{DescribeSettings(data.Settings)}";
        Callable.From(() => panelScroll.ScrollVertical = 0).CallDeferred();
        Report($"Selected navigation polygon {selectedPolygon} in {System.IO.Path.GetFileName(data.File)}.");
    }
    static string FormatPoint(NVector point) => $"({point.X:0.###}, {point.Y:0.###}, {point.Z:0.###})";
    static string DescribeSettings(NavigationSettings? settings) => settings is not { } s ? "Generator settings are not stored in this format." :
        $"Generator slope {s.Slope:0.##}°\nAgent cells: radius {s.Radius}, height {s.Height}, climb {s.Climb}\nVertices per map edge {s.VerticesPerMap}, per subtile edge {s.VerticesPerTile}; subtiles per edge {s.TilesPerMap}\nCell size {s.CellSize:0.####}, cell height {s.CellHeight:0.####}, base unit {s.BaseUnit:0.####}, simplification error {s.Simplification:0.###}";
    public Control BuildPanel()
    {
        var page = InspectorUi.Page("MMaps", out var scroll); panelScroll = scroll;
        detail = InspectorUi.Text("Enable Inspect navmesh, then click a navigation surface.", 12); InspectorUi.Card(page, "Polygon").AddChild(detail);
        var controls = InspectorUi.Card(page, "Navigation overlay");
        folder = InspectorUi.Text("Choose an MMaps folder.", 12, true); controls.AddChild(folder);
        controls.AddChild(InspectorUi.Button("Choose folder…", () =>
        {
            var dialog = new FileDialog { FileMode = FileDialog.FileModeEnum.OpenDir, Access = FileDialog.AccessEnum.Filesystem, Title = "Choose MMaps folder", UseNativeDialog = true };
            AddChild(dialog); dialog.DirSelected += path => { directory = path; UserSettings.SaveNavigationDirectory(kind, path); folder.Text = path; Reset(); dialog.QueueFree(); };
            dialog.Canceled += dialog.QueueFree; dialog.PopupCenteredRatio(.7f);
        }));
        void Toggle(string text, bool initial, Action<bool> action) { var box = new CheckBox { Text = text, ButtonPressed = initial }; controls.AddChild(box); box.Toggled += v => action(v); }
        enabled = new CheckBox { Text = "Show navigation overlay" }; controls.AddChild(enabled); enabled.Toggled += SetLayerEnabled;
        Toggle("Inspect navmesh", false, v => { Inspect = v; ClearSelection(); });
        Toggle("Surfaces", true, v => { surfaces = v; RefreshDisplay(); }); Toggle("Polygon edges", true, v => { edges = v; RefreshDisplay(); });
        Toggle("Tile boundaries", false, v => { bounds = v; RefreshDisplay(); }); Toggle("Off-mesh connections", true, v => { connections = v; RefreshDisplay(); });
        Toggle("X-ray", false, v => { xray = v; RefreshDisplay(); });
        controls.AddChild(InspectorUi.Text("Surface opacity", 12)); var slider = new HSlider { MinValue = 0, MaxValue = 1, Step = .05, Value = opacity }; controls.AddChild(slider);
        slider.ValueChanged += v => { opacity = (float)v; RefreshDisplay(); };
        controls.AddChild(InspectorUi.Button("Reload navigation", Reset));
        filters = new VBoxContainer(); InspectorUi.Card(page, "Terrain legend / filters").AddChild(filters); BuildFilters();

        return scroll;
    }
    void BuildFilters()
    {
        if (filters == null) return;
        foreach (var child in filters.GetChildren()) child.QueueFree(); hidden.Clear();
        foreach (byte area in kind == ClientKind.Retail ? new byte[] { 11, 10, 9, 8 } : new byte[] { 1, 2, 4, 8 })
        {
            var row = new HBoxContainer(); filters.AddChild(row);
            row.AddChild(new ColorRect { Color = AreaColor(area), CustomMinimumSize = new Vector2(12, 12), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter });
            var box = new CheckBox { Text = $"{reader.AreaName(area)} ({area})", ButtonPressed = true, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; row.AddChild(box);
            box.Toggled += v => { if (v) hidden.Remove(area); else hidden.Add(area); RefreshDisplay(); };
        }
        var unknown = new CheckBox { Text = "■ Unknown areas", ButtonPressed = true }; filters.AddChild(unknown);
        unknown.Toggled += value =>
        {
            for (byte area = 0; area < 64; area++)
                if (reader.AreaName(area).StartsWith("Unknown", StringComparison.Ordinal)) { if (value) hidden.Remove(area); else hidden.Add(area); }
            RefreshDisplay();
        };
        filters.AddChild(InspectorUi.Text("Unknown areas appear in gray. Flags are shown numerically. Lines: yellow tile boundary; pink off-mesh link.", 11, true));
    }
    public override void _ExitTree() { cancellation.Cancel(); cancellation.Dispose(); }
}
