namespace ACWorldEdit.Foundation;

/// <summary>
/// Pure staging logic for the change history: turn the current net draft changes into change
/// records, preserving each row's existing note and original staged timestamp. All database and
/// Godot work happens outside; this is the part unit tests pin, so a staged draft can never
/// silently vanish from the history window again.
/// </summary>
public static class ChangeLogMerge
{
    public static ChangeRecord BuildRecord(
        PendingChange change,
        string notes,
        DateTime createdAt,
        DateTime now,
        Func<string, IWorldSchema> schemaFor,
        Func<string, WorldDialect> dialectFor)
    {
        var schema = schemaFor(change.Source);
        SqlScripts scripts = change.Kind == PendingKind.Graveyard
            ? SqlExport.Build(change.Source, [change.Graveyard!], [], now, schema)
            : SqlExport.Build(change.Source, [], [change.Row!], now, schema);
        return new ChangeRecord(change.Id, change.Source, dialectFor(change.Source), change.Label, scripts.Apply, scripts.Revert, ChangeStatus.Staged, createdAt, null, null, notes);
    }

    /// <summary>
    /// One record per staged change. Rows already tracked keep their note and first-seen timestamp;
    /// brand-new rows start with a blank note stamped now.
    /// </summary>
    public static List<ChangeRecord> MergeStaged(
        IEnumerable<PendingChange> staged,
        IReadOnlyDictionary<string, ChangeRecord> existing,
        Func<string, IWorldSchema> schemaFor,
        Func<string, WorldDialect> dialectFor,
        DateTime now)
    {
        var records = new List<ChangeRecord>();
        foreach (var change in staged)
        {
            existing.TryGetValue(change.Id, out var prior);
            records.Add(BuildRecord(change, prior?.Notes ?? "", prior?.CreatedAt ?? now, now, schemaFor, dialectFor));
        }

        return records;
    }
}
