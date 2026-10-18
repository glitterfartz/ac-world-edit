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

/// <summary>
/// Single-writer local draft for NPC interaction rows (gossip, quest links, vendor, trainer, spell
/// click, npcflag). Stale rules match the other drafts: the first applied edit of each row records
/// the source row it replaced (or its absence for inserts); if the database row has since changed or
/// a reserved new ID now exists, the row is reported as a conflict and the draft is not applied over
/// it. Nothing here writes to a database.
/// </summary>
public sealed class InteractionDraft : IDisposable
{
    readonly DraftHistory<InteractionCommand> history;
    DraftSnapshot<InteractionCommand> state => history.State;
    public bool CanUndo => history.CanUndo;
    public bool CanRedo => history.CanRedo;
    public string? NextLabel => CanRedo ? state.History[state.Cursor].Label : null;
    public string? LastLabel => CanUndo ? state.History[state.Cursor - 1].Label : null;

    public InteractionDraft(string path) => history = new(path, "interaction", command =>
    {
        foreach (var edit in command.Edits)
        {
            ValidateEdit(edit);
        }

        return command;
    });

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

    static IEnumerable<InteractionCommand> Applied(DraftSnapshot<InteractionCommand> state, string source) => state.AppliedEntries().Where(c => c.Source == source);

    /// <summary>
    /// Every row key the draft touches for this source, so the reader can load those rows too.
    /// </summary>
    public IReadOnlyList<(TableSchema Table, string Key)> Keys(string source) => state.History.Where(c => c.Source == source).SelectMany(c => c.Edits).Select(e => (InteractionTables.Find(e.Table), e.Key)).Distinct().ToList();

    /// <summary>
    /// Rows whose first applied edit no longer matches the database snapshot.
    /// </summary>
    public IReadOnlyList<(string Table, string Key)> Conflicts(string source, RowSet snapshot) => Conflicts(state, source, snapshot);

    static IReadOnlyList<(string Table, string Key)> Conflicts(DraftSnapshot<InteractionCommand> state, string source, RowSet snapshot)
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

    static RowSet Effective(DraftSnapshot<InteractionCommand> state, string source, RowSet snapshot)
    {
        var conflicts = Conflicts(state, source, snapshot).ToHashSet();
        var result = snapshot.Clone();
        foreach (var edit in Applied(state, source).SelectMany(c => c.Edits))
        {
            if (conflicts.Contains((edit.Table, edit.Key)))
            {
                continue;
            }

            result.Apply(edit);
        }

        return result;
    }

    /// <summary>
    /// Record one command. Each edit's Before must equal the current effective row, so commands are
    /// built from what the user sees; no-op edits are dropped and an empty command adds nothing.
    /// </summary>
    public void Execute(string source, RowSet snapshot, string label, IEnumerable<RowEdit> edits)
    {
        var incoming = edits.ToList();
        var touched = incoming.Select(e => (e.Table, e.Key)).ToHashSet();
        var stale = Conflicts(source, snapshot).Where(touched.Contains).ToList();
        if (stale.Count > 0)
        {
            throw new InvalidDataException($"Source rows changed since this draft; review conflicts before editing ({string.Join(", ", stale.Select(c => $"{c.Table} {c.Key}"))}).");
        }

        var effective = Effective(source, snapshot);
        var kept = new List<RowEdit>();
        foreach (var edit in incoming)
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
            effective.Apply(edit);
        }

        if (kept.Count == 0)
        {
            return;
        }

        history.Append(new InteractionCommand(source, label, kept.ToArray()));
    }

    /// <summary>
    /// Largest ID the draft has reserved for a table column (for allocating the next new ID).
    /// </summary>
    public uint MaxReserved(string source, TableSchema table, string column) => state.History.Where(c => c.Source == source).SelectMany(c => c.Edits).Where(e => e.Table == table.Name && e.After != null).Select(e => e.After!.U(column)).DefaultIfEmpty(0u).Max();

    /// <summary>
    /// Net row change per (source, table, key) over the applied history, in first-touched order: the
    /// first edit's Before and the last edit's After. Rows that end where they began are omitted.
    /// </summary>
    public IReadOnlyList<(string Source, RowEdit Edit)> NetEdits() => state.AppliedEntries()
        .SelectMany(c => c.Edits.Select(e => (c.Source, Edit: e)))
        .GroupBy(x => (x.Source, x.Edit.Table, x.Edit.Key))
        .Select(g => (g.Key.Source, new RowEdit(g.Key.Table, g.Key.Key, g.First().Edit.Before, g.Last().Edit.After)))
        .Where(x => x.Item2.Before != x.Item2.After)
        .ToList();

    /// <summary>
    /// Discard one net row change as a normal undoable command that restores the row's baseline (its
    /// absence for an insert). It bypasses Execute's freshness checks on purpose: a stale or
    /// conflicting row must still be discardable.
    /// </summary>
    public void Revert(string source, RowEdit net) => history.Append(new InteractionCommand(source, $"Discard {net.Table} {net.Key}", [new RowEdit(net.Table, net.Key, net.After, net.Before)]));

    public void Undo() => history.Undo();

    public void Redo() => history.Redo();

    public void Dispose() => history.Dispose();
}
