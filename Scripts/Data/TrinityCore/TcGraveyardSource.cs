using ACWorldEdit.Foundation;

namespace ACWorldEdit.Data;

/// <summary>
/// Read-only TrinityCore graveyards. A graveyard is a `world_safe_locs` row that `graveyard_zone`
/// links to at least one ghost zone (the table is shared with area trigger teleports and battleground
/// start points, which are not graveyards). TrinityCore dropped graveyard_zone.Faction: a zone link
/// belongs to a team when `conditions` source type 27 (SourceGroup = ghost zone, SourceEntry =
/// graveyard) carries a CONDITION_TEAM row, with the team in ConditionValue1 (469 Alliance, 67 Horde).
/// </summary>
public sealed class TcGraveyardSource(WorldDatabase database)
{
    public const int Limit = AcGraveyardSource.Limit;
    const int GraveyardCondition = TcSqlDialect.GraveyardCondition, TeamCondition = TcSqlDialect.TeamCondition;

    public async Task<GraveyardRegionResult> ReadAsync(CreatureRegion region, SpawnQueryPoint? point, string search, CancellationToken token)
    {
        await using var session = await database.OpenAsync(token).ConfigureAwait(false);
        session.Require("world_safe_locs.ID", "world_safe_locs.MapID", "world_safe_locs.LocX", "world_safe_locs.LocY", "world_safe_locs.LocZ", "world_safe_locs.Comment",
            "graveyard_zone.ID", "graveyard_zone.GhostZone", "graveyard_zone.Comment");
        var records = await ReadGraveyards(session, region, point, search, token).ConfigureAwait(false);
        bool truncated = records.Count > Limit;
        records = records.Take(Limit).ToList();
        if (records.Count == 0)
        {
            return new([], false, database.SourceKey);
        }

        var (links, locked) = await ReadLinks(session, records.Select(r => r.Id), token).ConfigureAwait(false);
        var teleports = await ReadTeleportUses(session, records.Select(r => r.Id), token).ConfigureAwait(false);
        var healers = await AcGraveyardSource.ReadHealers(session, region.Map, records, token).ConfigureAwait(false);
        var sites = records.Select(r => new GraveyardSite(
            (r with { Links = links.TryGetValue(r.Id, out var list) ? list.ToArray() : [] }).Normalized(),
            healers.TryGetValue(r.Id, out var near) ? near.ToArray() : [],
            locked.TryGetValue(r.Id, out var zones) ? zones.Order().ToArray() : [],
            teleports.GetValueOrDefault(r.Id))).ToArray();
        return new(sites, truncated, database.SourceKey);
    }

    /// <summary>How many area trigger teleports land on each of these world_safe_locs rows.</summary>
    static async Task<Dictionary<uint, int>> ReadTeleportUses(WorldReadSession session, IEnumerable<uint> ids, CancellationToken token)
    {
        var uses = new Dictionary<uint, int>();
        if (!session.Has("areatrigger_teleport.PortLocID"))
        {
            return uses;
        }

        await using var command = session.Command("");
        string list = WorldReadSession.InList(command, ids, "id");
        command.CommandText = $"SELECT PortLocID,COUNT(*) FROM areatrigger_teleport WHERE PortLocID IN ({list}) GROUP BY PortLocID";
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            uses[Convert.ToUInt32(reader.GetValue(0))] = Convert.ToInt32(reader.GetValue(1));
        }

