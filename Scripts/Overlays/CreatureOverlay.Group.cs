using Godot;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// Several creatures selected at once: Ctrl-click and a Shift-drag box add to the selection, and Delete,
/// Duplicate and a move then act on the primary creature and the others together, as one undoable command.
/// </summary>
public partial class CreatureOverlay
{
    readonly SpawnGroupSelection group = new();
    List<ulong>? groupWhenDrawn;

    /// <summary>The primary creature first, then the others.</summary>
    public IReadOnlyList<ulong> Group => selectedGuid is ulong primary ? [primary, .. group.Members] : [];

    /// <summary>True for a creature selected along with the primary.</summary>
    public bool InGroup(ulong guid) => group.Contains(guid);

    /// <summary>Ctrl-click: add the creature to the selection, or take it out. With nothing selected it becomes the selection.</summary>
    public void ToggleInGroup(ulong guid)
    {
        if (!markers.ContainsKey(guid))
        {
            return;
        }

        if (!selectedGuid.HasValue)
        {
            Select(guid);
            return;
        }

        if (guid == selectedGuid)
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

        Select(selectedGuid.Value);
    }

    /// <summary>Shift-drag: add every creature whose marker lies inside the screen rectangle. Returns how many were added.</summary>
    public int SelectInBox(Camera3D camera, Rect2 box)
    {
        int added = 0;
        foreach (var (guid, marker) in markers)
        {
            var point = marker.Node.GlobalPosition + Vector3.Up;
            if (!marker.Node.Visible || camera.IsPositionBehind(point) || !box.HasPoint(camera.UnprojectPosition(point)))
            {
                continue;
            }

            if (!selectedGuid.HasValue)
            {
                Select(guid);
            }
            else if (guid != selectedGuid && !group.Contains(guid))
            {
                group.Add(guid);
                LoadRow(guid);
            }
            else
            {
                continue;
            }

            added++;
        }

        if (selectedGuid.HasValue)
        {
            Select(selectedGuid.Value);
        }

        return added;
    }

    /// <summary>Every selected creature has its row loaded (or is staged), so the group can be copied or deleted.</summary>
    bool RowReady => poses?.Available == true && Group.Count > 0 && Group.All(g => poses.Has(sourceKey, g) || IsStaged(g));

    public bool CanDuplicate => RowReady;
    public bool CanDelete => RowReady;

    ConfirmationDialog? deleteDialog;

    string SubjectOf(IReadOnlyList<ulong> guids) => guids.Count == 1 && markers.TryGetValue(guids[0], out var marker) ? $"{marker.Spawn.Name} ({guids[0]})" : $"{guids.Count} creatures";

    /// <summary>
    /// Delete the selected creatures and their addon rows as one undoable command. When other tables still
    /// name them, or a waypoint path would be left unused, a dialog lists that first.
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
        IReadOnlyList<SpawnPathUse> paths = [];
        if (existing.Length > 0)
        {
            try
            {
                var source = DBSource.Open(Connection());
                items = await source.CountSpawnReferencesAsync(SpawnKind.Creature, existing, default);
                paths = await source.ReadSpawnPathsAsync(existing, default);
            }
            catch (Exception)
            {
                Say($"Could not check what references {subject}; not deleted.");
                return;
            }
        }

        if (!SpawnImpact.Any(items, paths))
        {
            DeleteNow(guids, subject);
            return;
        }

        deleteDialog = SpawnDeleteDialog.Ask(this, subject, items, () => DeleteNow(guids, subject), paths);
    }

    void DeleteNow(IReadOnlyList<ulong> guids, string subject)
    {
        var (ok, message) = poses!.DeleteMany(this, ContentDraftFiles.Sources.SchemaFor(sourceKey), sourceKey, guids, guids.Count == 1 ? "Delete" : $"Delete {guids.Count} creatures");
        Say(ok ? $"Deleted {subject}." : message);
    }

    /// <summary>Copy the selected creatures two yards ahead of themselves, on the ground, and select the copies.</summary>
    public async void Duplicate() => await DuplicateAsync();

    public async Task<ulong?> DuplicateAsync()
    {
        if (poses == null || Group.Count == 0)
        {
            return null;
        }

        gizmo.Finish(false);
        var originals = Group.Where(markers.ContainsKey).ToArray();
        var database = DBSource.Open(Connection());
        string key = sourceKey;
        try
        {
            ulong max = await database.MaxIdAsync(SpawnEdits.SpawnTable(database.Schema, SpawnKind.Creature), "guid", default);
            var fresh = poses.NextGuids(key, max, originals.Length);
            var copies = new List<(ulong, ulong, WorldPose)>();
            for (int i = 0; i < originals.Length; i++)
            {
                var spawn = markers[originals[i]].Spawn;
                RememberLook(spawn.TemplateId, new CreatureLook(spawn.Name, spawn.DisplayId, spawn.DisplayScale));
                copies.Add((originals[i], fresh[i], CopyPose(PoseOf(spawn))));
            }

            string label = originals.Length == 1 ? "Duplicate" : $"Duplicate {originals.Length} creatures";
            var (ok, message) = poses.DuplicateMany(this, database.Schema, key, copies, label);
            Say(ok ? (originals.Length == 1 ? $"Duplicated {markers[originals[0]].Spawn.Name}." : $"Duplicated {originals.Length} creatures.") : message);
            if (!ok)
            {
                return null;
            }

            // The copies become the selection once they are drawn.
            selectWhenDrawn = fresh[0];
            groupWhenDrawn = fresh.Skip(1).ToList();
            return fresh[0];
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            Say("Could not duplicate the creature: " + error.Message);
            return null;
        }
    }

    /// <summary>Two yards along the facing, dropped to the ground when the probe finds one.</summary>
    WorldPose CopyPose(WorldPose pose)
    {
        var ahead = pose with { X = pose.X + 2 * Math.Cos(pose.Heading), Y = pose.Y + 2 * Math.Sin(pose.Heading) };
        return GroundAt?.Invoke(ViewPosition(ahead)).Height is float height ? ahead with { Z = height } : ahead;
    }

    /// <summary>
    /// The primary creature moved to <paramref name="after"/>: move the other selected creatures by the same
    /// offset in the same command. False when only the primary is selected.
    /// </summary>
    bool MoveGroup(string success, WorldPose before, WorldPose after)
    {
        var others = group.Members.ToArray();
        if (poses == null || others.Length == 0)
        {
            return false;
        }

        double dx = after.X - before.X, dy = after.Y - before.Y, dz = after.Z - before.Z;
        ulong primary = selectedGuid!.Value;
        var (ok, message) = poses.EditMany(this, sourceKey, [primary, .. others], $"Move {others.Length + 1} creatures", row =>
        {
            var pose = SpawnEdits.PoseOf(row);
            return row.UL("guid") == primary
                ? SpawnEdits.WithPose(row, after)
                : SpawnEdits.WithPose(row, pose with { X = pose.X + dx, Y = pose.Y + dy, Z = pose.Z + dz });
        });
        Say(ok ? $"{success.TrimEnd('.')} ({others.Length + 1} creatures)." : message);
        foreach (ulong guid in Group.Where(markers.ContainsKey))
        {
            ApplyPose(guid);
        }

        Select(primary);
        return true;
    }
}
