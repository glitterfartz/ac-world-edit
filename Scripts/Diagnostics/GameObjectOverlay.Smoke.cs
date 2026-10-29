using Godot;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>Opt-in checks that exercise the real gizmo, the draft and the model transforms of a GameObject.</summary>
public partial class GameObjectOverlay
{
    /// <summary>The nearest spawn whose native model is ready, so its drawn transform can be checked.</summary>
    public GameObjectSpawn? SmokeTarget(Vector3 from)
    {
        var ready = batches.Values.Where(b => b.Node.ModelReady).SelectMany(b => b.Guids).ToHashSet();
        return records.Where(s => ready.Contains(s.Guid)).OrderBy(s => ViewPosition(Effective(s)).DistanceSquaredTo(from)).FirstOrDefault();
    }

    public bool SmokeHasSelection => selected.HasValue;

    static bool Fail(string step)
    {
        GD.PushError("GAMEOBJECT_SMOKE_STEP_FAILED " + step);
        return false;
    }

    Vector3? DrawnOrigin(ulong guid)
    {
        foreach (var batch in batches.Values)
        {
            int index = Array.IndexOf(batch.Guids, guid);
            if (index >= 0)
            {
                return batch.Node.Transforms![index].Origin;
            }
        }

        return null;
    }

    /// <summary>The apply script of the content draft's net edits, written in the source's dialect.</summary>
    string ExportNet() => SqlExport.Build(sourceKey, [], content!.Draft!.NetEdits().Select(e => e.Edit), null, null, ContentDraftFiles.Sources.SchemaFor(sourceKey)).Apply;

    /// <summary>
    /// Drag the real move handle, then check the draft, the drawn transform, undo/redo, a typed
    /// turn that keeps the rotation a unit quaternion, and the SQL the net move exports.
    /// </summary>
    public bool SmokeEditing(Camera3D camera)
    {
        if (!selected.HasValue || poses?.Available != true || content?.Draft == null || !lookup.TryGetValue(selected.Value, out var source))
        {
            return Fail("no editable selection");
        }

        ulong guid = source.Guid;
        // The viewer loads the row in the background on selection; a smoke drives the gizmo in one step.
        var config = Connection();
        poses.Load(sourceKey, Task.Run(() => Data.DBSource.Open(config).ReadSpawnRowsAsync(SpawnKind.GameObject, [guid], default)).GetAwaiter().GetResult());
        var original = PlacementOf(source);
        var start = ViewPosition(With(source, original));
        Mode = GizmoMode.Move;
        gizmo.Update(camera, start, (float)original.Pose.Heading);
        var handle = start + Vector3.Right * gizmo.Scale.X * 0.7f;
        if (!BeginDrag(camera, camera.UnprojectPosition(handle)))
        {
            return Fail("handle pick");
        }

        Drag(camera, camera.UnprojectPosition(handle + Vector3.Right * 2));
        if (DrawnOrigin(guid) is not Vector3 preview || preview.DistanceTo(start + Vector3.Right * 2) > 0.05f)
        {
            return Fail("drag preview");
        }

        EndDrag(true);
        var moved = PlacementOf(source);
        // View X is minus server Y, so a +2 drag along view X lowers server Y by 2.
        if (Math.Abs(moved.Pose.Y - (original.Pose.Y - 2)) > 0.05 || moved.Pose.X != original.Pose.X || moved.Pose.Z != original.Pose.Z || moved.Pose.Heading != original.Pose.Heading)
        {
            return Fail($"move commit y={moved.Pose.Y:F3} expected={original.Pose.Y - 2:F3}");
        }

        if (moved.RotX != original.RotX || moved.RotY != original.RotY || moved.RotZ != original.RotZ || moved.RotW != original.RotW)
        {
            return Fail("move changed the rotation");
        }

        if (DrawnOrigin(guid) is not Vector3 drawn || drawn.DistanceTo(ViewPosition(With(source, moved))) > 0.01f)
        {
            return Fail("drawn transform after commit");
        }

        History(false);
        if (PlacementOf(source) != original || DrawnOrigin(guid) is not Vector3 undone || undone.DistanceTo(start) > 0.01f)
        {
            return Fail("undo");
        }

        History(true);
        if (PlacementOf(source) != moved)
        {
            return Fail("redo");
        }

        string sql = ExportNet();
        if (content.Draft.NetEdits().Count != 1 || !sql.Contains("UPDATE `gameobject` SET", StringComparison.Ordinal) || sql.Contains("rotation0", StringComparison.Ordinal))
        {
            return Fail("export");
        }

        SetPose(moved.Pose.X, moved.Pose.Y, moved.Pose.Z, Mathf.RadToDeg(WorldCoordinates.NormalizeHeading(moved.Pose.Heading + Math.PI / 2)));
        var turned = PlacementOf(source);
        var (x, y, z, w) = turned.FacingQuaternion();
        double norm = x * x + y * y + z * z + w * w;
        double error = Math.Abs(Mathf.Wrap((float)(turned.Pose.Heading - (moved.Pose.Heading + Math.PI / 2)), -Mathf.Pi, Mathf.Pi));
        if (error > 0.01 || Math.Abs(norm - 1) > 1e-6 || turned.Pose.X != moved.Pose.X || turned.Pose.Y != moved.Pose.Y || turned.Pose.Z != moved.Pose.Z)
        {
            return Fail($"typed turn heading error={error:F4} norm={norm:F6}");
        }

        if (!ExportNet().Contains("rotation3", StringComparison.Ordinal))
        {
            return Fail("turn export lacks the rotation columns");
        }

        History(false);
        History(false);
        if (PlacementOf(source) != original)
        {
            return Fail("final undo");
        }

        Mode = GizmoMode.Move;
        GD.Print(FormattableString.Invariant($"GAMEOBJECT_SMOKE_PASSED guid={guid} drag/preview/commit/reopen/undo/redo/turn/export"));
        return true;
    }

    public string SmokeSourceKey => sourceKey;

    public ulong? SmokeSelected => selected;

    public GameObjectSpawn? SmokeRecord(ulong guid) => lookup.GetValueOrDefault(guid);

    public bool SmokeListed(ulong guid)
    {
        for (int i = 0; i < list.ItemCount; i++)
        {
            if (list.GetItemMetadata(i).AsString() == guid.ToString())
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Undo or redo through the same path as the toolbar buttons and shortcuts.</summary>
    public void SmokeHistory(bool redo) => History(redo);
}
