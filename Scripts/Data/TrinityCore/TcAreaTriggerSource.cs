using ACWorldEdit.Assets;
using ACWorldEdit.Foundation;

namespace ACWorldEdit.Data;

/// <summary>
/// Read-only AreaTriggers for TrinityCore, of two kinds.
/// Client triggers: the server has no copy of their volumes, so positions and shapes come from the
/// client's AreaTrigger.db2, overlaid by the `hotfixes.area_trigger` rows (same ID replaces the client
/// row); polygons take their corners from the client's Path tables. What a trigger does comes from the
/// world database, keyed by the DB2 ID: areatrigger_scripts, areatrigger_teleport (PortLocID names a
/// world_safe_locs row), areatrigger_involvedrelation, areatrigger_tavern and smart_scripts source
/// type 2. Only the shapes Player::IsInAreaTrigger tests (sphere, box, polygon, cylinder) are kept; the
/// server never fires the others.
/// Server spawns: TrinityCore's own `areatrigger` table places AreaTrigger entities, shaped by their
/// areatrigger_create_properties (and its polygon vertices) and owned by an areatrigger_template whose
/// SmartAI rules are source type 11 (12 for custom templates).
/// </summary>
public sealed class TcAreaTriggerSource(WorldDatabase database, IClientLookup? lookup, TcHotfixSource hotfixes)
{
    public const int Limit = AcAreaTriggerSource.Limit;

    public async Task<AreaTriggerRegionResult> ReadAsync(CreatureRegion region, SpawnQueryPoint? point, string search, CancellationToken token)
    {
        if (lookup == null)
        {
            throw new DatabaseFeatureException("AreaTriggers come from the client's AreaTrigger.db2, which needs the retail client data.");
        }

        var client = await ClientTriggersAsync(lookup, token).ConfigureAwait(false);
        await using var session = await database.OpenAsync(token).ConfigureAwait(false);
        session.Require("areatrigger_scripts.entry", "areatrigger_scripts.ScriptName", "smart_scripts.entryorguid", "smart_scripts.source_type");
        var named = search.Trim().Length > 0 ? await MatchingIds(session, search, token).ConfigureAwait(false) : null;
        double x = point?.X ?? (region.MinServerX + region.MaxServerX) / 2, y = point?.Y ?? (region.MinServerY + region.MaxServerY) / 2, z = point?.Z ?? 0;
        double Distance(double px, double py, double pz) => (px - x) * (px - x) + (py - y) * (py - y) + (pz - z) * (pz - z);
        // Shaped before the region test: a polygon is where its corners are, not at its position.
        var inside = client.Values
            .Where(t => t.Map == region.Map)
            .Where(t => new[] { t.X, t.Y, t.Z, t.Radius, t.BoxLength, t.BoxWidth, t.BoxHeight, t.BoxYaw }.All(float.IsFinite))
            .Where(t => named == null || named.Contains(t.Id) || (uint.TryParse(search.Trim(), out uint id) && id == t.Id))
            .Select(t => (Row: t, Shape: Shape(t, lookup)))
            .Where(t => t.Shape is { Focus: var f } && f.X > region.MinServerX && f.X <= region.MaxServerX && f.Y > region.MinServerY && f.Y <= region.MaxServerY)
            .OrderBy(t => Distance(t.Shape!.Focus.X, t.Shape.Focus.Y, t.Shape.Focus.Z)).ThenBy(t => t.Row.Id)
            .Take(Limit + 1)
            .ToList();
        bool truncated = inside.Count > Limit;
        inside = inside.Take(Limit).ToList();

        var links = inside.Count == 0 ? new Dictionary<uint, Links>() : await ReadLinks(session, inside.Select(t => t.Row.Id), token).ConfigureAwait(false);
        var triggers = inside.Where(t => t.Row.Map <= ushort.MaxValue).Select(t =>
        {
            links.TryGetValue(t.Row.Id, out var link);
            link ??= new();
            return t.Shape! with { ScriptName = link.Script, Teleport = link.Teleport, QuestId = link.Quest, QuestTitle = link.QuestTitle, Tavern = link.Tavern, SmartRows = link.Smart };
        }).ToList();

        var (spawns, spawnsTruncated) = await ReadSpawns(session, region, point, search, token).ConfigureAwait(false);
        triggers.AddRange(spawns);
        triggers.Sort((a, b) => Distance(a.Focus.X, a.Focus.Y, a.Focus.Z).CompareTo(Distance(b.Focus.X, b.Focus.Y, b.Focus.Z)));
        return new(triggers.ToArray(), truncated || spawnsTruncated, database.SourceKey);
    }

