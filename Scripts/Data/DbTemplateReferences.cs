using ACWorldEdit.Foundation;

namespace ACWorldEdit.Data;

/// <summary>
/// Counts the rows elsewhere in the database that name a creature or GameObject template by entry. The
/// reference list is <see cref="TemplateReferences"/>; the entry form binds one named parameter rather than
/// a GUID list, so it does not go through <see cref="DbReferences.Match"/> (which is written for the
/// spawn-GUID form with two callers).
/// </summary>
internal static class DbTemplateReferences
{
    public static async Task<IReadOnlyList<SpawnImpactItem>> CountAsync(WorldDatabase database, WorldDialect dialect, SpawnKind kind, uint entry, CancellationToken token)
    {
        var counts = new List<SpawnImpactItem>();
        await using var session = await database.OpenAsync(token).ConfigureAwait(false);
        foreach (var reference in TemplateReferences.For(kind))
        {
            // AzerothCore's creature table carries the entry as id1, as every other read of it probes;
            // TrinityCore keeps `id`. `Has` only knows the tables the session's schema read covers, so a
            // reference outside that list is skipped rather than throwing.
            string column = dialect == WorldDialect.AzerothCore && reference.Table == "creature" && session.Has("creature.id1")
                ? "id1"
                : reference.Column;
            if (!session.Has($"{reference.Table}.{column}"))
            {
                continue;
            }

            await using var command = session.Command("");
            command.CommandText = $"SELECT COUNT(*) FROM `{reference.Table}` WHERE `{column}` = @entry"
                + (reference.Where == null ? "" : $" AND {reference.Where}");
            command.Parameters.AddWithValue("@entry", entry);
            int count = Convert.ToInt32(await command.ExecuteScalarAsync(token).ConfigureAwait(false));
            if (count > 0)
            {
                counts.Add(new(reference, count));
            }
        }

        return counts;
    }
}
