using ACWorldEdit.Foundation;

namespace ACWorldEdit.Data;

/// <summary>
/// The formation rows touching some spawns: rows they lead plus rows they belong to. Bounded, so a
/// crowded region reads what it can.
/// </summary>
internal static class DbFormations
{
    const int MaxGuids = 500;

    public static async Task<RowSet> ReadAsync(WorldDatabase database, ITableCatalog tables, IReadOnlyList<ulong> guids, CancellationToken token)
    {
        var rows = new RowSet();
        var wanted = guids.Distinct().Take(MaxGuids).ToArray();
        if (wanted.Length == 0)
        {
            return rows;
        }

        await using var session = await database.OpenAsync(token).ConfigureAwait(false);
        if (!session.Has("creature_formations.memberGUID"))
        {
            return rows;
        }

        var table = tables.Find("creature_formations");
        string names = string.Join(',', wanted.Select((_, i) => "@k" + i));
        await AcInteractionSource.Load(session, rows, table,
            $"`leaderGUID` IN ({names}) OR `memberGUID` IN ({names})",
            [.. wanted.Cast<object>(), .. wanted.Cast<object>()], token).ConfigureAwait(false);
        return rows;
    }
}
