namespace ACWorldEdit.Foundation;

/// <summary>
/// Row edits behind the SmartAI editor's Speech page (creature_text), kept out of the window so they
/// can be tested without Godot.
/// </summary>
public static class SpeechEdits
{
    static TableSchema Table => InteractionTables.CreatureText;

    public static IEnumerable<Row> LinesOf(RowSet rows, uint entry) => rows.All(Table).Where(t => t.U("CreatureID") == entry);

    /// <summary>The group ID a "+ New group" button uses: one past the highest existing group.</summary>
    public static uint NextGroup(RowSet rows, uint entry) => LinesOf(rows, entry).Select(t => t.U("GroupID") + 1).DefaultIfEmpty(0u).Max();

    /// <summary>
    /// A blank line appended to a group (a new group when it has no lines yet). Returns the label and
    /// the insert edit.
    /// </summary>
    public static (string Label, RowEdit Edit) NewLine(RowSet rows, uint entry, uint group, string comment)
    {
        uint id = LinesOf(rows, entry).Where(t => t.U("GroupID") == group).Select(t => t.U("ID") + 1).DefaultIfEmpty(0u).Max();
        var row = Row.Of(("CreatureID", entry), ("GroupID", group), ("ID", id), ("Text", ""), ("Type", 12u), ("Language", 0), ("Probability", 100f), ("Emote", 0u),
            ("Duration", 0u), ("Sound", 0u), ("BroadcastTextId", 0), ("TextRange", 0u), ("comment", comment.Length > 255 ? comment[..255] : comment));
        return (id == 0 ? $"Add speech group {group}" : $"Add line to group {group}", new RowEdit(Table.Name, Table.KeyOf(row), null, row));
    }

    /// <summary>Change columns of an existing line; null when the line is gone or nothing changes.</summary>
    public static RowEdit? Change(RowSet rows, Row line, params (string Column, object Value)[] values)
    {
        var current = rows.Get(Table, Table.KeyOf(line));
        if (current == null)
        {
            return null;
        }

        var after = current;
        foreach (var (column, value) in values)
        {
            after = after.With(column, value);
        }

        return after == current ? null : new RowEdit(Table.Name, Table.KeyOf(current), current, after);
    }

    /// <summary>Link a broadcast text (the server shows it instead of the line's own text); 0 unlinks.</summary>
    public static RowEdit? SetBroadcast(RowSet rows, Row line, uint broadcastTextId) => Change(rows, line, ("BroadcastTextId", (int)broadcastTextId));

    public static RowEdit Remove(Row line) => new(Table.Name, Table.KeyOf(line), line, null);
}
