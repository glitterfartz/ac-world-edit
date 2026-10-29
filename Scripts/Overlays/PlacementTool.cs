using Godot;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// Placing a new spawn with the mouse: after a template is chosen in a Place card, a ghost follows the
/// cursor over the ground and a click puts the spawn there, facing the camera. Shift-click keeps
/// placing more of the same; Esc stops. The ground under the cursor is the loaded terrain along the
/// camera ray; the final position is then lifted onto a building floor within a few yards above it.
/// </summary>
public partial class PlacementTool : Node3D
{
    public sealed record Template(SpawnKind Kind, uint Entry, string Name);

    /// <summary>The terrain height under a viewer-space (x, z), or null where no terrain is loaded.</summary>
    public Func<float, float, float?>? TerrainAt { get; set; }

    /// <summary>The full ground probe (terrain and building floors) used to settle the final height.</summary>
    public Func<Vector3, GroundHit>? GroundAt { get; set; }

    /// <summary>The map the camera is on.</summary>
    public Func<ushort>? Map { get; set; }

    /// <summary>Place a spawn of the template at the pose; the owner records it. Returns the new GUID.</summary>
    public Func<Template, WorldPose, Task<ulong?>>? Place { get; set; }

    /// <summary>The armed template changed (null: stopped), so the cards can say what is being placed.</summary>
    public event Action<Template?>? Changed;

    public Template? Current { get; private set; }

    /// <summary>Where the last hover or click found ground, for the status line and tests.</summary>
    public Vector3? Target { get; private set; }

    readonly MeshInstance3D ghost = new()
    {
        Mesh = new CapsuleMesh { Radius = 0.5f, Height = 2 },
        MaterialOverride = new StandardMaterial3D
        {
            AlbedoColor = new(0.36f, 0.85f, 0.76f, 0.45f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded
        },
        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        Visible = false
    };

    readonly Label3D label = new()
    {
        Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
        FontSize = 32,
        PixelSize = 0.018f,
        Position = new(0, 2.6f, 0),
        NoDepthTest = true
    };

    ulong lastHover;
    Vector2? lastScreen;

    /// <summary>The camera the ghost is computed for; the viewer sets it.</summary>
    public Camera3D? Camera { get; set; }

    public override void _Ready()
    {
        ghost.AddChild(label);
        AddChild(ghost);
    }

    /// <summary>
    /// Keep the ghost under the cursor when the camera moves and the mouse does not, and hide it while
    /// the cursor is over a panel.
    /// </summary>
    public override void _Process(double delta)
    {
        if (Current == null || Camera == null || lastScreen is not Vector2 screen)
        {
            return;
        }

        if (GetViewport().GuiGetHoveredControl() != null)
        {
            ghost.Visible = false;
            Target = null;
            return;
        }

        Hover(Camera, screen);
    }

    public void Arm(Template template)
    {
        Current = template;
        label.Text = template.Name;
        ghost.Visible = false;
        Target = null;
        Changed?.Invoke(template);
    }

    public void Disarm()
    {
        if (Current == null)
        {
            return;
        }

        Current = null;
        Target = null;
        ghost.Visible = false;
        Changed?.Invoke(null);
    }

    /// <summary>The ghost, for tests: whether it is shown and where.</summary>
    public (bool Visible, Vector3 Position) Ghost => (ghost.Visible, ghost.GlobalPosition);

    /// <summary>
    /// Follow the cursor: put the ghost where the camera ray meets the terrain. Called on mouse motion, at
    /// most about 30 times a second.
    /// </summary>
    public void Hover(Camera3D camera, Vector2 screen, bool force = false)
    {
        if (Current == null || (!force && Time.GetTicksMsec() - lastHover < 33))
        {
            return;
        }

        lastHover = Time.GetTicksMsec();
        lastScreen = screen;
        Target = RayGround(camera, screen, TerrainAt, p => GroundAt?.Invoke(p).Height);
        ghost.Visible = Target != null;
        if (Target is Vector3 hit)
        {
            ghost.GlobalPosition = hit + Vector3.Up;
        }
    }

    /// <summary>
    /// Place the armed template where the cursor points. Returns true when the click was used (there was a
    /// template armed), whether or not ground was found.
    /// </summary>
    public async Task<bool> ClickAsync(Camera3D camera, Vector2 screen, bool keepPlacing, Action<string> say)
    {
        if (Current is not { } template)
        {
            return false;
        }

        Hover(camera, screen, force: true);
        if (Target is not Vector3 hit)
        {
            say("Click on the ground to place " + template.Name + ".");
            return true;
        }

        // Lift onto a building floor within a few yards above the terrain; keep the terrain height otherwise.
        if (GroundAt?.Invoke(hit + Vector3.Up * 0.5f).Height is float floor)
        {
            hit.Y = floor;
        }

        var server = ViewSpace.ToServer(hit);
        var cameraServer = ViewSpace.ToServer(camera.GlobalPosition);
        var pose = new WorldPose(Map?.Invoke() ?? 0, server.X, server.Y, server.Z, SpawnPlacement.FacingHeading(server.X, server.Y, cameraServer.X, cameraServer.Y));
        if (!keepPlacing)
        {
            Disarm();
        }

        if (Place != null)
        {
            await Place(template, pose);
        }

        return true;
    }

    /// <summary>
    /// The first point along the camera ray at or below the ground. Terrain is read from its height map;
    /// where the terrain has a hole (under a building) <paramref name="floorAt"/> supplies the building
    /// floor instead. It marches in steps that drop at most about 2.5 yd, then halves the last step to
    /// settle on the surface; null when the ray never meets ground.
    /// </summary>
    public static Vector3? RayGround(Camera3D camera, Vector2 screen, Func<float, float, float?>? terrain, Func<Vector3, float?>? floorAt = null, float max = 600)
    {
        if (terrain == null)
        {
            return null;
        }

        float? HeightAt(Vector3 p) => terrain(p.X, p.Z) ?? floorAt?.Invoke(p);
        var ray = PickRay.From(camera, screen);
        float step = Math.Clamp(2.5f / Math.Max(Math.Abs(ray.Direction.Y), 0.05f), 0.5f, 4f);
        var above = ray.Origin;
        for (float distance = step; distance <= max; distance += step)
        {
            var point = ray.Origin + ray.Direction * distance;
            if (HeightAt(point) is not float height || point.Y > height)
            {
                above = point;
                continue;
            }

            // The ray crossed the surface between `above` and `point`.
            var low = point;
            for (int i = 0; i < 12; i++)
            {
                var middle = (above + low) / 2;
                if (HeightAt(middle) is float h && middle.Y <= h)
                {
                    low = middle;
                }
                else
                {
                    above = middle;
                }
            }

            return HeightAt(low) is float surface ? new Vector3(low.X, surface, low.Z) : null;
        }

        return null;
    }
}