    /// <summary>The client's AreaTrigger.db2 rows with the hotfix overrides applied.</summary>
    async Task<Dictionary<uint, ClientAreaTrigger>> ClientTriggersAsync(IClientLookup client, CancellationToken token)
    {
        var rows = client.AreaTriggers().ToDictionary(t => t.Id);
        foreach (var row in await ReadHotfixes(token).ConfigureAwait(false))
        {
            rows[row.Id] = row;
        }

        return rows;
    }

    /// <summary>Whether a client trigger with this ID exists (client or hotfix row), as SmartAIMgr checks for source type 2.</summary>
    public async Task<bool> ClientTriggerExistsAsync(uint id, CancellationToken token)
    {
        if (lookup == null)
        {
            throw new DatabaseFeatureException("AreaTrigger rules are keyed by the client's AreaTrigger.db2, which needs the retail client data.");
        }

        return (await ClientTriggersAsync(lookup, token).ConfigureAwait(false)).ContainsKey(id);
    }

    /// <summary>
    /// The client row as the server tests it (Player::IsInAreaTrigger), or null for a shape the server
    /// never fires or one without extent. A polygon has no floor and ends VolumeHeight above the
    /// trigger position (no ceiling when unset); a cylinder stands BoxHeight tall on it. BoxYaw turns
    /// only boxes. Polygons keep their corners' height as Ground, for drawing and region tests.
    /// </summary>
    static AreaTriggerRecord? Shape(ClientAreaTrigger t, IClientLookup client)
    {
        var record = new AreaTriggerRecord(t.Id, (ushort)Math.Min(t.Map, ushort.MaxValue), t.X, t.Y, t.Z, t.Radius, t.BoxLength, t.BoxWidth, t.BoxHeight, t.BoxYaw);
        switch ((AreaTriggerShape)t.ShapeType)
        {
            case AreaTriggerShape.Sphere when t.Radius > 0:
                return record with { Volume = AreaTriggerShape.Sphere };
            case AreaTriggerShape.Box when t.BoxLength > 0 && t.BoxWidth > 0 && t.BoxHeight > 0:
                return record with { Volume = AreaTriggerShape.Box };
            case AreaTriggerShape.Cylinder when t.Radius > 0 && t.BoxHeight > 0:
                return record with { Volume = AreaTriggerShape.Cylinder, Bottom = 0, Top = t.BoxHeight };
            case AreaTriggerShape.Polygon when client.Polygon(t.ShapeId) is { } polygon:
                return record with
                {
                    Volume = AreaTriggerShape.Polygon,
                    Vertices = polygon.Vertices.Select(v => ((double)v.X, (double)v.Y)).ToArray(),
                    Bottom = double.NegativeInfinity,
                    Top = polygon.Height ?? double.PositiveInfinity,
                    Ground = polygon.Ground
                };
            default:
                return null;
        }
    }

    /// <summary>
    /// The hotfix overrides, or none when the hotfixes database cannot be read (missing table,
    /// unreachable server): the client rows are what a client without those hotfixes would use.
    /// </summary>
    async Task<IReadOnlyList<ClientAreaTrigger>> ReadHotfixes(CancellationToken token)
    {
        try
        {
            return await hotfixes.ReadAreaTriggersAsync(token).ConfigureAwait(false);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            return [];
        }
    }

