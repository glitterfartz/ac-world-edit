using Godot;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;
using ACWorldEdit.Assets;

namespace ACWorldEdit;

/// <summary>
/// Opt-in checks that exercise actual selection, animation controls and gizmo history. Normal editing never calls these assertion workflows.
/// </summary>
public partial class CreatureOverlay
{
    /// <summary>
    /// Exercise the real dropdown/signal path instead of setting internal animation state directly.
    /// </summary>
    public bool ChooseAnimationForSmoke(ushort id)
    {
        for (int i = 0; i < animation.ItemCount; i++)
        {
            if (animation.GetItemId(i) == id)
            {
                animation.Select(i);
                animation.EmitSignal(OptionButton.SignalName.ItemSelected, i);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Check real handle pick/drag, model positions and reopen/undo/redo using the smoke setup&apos;s
    /// isolated draft.
    /// </summary>
    public bool SmokeGizmo(Camera3D camera)
    {
        if (!selectedGuid.HasValue || draft == null)
        {
            return false;
        }

        uint guid = selectedGuid.Value;
        var original = markers[guid].Node.Position;
        if (!Pick(camera, camera.UnprojectPosition(original + Vector3.Up)) || selectedGuid != guid)
        {
            return false;
        }

        Mode = GizmoMode.Move;
        gizmo.Update(camera, original, markers[guid].Node.Rotation.Y);
        var handle = original + Vector3.Right * gizmo.Scale.X * 0.7f;
        if (!BeginDrag(camera, camera.UnprojectPosition(handle)))
        {
            return false;
        }

        Drag(camera, camera.UnprojectPosition(handle + Vector3.Right * 2));
        EndDrag(true);
        var moved = markers[guid].Node.Position;
        if (moved.DistanceTo(original + Vector3.Right * 2) > 0.05f)
        {
            return false;
        }

        foreach (var batch in modelBatches.Values)
        {
            int i = Array.IndexOf(batch.Guids, guid);
            if (i >= 0 && batch.Node.Transforms![i].Origin.DistanceTo(moved) > 0.01f)
            {
                return false;
            }
        }

        draft.Dispose();
        draft = new CreatureDraft(draftPath);
        if (ViewPosition(PoseOf(markers[guid].Spawn)).DistanceTo(moved) > 0.01f)
        {
            return false;
        }

        History(false);
        if (markers[guid].Node.Position.DistanceTo(original) > 0.01f)
        {
            return false;
        }

        History(true);
        if (markers[guid].Node.Position.DistanceTo(moved) > 0.01f)
        {
            return false;
        }

        History(false);
        return markers[guid].Node.Position.DistanceTo(original) < 0.01f && SmokeRotate(camera, guid) && SmokeGround(guid);
    }

    /// <summary>
    /// Commit a +10° heading change for shortcut checks and return the heading before it, which a
    /// successful undo must restore.
    /// </summary>
    public double? SmokeNudgeHeading()
    {
        if (EditableMarker() is not { } marker)
        {
            return null;
        }

        var before = PoseOf(marker.Spawn);
        Commit(marker.Spawn, before with { Heading = WorldCoordinates.NormalizeHeading(before.Heading + Mathf.DegToRad(10)) }, "Smoke heading nudge.");
        return PoseOf(marker.Spawn) == before ? null : before.Heading;
    }

    public double? SmokeSelectedHeading() => selectedGuid.HasValue && markers.TryGetValue(selectedGuid.Value, out var marker) ? PoseOf(marker.Spawn).Heading : null;

    static bool Fail(string step)
    {
        GD.PushError("GIZMO_SMOKE_STEP_FAILED " + step);
        return false;
    }

    /// <summary>
    /// Grab the real ring handle, then check cancel restores yaw, a commit stores a normalized
    /// heading without touching coordinates, and undo/redo restore both rotation and pose.
    /// </summary>
    bool SmokeRotate(Camera3D camera, uint guid)
    {
        var spawn = markers[guid].Spawn;
        var node = markers[guid].Node;
        var before = PoseOf(spawn);
        Mode = GizmoMode.Rotate;
        gizmo.Update(camera, node.GlobalPosition, node.Rotation.Y);
        float radius = 0.95f * gizmo.Scale.X;
        Vector3 Ring(float angle) => node.GlobalPosition + new Vector3(Mathf.Sin(angle), 0, Mathf.Cos(angle)) * radius;
        // Ring parameter a sits at yaw a - π, so moving a quarter turn along it adds π/2 to yaw.
        float? startAngle = null;
        foreach (float candidate in new[] { 0f, Mathf.Pi / 2, Mathf.Pi, 3 * Mathf.Pi / 2 })
        {
            if (BeginDrag(camera, camera.UnprojectPosition(Ring(candidate))))
            {
                startAngle = candidate;
                break;
            }
        }

        if (startAngle is not float a)
        {
            return Fail("ring pick");
        }

        Drag(camera, camera.UnprojectPosition(Ring(a + Mathf.Pi / 2)));
        EndDrag(false);
        if (Math.Abs(node.Rotation.Y - (float)before.Heading) > 1e-4f || PoseOf(spawn) != before)
        {
            return Fail("rotate cancel");
        }

        gizmo.Update(camera, node.GlobalPosition, node.Rotation.Y);
        if (!BeginDrag(camera, camera.UnprojectPosition(Ring(a))))
        {
            return Fail("ring re-pick");
        }

        Drag(camera, camera.UnprojectPosition(Ring(a + Mathf.Pi / 2)));
        EndDrag(true);
        var after = PoseOf(spawn);
        double expected = WorldCoordinates.NormalizeHeading(before.Heading + Math.PI / 2);
        double error = Math.Abs(Mathf.Wrap((float)(after.Heading - expected), -Mathf.Pi, Mathf.Pi));
        if (error > 0.02 || after.Heading < 0 || after.Heading >= 2 * Math.PI)
        {
            return Fail($"rotate commit heading={after.Heading:F4} expected={expected:F4}");
        }

        if (after.X != before.X || after.Y != before.Y || after.Z != before.Z)
        {
            return Fail("rotate changed coordinates");
        }

        if (Math.Abs(node.Rotation.Y - (float)after.Heading) > 1e-4f)
        {
            return Fail("rotate node yaw");
        }

        History(false);
        if (PoseOf(spawn) != before || Math.Abs(node.Rotation.Y - (float)before.Heading) > 1e-4f)
        {
            return Fail("rotate undo");
        }

        History(true);
        if (PoseOf(spawn) != after || Math.Abs(node.Rotation.Y - (float)after.Heading) > 1e-4f)
        {
            return Fail("rotate redo");
        }

        History(false);
        Mode = GizmoMode.Move;
        GD.Print(FormattableString.Invariant($"ROTATE_SMOKE_PASSED heading {before.Heading:F4}->{after.Heading:F4} error={error:F4}"));
        return true;
    }

    /// <summary>
    /// Drop the selection onto real loaded ground, require an exact Z-only change with undo, and
    /// prove the probe refuses to snap when nothing lies at or below the query point.
    /// </summary>
    bool SmokeGround(uint guid)
    {
        if (GroundAt == null)
        {
            return Fail("ground probe not wired");
        }

        var spawn = markers[guid].Spawn;
        var before = PoseOf(spawn);
        DropToGround();
        if (LastGround is not { Height: float height } hit)
        {
            return Fail("drop to ground: " + LastGround?.Message);
        }

        var dropped = PoseOf(spawn);
        if (dropped.X != before.X || dropped.Y != before.Y || dropped.Heading != before.Heading || Math.Abs(dropped.Z - height) > 1e-4)
        {
            return Fail("drop changed more than Z");
        }

        GD.Print(FormattableString.Invariant($"GROUND_SMOKE_DROP z {before.Z:F3}->{dropped.Z:F3} via={hit.Message} ms={hit.Milliseconds:F3}"));
        if (dropped != before)
        {
            History(false);
        }

        if (PoseOf(spawn) != before)
        {
            return Fail("drop undo");
        }

        // Far below every surface: nothing lies at or below this point, so snapping must refuse.
        var below = GroundAt(ViewPosition(before) + Vector3.Down * 2000);
        if (below.Height != null)
        {
            return Fail("probe snapped from below all ground");
        }

        GD.Print("GROUND_SMOKE_FAIL_CLOSED " + below.Message);
        return true;
    }
}
