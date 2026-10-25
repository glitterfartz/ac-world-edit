using ACWorldEdit.Assets;
using ACWorldEdit.Foundation;

namespace ACWorldEdit.Data;

/// <summary>
/// Read-only client area triggers with their TrinityCore server links. The server has no copy of the
/// trigger volumes: positions and shapes come from the client's AreaTrigger.db2, overlaid by the
/// `hotfixes.area_trigger` rows (same ID replaces the client row). What a trigger does comes from the
/// world database, keyed by the DB2 ID: areatrigger_scripts, areatrigger_teleport (PortLocID names a
/// world_safe_locs row), areatrigger_involvedrelation, areatrigger_tavern and smart_scripts source
/// type 2. TrinityCore's own `areatrigger` table holds server-spawned AreaTrigger entities, a different
/// thing, and is not read here. Triggers with no sphere or box extent (polygons) cannot be drawn and are skipped.
/// </summary>
public sealed class TcAreaTriggerSource(WorldDatabase database, IClientLookup? lookup, TcHotfixSource hotfixes)
{
    public const int Limit = AcAreaTriggerSource.Limit;

    public async Task<AreaTriggerRegionResult> ReadAsync(CreatureRegion region, SpawnQueryPoint? point, string search, CancellationToken token)
    {
        if (lookup == null)
        {
            throw new InvalidDataException("AreaTriggers come from the client's AreaTrigger.db2, which needs the retail client data.");
        }

        var client = lookup.AreaTriggers().ToDictionary(t => t.Id);
        foreach (var row in await hotfixes.ReadAreaTriggersAsync(token).ConfigureAwait(false))
        {
            client[row.Id] = row;
        }

        await using var session = await database.OpenAsync(token).ConfigureAwait(false);
        session.Require("areatrigger_scripts.entry", "areatrigger_scripts.ScriptName", "smart_scripts.entryorguid", "smart_scripts.source_type");
        var named = search.Trim().Length > 0 ? await MatchingIds(session, search, token).ConfigureAwait(false) : null;
        double x = point?.X ?? (region.MinServerX + region.MaxServerX) / 2, y = point?.Y ?? (region.MinServerY + region.MaxServerY) / 2, z = point?.Z ?? 0;
        var inside = client.Values
            .Where(t => t.Map == region.Map && t.X > region.MinServerX && t.X <= region.MaxServerX && t.Y > region.MinServerY && t.Y <= region.MaxServerY)
            .Where(t => t.Radius > 0 || (t.BoxLength > 0 && t.BoxWidth > 0 && t.BoxHeight > 0))
            .Where(t => new[] { t.X, t.Y, t.Z, t.Radius, t.BoxLength, t.BoxWidth, t.BoxHeight, t.BoxYaw }.All(float.IsFinite))
            .Where(t => named == null || named.Contains(t.Id) || (uint.TryParse(search.Trim(), out uint id) && id == t.Id))
            .OrderBy(t => (t.X - x) * (t.X - x) + (t.Y - y) * (t.Y - y) + (t.Z - z) * (t.Z - z)).ThenBy(t => t.Id)
            .Take(Limit + 1)
            .ToList();
        bool truncated = inside.Count > Limit;
        inside = inside.Take(Limit).ToList();
        if (inside.Count == 0)
        {
            return new([], false, database.SourceKey);
        }

        var links = await ReadLinks(session, inside.Select(t => t.Id), token).ConfigureAwait(false);
        var triggers = inside.Select(t =>
        {
            links.TryGetValue(t.Id, out var link);
            link ??= new();
            return new AreaTriggerRecord(t.Id, (ushort)t.Map, t.X, t.Y, t.Z, t.Radius, t.BoxLength, t.BoxWidth, t.BoxHeight, t.BoxYaw,
                link.Script, link.Teleport, link.Quest, link.QuestTitle, link.Tavern, link.Smart);
        }).Where(t => t.Map <= ushort.MaxValue).ToArray();
        return new(triggers, truncated, database.SourceKey);
    }

