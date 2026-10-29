using ACWorldEdit.Foundation;

namespace ACWorldEdit.Data;

/// <summary>
/// The name, first model and model scale of some creature templates: what a spawn that is only in the
/// draft is drawn with, since the region read only returns spawns the database already holds.
/// </summary>
internal static class DbCreatureLooks
{
    const int MaxEntries = 500;

    public static async Task<IReadOnlyDictionary<uint, CreatureLook>> ReadAsync(WorldDatabase database, WorldDialect dialect, IReadOnlyList<uint> entries, CancellationToken token)
    {
        var looks = new Dictionary<uint, CreatureLook>();
        var wanted = entries.Distinct().Take(MaxEntries).ToArray();
        if (wanted.Length == 0)
        {
            return looks;
        }

        await using var session = await database.OpenAsync(token).ConfigureAwait(false);
        // Same model and scale rules as the region reads: TrinityCore models live in creature_template_model;
        // AzerothCore has them there too on current builds and in modelid1..4 on older ones.
        string display, scale;
        if (dialect == WorldDialect.TrinityCore || session.Has("creature_template_model.CreatureDisplayID"))
        {
            display = "COALESCE((SELECT m.CreatureDisplayID FROM creature_template_model m WHERE m.CreatureID=t.entry ORDER BY m.Idx LIMIT 1),0)";
        }
        else
        {
            display = "COALESCE(NULLIF(t.modelid1,0),NULLIF(t.modelid2,0),NULLIF(t.modelid3,0),t.modelid4,0)";
        }

        if (dialect == WorldDialect.TrinityCore)
        {
            scale = "COALESCE(NULLIF(t.scale,0),1)*COALESCE((SELECT m.DisplayScale FROM creature_template_model m WHERE m.CreatureID=t.entry ORDER BY m.Idx LIMIT 1),1)";
        }
        else
        {
            scale = session.Has("creature_template_model.DisplayScale")
                ? "COALESCE((SELECT m.DisplayScale FROM creature_template_model m WHERE m.CreatureID=t.entry ORDER BY m.Idx LIMIT 1),1)"
                : "COALESCE(NULLIF(t.scale,0),1)";
        }

        await using var command = session.Command("");
        command.CommandText = $"SELECT t.entry,t.name,{display},{scale},EXISTS(SELECT 1 FROM creature_equip_template e WHERE e.CreatureID=t.entry AND e.ID=1),t.MovementType FROM creature_template t WHERE t.entry IN ({string.Join(',', wanted.Select((_, i) => "@k" + i))})";
        for (int i = 0; i < wanted.Length; i++)
        {
            command.Parameters.AddWithValue("@k" + i, wanted[i]);
        }

        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            float modelScale = reader.GetFloat(3);
            looks[reader.GetUInt32(0)] = new(reader.GetString(1), reader.GetUInt32(2), float.IsFinite(modelScale) && modelScale > 0 ? modelScale : 1, reader.GetBoolean(4) ? 1 : 0, Convert.ToUInt32(reader.GetValue(5)));
        }

        return looks;
    }
}
