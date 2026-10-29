using ACWorldEdit.Foundation;

namespace ACWorldEdit.Data;

/// <summary>
/// The full `creature` or `gameobject` rows of some spawns, with their addon rows, as the editable
/// catalog sees them: the baseline a spawn edit replaces. Overlay records carry only a pose, but a row
/// edit needs every column, and a delete or a copy needs the addon row too.
/// </summary>
internal static class DbSpawnRows
{
    const int MaxGuids = 500;

    public static async Task<RowSet> ReadAsync(WorldDatabase database, ITableCatalog tables, SpawnKind kind, IReadOnlyList<ulong> guids, CancellationToken token)
    {
        var rows = new RowSet();
        var wanted = guids.Distinct().Take(MaxGuids).ToArray();
        if (wanted.Length == 0)
        {
            return rows;
        }

        await using var session = await database.OpenAsync(token).ConfigureAwait(false);
        string predicate = $"`guid` IN ({string.Join(',', wanted.Select((_, i) => "@k" + i))})";
        foreach (string name in kind == SpawnKind.Creature ? (string[])["creature", "creature_addon"] : ["gameobject", "gameobject_addon"])
        {
            await AcInteractionSource.Load(session, rows, tables.Find(name), predicate, wanted.Cast<object>().ToArray(), token).ConfigureAwait(false);
        }

        return rows;
    }
}
