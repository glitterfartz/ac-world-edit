using Godot;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// Read-only projection of server `areatrigger` volumes near the streamed region: a translucent
/// sphere or rotated box per trigger, colored by what it does, with a label and an outline on the
/// selection. Shapes mirror Player::IsInAreaTriggerRadius; the client's own AreaTrigger.dbc (which
/// decides when the trigger fires) is not shown. Hidden layers stop queries and free volumes.
/// </summary>
public partial class AreaTriggerOverlay : RegionOverlay
{
    // Off by default: large tavern and zone-line volumes would otherwise cover towns.
    public AreaTriggerOverlay() : base(false, "AreaTrigger layer hidden")
    {
    }

    const float LabelDistance = 150;
    static readonly Dictionary<AreaTriggerKind, Color> KindColors = new()
    {
        [AreaTriggerKind.SmartAI] = new("ff9f1a"),
        [AreaTriggerKind.Script] = new("b06cff"),
        [AreaTriggerKind.Teleport] = new("35c9e8"),
        [AreaTriggerKind.Quest] = new("f2d43a"),
        [AreaTriggerKind.Tavern] = new("5ccf6b"),
        [AreaTriggerKind.Unlinked] = new("8a8f96")
    };

    public uint? SelectedEntry => selected;
    public bool HasSelection => selected.HasValue;
    public IReadOnlyList<AreaTriggerRecord> Triggers => triggers;
    public bool IsLoading => query.IsLoading;
    public event Action? SelectionChanged;
    public event Action<WorldPose>? FocusRequested;
    /// <summary>Open the behavior editor on this trigger's SmartAI rules.</summary>
    public event Action<AreaTriggerRecord>? BehaviorRequested;

    // Unit shapes scaled per trigger: the box spans ±0.5 on every axis, the sphere has radius 0.5.
    static readonly BoxMesh UnitBox = new() { Size = Vector3.One };
    static readonly SphereMesh UnitSphere = new() { Radius = 0.5f, Height = 1, RadialSegments = 32, Rings = 16 };
    readonly Dictionary<AreaTriggerKind, StandardMaterial3D> fills = [];
    readonly Dictionary<uint, AreaTriggerRecord> lookup = [];
    readonly Dictionary<uint, (Node3D Node, MeshInstance3D Volume, Label3D Label)> markers = [];
    AreaTriggerRecord[] triggers = [];
    MeshInstance3D? outline;
    uint? selected;
    readonly RegionQuery<AreaTriggerRegionResult> query = new();
    string message = "";

    StandardMaterial3D Fill(AreaTriggerKind kind)
    {
        if (!fills.TryGetValue(kind, out var material))
        {
            var color = KindColors[kind];
            // Unshaded, double-sided and without depth writes so the volume reads from inside too
            // and never hides the geometry it overlaps.
            fills[kind] = material = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.Disabled,
                AlbedoColor = new(color.R, color.G, color.B, 0.16f)
            };
        }

