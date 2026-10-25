using Godot;

namespace ACWorldEdit;

/// <summary>
/// Editing tool chosen from the toolbar. Select hides handles so clicks only pick entities.
/// </summary>
public enum GizmoMode
{
    Select,
    Move,
    Rotate
}

/// <summary>
/// One gizmo update. Position is viewer space; Yaw is Godot Rotation.Y, which equals server heading
/// under the WorldCoordinates basis. Moved distinguishes translation gestures from rotations.
/// </summary>
public readonly record struct GizmoEdit(Vector3 Position, float Yaw, bool Commit, bool Moved);

/// <summary>
/// World-axis movement arrows and a horizontal yaw ring, sized in screen space. Preview callbacks
/// change scene state; exactly one final callback per gesture commits local history or cancels.
/// </summary>
public partial class TransformGizmo : Node3D
{
    static readonly Vector3[] axes = [Vector3.Right, Vector3.Up, Vector3.Back];
    static readonly Color[] colors = [new("ff5656"), new("6bff76"), new("559cff")];
    const int RingSamples = 48;
    const float RingRadius = 0.95f;
    // Closest screen distance, in pixels, that still grabs a handle.
    const float PickPixels = 10;
    Node3D moveHandles = null!, ring = null!, pointer = null!;
    int axis = -1;
    bool rotating;
    Vector3 start;
    float initial, startYaw, startAngle, yaw;
    GizmoMode mode = GizmoMode.Move;
    public bool Dragging => axis >= 0 || rotating;
    /// <summary>
    /// Fixed rotation increment in radians; zero is free rotation. Holding Ctrl while free snaps to
    /// 15 degrees.
    /// </summary>
    public float RotationStep { get; set; }

    public GizmoMode Mode
    {
        get => mode;
        set
        {
            if (Dragging)
            {
                Finish(false);
            }

            mode = value;
            if (moveHandles != null)
            {
                moveHandles.Visible = mode == GizmoMode.Move;
                ring.Visible = mode == GizmoMode.Rotate;
            }
        }
    }

