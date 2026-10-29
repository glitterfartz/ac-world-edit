using ACWorldEdit.Foundation;

namespace ACWorldEdit.Data;

/// <summary>
/// The waypoint paths some creatures walk, with each path's node count and how many other creatures
/// and templates use it. The tables differ by dialect only in names: AzerothCore keeps `path_id` and
/// `waypoint_data`, TrinityCore `PathId` and `waypoint_path_node`.
/// </summary>
internal static class DbSpawnPaths
{
    const int MaxGuids = 500;

    public static async Task<IReadOnlyList<SpawnPathUse>> ReadAsync(WorldDatabase database, WorldDialect dialect, IReadOnlyList<ulong> guids, CancellationToken token)
    {
        var wanted = guids.Distinct().Take(MaxGuids).ToArray();
        if (wanted.Length == 0)
        {
            return [];
        }

        bool tc = dialect == WorldDialect.TrinityCore;
        string column = tc ? "PathId" : "path_id", nodes = tc ? "waypoint_path_node" : "waypoint_data", nodeKey = tc ? "PathId" : "id";
        await using var session = await database.OpenAsync(token).ConfigureAwait(false);
        if (!session.Has($"creature_addon.{column}") || !session.Has($"{nodes}.{nodeKey}"))
        {
            return [];
        }

        string names = string.Join(',', wanted.Select((_, i) => "@k" + i));
        bool template = session.Has($"creature_template_addon.{column}");
        // Every other creature or template on the path; the deleted creatures themselves do not count.
        string others = $"(SELECT COUNT(*) FROM creature_addon o WHERE o.`{column}`=a.`{column}` AND o.guid NOT IN ({names}))"
            + (template ? $"+(SELECT COUNT(*) FROM creature_template_addon t WHERE t.`{column}`=a.`{column}`)" : "");
        await using var command = session.Command("");
        command.CommandText = $"SELECT a.guid,a.`{column}`,(SELECT COUNT(*) FROM `{nodes}` n WHERE n.`{nodeKey}`=a.`{column}`),{others} FROM creature_addon a WHERE a.guid IN ({names}) AND a.`{column}`<>0";
        for (int i = 0; i < wanted.Length; i++)
        {
            command.Parameters.AddWithValue("@k" + i, wanted[i]);
        }

        var paths = new List<SpawnPathUse>();
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            paths.Add(new(Convert.ToUInt64(reader.GetValue(0)), Convert.ToUInt64(reader.GetValue(1)), Convert.ToInt32(reader.GetValue(2)), Convert.ToInt32(reader.GetValue(3))));
        }

        return paths;
    }
}
