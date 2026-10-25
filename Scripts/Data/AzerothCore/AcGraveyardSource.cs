using ACWorldEdit.Foundation;

namespace ACWorldEdit.Data;

/// <summary>
/// Read-only game_graveyard/graveyard_zone snapshots (server GameGraveyard.cpp tables). Rows are
/// limited to the streamed region, ordered nearest-first and bounded; links and healers are fetched
/// only for the returned IDs inside the same read-only transaction.
/// </summary>
public sealed class AcGraveyardSource(WorldDatabase database)
{
    public AcGraveyardSource(DatabaseSettings settings) : this(new WorldDatabase(settings))
    {
    }

    public const int Limit = 128;
    const uint SpiritHealerFlag = 0x4000;

    public async Task<GraveyardRegionResult> ReadAsync(CreatureRegion region, SpawnQueryPoint? point, string search, CancellationToken token)
    {
        await using var session = await database.OpenAsync(token).ConfigureAwait(false);
        session.Require("game_graveyard.ID", "game_graveyard.Map", "game_graveyard.x", "game_graveyard.y", "game_graveyard.z", "game_graveyard.Comment",
            "graveyard_zone.ID", "graveyard_zone.GhostZone", "graveyard_zone.Faction", "graveyard_zone.Comment");
        var records = await ReadGraveyards(session, region, point, search, token).ConfigureAwait(false);
        bool truncated = records.Count > Limit;
        records = records.Take(Limit).ToList();
        if (records.Count == 0)
        {
            return new([], false, database.SourceKey);
        }

        var links = await ReadLinks(session, records.Select(r => r.Id), token).ConfigureAwait(false);
        var healers = await ReadHealers(session, region.Map, records, token).ConfigureAwait(false);
        var sites = records.Select(r => new GraveyardSite(
            (r with { Links = links.TryGetValue(r.Id, out var list) ? list.ToArray() : [] }).Normalized(),
            healers.TryGetValue(r.Id, out var near) ? near.ToArray() : [])).ToArray();
        return new(sites, truncated, database.SourceKey);
    }

    static async Task<List<GraveyardRecord>> ReadGraveyards(WorldReadSession session, CreatureRegion region, SpawnQueryPoint? point, string search, CancellationToken token)
    {
        await using var command = session.Command("");
        WorldReadSession.AddRegion(command, region, point, Limit + 1);
        string filter = "1=1";
        if (WorldReadSession.AddSearch(command, search))
        {
            filter = "(g.Comment LIKE @search ESCAPE '=' OR g.ID=@searchId)";
        }

        command.CommandText = $"""
            SELECT g.ID,g.Map,g.x,g.y,g.z,COALESCE(g.Comment,'')
            FROM game_graveyard g
            WHERE g.Map=@map AND g.x>@minX AND g.x<=@maxX AND g.y>@minY AND g.y<=@maxY AND {filter}
            ORDER BY (POW(g.x-@x,2)+POW(g.y-@y,2)+POW(g.z-@z,2)),g.ID LIMIT @limit
            """;
        List<GraveyardRecord> records = [];
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            int map = reader.GetInt32(1);
            if (map is < 0 or > ushort.MaxValue)
            {
                continue;
            }

            var record = new GraveyardRecord(checked((uint)reader.GetInt32(0)), (ushort)map, reader.GetDouble(2), reader.GetDouble(3), reader.GetDouble(4), reader.GetString(5), []);
            // Rows the server could not use either (non-finite) are left out rather than drawn.
            if (double.IsFinite(record.X) && double.IsFinite(record.Y) && double.IsFinite(record.Z))
            {
                records.Add(record);
            }
        }

        return records;
    }

    static async Task<Dictionary<uint, List<GraveyardLink>>> ReadLinks(WorldReadSession session, IEnumerable<uint> ids, CancellationToken token)
    {
        await using var command = session.Command("");
        string list = WorldReadSession.InList(command, ids, "id");
        command.CommandText = $"SELECT ID,GhostZone,Faction,COALESCE(Comment,'') FROM graveyard_zone WHERE ID IN ({list}) ORDER BY ID,GhostZone";
        var links = new Dictionary<uint, List<GraveyardLink>>();
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            uint id = reader.GetUInt32(0);
            if (!links.TryGetValue(id, out var rows))
            {
                links[id] = rows = [];
            }

            rows.Add(new(reader.GetUInt32(1), reader.GetUInt16(2), reader.GetString(3)));
        }

        return links;
    }

    /// <summary>
    /// Spirit healers within DbLimits.HealerRadius of each returned graveyard, as read-only context.
    /// </summary>
    internal static async Task<Dictionary<uint, List<SpiritHealer>>> ReadHealers(WorldReadSession session, ushort map, List<GraveyardRecord> records, CancellationToken token)
    {
        var result = new Dictionary<uint, List<SpiritHealer>>();
        if (!session.Has("creature_template.npcflag"))
        {
            return result;
        }

        string id = session.Has("creature.id1") ? "c.id1" : "c.id";
        await using var command = session.Command("");
        command.Parameters.AddWithValue("@map", map);
        command.Parameters.AddWithValue("@flag", SpiritHealerFlag);
        command.Parameters.AddWithValue("@minX", records.Min(r => r.X) - DbLimits.HealerRadius);
        command.Parameters.AddWithValue("@maxX", records.Max(r => r.X) + DbLimits.HealerRadius);
        command.Parameters.AddWithValue("@minY", records.Min(r => r.Y) - DbLimits.HealerRadius);
        command.Parameters.AddWithValue("@maxY", records.Max(r => r.Y) + DbLimits.HealerRadius);
        command.CommandText = $"""
            SELECT c.guid,{id},COALESCE(t.name,''),c.position_x,c.position_y,c.position_z
            FROM creature c JOIN creature_template t ON t.entry={id}
            WHERE c.map=@map AND (t.npcflag & @flag)<>0
              AND c.position_x BETWEEN @minX AND @maxX AND c.position_y BETWEEN @minY AND @maxY
            LIMIT 512
            """;
        List<SpiritHealer> healers = [];
        await using (var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                healers.Add(new(Convert.ToUInt64(reader.GetValue(0)), reader.GetUInt32(1), reader.GetString(2), reader.GetDouble(3), reader.GetDouble(4), reader.GetDouble(5)));
            }
        }

        foreach (var record in records)
        {
            var near = healers.Where(h => Math.Pow(h.X - record.X, 2) + Math.Pow(h.Y - record.Y, 2) <= DbLimits.HealerRadius * DbLimits.HealerRadius).ToList();
            if (near.Count > 0)
            {
                result[record.Id] = near;
            }
        }

        return result;
    }
}
