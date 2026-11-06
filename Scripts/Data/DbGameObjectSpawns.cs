using System.Globalization;
using ACWorldEdit.Foundation;

namespace ACWorldEdit.Data;

/// <summary>The bounded spawn count and sample shown on the GameObject Spawns page.</summary>
internal static class DbGameObjectSpawns
{
    public static async Task<(int Count, IReadOnlyList<GameObjectSpawnSample> Spawns)> ReadAsync(
        WorldReadSession session, uint entry, int limit, CancellationToken token)
    {
        int count;
        await using (var command = session.Command("SELECT COUNT(*) FROM gameobject WHERE id=@k0"))
        {
            command.Parameters.AddWithValue("@k0", entry);
            count = Convert.ToInt32(await command.ExecuteScalarAsync(token).ConfigureAwait(false), CultureInfo.InvariantCulture);
        }

        List<GameObjectSpawnSample> spawns = [];
        await using (var command = session.Command(""))
        {
            command.CommandText = $"SELECT guid,map,position_x,position_y,position_z FROM gameobject WHERE id=@k0 ORDER BY guid LIMIT {Math.Clamp(limit, 1, DbLimits.MaxRows)}";
            command.Parameters.AddWithValue("@k0", entry);
            await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                spawns.Add(new GameObjectSpawnSample(
                    Convert.ToUInt64(reader.GetValue(0), CultureInfo.InvariantCulture),
                    Convert.ToUInt32(reader.GetValue(1), CultureInfo.InvariantCulture),
                    Convert.ToDouble(reader.GetValue(2), CultureInfo.InvariantCulture),
                    Convert.ToDouble(reader.GetValue(3), CultureInfo.InvariantCulture),
                    Convert.ToDouble(reader.GetValue(4), CultureInfo.InvariantCulture)));
            }
        }

        return (count, spawns);
    }
}
