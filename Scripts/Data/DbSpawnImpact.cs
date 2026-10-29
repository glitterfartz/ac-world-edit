using ACWorldEdit.Foundation;

namespace ACWorldEdit.Data;

/// <summary>
/// Counts the rows outside the spawn tables that name some spawn GUIDs (<see cref="SpawnEdits.References"/>).
/// A table or column the database lacks is skipped, so an older or trimmed schema reports what it can.
/// </summary>
internal static class DbSpawnImpact
{
    const int MaxGuids = 500;

    public static async Task<IReadOnlyList<SpawnImpactItem>> CountAsync(WorldDatabase database, WorldDialect dialect, SpawnKind kind, IReadOnlyList<ulong> guids, CancellationToken token)
    {
        var wanted = guids.Distinct().Take(MaxGuids).ToArray();
        if (wanted.Length == 0)
        {
            return [];
        }

        await using var session = await database.OpenAsync(token).ConfigureAwait(false);
        var items = new List<SpawnImpactItem>();
        foreach (var reference in SpawnEdits.References(dialect, kind))
        {
            if (!session.Has($"{reference.Table}.{reference.Column}"))
            {
                continue;
            }

            await using var command = session.Command("");
            // Names come from code; the GUIDs are parameters (negated for tables that store them so).
            string names = string.Join(',', wanted.Select((_, i) => "@k" + i));
            command.CommandText = $"SELECT COUNT(*) FROM `{reference.Table}` WHERE `{reference.Column}` IN ({names})" + (reference.Where == null ? "" : $" AND {reference.Where}");
            for (int i = 0; i < wanted.Length; i++)
            {
                command.Parameters.AddWithValue("@k" + i, reference.Negated ? -(long)wanted[i] : (object)wanted[i]);
            }

            int count = Convert.ToInt32(await command.ExecuteScalarAsync(token).ConfigureAwait(false));
            if (count > 0)
            {
                items.Add(new(reference, count));
            }
        }

        return items;
    }
}