    /// <summary>
    /// Server-spawned AreaTriggers in the region, nearest first, with their create properties' shape
    /// (AreaTriggerDataStore::LoadAreaTriggerSpawns skips spawns whose properties are missing) and the
    /// SmartAI rule count of their template. A spawn's ScriptName overrides its properties' one
    /// (AreaTrigger::GetScriptId). Shapes turn with the spawn's orientation; the properties' roll,
    /// pitch and yaw are not applied.
    /// </summary>
    static async Task<(List<AreaTriggerRecord> Spawns, bool Truncated)> ReadSpawns(WorldReadSession session, CreatureRegion region, SpawnQueryPoint? point, string search, CancellationToken token)
    {
        if (!session.Has("areatrigger.SpawnId") || !session.Has("areatrigger_create_properties.Shape"))
        {
            return ([], false);
        }

        await using var command = session.Command("");
        WorldReadSession.AddRegion(command, region, point, Limit + 1);
        bool filter = WorldReadSession.AddSearch(command, search);
        string matching = filter ? "AND (a.SpawnId=@searchId OR p.AreaTriggerId=@searchId OR a.ScriptName LIKE @search ESCAPE '=' OR p.ScriptName LIKE @search ESCAPE '=' OR a.Comment LIKE @search ESCAPE '=')" : "";
        command.CommandText = $"""
            SELECT a.SpawnId,a.AreaTriggerCreatePropertiesId,a.IsCustom,a.PosX,a.PosY,a.PosZ,a.Orientation,COALESCE(a.ScriptName,''),COALESCE(a.Comment,''),
                   p.AreaTriggerId,p.IsAreatriggerCustom,p.Shape,p.ShapeData0,p.ShapeData1,p.ShapeData2,p.ShapeData3,p.ShapeData4,p.ShapeData5,p.ShapeData6,p.ShapeData7,COALESCE(p.ScriptName,'')
            FROM areatrigger a JOIN areatrigger_create_properties p ON p.Id=a.AreaTriggerCreatePropertiesId AND p.IsCustom=a.IsCustom
            WHERE a.MapId=@map AND a.PosX>@minX AND a.PosX<=@maxX AND a.PosY>@minY AND a.PosY<=@maxY {matching}
            ORDER BY POW(a.PosX-@x,2)+POW(a.PosY-@y,2)+POW(a.PosZ-@z,2), a.SpawnId
            LIMIT @limit
            """;
        var spawns = new List<(AreaTriggerRecord Record, uint Properties, bool Custom)>();
        await using (var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                float[] data = Enumerable.Range(12, 8).Select(i => reader.IsDBNull(i) ? 0f : reader.GetFloat(i)).ToArray();
                string own = reader.GetString(7), properties = reader.GetString(20);
                var record = new AreaTriggerRecord(Convert.ToUInt32(reader.GetValue(9)), (ushort)region.Map, reader.GetFloat(3), reader.GetFloat(4), reader.GetFloat(5), 0, 0, 0, 0, reader.GetFloat(6),
                    own.Length > 0 ? own : properties)
                {
                    SpawnId = Convert.ToUInt64(reader.GetValue(0)),
                    CreatePropertiesId = Convert.ToUInt32(reader.GetValue(1)),
                    IsCustom = Convert.ToInt32(reader.GetValue(10)) != 0,
                    Comment = reader.GetString(8)
                };
                if (SpawnShape(record, (AreaTriggerShape)Convert.ToByte(reader.GetValue(11)), data) is { } shaped)
                {
                    spawns.Add((shaped, shaped.CreatePropertiesId, Convert.ToInt32(reader.GetValue(2)) != 0));
                }
            }
        }

        bool truncated = spawns.Count > Limit;
        spawns = spawns.Take(Limit).ToList();
        if (spawns.Count == 0)
        {
            return ([], truncated);
        }

