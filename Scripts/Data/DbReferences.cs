using ACWorldEdit.Foundation;

namespace ACWorldEdit.Data;

/// <summary>
/// The rows outside the spawn tables that name some spawn GUIDs (<see cref="SpawnEdits.References"/>).
/// <see cref="CountAsync"/> counts matches per reference, as the warning always has; <see cref="ReadAsync"/>
/// loads the rows and counts distinct rows, which is what a cleanup command will delete. Both share one
/// predicate builder, so they cannot drift apart.
/// </summary>
internal static class DbReferences
{
    const int MaxGuids = 500;

    /// <summary>Matches per reference: the number the delete warning reports.</summary>
    public static async Task<IReadOnlyList<SpawnImpactItem>> CountAsync(WorldDatabase database, WorldDialect dialect, SpawnKind kind, IReadOnlyList<ulong> guids, CancellationToken token)
    {
        var wanted = guids.Distinct().Take(MaxGuids).ToArray();
        var counts = new List<SpawnImpactItem>();
        if (wanted.Length == 0)
        {
            return counts;
        }

        await using var session = await database.OpenAsync(token).ConfigureAwait(false);
        foreach (var reference in SpawnEdits.References(dialect, kind))
        {
            if (!session.Has($"{reference.Table}.{reference.Column}"))
            {
                continue;
            }

            var (where, values) = Match(reference, wanted);
            int count = await CountOneAsync(session, reference.Table, where, values, token).ConfigureAwait(false);
            if (count > 0)
            {
                counts.Add(new(reference, count));
            }
        }

        return counts;
    }

    /// <summary>
    /// The rows themselves, counted once each: what a delete can remove with the spawns. A table the
    /// source cannot edit is skipped, and a read that hits <see cref="DbLimits.MaxRows"/> offers no
    /// cleanup at all rather than a partial one.
    /// </summary>
    public static async Task<SpawnReferences> ReadAsync(WorldDatabase database, ITableCatalog tables, WorldDialect dialect, SpawnKind kind, IReadOnlyList<ulong> guids, CancellationToken token)
    {
        var wanted = guids.Distinct().Take(MaxGuids).ToArray();
        var rows = new RowSet();
        if (wanted.Length == 0)
        {
            return new([], rows);
        }

        var counts = new List<SpawnImpactItem>();
        bool capped = false;
        await using var session = await database.OpenAsync(token).ConfigureAwait(false);
        foreach (var reference in SpawnEdits.References(dialect, kind))
        {
            // ReferenceCatalogTests keeps every reference table in the catalog; a table the source
            // cannot edit is skipped rather than throwing, as any uneditable table is.
            if (!session.Has($"{reference.Table}.{reference.Column}")
                || tables.All.FirstOrDefault(t => t.Name == reference.Table) is not { } table)
            {
                continue;
            }

            var (where, values) = Match(reference, wanted);
            var before = rows.All(table).Select(table.KeyOf).ToHashSet();
            await AcInteractionSource.Load(session, rows, table, where, values.Select(v => (object)v.Value).ToArray(), token).ConfigureAwait(false);
            int count = rows.All(table).Count(row => !before.Contains(table.KeyOf(row)));
            // Load stops at DbLimits.MaxRows. A reference that fills that window is not removable in
            // one command, so no cleanup is offered at all rather than a partial one.
            capped |= count >= DbLimits.MaxRows;
            if (count > 0)
            {
                counts.Add(new(reference, count));
            }
        }

        return capped
            ? new(await CountAsync(database, dialect, kind, wanted, token).ConfigureAwait(false), new RowSet())
            : new(counts, rows);
    }

    /// <summary>One reference's match count; the one query shape both readers use.</summary>
    internal static async Task<int> CountOneAsync(WorldReadSession session, string table, string where, (string Name, object Value)[] values, CancellationToken token)
    {
        await using var command = session.Command("");
        command.CommandText = $"SELECT COUNT(*) FROM `{table}` WHERE {where}";
        foreach (var (name, value) in values)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return Convert.ToInt32(await command.ExecuteScalarAsync(token).ConfigureAwait(false));
    }

    /// <summary>The WHERE clause and its parameters; names come from code, the GUIDs are parameters (negated where the table stores them so).</summary>
    static (string Where, (string Name, object Value)[] Values) Match(SpawnReference reference, ulong[] wanted)
    {
        string names = string.Join(',', wanted.Select((_, i) => "@k" + i));
        var values = wanted.Select((guid, i) => ("@k" + i, reference.Negated ? (object)-(long)guid : guid)).ToArray();
        string where = $"`{reference.Column}` IN ({names})" + (reference.Where == null ? "" : $" AND {reference.Where}");
        return (where, values);
    }
}
