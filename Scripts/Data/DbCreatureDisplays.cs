using System.Globalization;

namespace ACWorldEdit.Data;

/// <summary>
/// Searchable creature display models (creature_template_model.CreatureDisplayID), named by a
/// creature that uses them. Both databases keep the same table, so one read serves the template and
/// spawn appearance pickers.
/// </summary>
internal static class DbCreatureDisplays
{
    public static async Task<IReadOnlyList<(uint Id, string Name)>> SearchAsync(WorldDatabase database, string term, CancellationToken token)
    {
        await using var session = await database.OpenAsync(token).ConfigureAwait(false);
        await using var command = session.Command("");
        WorldReadSession.AddSearch(command, term, "@s", "@id");
        command.CommandText = $"""
            SELECT m.CreatureDisplayID, COALESCE(MIN(t.name),'')
            FROM creature_template_model m
            LEFT JOIN creature_template t ON t.entry=m.CreatureID
            WHERE m.CreatureDisplayID=@id OR t.name LIKE @s ESCAPE '='
            GROUP BY m.CreatureDisplayID
            ORDER BY (m.CreatureDisplayID=@id) DESC, CHAR_LENGTH(MIN(t.name)), m.CreatureDisplayID
            LIMIT {DbLimits.MaxSearch}
            """;
        List<(uint, string)> found = [];
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            found.Add((Convert.ToUInt32(reader.GetValue(0), CultureInfo.InvariantCulture), reader.GetString(1)));
        }

        return found;
    }
}