        return material;
    }

    protected override void OnRegionChanged(bool mapChanged)
    {
        point = null;
        Restart(mapChanged);
    }

    protected override void Restart(bool hard)
    {
        query.Restart();
        if (hard)
        {
            Deselect();
            markers.FreeAll(m => m.Node);
            lookup.Clear();
            triggers = [];
            list?.Clear();
        }

        queryStatus = LayerEnabled ? "Loading AreaTriggers…" : "AreaTrigger layer hidden";
    }

    public void Update(Camera3D camera)
    {
        if (LayerEnabled && region != null)
        {
            var server = WorldCoordinates.ToServer(new(camera.Position.X, camera.Position.Y, camera.Position.Z));
            point ??= SpawnQueryPoint.FromPose(new(region.Map, server.X, server.Y, server.Z, 0));
        }

        if (query.TakeSettledSearch())
        {
            Restart(true);
        }

        query.Poll(Publish, error => queryStatus = error);
        if (LayerEnabled && region != null && query.WantsStart)
        {
            var area = region;
            var center = point;
            string term = search.Text;
            var config = Connection();
            query.Start(token => new AreaTriggerSource(config).ReadAsync(area, center, term, token));
        }

        foreach (var (entry, marker) in markers)
        {
            marker.Node.Visible = LayerEnabled;
            marker.Label.Visible = LayerEnabled && (selected == entry || camera.GlobalPosition.DistanceSquaredTo(marker.Node.GlobalPosition) < LabelDistance * LabelDistance);
        }

        status.Text = queryStatus;
        UpdateActions();
    }

    /// <summary>
    /// Replace the snapshot and reconcile volumes: keep nodes for surviving entries, free the rest and
    /// create new ones. The selection survives when its trigger is still present.
    /// </summary>
    void Publish(AreaTriggerRegionResult result)
    {
        triggers = result.Triggers;
        var incoming = result.Triggers.ToDictionary(t => t.Entry);
        foreach (uint entry in markers.Keys.Where(e => !incoming.ContainsKey(e)).ToArray())
        {
            if (selected == entry)
            {
                Deselect();
            }

            markers.Free(entry, m => m.Node);
        }

        lookup.Clear();
        foreach (var trigger in result.Triggers)
        {
            lookup[trigger.Entry] = trigger;
            markers.Free(trigger.Entry, m => m.Node);
            AddMarker(trigger);
        }

        PopulateList();
        queryStatus = result.Triggers.Length == 0 ? "No AreaTriggers in this region." : result.Truncated ? $"{result.Triggers.Length} nearest AreaTriggers · limit reached" : $"{result.Triggers.Length} AreaTriggers in this region";
        if (selected is uint current && lookup.ContainsKey(current))
        {
            Select(current);
        }
    }

    /// <summary>
    /// Place the volume: view position from the server center, yaw = orientation (the same mapping as
    /// creature headings), and for boxes a local scale of (width, height, length) because the server
    /// measures length along the facing, which is the node's local -Z after the yaw.
    /// </summary>
    void AddMarker(AreaTriggerRecord trigger)
    {
        var node = new Node3D
        {
            Name = "AreaTrigger_" + trigger.Entry,
            Position = ViewPosition(trigger),
            Rotation = new(0, (float)trigger.Orientation, 0)
        };
        var volume = new MeshInstance3D
        {
            Mesh = trigger.IsSphere ? UnitSphere : UnitBox,
            Scale = VolumeScale(trigger),
            MaterialOverride = Fill(trigger.Kind),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
        };
        var label = new Label3D
        {
            Text = LabelText(trigger),
            Position = new(0, (float)(trigger.IsSphere ? trigger.Radius : trigger.Height / 2) + 1.5f, 0),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            FontSize = 28,
            PixelSize = 0.018f,
            OutlineSize = 8,
            Modulate = KindColors[trigger.Kind].Lerp(Colors.White, 0.5f),
            NoDepthTest = true
        };
        node.AddChild(volume);
        node.AddChild(label);
        AddChild(node);
        markers[trigger.Entry] = (node, volume, label);
    }

    public static Vector3 VolumeScale(AreaTriggerRecord trigger)
    {
        // Degenerate rows (zero size) still get a visible sliver so they can be found and selected.
        static float Size(double value) => (float)Math.Max(value, 0.2);
        return trigger.IsSphere ? Vector3.One * Size(trigger.Radius * 2) : new(Size(trigger.Width), Size(trigger.Height), Size(trigger.Length));
    }

    public static Vector3 ViewPosition(AreaTriggerRecord trigger)
    {
        var p = WorldCoordinates.ToView(new(trigger.X, trigger.Y, trigger.Z));
        return new((float)p.X, (float)p.Y, (float)p.Z);
    }

    static string LabelText(AreaTriggerRecord trigger) => $"AreaTrigger {trigger.Entry}\n{Summary(trigger)}";

    /// <summary>One-line description of what the trigger does, for labels and the list.</summary>
    public static string Summary(AreaTriggerRecord trigger) => trigger.Kind switch
    {
        AreaTriggerKind.SmartAI => $"SmartAI · {trigger.SmartRows} rule{(trigger.SmartRows == 1 ? "" : "s")}",
        AreaTriggerKind.Script => "C++ " + trigger.ScriptName,
        AreaTriggerKind.Teleport => $"Teleport → map {trigger.Teleport!.Map}{(trigger.Teleport.Name.Length > 0 ? " · " + trigger.Teleport.Name : "")}",
        AreaTriggerKind.Quest => $"Quest {trigger.QuestId}{(trigger.QuestTitle.Length > 0 ? " · " + trigger.QuestTitle : "")}",
        AreaTriggerKind.Tavern => "Tavern" + (trigger.Tavern.Length > 0 ? " · " + trigger.Tavern : ""),
        _ => "No server link"
    };

    public void Select(uint entry)
    {
        if (!lookup.TryGetValue(entry, out var trigger) || !markers.TryGetValue(entry, out var marker))
        {
            return;
        }

        uint? previous = selected;
        selected = entry;
        outline?.QueueFree();
        outline = new MeshInstance3D
        {
            Mesh = Outline(trigger.IsSphere),
            Scale = marker.Volume.Scale,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                NoDepthTest = true,
                AlbedoColor = KindColors[trigger.Kind].Lerp(Colors.White, 0.35f)
            }
        };
        marker.Node.AddChild(outline);
        if (previous != entry)
        {
            message = "";
            SelectionChanged?.Invoke();
        }

        ShowSelection();
    }

    public void Deselect()
    {
        selected = null;
        outline?.QueueFree();
        outline = null;
        list?.DeselectAll();
        if (title != null)
        {
            ShowSelection();
        }
    }

    /// <summary>
    /// Edges of the unit shape: the 12 box edges, or three great circles for a sphere. Drawn without
    /// depth testing so a selected trigger stays visible inside walls and terrain.
    /// </summary>
    static ImmediateMesh Outline(bool sphere)
    {
        var mesh = new ImmediateMesh();
        mesh.SurfaceBegin(Mesh.PrimitiveType.Lines);
        if (sphere)
        {
            const int segments = 48;
            for (int plane = 0; plane < 3; plane++)
            {
                for (int i = 0; i < segments; i++)
                {
                    foreach (int step in new[] { i, i + 1 })
                    {
                        float angle = step * Mathf.Tau / segments;
                        float a = Mathf.Cos(angle) * 0.5f, b = Mathf.Sin(angle) * 0.5f;
                        mesh.SurfaceAddVertex(plane == 0 ? new(a, b, 0) : plane == 1 ? new(a, 0, b) : new(0, a, b));
                    }
                }
            }
        }
        else
        {
            for (int axis = 0; axis < 3; axis++)
            {
                for (int corner = 0; corner < 4; corner++)
                {
                    float u = (corner & 1) == 0 ? -0.5f : 0.5f, v = (corner & 2) == 0 ? -0.5f : 0.5f;
                    var from = axis == 0 ? new Vector3(-0.5f, u, v) : axis == 1 ? new Vector3(u, -0.5f, v) : new Vector3(u, v, -0.5f);
                    mesh.SurfaceAddVertex(from);
                    var to = from;
                    to[axis] = 0.5f;
                    mesh.SurfaceAddVertex(to);
                }
            }
        }

        mesh.SurfaceEnd();
        return mesh;
    }

    /// <summary>
    /// Ray test in each volume's local unit space (box slab or unit sphere). The local direction is
    /// not renormalized, so the returned parameter equals the world ray distance and compares with
    /// creature, GameObject and graveyard hits. Volumes containing the camera are skipped so a large
    /// zone-line or tavern trigger does not swallow every click made from inside it.
    /// </summary>
    public (uint Entry, float Distance)? FindHit(Camera3D camera, Vector2 screen)
    {
        if (!LayerEnabled)
        {
            return null;
        }

        var ray = PickRay.From(camera, screen);
        var (origin, direction) = (ray.Origin, ray.Direction);
        (uint Entry, float Distance)? best = null;
        foreach (var (entry, marker) in markers)
        {
            var inverse = marker.Volume.GlobalTransform.AffineInverse();
            var localOrigin = inverse * origin;
            var localDirection = inverse.Basis * direction;
            bool sphere = lookup.TryGetValue(entry, out var trigger) && trigger.IsSphere;
            bool inside = sphere ? localOrigin.LengthSquared() <= 0.25f : Mathf.Abs(localOrigin.X) <= 0.5f && Mathf.Abs(localOrigin.Y) <= 0.5f && Mathf.Abs(localOrigin.Z) <= 0.5f;
            if (inside)
            {
                continue;
            }

            float? hit = sphere ? ScenePicking.SphereHit(Vector3.Zero, 0.5f, localOrigin, localDirection) : ScenePicking.BoxHit(new Aabb(-Vector3.One * 0.5f, Vector3.One), localOrigin, localDirection);
            if (hit.HasValue && hit.Value < camera.Far && (best == null || hit.Value < best.Value.Distance))
            {
                best = (entry, hit.Value);
            }
        }

        return best;
    }

    public void FlyToSelection()
    {
        if (selected is uint entry && lookup.TryGetValue(entry, out var t))
        {
            FocusRequested?.Invoke(new WorldPose(t.Map, t.X, t.Y, t.Z, 0));
        }
    }

    /// <summary>
    /// A GM command that lands on the trigger center. GMs only run AreaTrigger scripts with
    /// `.debug areatriggers` enabled (MiscHandler.cpp HandleAreaTriggerOpcode).
    /// </summary>
    public string? GoCommand() => selected is uint entry && lookup.TryGetValue(entry, out var t)
        ? FormattableString.Invariant($".go xyz {t.X:0.###} {t.Y:0.###} {t.Z:0.###} {t.Map}")
        : null;

    public override void _ExitTree() => query.Dispose();
}
