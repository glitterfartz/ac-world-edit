namespace ACWorldEdit.Foundation;

// Editing, deleting and duplicating several spawns as one undoable command (a group move/paste).
public sealed partial class SpawnPoses
{
    /// <summary>
    /// Change several spawn rows as one undoable command (a group move). Every spawn's row must be loaded;
    /// otherwise nothing is changed and the message says so.
    /// </summary>
    public (bool Ok, string Message) EditMany(object? sender, string source, IReadOnlyList<ulong> guids, string label, Func<Row, Row> change)
    {
        if (drafts.Draft == null)
        {
            return (false, drafts.Error ?? "Editing is disabled.");
        }

        var table = Table(source);
        var snapshot = Snapshot(source);
        var effective = drafts.Draft.Effective(source, snapshot);
        var edits = new List<RowEdit>();
        foreach (ulong guid in guids.Distinct())
        {
            if (effective.Get(table, Key(guid)) is not { } before)
            {
                return (false, "A selected spawn is still loading; try again.");
            }

            try
            {
                var after = change(before);
                if (after != before)
                {
                    edits.Add(InteractionEdits.Update(table, before, after));
                }
            }
            catch (InvalidDataException error)
            {
                return (false, "Not saved: " + error.Message);
            }
        }

        return edits.Count == 0 ? (true, "") : Commit(sender, source, label, edits);
    }

    /// <summary>Delete several spawns, each with its addon row, as one undoable command.</summary>
    public (bool Ok, string Message) DeleteMany(object? sender, IWorldSchema schema, string source, IReadOnlyList<ulong> guids, string label = "Delete")
    {
        if (drafts.Draft == null)
        {
            return (false, drafts.Error ?? "Editing is disabled.");
        }

        var effective = drafts.Draft.Effective(source, Snapshot(source));
        var edits = new List<RowEdit>();
        foreach (ulong guid in guids.Distinct())
        {
            if (effective.Get(Table(source), Key(guid)) is not { } row)
            {
                return (false, "A selected spawn is still loading; try again.");
            }

            edits.AddRange(SpawnEdits.Delete(schema, kind, effective, row));
        }

        return Commit(sender, source, label, edits);
    }

    /// <summary>Copy several spawns, each to its own pose under the matching new GUID, as one undoable command.</summary>
    public (bool Ok, string Message) DuplicateMany(object? sender, IWorldSchema schema, string source, IReadOnlyList<(ulong Guid, ulong NewGuid, WorldPose Pose)> copies, string label = "Duplicate")
    {
        if (drafts.Draft == null)
        {
            return (false, drafts.Error ?? "Editing is disabled.");
        }

        var effective = drafts.Draft.Effective(source, Snapshot(source));
        var edits = new List<RowEdit>();
        try
        {
            foreach (var (guid, newGuid, pose) in copies)
            {
                if (effective.Get(Table(source), Key(guid)) is not { } row)
                {
                    return (false, "A selected spawn is still loading; try again.");
                }

                edits.AddRange(SpawnEdits.Duplicate(schema, kind, effective, row, newGuid, pose));
            }
        }
        catch (InvalidDataException error)
        {
            return (false, "Not saved: " + error.Message);
        }

        return Commit(sender, source, label, edits);
    }

    /// <summary>The next <paramref name="count"/> free GUIDs, contiguous, past the database and the draft.</summary>
    public ulong[] NextGuids(string source, ulong databaseMax, int count) => IdAllocator.Block(count, databaseMax, drafts.Draft?.MaxStaged(source, Table(source), "guid") ?? 0);
}
