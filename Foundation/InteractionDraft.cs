using System.Text.Json;

namespace ACWorldEdit.Foundation;

/// <summary>
/// One row change: insert when Before is null, delete when After is null, otherwise update.
/// </summary>
public sealed record RowEdit(string Table, string Key, Row? Before, Row? After);
/// <summary>
/// One user command (e.g. "add option with new submenu") made of several row edits that undo and
/// redo together.
/// </summary>
public sealed record InteractionCommand(string Source, string Label, RowEdit[] Edits);
public sealed record InteractionDraftSnapshot(int Version, InteractionCommand[] History, int Cursor);

/// <summary>
/// Single-writer local draft for NPC interaction rows (gossip, quest links, vendor, trainer, spell
/// click, npcflag). Stale rules match the other drafts: the first applied edit of each row records
/// the source row it replaced (or its absence for inserts); if the database row has since changed or
/// a reserved new ID now exists, the row is reported as a conflict and the draft is not applied over
/// it. Nothing here writes to a database.
/// </summary>
public sealed class InteractionDraft : IDisposable
{
    readonly DraftFile file;
    InteractionDraftSnapshot state = new(1, [], 0);
    public bool CanUndo => state.Cursor > 0;
    public bool CanRedo => state.Cursor < state.History.Length;
    public string? NextLabel => CanRedo ? state.History[state.Cursor].Label : null;
    public string? LastLabel => CanUndo ? state.History[state.Cursor - 1].Label : null;

    public InteractionDraft(string path)
    {
        file = new DraftFile(path);
        try
        {
            Recover();
        }
        catch
        {
            file.Dispose();
            throw;
        }
    }

    void Recover()
    {
        if (file.Read() is not string payload)
        {
            return;
        }

        var restored = JsonSerializer.Deserialize<InteractionDraftSnapshot>(payload) ?? throw new InvalidDataException("Invalid interaction draft.");
        if (restored.Version != 1 || restored.Cursor < 0 || restored.Cursor > restored.History.Length)
        {
            throw new InvalidDataException("Unsupported interaction draft.");
        }

        foreach (var command in restored.History)
        {
            foreach (var edit in command.Edits)
            {
                ValidateEdit(edit);
            }
        }

        state = restored;
    }

    static void ValidateEdit(RowEdit edit)
    {
        var schema = InteractionTables.Find(edit.Table);
        if (edit.Before == null && edit.After == null)
        {
            throw new InvalidDataException("Empty row edit.");
        }

        foreach (var row in new[] { edit.Before, edit.After })
        {
            if (row == null)
            {
                continue;
            }

            row.Validate(schema);
            if (schema.KeyOf(row) != edit.Key)
            {
                throw new InvalidDataException($"{schema.Name}: edits cannot change a row's primary key.");
            }
        }
    }

    static IEnumerable<InteractionCommand> Applied(InteractionDraftSnapshot state, string source) => state.History.Take(state.Cursor).Where(c => c.Source == source);

    /// <summary>
    /// Every row key the draft touches for this source, so the reader can load those rows too.
    /// </summary>
    public IReadOnlyList<(TableSchema Table, string Key)> Keys(string source) => state.History.Where(c => c.Source == source).SelectMany(c => c.Edits).Select(e => (InteractionTables.Find(e.Table), e.Key)).Distinct().ToList();

    /// <summary>
    /// Rows whose first applied edit no longer matches the database snapshot.
    /// </summary>
    public IReadOnlyList<(string Table, string Key)> Conflicts(string source, RowSet snapshot) => Conflicts(state, source, snapshot);

    static IReadOnlyList<(string Table, string Key)> Conflicts(InteractionDraftSnapshot state, string source, RowSet snapshot)
    {
        var seen = new HashSet<(string, string)>();
        List<(string, string)> conflicts = [];
        foreach (var edit in Applied(state, source).SelectMany(c => c.Edits))
        {
            if (seen.Add((edit.Table, edit.Key)) && snapshot.Get(InteractionTables.Find(edit.Table), edit.Key) != edit.Before)
            {
                conflicts.Add((edit.Table, edit.Key));
            }
        }

        return conflicts;
    }

    /// <summary>
    /// Snapshot with applied edits layered on top. Conflicting rows keep their database value and
    /// later edits to them are skipped, so stale drafts never silently win.
    /// </summary>
    public RowSet Effective(string source, RowSet snapshot) => Effective(state, source, snapshot);

    /// <summary>
    /// Effective-view function over the draft as it is now. The captured history is immutable, so
    /// a worker can apply it while the main thread records further edits.
    /// </summary>
    public Func<RowSet, RowSet> Frozen(string source)
    {
        var captured = state;
        return rows => Effective(captured, source, rows);
    }

    static RowSet Effective(InteractionDraftSnapshot state, string source, RowSet snapshot)
    {
        var conflicts = Conflicts(state, source, snapshot).ToHashSet();
        var result = snapshot.Clone();
        foreach (var edit in Applied(state, source).SelectMany(c => c.Edits))
        {
            if (conflicts.Contains((edit.Table, edit.Key)))
            {
                continue;
            }

            var schema = InteractionTables.Find(edit.Table);
            if (edit.After == null)
            {
                result.Remove(schema, edit.Key);
            }
            else
            {
                result.Set(schema, edit.After);
            }
        }

        return result;
    }

    /// <summary>
    /// Record one command. Each edit's Before must equal the current effective row, so commands are
    /// built from what the user sees; no-op edits are dropped and an empty command adds nothing.
    /// </summary>
    public void Execute(string source, RowSet snapshot, string label, IEnumerable<RowEdit> edits)
    {
        if (Conflicts(source, snapshot).Count > 0)
        {
            throw new InvalidDataException("Source rows changed since this draft; review conflicts before editing.");
        }

        var effective = Effective(source, snapshot);
        var kept = new List<RowEdit>();
        foreach (var edit in edits)
        {
            ValidateEdit(edit);
            var schema = InteractionTables.Find(edit.Table);
            if (effective.Get(schema, edit.Key) != edit.Before)
            {
                throw new InvalidDataException($"{schema.Name} {edit.Key}: the row changed; reload and retry.");
            }

            if (edit.Before == edit.After)
            {
                continue;
            }

            kept.Add(edit);
            if (edit.After == null)
            {
                effective.Remove(schema, edit.Key);
            }
            else
            {
                effective.Set(schema, edit.After);
            }
        }

        if (kept.Count == 0)
        {
            return;
        }

        Save(new(1, state.History.Take(state.Cursor).Append(new InteractionCommand(source, label, kept.ToArray())).ToArray(), state.Cursor + 1));
    }

    /// <summary>
    /// Largest ID the draft has reserved for a table column (for allocating the next new ID).
    /// </summary>
    public uint MaxReserved(string source, TableSchema table, string column) => state.History.Where(c => c.Source == source).SelectMany(c => c.Edits).Where(e => e.Table == table.Name && e.After != null).Select(e => e.After!.U(column)).DefaultIfEmpty(0u).Max();

    public void Undo()
    {
        if (state.Cursor > 0)
        {
            Save(state with { Cursor = state.Cursor - 1 });
        }
    }

    public void Redo()
    {
        if (state.Cursor < state.History.Length)
        {
            Save(state with { Cursor = state.Cursor + 1 });
        }
    }

    void Save(InteractionDraftSnapshot next)
    {
        file.Write(JsonSerializer.Serialize(next));
        state = next;
    }

    public void Dispose() => file.Dispose();
}