    public event Action<GizmoEdit>? Changed;
    /// <summary>
    /// Build lightweight unshaded handles that remain visible over scenery without casting world
    /// shadows.
    /// </summary>
    public override void _Ready()
    {
        Visible = false;
        moveHandles = new Node3D();
        AddChild(moveHandles);
        for (int i = 0; i < 3; i++)
        {
            var arm = new Node3D();
            moveHandles.AddChild(arm);
            if (i == 0)
            {
                arm.Rotation = new(0, 0, -Mathf.Pi / 2);
            }

            if (i == 2)
            {
                arm.Rotation = new(Mathf.Pi / 2, 0, 0);
            }

            var material = Material(colors[i]);
            arm.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.025f, BottomRadius = 0.025f, Height = 0.85f, RadialSegments = 8 }, Position = new(0, 0.425f, 0), MaterialOverride = material, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
            arm.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0, BottomRadius = 0.08f, Height = 0.2f, RadialSegments = 10 }, Position = new(0, 0.95f, 0), MaterialOverride = material, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        }

        // TorusMesh lies in the XZ plane, so the ring needs no rotation to circle the Y axis.
        var ringMaterial = Material(new Color("ffd24a"));
        ring = new Node3D();
        AddChild(ring);
        ring.AddChild(new MeshInstance3D { Mesh = new TorusMesh { InnerRadius = RingRadius - 0.03f, OuterRadius = RingRadius + 0.03f, Rings = 48, RingSegments = 8 }, MaterialOverride = ringMaterial, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        // The pointer shows the current heading: Rotation.Y = yaw turns Godot forward (-Z) to face it.
        pointer = new Node3D();
        ring.AddChild(pointer);
        pointer.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0, BottomRadius = 0.09f, Height = 0.25f, RadialSegments = 10 }, Position = new(0, 0, -RingRadius - 0.15f), Rotation = new(-Mathf.Pi / 2, 0, 0), MaterialOverride = ringMaterial, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        Mode = mode;
    }

    static StandardMaterial3D Material(Color color) => new()
    {
        AlbedoColor = color,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        NoDepthTest = true,
        // Above the waypoint dots and handle (priorities 10-12) so the handles are never covered.
        RenderPriority = 20
    };

    /// <summary>
    /// Anchor/scale inactive handles from selection and camera depth. Resizing during drag would
    /// change the reference axis.
    /// </summary>
    public void Update(Camera3D camera, Vector3? position, float currentYaw)
    {
        if (Dragging)
        {
            return;
        }

        Visible = mode != GizmoMode.Select && position.HasValue && !camera.IsPositionBehind(position.Value);
        if (!Visible)
        {
            return;
        }

        GlobalPosition = position!.Value;
        yaw = currentYaw;
        pointer.Rotation = new(0, yaw, 0);
        float depth = Math.Max(1, -(camera.GlobalTransform.AffineInverse() * GlobalPosition).Z);
        float length = depth * Mathf.Tan(Mathf.DegToRad(camera.Fov / 2)) * 190 /camera.GetViewport().GetVisibleRect().Size.Y;
        Scale = Vector3.One * Math.Clamp(length, 0.5f, 40);
    }

    /// <summary>
    /// Pick the closest projected handle for the active mode and capture the starting pose.
    /// </summary>
    public bool Begin(Camera3D camera, Vector2 screen)
    {
        if (!Visible)
        {
            return false;
        }

        start = GlobalPosition;
        startYaw = yaw;
        return mode == GizmoMode.Rotate ? BeginRotate(camera, screen) : mode == GizmoMode.Move && BeginMove(camera, screen);
    }

    bool BeginMove(Camera3D camera, Vector2 screen)
    {
        Vector2 origin = camera.UnprojectPosition(GlobalPosition);
        float closest = PickPixels;
        for (int i = 0; i < 3; i++)
        {
            Vector2 end = camera.UnprojectPosition(GlobalPosition + axes[i] * Scale.X), segment = end - origin;
            if (segment.LengthSquared() < 4)
            {
                continue;
            }

            float t = Mathf.Clamp((screen - origin).Dot(segment) / segment.LengthSquared(), 0.18f, 1.12f);
            float distance = screen.DistanceTo(origin + segment * t);
            if (distance < closest)
            {
                closest = distance;
                axis = i;
            }
        }

        if (axis < 0)
        {
            return false;
        }

        if (!Parameter(camera, screen, out initial))
        {
            axis = -1;
            return false;
        }

        return true;
    }

    /// <summary>
    /// Grab the ring when the cursor is near its projected outline (sampled polyline), then record
    /// the starting angle on the horizontal plane.
    /// </summary>
    bool BeginRotate(Camera3D camera, Vector2 screen)
    {
        float radius = RingRadius * Scale.X;
        float closest = PickPixels;
        Vector2? previous = null;
        for (int i = 0; i <= RingSamples; i++)
        {
            float angle = i * Mathf.Tau / RingSamples;
            var point = GlobalPosition + new Vector3(Mathf.Sin(angle), 0, Mathf.Cos(angle)) * radius;
            if (camera.IsPositionBehind(point))
            {
                previous = null;
                continue;
            }

            var projected = camera.UnprojectPosition(point);
            if (previous.HasValue)
            {
                closest = Math.Min(closest, DistanceToSegment(screen, previous.Value, projected));
            }

            previous = projected;
        }

        if (closest >= PickPixels || !PlaneAngle(camera, screen, out startAngle))
        {
            return false;
        }

        rotating = true;
        return true;
    }

    static float DistanceToSegment(Vector2 point, Vector2 a, Vector2 b)
    {
        var segment = b - a;
        float t = segment.LengthSquared() < 0.0001f ? 0 : Mathf.Clamp((point - a).Dot(segment) / segment.LengthSquared(), 0, 1);
        return point.DistanceTo(a + segment * t);
    }

    /// <summary>
    /// Solve closest approach of camera ray and chosen axis. Reject near-parallel rays to prevent
    /// unstable movement jumps.
    /// </summary>
    bool Parameter(Camera3D camera, Vector2 screen, out float parameter)
    {
        var ray = camera.ProjectRayNormal(screen);
        var w = camera.ProjectRayOrigin(screen) - start;
        float dot = axes[axis].Dot(ray), denominator = 1 - dot * dot;
        parameter = denominator < 0.005f ? 0 : (axes[axis].Dot(w) - dot * ray.Dot(w)) / denominator;
        return denominator >= 0.005f;
    }

    /// <summary>
    /// Intersect the mouse ray with the horizontal plane through the gizmo and return that point's
    /// yaw (the heading that would face it). Grazing rays are rejected like axis drags.
    /// </summary>
    bool PlaneAngle(Camera3D camera, Vector2 screen, out float angle)
    {
        angle = 0;
        var ray = camera.ProjectRayNormal(screen);
        var origin = camera.ProjectRayOrigin(screen);
        if (Math.Abs(ray.Y) < 0.02f)
        {
            return false;
        }

        float t = (start.Y - origin.Y) / ray.Y;
        if (t < 0)
        {
            return false;
        }

        var offset = origin + ray * t - start;
        if (offset.LengthSquared() < 0.0001f)
        {
            return false;
        }

        // Yaw θ faces (-sin θ, 0, -cos θ), so invert that direction to recover θ.
        angle = Mathf.Atan2(-offset.X, -offset.Z);
        return true;
    }

    /// <summary>
    /// Publish transient displacement or yaw relative to the gesture start; nothing persists here.
    /// </summary>
    public void Drag(Camera3D camera, Vector2 screen)
    {
        if (axis >= 0 && Parameter(camera, screen, out float parameter))
        {
            GlobalPosition = start + axes[axis] * (parameter - initial);
            Changed?.Invoke(new(GlobalPosition, yaw, false, true));
        }
        else if (rotating && PlaneAngle(camera, screen, out float angle))
        {
            float delta = Mathf.Wrap(angle - startAngle, -Mathf.Pi, Mathf.Pi);
            float step = RotationStep > 0 ? RotationStep : Input.IsKeyPressed(Key.Ctrl) ? Mathf.DegToRad(15) : 0;
            if (step > 0)
            {
                delta = Mathf.Round(delta / step) * step;
            }

            yaw = startYaw + delta;
            pointer.Rotation = new(0, yaw, 0);
            Changed?.Invoke(new(GlobalPosition, yaw, false, false));
        }
    }

    /// <summary>
    /// Restore the canceled pose and emit one completion. Clear gesture state before callbacks to
    /// prevent duplicate release commits.
    /// </summary>
    public void Finish(bool commit)
    {
        if (!Dragging)
        {
            return;
        }

        bool moved = axis >= 0;
        if (!commit)
        {
            GlobalPosition = start;
            yaw = startYaw;
            pointer.Rotation = new(0, yaw, 0);
        }

        axis = -1;
        rotating = false;
        Changed?.Invoke(new(GlobalPosition, yaw, commit, moved));
    }
}
