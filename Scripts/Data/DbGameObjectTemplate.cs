using System.Globalization;
using ACWorldEdit.Foundation;

namespace ACWorldEdit.Data;

/// <summary>
/// Reads one GameObject template and the rows its pages edit: the template, its optional
/// `gameobject_template_addon`, quest starters/enders/items and the spawns that use it.
/// The table catalog supplies the dialect's column whitelist; the shared read is identical.
/// </summary>
internal static class DbGameObjectTemplate
{
    static readonly string[] RequiredColumns =
    [
        "gameobject_template.entry", "gameobject_template.name", "gameobject_template.type", "gameobject_template.displayId",
        "gameobject_template_addon.entry", "gameobject_queststarter.id", "gameobject_questender.id",
        "gameobject_questitem.GameObjectEntry"
    ];

    public static async Task<GameObjectSnapshot> ReadAsync(
        WorldDatabase database,
        ITableCatalog tables,
        uint entry,
        IReadOnlyList<(TableSchema Table, string Key)> draftKeys,
        Func<RowSet, RowSet> effective,
        CancellationToken token)
    {
        await using var session = await database.OpenAsync(token).ConfigureAwait(false);
        session.Require(RequiredColumns);
        var rows = new RowSet();
        var templateTable = tables.Find("gameobject_template");
        await AcInteractionSource.Load(session, rows, templateTable, "entry=@k0", [entry], token).ConfigureAwait(false);
        var template = rows.Get(templateTable, entry.ToString(CultureInfo.InvariantCulture));
        if (template == null)
        {
            throw new InvalidDataException($"GameObject template {entry} does not exist.");
        }

        var addon = tables.Find("gameobject_template_addon");
        var starters = tables.Find("gameobject_queststarter");
        var enders = tables.Find("gameobject_questender");
        var items = tables.Find("gameobject_questitem");
        await AcInteractionSource.Load(session, rows, addon, "entry=@k0", [entry], token).ConfigureAwait(false);
        await AcInteractionSource.Load(session, rows, starters, "id=@k0", [entry], token).ConfigureAwait(false);
        await AcInteractionSource.Load(session, rows, enders, "id=@k0", [entry], token).ConfigureAwait(false);
        await AcInteractionSource.Load(session, rows, items, "GameObjectEntry=@k0", [entry], token).ConfigureAwait(false);

        foreach (var (table, key) in draftKeys)
        {
            var parts = key.Split('|');
            await AcInteractionSource.Load(session, rows, table,
                string.Join(" AND ", table.Key.Select((k, i) => $"`{k}`=@k{i}")), parts.Cast<object>().ToArray(), token).ConfigureAwait(false);
        }

        var (count, spawns) = await DbGameObjectSpawns.ReadAsync(session, entry, 20, token).ConfigureAwait(false);
        var view = effective(rows);
        var questIds = view.All(starters).Concat(view.All(enders)).Select(r => r.U("quest")).Where(id => id != 0).Distinct().ToArray();
        var questTitles = new Dictionary<uint, string>();
        if (questIds.Length > 0)
        {
            await using var command = session.Command("");
            command.CommandText = $"SELECT ID,COALESCE(LogTitle,'') FROM quest_template WHERE ID IN ({AcInteractionSource.InList(command, questIds, "q")})";
            await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                questTitles[reader.GetUInt32(0)] = reader.GetString(1);
            }
        }

        return new GameObjectSnapshot(entry, template.T("name"), (int)template.U("type"), count, spawns, questTitles, rows, database.SourceKey);
    }
}
