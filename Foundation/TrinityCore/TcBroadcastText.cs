namespace ACWorldEdit.Foundation;

/// <summary>
/// A new broadcast text: the <c>hotfixes.broadcast_text</c> row and the <c>hotfixes.hotfix_data</c> row that
/// announces it. The server reads broadcast_text from the hotfixes database (rows with a VerifiedBuild of 0
/// or less are its own, loaded after the client's); hotfix_data tells connected clients the row exists.
/// </summary>
public static class TcBroadcastText
{
    /// <summary>The VerifiedBuild of rows this tool writes: not a client build, so the server treats them as custom.</summary>
    public const int CustomBuild = -1;

    /// <summary>The table hash in the retail client's BroadcastText.db2 header, which hotfix_data uses to name the table.</summary>
    public const uint TableHash = 35137211;

    /// <summary>hotfix_data.Status for a record that is valid (1); 3 would be an invalid (deleted) record.</summary>
    const uint StatusValid = 1;

    /// <summary>
    /// Both rows for a new text. <paramref name="id"/> must not exist in the client's table or the hotfixes
    /// database, and <paramref name="hotfixId"/> must be a free hotfix_data Id (its UniqueId repeats it).
    /// </summary>
    public static RowEdit[] Create(uint id, string text, int hotfixId)
    {
        if (id == 0)
        {
            throw new InvalidDataException("A broadcast text needs an ID above 0.");
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidDataException("A broadcast text needs some text.");
        }

        var broadcast = Row.Of(TcTables.BroadcastText.Columns.Select(c => (c.Name, (object)(c.Kind == ColumnKind.Text ? "" : 0))).ToArray())
            .With("ID", id).With("Text", text).With("VerifiedBuild", CustomBuild);
        var hotfix = Row.Of(("Id", hotfixId), ("UniqueId", (uint)hotfixId), ("TableHash", TableHash), ("RecordId", (int)id), ("Status", StatusValid), ("VerifiedBuild", CustomBuild));
        return [InteractionEdits.Insert(TcTables.BroadcastText, broadcast), InteractionEdits.Insert(TcTables.HotfixData, hotfix)];
    }

    /// <summary>The text of a staged broadcast text, or null when none of the rows is for this ID.</summary>
    public static (string Male, string Female)? Staged(RowSet rows, uint id) =>
        rows.All(TcTables.BroadcastText).Where(r => r.U("ID") == id).OrderByDescending(r => r.I("VerifiedBuild")).FirstOrDefault() is { } row
            ? (row.T("Text"), row.T("Text1").Length > 0 ? row.T("Text1") : row.T("Text"))
            : null;
}
