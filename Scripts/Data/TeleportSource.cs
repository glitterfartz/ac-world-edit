using ACWorldEdit.Foundation;

namespace ACWorldEdit.Data;

/// <summary>One game_tele row: a named server position (orientation in radians, as the server stores it).</summary>
public sealed record TeleportLocation(uint Id, string Name, ushort Map, double X, double Y, double Z, double Orientation);

/// <summary>
/// Read-only game_tele snapshot (the server's .tele command table). The table is small and
/// administrator-curated, so it is read whole, ordered by name; non-finite rows are dropped.
/// </summary>
public sealed class TeleportSource(DatabaseSettings settings)
{
    readonly WorldDatabase database = new(settings);

    /// <summary>A random location on any of the given maps, or null when the table has none there.</summary>
    public static TeleportLocation? Pick(IReadOnlyList<TeleportLocation> rows, IReadOnlySet<ushort> maps, Random random)
    {
        var usable = rows.Where(r => maps.Contains(r.Map)).ToArray();
        return usable.Length == 0 ? null : usable[random.Next(usable.Length)];
    }

    public async Task<TeleportLocation[]> ReadAsync(CancellationToken token)
    {
        await using var session = await database.OpenAsync(token).ConfigureAwait(false);
        session.Require("game_tele.id", "game_tele.position_x", "game_tele.position_y", "game_tele.position_z",
            "game_tele.orientation", "game_tele.map", "game_tele.name");
        await using var command = session.Command("SELECT id,position_x,position_y,position_z,orientation,map,name FROM game_tele ORDER BY name,id");
        List<TeleportLocation> rows = [];
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            var row = new TeleportLocation(reader.GetUInt32(0), reader.GetString(6), reader.GetUInt16(5),
                reader.GetDouble(1), reader.GetDouble(2), reader.GetDouble(3), reader.GetDouble(4));
            if (double.IsFinite(row.X) && double.IsFinite(row.Y) && double.IsFinite(row.Z) && double.IsFinite(row.Orientation))
            {
                rows.Add(row);
            }
        }

        return rows.ToArray();
    }
}