        var vertices = await ReadVertices(session, spawns.Where(s => s.Record.Volume == AreaTriggerShape.Polygon).Select(s => (s.Properties, s.Custom)).Distinct().ToArray(), token).ConfigureAwait(false);
        var rules = await ReadEntityRules(session, spawns.Select(s => s.Record.Entry).Distinct().ToArray(), token).ConfigureAwait(false);
        var result = spawns.Select(s =>
        {
            var record = s.Record with { SmartRows = rules.GetValueOrDefault((s.Record.Entry, s.Record.IsCustom)) };
            if (record.Volume != AreaTriggerShape.Polygon)
            {
                return record;
            }

            // AreaTrigger::UpdatePolygonVertices turns the stored corners by the trigger's rotation.
            double cos = Math.Cos(record.Orientation), sin = Math.Sin(record.Orientation);
            var corners = vertices.GetValueOrDefault((s.Properties, s.Custom)) ?? [];
            return record with { Vertices = corners.Select(v => (v.X * cos - v.Y * sin, v.X * sin + v.Y * cos)).ToArray() };
        }).Where(r => r.Volume != AreaTriggerShape.Polygon || r.Vertices.Count >= 3).ToList();
        return (result, truncated);
    }

    /// <summary>
    /// The ShapeData columns as AreaTriggerShapeInfo reads them: sphere radius; box half extents;
    /// polygon half height; cylinder radius, height and floor offset; disk inner and outer radius,
    /// height and floor offset; bounded plane half width and half height. Targets (morphing) are not
    /// shown; spawns cannot morph (LoadAreaTriggerSpawns rejects curves and time-to-target).
    /// </summary>
    static AreaTriggerRecord? SpawnShape(AreaTriggerRecord spawn, AreaTriggerShape shape, float[] d) => shape switch
    {
        AreaTriggerShape.Sphere => spawn with { Volume = shape, Radius = d[0] },
        AreaTriggerShape.Box => spawn with { Volume = shape, Length = d[0] * 2, Width = d[1] * 2, Height = d[2] * 2 },
        AreaTriggerShape.Polygon => spawn with { Volume = shape, Bottom = -d[0], Top = d[0] },
        AreaTriggerShape.Cylinder => spawn with { Volume = shape, Radius = d[0], Bottom = d[4], Top = d[4] + d[2] },
        AreaTriggerShape.Disk => spawn with { Volume = shape, InnerRadius = d[0], Radius = d[2], Bottom = d[6], Top = d[6] + d[4] },
        AreaTriggerShape.BoundedPlane => spawn with { Volume = shape, Width = d[0] * 2, Height = d[1] * 2 },
        _ => null
    };

    static async Task<Dictionary<(uint Id, bool Custom), List<(double X, double Y)>>> ReadVertices(WorldReadSession session, (uint Id, bool Custom)[] properties, CancellationToken token)
    {
        var result = new Dictionary<(uint, bool), List<(double, double)>>();
        if (properties.Length == 0 || !session.Has("areatrigger_create_properties_polygon_vertex.VerticeX"))
        {
            return result;
        }

        await using var command = session.Command("");
        string ids = WorldReadSession.InList(command, properties.Select(p => p.Id).Distinct(), "p");
        command.CommandText = $"SELECT AreaTriggerCreatePropertiesId,IsCustom,VerticeX,VerticeY FROM areatrigger_create_properties_polygon_vertex WHERE AreaTriggerCreatePropertiesId IN ({ids}) ORDER BY AreaTriggerCreatePropertiesId,IsCustom,Idx";
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            var key = (Convert.ToUInt32(reader.GetValue(0)), Convert.ToInt32(reader.GetValue(1)) != 0);
            if (!result.TryGetValue(key, out var list))
            {
                result[key] = list = [];
            }

            list.Add((reader.GetFloat(2), reader.GetFloat(3)));
        }

        return result;
    }

    /// <summary>SmartAI rule counts per template: source type 11, or 12 for custom templates.</summary>
    static async Task<Dictionary<(uint Template, bool Custom), int>> ReadEntityRules(WorldReadSession session, uint[] templates, CancellationToken token)
    {
        var counts = new Dictionary<(uint, bool), int>();
        await using var command = session.Command("");
        string ids = WorldReadSession.InList(command, templates, "t");
        command.CommandText = $"SELECT entryorguid,source_type,COUNT(*) FROM smart_scripts WHERE source_type IN ({SmartScope.AreaTriggerEntity},{SmartScope.AreaTriggerEntityCustom}) AND entryorguid IN ({ids}) GROUP BY entryorguid,source_type";
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            counts[(Convert.ToUInt32(reader.GetValue(0)), Convert.ToUInt32(reader.GetValue(1)) == SmartScope.AreaTriggerEntityCustom)] = Convert.ToInt32(reader.GetValue(2));
        }

        return counts;
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
