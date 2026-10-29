using System.Globalization;
using ACWorldEdit.Foundation;

namespace ACWorldEdit.Data;

/// <summary>
/// Some columns of one creature or GameObject template, as text: what a spawn that leaves an override
/// empty will use. Column names come from code (<see cref="SpawnFields"/>), never from input, and a
/// column the database lacks is left out.
/// </summary>
internal static class DbTemplateColumns
{
    public static async Task<IReadOnlyDictionary<string, string>> ReadAsync(WorldDatabase database, SpawnKind kind, uint entry, IReadOnlyList<string> columns, CancellationToken token)
    {
        string table = kind == SpawnKind.Creature ? "creature_template" : "gameobject_template";
        await using var session = await database.OpenAsync(token).ConfigureAwait(false);
        var present = columns.Where(c => session.Has($"{table}.{c}")).Distinct().ToArray();
        var values = new Dictionary<string, string>();
        if (present.Length == 0)
        {
            return values;
        }

        await using var command = session.Command("");
        command.CommandText = $"SELECT {string.Join(',', present.Select(c => $"`{c}`"))} FROM `{table}` WHERE entry=@entry";
        command.Parameters.AddWithValue("@entry", entry);
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        if (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            for (int i = 0; i < present.Length; i++)
            {
                values[present[i]] = reader.IsDBNull(i) ? "" : Convert.ToString(reader.GetValue(i), CultureInfo.InvariantCulture) ?? "";
            }
        }

        return values;
    }
}