        return uses;
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
            SELECT g.ID,g.MapID,g.LocX,g.LocY,g.LocZ,COALESCE(g.Comment,'')
            FROM world_safe_locs g
            WHERE g.MapID=@map AND g.LocX>@minX AND g.LocX<=@maxX AND g.LocY>@minY AND g.LocY<=@maxY AND {filter}
              AND EXISTS(SELECT 1 FROM graveyard_zone z WHERE z.ID=g.ID)
            ORDER BY (POW(g.LocX-@x,2)+POW(g.LocY-@y,2)+POW(g.LocZ-@z,2)),g.ID LIMIT @limit
            """;
        List<GraveyardRecord> records = [];
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            uint map = reader.GetUInt32(1);
            if (map > ushort.MaxValue)
            {
                continue;
            }

            var record = new GraveyardRecord(reader.GetUInt32(0), (ushort)map, reader.GetDouble(2), reader.GetDouble(3), reader.GetDouble(4), reader.GetString(5), []);
            // Rows the server could not use either (non-finite) are left out rather than drawn.
            if (double.IsFinite(record.X) && double.IsFinite(record.Y) && double.IsFinite(record.Z))
            {
                records.Add(record);
            }
        }

        return records;
    }

    /// <summary>
    /// Zone links with their team: the link row supplies the zone and comment, the team condition (if
    /// any) the faction. A zone with no team condition is neutral. A zone whose conditions are anything
    /// but none or one plain team row (see <see cref="TcSqlDialect.Graveyard"/>) is locked; it still
    /// shows the first non-negated team it names.
    /// </summary>
    static async Task<(Dictionary<uint, List<GraveyardLink>> Links, Dictionary<uint, List<uint>> Locked)> ReadLinks(WorldReadSession session, IEnumerable<uint> ids, CancellationToken token)
    {
        var teams = new Dictionary<(uint Graveyard, uint Zone), ushort>();
        var counts = new Dictionary<(uint Graveyard, uint Zone), (int Rows, bool Plain)>();
        var graveyards = ids.ToArray();
        if (session.Has("conditions.SourceTypeOrReferenceId") && session.Has("conditions.ConditionValue1"))
        {
            await using var conditions = session.Command("");
            string conditionList = WorldReadSession.InList(conditions, graveyards, "id");
            string text = session.Has("conditions.ConditionStringValue1") ? "ConditionStringValue1" : "''";
            conditions.CommandText = $"""
                SELECT SourceEntry,SourceGroup,ConditionTypeOrReference,ConditionValue1,NegativeCondition,
                       (SourceId=0 AND ElseGroup=0 AND ConditionTarget=0 AND ConditionValue2=0 AND ConditionValue3=0 AND {text}='')
                FROM conditions
                WHERE SourceTypeOrReferenceId={GraveyardCondition} AND SourceEntry IN ({conditionList})
                ORDER BY SourceEntry,SourceGroup,ConditionValue1
                """;
            await using var teamReader = await conditions.ExecuteReaderAsync(token).ConfigureAwait(false);
            while (await teamReader.ReadAsync(token).ConfigureAwait(false))
            {
                var link = (Convert.ToUInt32(teamReader.GetValue(0)), Convert.ToUInt32(teamReader.GetValue(1)));
                long type = Convert.ToInt64(teamReader.GetValue(2)), team = Convert.ToInt64(teamReader.GetValue(3));
                bool negated = Convert.ToInt64(teamReader.GetValue(4)) != 0, plain = Convert.ToInt64(teamReader.GetValue(5)) != 0;
                bool isTeam = type == TeamCondition && !negated && team is GraveyardLink.Alliance or GraveyardLink.Horde;
                var (rows, _) = counts.GetValueOrDefault(link);
                counts[link] = (rows + 1, isTeam && plain);
                if (isTeam)
                {
                    teams.TryAdd(link, (ushort)team);
                }
            }
        }

        var locked = new Dictionary<uint, List<uint>>();
        foreach (var ((graveyard, zone), (rows, plain)) in counts)
        {
            if (rows > 1 || !plain)
            {
                if (!locked.TryGetValue(graveyard, out var zones))
                {
                    locked[graveyard] = zones = [];
                }

                zones.Add(zone);
            }
        }

        await using var command = session.Command("");
        string list = WorldReadSession.InList(command, graveyards, "id");
        command.CommandText = $"SELECT ID,GhostZone,COALESCE(Comment,'') FROM graveyard_zone WHERE ID IN ({list}) ORDER BY ID,GhostZone";
        var links = new Dictionary<uint, List<GraveyardLink>>();
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            uint id = reader.GetUInt32(0), zone = reader.GetUInt32(1);
            if (!links.TryGetValue(id, out var rows))
            {
                links[id] = rows = [];
            }

            rows.Add(new(zone, teams.GetValueOrDefault((id, zone)), reader.GetString(2)));
        }

        return (links, locked);
    }
}
