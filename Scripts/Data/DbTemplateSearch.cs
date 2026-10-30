using ACWorldEdit.Foundation;

namespace ACWorldEdit.Data;

/// <summary>A creature or GameObject template found by name or entry, with its `type` column.</summary>
public sealed record TemplateHit(uint Entry, string Name, int Type);

/// <summary>
/// The content browser's template search: a bounded, escaped LIKE over the name or an exact entry, optionally
/// limited to one `type`. Unnamed rows only match by entry, so an empty search lists real names first.
/// Both dialects keep `name` and `type` in `creature_template` and `gameobject_template`.
/// </summary>
internal static class DbTemplateSearch
{
    public static async Task<IReadOnlyList<TemplateHit>> SearchAsync(WorldDatabase database, SpawnKind kind, string term, int? type, CancellationToken token)
    {
        string table = kind == SpawnKind.Creature ? "creature_template" : "gameobject_template";
        await using var session = await database.OpenAsync(token).ConfigureAwait(false);
        await using var command = session.Command($"SELECT entry,COALESCE(name,''),COALESCE(type,0) FROM {table} WHERE (((name LIKE @s ESCAPE '=' AND name<>'') OR entry=@id){(type == null ? "" : " AND type=@type")}) ORDER BY entry=@id DESC, CHAR_LENGTH(name), entry LIMIT {DbLimits.MaxSearch}");
        WorldReadSession.AddSearch(command, term, "@s", "@id");
        if (type != null)
        {
            command.Parameters.AddWithValue("@type", type.Value);
        }

        List<TemplateHit> hits = [];
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            hits.Add(new(Convert.ToUInt32(reader.GetValue(0)), reader.GetString(1), Convert.ToInt32(reader.GetValue(2))));
        }

        return hits;
    }
}
