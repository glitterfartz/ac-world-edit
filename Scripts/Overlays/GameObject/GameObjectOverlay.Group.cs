using Godot;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// Several GameObjects selected at once: Ctrl-click and a Shift-drag box add to the selection, and Delete,
/// Duplicate and a move then act on the primary GameObject and the others together, as one undoable command.
/// </summary>
public partial class GameObjectOverlay
{
    readonly SpawnGroupSelection group = new();

    /// <summary>The primary GameObject first, then the others.</summary>
    public IReadOnlyList<ulong> Group => selected is ulong primary ? [primary, .. group.Members] : [];

    public bool InGroup(ulong guid) => group.Contains(guid);

    /// <summary>Ctrl-click: add the GameObject to the selection, or take it out. With nothing selected it becomes the selection.</summary>
    public void ToggleInGroup(ulong guid)
    {
        if (!lookup.ContainsKey(guid))
        {
            return;
        }

        if (!selected.HasValue)
        {
            Select(guid);
            return;
        }

        if (guid == selected)
        {
            // Taking the primary out hands the selection to the next member.
            if (group.Members.Count > 0)
            {
                ulong next = group.Members[0];
                var rest = group.Members.Skip(1).ToArray();
                Select(next);
                foreach (ulong other in rest)
                {
                    group.Add(other);
                }

                Select(next);
            }

            return;
        }

        if (group.Toggle(guid))
        {
            LoadRow(guid);
        }

        Select(selected.Value);
    }

    /// <summary>Shift-drag: add every GameObject whose base lies inside the screen rectangle. Returns how many were added.</summary>
    public int SelectInBox(Camera3D camera, Rect2 box)
    {
        int added = 0;
        foreach (var source in lookup.Values.ToArray())
        {
            var point = ViewPosition(Effective(source)) + Vector3.Up;
            if (camera.IsPositionBehind(point) || !box.HasPoint(camera.UnprojectPosition(point)))
            {
                continue;
            }

            if (!selected.HasValue)
            {
                Select(source.Guid);
            }
            else if (source.Guid != selected && !group.Contains(source.Guid))
            {
                group.Add(source.Guid);
                LoadRow(source.Guid);
            }
            else
            {
                continue;
            }

            added++;
        }

        if (selected.HasValue)
        {
            Select(selected.Value);
        }

        return added;
    }

    /// <summary>Every selected GameObject has its row loaded (or is staged), so the group can be copied or deleted.</summary>
    bool RowReady => poses?.Available == true && Group.Count > 0 && Group.All(g => poses.Has(sourceKey, g) || IsStaged(g));

    public bool CanDuplicate => RowReady;
    public bool CanDelete => RowReady;

    ConfirmationDialog? deleteDialog;

    string SubjectOf(IReadOnlyList<ulong> guids) => guids.Count == 1 && lookup.TryGetValue(guids[0], out var spawn) ? $"{spawn.Name} ({guids[0]})" : $"{guids.Count} GameObjects";

    /// <summary>
    /// Delete the selected GameObjects and their addon rows as one undoable command. When other tables still
    /// name them, a dialog lists that first.
    /// </summary>
    public async void Delete() => await DeleteAsync();

    public async Task DeleteAsync()
    {
        if (poses == null || Group.Count == 0 || (deleteDialog != null && GodotObject.IsInstanceValid(deleteDialog)))
        {
            return;
        }

        gizmo.Finish(false);
        var guids = Group.ToArray();
        string subject = SubjectOf(guids);
        var existing = guids.Where(g => !IsStaged(g)).ToArray();
        IReadOnlyList<SpawnImpactItem> items = [];
        if (existing.Length > 0)
        {
            try
            {
                items = await DBSource.Open(Connection()).CountSpawnReferencesAsync(SpawnKind.GameObject, existing, default);
            }
            catch (Exception)
            {
                Say($"Could not check what references {subject}; not deleted.");
                return;
            }
        }

        if (items.Count == 0)
        {
            DeleteNow(guids, subject);
            return;
        }

        deleteDialog = SpawnDeleteDialog.Ask(this, subject, items, () => DeleteNow(guids, subject));
    }

