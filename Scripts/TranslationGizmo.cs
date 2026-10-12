using Godot;

namespace ACWorldEdit;

/// <summary>
/// World-axis movement handles sized in screen space. Preview callbacks change scene state; a
/// final callback commits local history or cancels.
/// </summary>
public partial class TranslationGizmo : Node3D
{
    static readonly Vector3[] axes = [Vector3.Right, Vector3.Up, Vector3.Back];
    static readonly Color[] colors = [new("ff5656"), new("6bff76"), new("559cff")];
    int axis = -1;
    Vector3 start;
    float initial;
    public bool Dragging => axis >= 0;

    public event Action<Vector3, bool>? Changed;
    /// <summary>
    /// Build lightweight unshaded handles that remain visible over scenery without casting world
    /// shadows.
    /// </summary>
    public override void _Ready()
    {
        Visible = false;
        for (int i = 0; i < 3; i++)
        {
            var arm = new Node3D();
            AddChild(arm);
            if (i == 0)
            {
                arm.Rotation = new(0, 0, -Mathf.Pi / 2);
            }

            if (i == 2)
            {
                arm.Rotation = new(Mathf.Pi / 2, 0, 0);
            }

            var material = new StandardMaterial3D
            {
                AlbedoColor = colors[i],
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                NoDepthTest = true
            };
            arm.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.025f, BottomRadius = 0.025f, Height = 0.85f, RadialSegments = 8 }, Position = new(0, 0.425f, 0), MaterialOverride = material, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
            arm.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0, BottomRadius = 0.08f, Height = 0.2f, RadialSegments = 10 }, Position = new(0, 0.95f, 0), MaterialOverride = material, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        }
    }

    /// <summary>
    /// Anchor/scale inactive handles from selection and camera depth. Resizing during drag would
    /// change the reference axis.
    /// </summary>
    public void Update(Camera3D camera, Vector3? position)
    {
        if (Dragging)
        {
            return;
        }

        Visible = position.HasValue && !camera.IsPositionBehind(position.Value);
        if (!Visible)
        {
            return;
        }

        GlobalPosition = position!.Value;
        float depth = Math.Max(1, -(camera.GlobalTransform.AffineInverse() * GlobalPosition).Z);
        float length = depth * Mathf.Tan(Mathf.DegToRad(camera.Fov / 2)) * 150 / camera.GetViewport().GetVisibleRect().Size.Y;
        Scale = Vector3.One * Math.Clamp(length, 0.5f, 40);
    }

    /// <summary>
    /// Pick the closest projected segment and capture the starting pose/ray-axis parameter.
    /// </summary>
    public bool Begin(Camera3D camera, Vector2 screen)
    {
        if (!Visible)
        {
            return false;
        }

        Vector2 origin = camera.UnprojectPosition(GlobalPosition);
        float closest = 10;
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

        start = GlobalPosition;
        if (!Parameter(camera, screen, out initial))
        {
            axis = -1;
            return false;
        }

        return true;
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
    /// Project mouse rays onto the captured axis and publish transient displacement from the
    /// starting parameter.
    /// </summary>
    public void Drag(Camera3D camera, Vector2 screen)
    {
        if (Dragging && Parameter(camera, screen, out float parameter))
        {
            GlobalPosition = start + axes[axis] * (parameter - initial);
            Changed?.Invoke(GlobalPosition, false);
        }
    }

    /// <summary>
    /// Restore canceled position and emit one completion. Clear the active axis before callbacks to
    /// prevent duplicate release commits.
    /// </summary>
    public void Finish(bool commit)
    {
        if (!Dragging)
        {
            return;
        }

        if (!commit)
        {
            GlobalPosition = start;
        }

        axis = -1;
        Changed?.Invoke(GlobalPosition, commit);
    }
}
