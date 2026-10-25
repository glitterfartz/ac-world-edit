using Godot;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// Bounded projection of waypoint_data paths used by the creatures near the streamed region: a
/// direction-shaded line per path (closed back to the first point, as the server repeats the path),
/// a dot per point and, for the emphasized path, numbered labels. Source rows stay detached and
/// read-only; WaypointDraft owns durable local moves. A path is emphasized while one of its points
/// is selected or one of its creatures is selected in the creature layer.
/// </summary>
public partial class WaypointOverlay : RegionOverlay
{
    public WaypointOverlay() : base(true, "Waiting for a world…")
    {
    }

    /// <summary>
    /// A path's scene nodes plus the effective view positions of its points, in point order.
    /// </summary>
    sealed class PathView(Node3D root)
    {
        public Node3D Root { get; } = root;
        public MeshInstance3D? Line, Closing;
        public MultiMeshInstance3D? Dots;
        public Node3D? Labels;
        public Vector3[] Positions = [];
        public bool Emphasized;
    }

    static readonly Color StartColor = new("6bff76"), EndColor = new("ff9d4a"), DelayColor = new("ffe45c"), IdleColor = new("8a949e"), SelectedColor = new("ffcf69"), ClosingColor = new("c9a0ff");
    const float DotRadius = 0.45f, EmphasizedDotRadius = 0.65f, PickPixels = 12;

    /// <summary>The creature selected in the creature layer; its path is emphasized.</summary>
    public Func<ulong?> FocusedCreature { get; set; } = () => null;
    public IReadOnlyCollection<WaypointPath> Paths => lookup.Values;
    public bool IsLoading => query.IsLoading;
    public event Action? SelectionChanged;
    public event Action<WorldPose>? FocusRequested;

    /// <summary>
    /// Draw every path in the region (true) or only the emphasized one (false).
    /// </summary>
    public bool ShowAll
    {
        get => showAll;
        set => showAll = value;
    }

    bool showAll;
    readonly Dictionary<uint, WaypointPath> lookup = [];
    readonly Dictionary<uint, PathView> views = [];
    readonly RegionQuery<WaypointRegionResult> query = new();
    string sourceKey = "";
    (uint Path, uint Point)? selected;
    uint? focusedPath, emphasized;
    ulong? focusedGuid;
    SphereMesh dotMesh = null!;
    StandardMaterial3D lineMaterial = null!, dotMaterial = null!;
    MeshInstance3D handle = null!, walker = null!;