    void DeleteNow(IReadOnlyList<ulong> guids, string subject)
    {
        var (ok, message) = poses!.DeleteMany(this, ContentDraftFiles.Sources.SchemaFor(sourceKey), sourceKey, guids, guids.Count == 1 ? "Delete" : $"Delete {guids.Count} GameObjects");
        Say(ok ? $"Deleted {subject}." : message);
    }

    /// <summary>Copy the selected GameObjects two yards ahead of themselves, on the ground, and select the copies.</summary>
    public async void Duplicate() => await DuplicateAsync();

    public async Task<ulong?> DuplicateAsync()
    {
        if (poses == null || Group.Count == 0)
        {
            return null;
        }

        gizmo.Finish(false);
        var originals = Group.Where(lookup.ContainsKey).ToArray();
        var database = DBSource.Open(Connection());
        string key = sourceKey;
        try
        {
            ulong max = await database.MaxIdAsync(SpawnEdits.SpawnTable(database.Schema, SpawnKind.GameObject), "guid", default);
            var fresh = poses.NextGuids(key, max, originals.Length);
            var copies = new List<(ulong, ulong, WorldPose)>();
            for (int i = 0; i < originals.Length; i++)
            {
                var source = lookup[originals[i]];
                RememberLook(source.TemplateId, new GameObjectLook(source.Name, source.DisplayId, source.Scale, source.Type));
                copies.Add((originals[i], fresh[i], CopyPose(Effective(source).Pose)));
            }

            string label = originals.Length == 1 ? "Duplicate" : $"Duplicate {originals.Length} GameObjects";
            var (ok, message) = poses.DuplicateMany(this, database.Schema, key, copies, label);
            Say(ok ? (originals.Length == 1 ? $"Duplicated {lookup[originals[0]].Name}." : $"Duplicated {originals.Length} GameObjects.") : message);
            if (!ok)
            {
                return null;
            }

            // The copies become the selection.
            Select(fresh[0]);
            foreach (ulong other in fresh.Skip(1).Where(lookup.ContainsKey))
            {
                group.Add(other);
                LoadRow(other);
            }

            Select(fresh[0]);
            return fresh[0];
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            Say("Could not duplicate the GameObject: " + error.Message);
            return null;
        }
    }

    /// <summary>Two yards along the facing, dropped to the ground when the probe finds one.</summary>
    WorldPose CopyPose(WorldPose pose)
    {
        var ahead = pose with { X = pose.X + 2 * Math.Cos(pose.Heading), Y = pose.Y + 2 * Math.Sin(pose.Heading) };
        return GroundAt?.Invoke(ViewSpace.ToView(ahead.X, ahead.Y, ahead.Z)).Height is float height ? ahead with { Z = height } : ahead;
    }

    /// <summary>
    /// The primary GameObject moved to <paramref name="after"/>: move the other selected GameObjects by the same
    /// offset in the same command, leaving their facing alone. False when only the primary is selected.
    /// </summary>
    bool MoveGroup(string success, GameObjectPlacement before, GameObjectPlacement after)
    {
        var others = group.Members.ToArray();
        if (poses == null || others.Length == 0)
        {
            return false;
        }

        double dx = after.Pose.X - before.Pose.X, dy = after.Pose.Y - before.Pose.Y, dz = after.Pose.Z - before.Pose.Z;
        ulong primary = selected!.Value;
        var (ok, message) = poses.EditMany(this, sourceKey, [primary, .. others], $"Move {others.Length + 1} GameObjects", row =>
        {
            var pose = SpawnEdits.PoseOf(row);
            return row.UL("guid") == primary
                ? SpawnPoses.WithPlacement(row, after)
                : SpawnEdits.WithPose(row, pose with { X = pose.X + dx, Y = pose.Y + dy, Z = pose.Z + dz });
        });
        Say(ok ? $"{success.TrimEnd('.')} ({others.Length + 1} GameObjects)." : message);
        Resync();
        return true;
    }
}
