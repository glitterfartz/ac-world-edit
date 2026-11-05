using Godot;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// Read-only projection of AreaTrigger volumes near the streamed region: AzerothCore `areatrigger`
/// rows, TrinityCore client triggers (AreaTrigger.db2) and TrinityCore server spawns. Each is a
/// translucent volume in the shape the server tests (<see cref="AreaTriggerShapes"/>), colored by what
/// it does, with a label and an outline on the selection. Hidden layers stop queries and free volumes.
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

    /// <summary>The selected trigger's row, null when nothing is selected.</summary>
    public AreaTriggerRecord? Selected => selected is { } key ? lookup.GetValueOrDefault(key) : null;
    public bool HasSelection => selected.HasValue;
    public IReadOnlyList<AreaTriggerRecord> Triggers => triggers;
    public bool IsLoading => query.IsLoading;
    public event Action? SelectionChanged;
    public event Action<WorldPose>? FocusRequested;
    /// <summary>Open the behavior editor on this trigger's SmartAI rules.</summary>
    public event Action<AreaTriggerRecord>? BehaviorRequested;

    readonly Dictionary<AreaTriggerKind, StandardMaterial3D> fills = [];
    readonly Dictionary<AreaTriggerKey, AreaTriggerRecord> lookup = [];
    readonly Dictionary<AreaTriggerKey, (Node3D Node, MeshInstance3D Volume, Label3D Label, AreaTriggerShapes.Volume Shape)> markers = [];
    AreaTriggerRecord[] triggers = [];
    MeshInstance3D? outline;
    AreaTriggerKey? selected;
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
            point ??= ViewSpace.QueryPointAt(camera.Position, region.Map);
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
            var lookup = Lookup();
            query.Start(token => DBSource.Open(config, lookup).ReadAreaTriggersAsync(area, center, term, token));
        }

        foreach (var (key, marker) in markers)
        {
            marker.Node.Visible = LayerEnabled;
            marker.Label.Visible = LayerEnabled && (selected == key || camera.GlobalPosition.DistanceSquaredTo(marker.Label.GlobalPosition) < LabelDistance * LabelDistance);
        }

        status.Text = queryStatus;
        UpdateActions();
    }

    /// <summary>
    /// Replace the snapshot and reconcile volumes: keep nodes for surviving keys, free the rest and
    /// create new ones. The selection survives when its trigger is still present.
    /// </summary>
    void Publish(AreaTriggerRegionResult result)
    {
        triggers = result.Triggers;
        var incoming = result.Triggers.ToDictionary(t => t.Key);
        foreach (var key in markers.Keys.Where(k => !incoming.ContainsKey(k)).ToArray())
        {
            if (selected == key)
            {
                Deselect();
            }

            markers.Free(key, m => m.Node);
        }

        lookup.Clear();
        foreach (var trigger in result.Triggers)
        {
            lookup[trigger.Key] = trigger;
            markers.Free(trigger.Key, m => m.Node);
            AddMarker(trigger);
        }

        PopulateList();
        int spawned = result.Triggers.Count(t => t.IsSpawn);
        string count = spawned == 0 ? $"{result.Triggers.Length} AreaTriggers" : $"{result.Triggers.Length} AreaTriggers ({spawned} spawned)";
        queryStatus = result.Triggers.Length == 0 ? "No AreaTriggers in this region." : result.Truncated ? $"{count} nearest · limit reached" : $"{count} in this region";
        if (selected is { } current && lookup.ContainsKey(current))
        {
            Select(current);
        }
    }

    /// <summary>
    /// Place the volume: view position from the server center, yaw = orientation (the same mapping as
    /// creature headings; polygons carry their turn in their corners), and the shape's mesh under it.
    /// </summary>
    void AddMarker(AreaTriggerRecord trigger)
    {
        var shape = AreaTriggerShapes.Build(trigger);
        var node = new Node3D
        {
            Name = trigger.IsSpawn ? "AreaTriggerSpawn_" + trigger.SpawnId : "AreaTrigger_" + trigger.Entry,
            Position = ViewSpace.ToView(trigger.X, trigger.Y, trigger.Z),
            Rotation = new(0, AreaTriggerShapes.Yaw(trigger), 0)
        };
        var volume = new MeshInstance3D
        {
            Mesh = shape.Fill,
            Transform = shape.Local,
            MaterialOverride = Fill(trigger.Kind),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
        };
        var label = new Label3D
        {
            Text = LabelText(trigger),
            Position = ViewPosition(trigger) - ViewSpace.ToView(trigger.X, trigger.Y, trigger.Z) + new Vector3(0, AreaTriggerShapes.TopOf(trigger) - (float)(trigger.Focus.Z - trigger.Z) + 1.5f, 0),
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
        markers[trigger.Key] = (node, volume, label, shape);
    }

    /// <summary>Where the volume is in view space (its <see cref="AreaTriggerRecord.Focus"/>); the marker node itself sits at the trigger position.</summary>
    public static Vector3 ViewPosition(AreaTriggerRecord trigger) => ViewSpace.ToView(trigger.Focus.X, trigger.Focus.Y, trigger.Focus.Z);

    static string LabelText(AreaTriggerRecord trigger) => $"{DisplayName(trigger)}\n{Summary(trigger)}";

    /// <summary>"AreaTrigger 1125", or "Spawned AreaTrigger 290" for a TrinityCore server spawn.</summary>
    public static string DisplayName(AreaTriggerRecord trigger) => trigger.IsSpawn ? $"Spawned AreaTrigger {trigger.SpawnId}" : $"AreaTrigger {trigger.Entry}";

    /// <summary>One-line description of what the trigger does, for labels and the list.</summary>
    public static string Summary(AreaTriggerRecord trigger) => trigger.Kind switch
    {
        AreaTriggerKind.SmartAI => $"SmartAI · {trigger.SmartRows} rule{(trigger.SmartRows == 1 ? "" : "s")}",
        AreaTriggerKind.Script => "C++ " + trigger.ScriptName,
        AreaTriggerKind.Teleport => $"Teleport → map {trigger.Teleport!.Map}{(trigger.Teleport.Name.Length > 0 ? " · " + trigger.Teleport.Name : "")}",
        AreaTriggerKind.Quest => $"Quest {trigger.QuestId}{(trigger.QuestTitle.Length > 0 ? " · " + trigger.QuestTitle : "")}",
        AreaTriggerKind.Tavern => "Tavern" + (trigger.Tavern.Length > 0 ? " · " + trigger.Tavern : ""),
        _ => trigger.IsSpawn ? "No script" : "No server link"
    };

    public void Select(AreaTriggerKey key)
    {
        if (!lookup.TryGetValue(key, out var trigger) || !markers.TryGetValue(key, out var marker))
        {
            return;
        }

        var previous = selected;
        selected = key;
        outline?.QueueFree();
        // The shape's edges, drawn without depth testing so a selected trigger stays visible inside
        // walls and terrain.
        outline = new MeshInstance3D
        {
            Mesh = marker.Shape.Outline,
            Transform = marker.Shape.Local,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                NoDepthTest = true,
                AlbedoColor = KindColors[trigger.Kind].Lerp(Colors.White, 0.35f)
            }
        };
        marker.Node.AddChild(outline);
        if (previous != key)
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
    /// Ray test in each volume's local space: the unit sphere, or the slab of the mesh's bounds (exact
    /// for boxes and planes, the enclosing box for cylinders, disks and polygons). The local direction
    /// is not renormalized, so the returned parameter equals the world ray distance and compares with
    /// creature, GameObject and graveyard hits. Volumes containing the camera are skipped so a large
    /// zone-line or tavern trigger does not swallow every click made from inside it.
    /// </summary>
    public (AreaTriggerKey Key, float Distance)? FindHit(Camera3D camera, Vector2 screen)
    {
        if (!LayerEnabled)
        {
            return null;
        }

        var ray = PickRay.From(camera, screen);
        var (origin, direction) = (ray.Origin, ray.Direction);
        (AreaTriggerKey Key, float Distance)? best = null;
        foreach (var (key, marker) in markers)
        {
            var inverse = marker.Volume.GlobalTransform.AffineInverse();
            var localOrigin = inverse * origin;
            var localDirection = inverse.Basis * direction;
            bool sphere = marker.Shape.Sphere;
            var bounds = marker.Shape.Fill.GetAabb();
            bool inside = sphere ? localOrigin.LengthSquared() <= 0.25f : bounds.HasPoint(localOrigin);
            if (inside)
            {
                continue;
            }

            float? hit = sphere ? ScenePicking.SphereHit(Vector3.Zero, 0.5f, localOrigin, localDirection) : ScenePicking.BoxHit(bounds, localOrigin, localDirection);
            if (hit.HasValue && hit.Value < camera.Far && (best == null || hit.Value < best.Value.Distance))
            {
                best = (key, hit.Value);
            }
        }

        return best;
    }

    public void FlyToSelection()
    {
        if (Selected is { } t)
        {
            FocusRequested?.Invoke(new WorldPose(t.Map, t.Focus.X, t.Focus.Y, t.Focus.Z, 0));
        }
    }

    /// <summary>
    /// A GM command that lands on the trigger center. GMs only run AreaTrigger scripts with
    /// `.debug areatriggers` enabled (MiscHandler.cpp HandleAreaTriggerOpcode).
    /// </summary>
    public string? GoCommand() => Selected is { } t ? PoseText.GoCommand(t.Map, t.Focus.X, t.Focus.Y, t.Focus.Z) : null;

    public override void _ExitTree() => query.Dispose();
}
