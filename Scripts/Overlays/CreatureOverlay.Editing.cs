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
    public IReadOnlyList<CreatureMove> NetMoves() => draft?.NetMoves() ?? [];
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

    /// <summary>
    /// The selected creature with its effective (draft) pose, or null.
    /// </summary>
    public CreatureSpawn? SelectedSpawn => selectedGuid.HasValue && markers.TryGetValue(selectedGuid.Value, out var marker) ? marker.Spawn with { Pose = PoseOf(marker.Spawn) } : null;

    /// <summary>
    /// Select and fly to the nearest loaded spawn of a template entry (quest editor links).
    /// </summary>
    public bool FlyToEntry(uint entry, Vector3 from)
    {
        var nearest = markers.Values.Where(m => m.Spawn.TemplateId == entry).OrderBy(m => m.Node.GlobalPosition.DistanceSquaredTo(from)).Select(m => (uint?)m.Spawn.Guid).FirstOrDefault();
        if (nearest is not uint guid)
        {
            return false;
        }

        Select(guid);
        FlyToSelection();
        return true;
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
        var after = PoseText.Typed(PoseOf(marker.Spawn), x, y, z, headingDegrees);
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
        return PoseText.GoCommand(pose.Map, pose.X, pose.Y, pose.Z, pose.Heading);
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
}
