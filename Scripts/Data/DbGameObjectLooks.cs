using ACWorldEdit.Foundation;

namespace ACWorldEdit.Data;

/// <summary>The name, model, size and type of some GameObject templates: what a spawn that is only in the draft is drawn with. Both dialects share the columns.</summary>
internal static class DbGameObjectLooks
{
    const int MaxEntries = 500;

    public static async Task<IReadOnlyDictionary<uint, GameObjectLook>> ReadAsync(WorldDatabase database, IReadOnlyList<uint> entries, CancellationToken token)
    {
        var looks = new Dictionary<uint, GameObjectLook>();
        var wanted = entries.Distinct().Take(MaxEntries).ToArray();
        if (wanted.Length == 0)
        {
            return looks;
        }

        await using var session = await database.OpenAsync(token).ConfigureAwait(false);
        await using var command = session.Command("");
        command.CommandText = $"SELECT entry,name,COALESCE(displayId,0),COALESCE(size,1),type FROM gameobject_template WHERE entry IN ({string.Join(',', wanted.Select((_, i) => "@k" + i))})";
        for (int i = 0; i < wanted.Length; i++)
        {
            command.Parameters.AddWithValue("@k" + i, wanted[i]);
        }

        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            float scale = reader.GetFloat(3);
            looks[reader.GetUInt32(0)] = new(reader.GetString(1), reader.GetUInt32(2), float.IsFinite(scale) && scale > 0 ? scale : 1, Convert.ToInt32(reader.GetValue(4)));
        }

        return looks;
    }
}
