using Godot;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// Result of a ground probe in viewer space. A null height means "do not snap"; Message explains why
/// without guessing a surface.
/// </summary>
public readonly record struct GroundHit(float? Height, string Message, double Milliseconds = 0);

/// <summary>
/// Placement commands shared by the gizmo, inspector, menus, toolbar and shortcuts. Each command
/// records at most one local draft command; source rows are never touched.
/// </summary>
public partial class CreatureOverlay : IPlacementEditor
{
    public bool HasSelection => selectedGuid.HasValue;
    public bool SupportsRotation => true;

    /// <summary>
    /// Viewer-supplied probe over loaded terrain and ready WMO floors. Unset means snapping is
    /// unavailable.
    /// </summary>
    public Func<Vector3, GroundHit>? GroundAt { get; set; }
    /// <summary>
    /// Snap Z to the ground when a move gesture is released. Off by default so existing Z values
    /// never change without an explicit choice.
    /// </summary>
    public bool SnapOnDrop { get; set; }
    public bool CanEdit => EditableMarker() != null;
    public bool CanUndo => draft?.CanUndo == true;
    public bool CanRedo => draft?.CanRedo == true;

    public GizmoMode Mode
    {
        get => gizmo.Mode;
        set => gizmo.Mode = value;
    }

    public float RotationStep
    {
        get => gizmo.RotationStep;
        set => gizmo.RotationStep = value;
    }

    public void Undo() => History(false);
    public void Redo() => History(true);

    (CreatureSpawn Spawn, Node3D Node, MeshInstance3D Body, Label3D Label)? EditableMarker() =>
        draft != null && selectedGuid.HasValue && markers.TryGetValue(selectedGuid.Value, out var marker) ? marker : null;

    /// <summary>
    /// Snap the selected creature's Z to the ground below its effective pose. Probe failures leave
    /// the draft unchanged and explain why.
    /// </summary>
    public void DropToGround()
    {
        if (EditableMarker() is not { } marker)
        {
            return;
        }

        gizmo.Finish(false);
        var pose = PoseOf(marker.Spawn);
        var ground = GroundAt?.Invoke(ViewPosition(pose)) ?? new GroundHit(null, "Ground probe unavailable.");
        LastGround = ground;
        if (ground.Height is not float height)
        {
            draftMessage = "Not dropped: " + ground.Message;
            Select(marker.Spawn.Guid);
            return;
        }

        // View Y is server Z, so the probe height commits directly.
        Commit(marker.Spawn, pose with { Z = height }, "Dropped to ground; saved local draft.");
    }

    /// <summary>
    /// Last probe result, retained for diagnostics and smoke measurements.
    /// </summary>
    public GroundHit? LastGround { get; private set; }

    /// <summary>
    /// Return the selection to its source pose as a normal undoable command.
    /// </summary>
    public void ResetToSource()
    {
        if (EditableMarker() is { } marker)
        {
            gizmo.Finish(false);
            Commit(marker.Spawn, marker.Spawn.Pose, "Reset to source pose; saved local draft.");
        }
    }

    /// <summary>
    /// Commit typed coordinates (server space) and heading in degrees. Invalid numbers are rejected
    /// before they reach the draft.
    /// </summary>
    public void SetPose(double x, double y, double z, double headingDegrees)
    {
        if (EditableMarker() is not { } marker)
        {
            return;
        }

        gizmo.Finish(false);
        var pose = PoseOf(marker.Spawn);
        // Fields show rounded text; values that still match it keep their exact stored doubles so
        // editing one field cannot rewrite the others through rounding.
        static double Keep(double typed, double stored, double tolerance) => Math.Abs(typed - stored) < tolerance ? stored : typed;
        double heading = WorldCoordinates.NormalizeHeading(Mathf.DegToRad(headingDegrees));
        if (Math.Abs(heading - WorldCoordinates.NormalizeHeading(pose.Heading)) < 1e-4)
        {
            heading = pose.Heading;
        }

        var after = pose with { X = Keep(x, pose.X, 0.5e-4), Y = Keep(y, pose.Y, 0.5e-4), Z = Keep(z, pose.Z, 0.5e-4), Heading = heading };
        try
        {
            after.Validate();
        }
        catch (ArgumentException)
        {
            draftMessage = "Coordinates must be finite numbers.";
            Select(marker.Spawn.Guid);
            return;
        }

        Commit(marker.Spawn, after, "Saved typed pose to local draft.");
    }

    /// <summary>
    /// GM teleport for comparing the effective pose in the client: `.go xyz X Y Z MAP O`
    /// (AzerothCore cs_go.cpp HandleGoXYZCommand argument order).
    /// </summary>
    public string? GoCommand()
    {
        if (!selectedGuid.HasValue || !markers.TryGetValue(selectedGuid.Value, out var marker))
        {
            return null;
        }

        var pose = PoseOf(marker.Spawn);
        return FormattableString.Invariant($".go xyz {pose.X:F4} {pose.Y:F4} {pose.Z:F4} {pose.Map} {pose.Heading:F4}");
    }

    public void CopyGoCommand()
    {
        if (GoCommand() is { } command)
        {
            DisplayServer.ClipboardSet(command);
            draftMessage = "Copied " + command;
            Select(selectedGuid!.Value);
        }
    }

    /// <summary>
    /// Describe a server heading as degrees plus an eight-point compass direction. Server heading 0
    /// faces +X (north) and increases counterclockwise toward +Y (west).
    /// </summary>
    public static string HeadingText(double heading)
    {
        double degrees = Mathf.RadToDeg(WorldCoordinates.NormalizeHeading(heading));
        string[] points = ["N", "NW", "W", "SW", "S", "SE", "E", "NE"];
        return FormattableString.Invariant($"{heading:F3} rad · {degrees:F1}° {points[(int)Math.Round(degrees / 45) % 8]}");
    }
}
