using ACWorldEdit.Foundation;

namespace ACWorldEdit.Data;

/// <summary>
/// A creature template's `creature_template_addon` row, and how its spawns share it. A spawn with its own
/// `creature_addon` row ignores the template's, so the count of those says how many spawns an edit will not reach.
/// </summary>
internal static class DbTemplateAddon
{
    public static async Task<Row?> ReadAsync(WorldDatabase database, ITableCatalog tables, uint entry, CancellationToken token)
    {
        var rows = new RowSet();
        var table = tables.Find("creature_template_addon");
        await using var session = await database.OpenAsync(token).ConfigureAwait(false);
        await AcInteractionSource.Load(session, rows, table, "`entry` = @k0", [entry], token).ConfigureAwait(false);
        return rows.Get(table, entry.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    public static async Task<TemplateUse> CountAsync(WorldDatabase database, uint entry, CancellationToken token)
    {
        await using var session = await database.OpenAsync(token).ConfigureAwait(false);
        await using var command = session.Command("");
        command.CommandText = "SELECT (SELECT COUNT(*) FROM creature WHERE id=@e), (SELECT COUNT(*) FROM creature c JOIN creature_addon a ON a.guid=c.guid WHERE c.id=@e)";
        command.Parameters.AddWithValue("@e", entry);
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        return await reader.ReadAsync(token).ConfigureAwait(false)
            ? new(Convert.ToInt32(reader.GetValue(0)), Convert.ToInt32(reader.GetValue(1)))
            : new(0, 0);
    }
}