    sealed class Links
    {
        public string Script = "", QuestTitle = "", Tavern = "";
        public AreaTriggerTeleport? Teleport;
        public uint Quest;
        public int Smart;
    }

    /// <summary>IDs whose script, teleport or tavern name contains the term (the numeric ID match is applied by the caller).</summary>
    static async Task<HashSet<uint>> MatchingIds(WorldReadSession session, string search, CancellationToken token)
    {
        await using var command = session.Command("");
        WorldReadSession.AddSearch(command, search);
        command.CommandText = """
            SELECT entry FROM areatrigger_scripts WHERE ScriptName LIKE @search ESCAPE '='
            UNION SELECT ID FROM areatrigger_teleport WHERE Name LIKE @search ESCAPE '='
            UNION SELECT id FROM areatrigger_tavern WHERE name LIKE @search ESCAPE '='
            LIMIT 2000
            """;
        var ids = new HashSet<uint>();
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            ids.Add(Convert.ToUInt32(reader.GetValue(0)));
        }

        return ids;
    }

    static async Task<Dictionary<uint, Links>> ReadLinks(WorldReadSession session, IEnumerable<uint> ids, CancellationToken token)
    {
        var all = ids.ToArray();
        var links = new Dictionary<uint, Links>();
        Links Of(uint id) => links.TryGetValue(id, out var link) ? link : links[id] = new();

        async Task Read(string sql, Action<System.Data.Common.DbDataReader> each)
        {
            await using var command = session.Command("");
            string list = WorldReadSession.InList(command, all, "id");
            command.CommandText = sql.Replace("{ids}", list, StringComparison.Ordinal);
            await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                each(reader);
            }
        }

        await Read("SELECT entry,ScriptName FROM areatrigger_scripts WHERE entry IN ({ids})", r => Of(Convert.ToUInt32(r.GetValue(0))).Script = r.GetString(1));
        if (session.Has("areatrigger_teleport.PortLocID") && session.Has("world_safe_locs.LocX"))
        {
            await Read("""
                SELECT t.ID,COALESCE(t.Name,''),w.MapID,w.LocX,w.LocY,w.LocZ,w.Facing
                FROM areatrigger_teleport t JOIN world_safe_locs w ON w.ID=t.PortLocID WHERE t.ID IN ({ids})
                """, r =>
            {
                uint map = Convert.ToUInt32(r.GetValue(2));
                if (map <= ushort.MaxValue)
                {
                    Of(Convert.ToUInt32(r.GetValue(0))).Teleport = new(r.GetString(1), (ushort)map, r.GetDouble(3), r.GetDouble(4), r.GetDouble(5), r.GetDouble(6));
                }
            });
        }

        if (session.Has("areatrigger_involvedrelation.quest"))
        {
            string title = session.Has("quest_template.LogTitle") ? "COALESCE(qt.LogTitle,'')" : "''";
            string join = session.Has("quest_template.ID") ? "LEFT JOIN quest_template qt ON qt.ID=q.quest" : "";
            await Read($"SELECT q.id,q.quest,{title} FROM areatrigger_involvedrelation q {join} WHERE q.id IN ({{ids}})", r =>
            {
                var link = Of(Convert.ToUInt32(r.GetValue(0)));
                link.Quest = Convert.ToUInt32(r.GetValue(1));
                link.QuestTitle = r.GetString(2);
            });
        }

        if (session.Has("areatrigger_tavern.name"))
        {
            await Read("SELECT id,COALESCE(name,'') FROM areatrigger_tavern WHERE id IN ({ids})", r => Of(Convert.ToUInt32(r.GetValue(0))).Tavern = r.GetString(1));
        }

        await Read("SELECT entryorguid,COUNT(*) FROM smart_scripts WHERE source_type=2 AND entryorguid IN ({ids}) GROUP BY entryorguid", r => Of(Convert.ToUInt32(r.GetValue(0))).Smart = Convert.ToInt32(r.GetValue(1)));
        return links;
    }
}
