using Godot;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// The drawn waypoint paths: one direction-shaded line per path (closed back to the first point, as
/// the server repeats the path), a dot per point and, for the emphasized path, numbered labels. The
/// source rows stay detached and read-only; the overlay owns the selection and the draft.
/// </summary>
internal sealed class WaypointPaths(Node3D owner)
{
    /// <summary>A path's scene nodes plus the effective view positions of its points, in point order.</summary>
    public sealed class View(Node3D root)
    {
        public Node3D Root { get; } = root;
        public MeshInstance3D? Line, Closing;
        public MultiMeshInstance3D? Dots;
        public Node3D? Labels;
        public Vector3[] Positions = [];
        public bool Emphasized;
    }

    public static readonly Color StartColor = new("6bff76"), EndColor = new("ff9d4a"), DelayColor = new("ffe45c"), IdleColor = new("8a949e"), SelectedColor = new("ffcf69"), ClosingColor = new("c9a0ff");
    const float DotRadius = 0.45f, EmphasizedDotRadius = 0.65f;

    public readonly SphereMesh DotMesh = new()
    {
        Radius = 1,
        Height = 2,
        RadialSegments = 12,
        Rings = 6
    };

    readonly StandardMaterial3D lineMaterial = new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        VertexColorUseAsAlbedo = true,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        NoDepthTest = true,
        RenderPriority = 10
    };
    readonly StandardMaterial3D dotMaterial = new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        VertexColorUseAsAlbedo = true,
        NoDepthTest = true,
        RenderPriority = 11
    };
    readonly Dictionary<uint, View> views = [];

    public IReadOnlyDictionary<uint, View> Views => views;

    public bool Contains(uint id) => views.ContainsKey(id);

    public bool TryView(uint id, out View? view) => views.TryGetValue(id, out view);

    /// <summary>Remove every path's nodes; the lookup and selection are owned elsewhere.</summary>
    public void Clear() => views.FreeAll(v => v.Root);

    /// <summary>Remove one path's nodes.</summary>
    public void Remove(uint id) => views.Free(id, v => v.Root);

    /// <summary>A solid dot marker for the selection handle or the followed creature's position.</summary>
    public MeshInstance3D Marker(Color color, float radius)
    {
        var marker = new MeshInstance3D
        {
            Mesh = DotMesh,
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
        owner.AddChild(marker);
        return marker;
    }

    /// <summary>
    /// (Re)create a path's nodes from its effective positions. <paramref name="stateOf"/> resolves the
    /// draft over the source row; the optional override substitutes a point's view position while it
    /// is being dragged. <paramref name="nodes"/> defaults to the region records; pass the display
    /// nodes when row edits added or renumbered points the records do not have yet.
    /// </summary>
    public void Rebuild(WaypointPath path, Func<WaypointNode, WaypointState> stateOf, bool emphasized, (uint Point, Vector3 View)? preview = null, WaypointNode[]? nodes = null)
    {
        if (!views.TryGetValue(path.Id, out var view))
        {
            var root = new Node3D
            {
                Name = "Waypoints_" + path.Id,
                Visible = false
            };
            owner.AddChild(root);
            views[path.Id] = view = new View(root);
        }

        foreach (var child in view.Root.GetChildren())
        {
            child.QueueFree();
        }

        view.Line = view.Closing = null;
        view.Dots = null;
        view.Labels = null;
        view.Emphasized = emphasized;
        var display = nodes ?? path.Nodes;
        var states = display.Select(stateOf).ToArray();
        view.Positions = display.Select((n, i) => preview is { } p && p.Point == n.Point ? p.View : ViewSpace.ToView(states[i].Position.X, states[i].Position.Y, states[i].Position.Z)).ToArray();
        int count = display.Length;
        if (count == 0)
        {
            return;
        }

        BuildLine(path, view, emphasized, count);
        BuildDots(path, view, states, display, emphasized, count);
        if (emphasized)
        {
            BuildLabels(path, view, states, display, count);
        }
    }

    void BuildLine(WaypointPath path, View view, bool emphasized, int count)
    {
        float alpha = emphasized ? 1 : 0.55f;
        Color Gradient(int i) => path.Active ? StartColor.Lerp(EndColor, count > 1 ? i / (float)(count - 1) : 0) : IdleColor;
        if (count <= 1)
        {
            return;
        }

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

    void BuildDots(WaypointPath path, View view, WaypointState[] states, WaypointNode[] display, bool emphasized, int count)
    {
        float alpha = emphasized ? 1 : 0.55f;
        Color Gradient(int i) => path.Active ? StartColor.Lerp(EndColor, count > 1 ? i / (float)(count - 1) : 0) : IdleColor;
        float radius = emphasized ? EmphasizedDotRadius : DotRadius;
        var multi = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            Mesh = DotMesh,
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
    }

    static void BuildLabels(WaypointPath path, View view, WaypointState[] states, WaypointNode[] nodes, int count)
    {
        view.Labels = new Node3D();
        view.Root.AddChild(view.Labels);
        for (int i = 0; i < count; i++)
        {
            var node = nodes[i];
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
}
