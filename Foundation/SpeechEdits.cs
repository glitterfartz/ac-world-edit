namespace ACWorldEdit.Foundation;

/// <summary>
/// Row edits behind the SmartAI editor's Speech page (creature_text), kept out of the window so they
/// can be tested without Godot.
/// </summary>
public static class SpeechEdits
{
    public static IEnumerable<Row> LinesOf(SmartCatalog smart, RowSet rows, uint entry) => rows.All(smart.CreatureText).Where(t => t.U("CreatureID") == entry);

    /// <summary>The group ID a "+ New group" button uses: one past the highest existing group.</summary>
    public static uint NextGroup(SmartCatalog smart, RowSet rows, uint entry) => LinesOf(smart, rows, entry).Select(t => t.U("GroupID") + 1).DefaultIfEmpty(0u).Max();

    /// <summary>
    /// A blank line appended to a group (a new group when it has no lines yet). Returns the label and
    /// the insert edit.
    /// </summary>
    public static (string Label, RowEdit Edit) NewLine(SmartCatalog smart, RowSet rows, uint entry, uint group, string comment)
    {
        uint id = LinesOf(smart, rows, entry).Where(t => t.U("GroupID") == group).Select(t => t.U("ID") + 1).DefaultIfEmpty(0u).Max();
        var row = Row.Of(("CreatureID", entry), ("GroupID", group), ("ID", id), ("Text", ""), ("Type", 12u), ("Language", 0), ("Probability", 100f), ("Emote", 0u),
            ("Duration", 0u), ("Sound", 0u), ("BroadcastTextId", 0), ("TextRange", 0u), ("comment", comment.Length > 255 ? comment[..255] : comment));
        if (smart.Dialect == WorldDialect.TrinityCore)
        {
            row = row.With("SoundPlayType", 0u);
        }

        return (id == 0 ? $"Add speech group {group}" : $"Add line to group {group}", new RowEdit(smart.CreatureText.Name, smart.CreatureText.KeyOf(row), null, row));
    }

    /// <summary>Change columns of an existing line; null when the line is gone or nothing changes.</summary>
    public static RowEdit? Change(SmartCatalog smart, RowSet rows, Row line, params (string Column, object Value)[] values)
    {
        var table = smart.CreatureText;
        var current = rows.Get(table, table.KeyOf(line));
        if (current == null)
        {
            return null;
        }

        var after = current;
        foreach (var (column, value) in values)
        {
            after = after.With(column, value);
        }

        return after == current ? null : new RowEdit(table.Name, table.KeyOf(current), current, after);
    }

    /// <summary>Link a broadcast text (the server shows it instead of the line's own text); 0 unlinks.</summary>
    public static RowEdit? SetBroadcast(SmartCatalog smart, RowSet rows, Row line, uint broadcastTextId) => Change(smart, rows, line, ("BroadcastTextId", (int)broadcastTextId));

    public static RowEdit Remove(SmartCatalog smart, Row line) => new(smart.CreatureText.Name, smart.CreatureText.KeyOf(line), line, null);
}