    public override void _Ready()
    {
        dotMesh = new SphereMesh
        {
            Radius = 1,
            Height = 2,
            RadialSegments = 12,
            Rings = 6
        };
        lineMaterial = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            VertexColorUseAsAlbedo = true,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            NoDepthTest = true,
            RenderPriority = 10
        };
        dotMaterial = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            VertexColorUseAsAlbedo = true,
            NoDepthTest = true,
            RenderPriority = 11
        };
        handle = Marker(SelectedColor, 0.9f);
        walker = Marker(new Color("5bd8ff"), 0.7f);
        OpenDraft();
    }

    MeshInstance3D Marker(Color color, float radius)
    {
        var marker = new MeshInstance3D
        {
            Mesh = dotMesh,
            Scale = Vector3.One * radius,
            Visible = false,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                AlbedoColor = color,
                NoDepthTest = true,
                RenderPriority = 12
            }
        };
        AddChild(marker);
        return marker;
    }

    protected override void OnRegionChanged(bool mapChanged)
    {
        // Re-center nearest-first ordering on the camera at the next update.
        point = null;
        Restart(mapChanged);
    }

    protected override void Restart(bool hard)
    {
        query.Restart();
        if (hard)
        {
            Deselect();
            views.FreeAll(v => v.Root);
            lookup.Clear();
            emphasized = focusedPath = null;
            focusedGuid = null;
            StopPreview();
            list?.Clear();
        }

        queryStatus = LayerEnabled ? "Loading waypoint paths…" : "Waypoint layer hidden";
    }

    public void Update(Camera3D camera)
    {
        if (LayerEnabled && region != null)
        {
            point ??= ViewSpace.QueryPointAt(camera.Position, region.Map);
        }

        if (query.TakeSettledSearch())
        {
            PopulateList();
        }

        query.Poll(Publish, error => queryStatus = error);
        if (LayerEnabled && region != null && query.WantsStart)
        {
            var area = region;
            var center = point;
            var config = Connection();
            query.Start(token => DBSource.Open(config).ReadWaypointsAsync(area, center, token));
        }

        UpdateFocus();
        foreach (var (id, view) in views)
        {
            view.Root.Visible = LayerEnabled && (showAll || id == emphasized);
        }

        UpdateHandle();
        UpdatePreview();
        if (status != null)
        {
            status.Text = queryStatus;
            UpdateInspectorActions();
        }

        var editing = EditableNode();
        gizmo.Update(camera, editing?.GlobalPosition, 0);
    }

    /// <summary>
    /// Follow the creature layer's selection to its path and re-emphasize on any change.
    /// </summary>
    void UpdateFocus()
    {
        ulong? guid = FocusedCreature();
        if (guid != focusedGuid)
        {
            focusedGuid = guid;
            focusedPath = guid.HasValue ? lookup.Values.FirstOrDefault(p => p.Users.Any(u => u.Guid == guid.Value))?.Id : null;
        }

        uint? next = selected?.Path ?? focusedPath;
        if (next == emphasized)
        {
            return;
        }

        uint? previous = emphasized;
        emphasized = next;
        StopPreview();
        foreach (uint? id in new[] { previous, next })
        {
            if (id.HasValue && lookup.TryGetValue(id.Value, out var path))
            {
                BuildPath(path);
            }
        }
    }

    /// <summary>
    /// Replace the source snapshot and reconcile path nodes: unchanged paths keep their nodes.
    /// </summary>
    void Publish(WaypointRegionResult result)
    {
        sourceKey = result.SourceKey;
        var incoming = result.Paths.ToDictionary(p => p.Id);
        foreach (uint id in views.Keys.Where(id => !incoming.ContainsKey(id)).ToArray())
        {
            if (selected?.Path == id)
            {
                Deselect();
            }

            views.Free(id, v => v.Root);
        }

        foreach (var path in result.Paths)
        {
            bool unchanged = lookup.TryGetValue(path.Id, out var old) && old.Nodes.SequenceEqual(path.Nodes) && views.ContainsKey(path.Id);
            lookup[path.Id] = path;
            if (!unchanged)
            {
                BuildPath(path);
            }
        }

        foreach (uint id in lookup.Keys.Where(id => !incoming.ContainsKey(id)).ToArray())
        {
            lookup.Remove(id);
        }

        focusedGuid = null;
        PopulateList();
        int points = result.Paths.Sum(p => p.Nodes.Length);
        queryStatus = result.Paths.Length == 0 ? "No waypoint paths in this region." : result.Truncated ? $"{result.Paths.Length} nearest paths · limit reached" : $"{result.Paths.Length} paths · {points} points in this region";
        if (selected.HasValue)
        {
            ShowSelection();
        }
    }

    static WaypointState SourceState(WaypointNode node) => new(new(node.X, node.Y, node.Z), new(node.Orientation, node.Velocity, node.Delay, node.Smooth, node.MoveType, node.Action, node.ActionChance));

    /// <summary>
    /// The point as the draft leaves it: every editable column, source values where untouched.
    /// </summary>
    WaypointState StateOf(WaypointPath path, WaypointNode node) => draft?.Effective(sourceKey, path.Id, node.Point, SourceState(node)) ?? SourceState(node).ToColumn();

    WaypointPosition PositionOf(WaypointPath path, WaypointNode node) => StateOf(path, node).Position;

    static Vector3 ViewPosition(WaypointPosition p) => ViewSpace.ToView(p.X, p.Y, p.Z);

    /// <summary>
    /// (Re)create a path's nodes from its effective positions. The optional override substitutes a
    /// point's view position while it is being dragged.
    /// </summary>
    void BuildPath(WaypointPath path, (uint Point, Vector3 View)? preview = null)
    {
        if (!views.TryGetValue(path.Id, out var view))
        {
            var root = new Node3D
            {
                Name = "Waypoints_" + path.Id,
                Visible = false
            };
            AddChild(root);
            views[path.Id] = view = new PathView(root);
        }

        foreach (var child in view.Root.GetChildren())
        {
            child.QueueFree();
        }

        view.Line = view.Closing = null;
        view.Dots = null;
        view.Labels = null;
        bool emphasis = path.Id == emphasized;
        view.Emphasized = emphasis;
        var states = path.Nodes.Select(n => StateOf(path, n)).ToArray();
        view.Positions = path.Nodes.Select((n, i) => preview is { } p && p.Point == n.Point ? p.View : ViewPosition(states[i].Position)).ToArray();
        int count = path.Nodes.Length;
        if (count == 0)
        {
            return;
        }

        float alpha = emphasis ? 1 : 0.55f;
        Color Gradient(int i) => path.Active ? StartColor.Lerp(EndColor, count > 1 ? i / (float)(count - 1) : 0) : IdleColor;
        if (count > 1)
        {
            var colors = Enumerable.Range(0, count).Select(i => Gradient(i) with { A = alpha }).ToArray();
            view.Line = LineMesh(view.Positions, colors, Mesh.PrimitiveType.LineStrip);
            view.Root.AddChild(view.Line);
            if (count > 2)
            {
                // The server repeats the path, so the creature walks from the last point back to the first.
                var close = new[] { view.Positions[^1], view.Positions[0] };
                var closeColor = (path.Active ? ClosingColor : IdleColor) with { A = alpha * 0.8f };
                view.Closing = LineMesh(close, [closeColor, closeColor], Mesh.PrimitiveType.Lines);
                view.Root.AddChild(view.Closing);
            }
        }

        float radius = emphasis ? EmphasizedDotRadius : DotRadius;
        var multi = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            Mesh = dotMesh,
            InstanceCount = count
        };
        for (int i = 0; i < count; i++)
        {
            multi.SetInstanceTransform(i, new Transform3D(Basis.FromScale(Vector3.One * radius), view.Positions[i]));
            var color = i == 0 ? StartColor : states[i].Props.Delay > 0 ? DelayColor : Gradient(i);
            multi.SetInstanceColor(i, path.Active || i == 0 ? color : IdleColor);
        }

        view.Dots = new MultiMeshInstance3D
        {
            Multimesh = multi,
            MaterialOverride = dotMaterial,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
        };
        view.Root.AddChild(view.Dots);
        if (emphasis)
        {
            view.Labels = new Node3D();
            view.Root.AddChild(view.Labels);
            for (int i = 0; i < count; i++)
            {
                var node = path.Nodes[i];
                view.Labels.AddChild(new Label3D
                {
                    Text = states[i].Props.Delay > 0 ? $"{node.Point} · wait {states[i].Props.Delay / 1000.0:0.#}s" : node.Point.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    Position = view.Positions[i] + Vector3.Up * 1.8f,
                    Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                    NoDepthTest = true,
                    FontSize = 28,
                    PixelSize = 0.014f,
                    OutlineSize = 8
                });
            }
        }
    }

    MeshInstance3D LineMesh(Vector3[] points, Color[] colors, Mesh.PrimitiveType type)
    {
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = points;
        arrays[(int)Mesh.ArrayType.Color] = colors;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(type, arrays);
        return new MeshInstance3D
        {
            Mesh = mesh,
            MaterialOverride = lineMaterial,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
        };
    }

    /// <summary>
    /// Show the gold handle on the selected point (its node is where the gizmo attaches).
    /// </summary>
    void UpdateHandle()
    {
        var view = SelectedView();
        handle.Visible = LayerEnabled && view.HasValue;
        if (handle.Visible && !gizmo.Dragging)
        {
            handle.Position = view!.Value;
        }
    }

    /// <summary>The handle node when a point is selected and editing is available.</summary>
    Node3D? EditableNode() => draft != null && selected.HasValue && handle.Visible ? handle : null;

    Vector3? SelectedView()
    {
        if (!selected.HasValue || !views.TryGetValue(selected.Value.Path, out var view) || !lookup.TryGetValue(selected.Value.Path, out var path))
        {
            return null;
        }

        int index = Array.FindIndex(path.Nodes, n => n.Point == selected.Value.Point);
        return index >= 0 && index < view.Positions.Length ? view.Positions[index] : null;
    }

    public void Select(uint pathId, uint point)
    {
        if (!lookup.TryGetValue(pathId, out var path) || path.Nodes.All(n => n.Point != point))
        {
            return;
        }

        var previous = selected;
        selected = (pathId, point);
        if (previous != selected)
        {
            SelectionChanged?.Invoke();
        }

        handle.Visible = true;
        handle.Position = SelectedView() ?? handle.Position;
        ShowSelection();
    }

    /// <summary>
    /// Select a path as a whole by its first point.
    /// </summary>
    public void SelectPath(uint pathId)
    {
        if (lookup.TryGetValue(pathId, out var path) && path.Nodes.Length > 0)
        {
            Select(pathId, path.Nodes[0].Point);
        }
    }

    public void Deselect()
    {
        gizmo?.Finish(false);
        var previous = selected;
        selected = null;
        if (handle != null)
        {
            handle.Visible = false;
        }

        if (previous.HasValue && lookup.TryGetValue(previous.Value.Path, out var path) && views.ContainsKey(path.Id))
        {
            BuildPath(path);
        }

        list?.DeselectAll();
        if (title != null)
        {
            ShowSelection();
        }
    }

    /// <summary>
    /// Nearest waypoint dot to the cursor among the drawn paths. The distance is along the pick ray,
    /// so it compares with creature, GameObject and graveyard hits.
    /// </summary>
    public (uint Path, uint Point, float Distance)? FindHit(Camera3D camera, Vector2 screen)
    {
        if (!LayerEnabled)
        {
            return null;
        }

        var ray = PickRay.From(camera, screen);
        (uint, uint, float)? best = null;
        float bestPixels = PickPixels * PickPixels;
        foreach (var (id, view) in views)
        {
            if (!view.Root.Visible || !lookup.TryGetValue(id, out var path))
            {
                continue;
            }

            for (int i = 0; i < view.Positions.Length && i < path.Nodes.Length; i++)
            {
                var position = view.Positions[i];
                if (camera.IsPositionBehind(position))
                {
                    continue;
                }

                float along = (position - ray.Origin).Dot(ray.Direction);
                if (along < 0 || along > camera.Far)
                {
                    continue;
                }

                float pixels = camera.UnprojectPosition(position).DistanceSquaredTo(screen);
                if (pixels <= bestPixels)
                {
                    bestPixels = pixels;
                    best = (id, path.Nodes[i].Point, along);
                }
            }
        }

        return best is { } hit ? (hit.Item1, hit.Item2, hit.Item3) : null;
    }

    public override void _ExitTree()
    {
        query.Dispose();
        draft?.Dispose();
    }
}
