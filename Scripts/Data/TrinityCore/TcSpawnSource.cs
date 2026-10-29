using System.Numerics;
using ACWorldEdit.Foundation;
using MySqlConnector;

namespace ACWorldEdit.Data;

/// <summary>
/// Bounded SELECT-only creature and GameObject snapshots of a TrinityCore world database. The shape
/// is AzerothCore's (visibility predicates run before the nearest-first LIMIT) with the TrinityCore
/// differences: a difficulty ID list and a phase instead of masks, pools in pool_members, and
/// object visibility conditions in `conditions` source type 32.
/// </summary>
public sealed class TcSpawnSource(WorldDatabase database)
{
    /// <summary>conditions.SourceTypeOrReferenceId for "object ID visibility"; SourceGroup is the object's type ID.</summary>
    const int ObjectVisibility = 32, CreatureType = 5, GameObjectType = 8;

    public async Task<CreatureRegionResult> ReadCreaturesAsync(CreatureRegion region, SpawnPreview preview, SpawnQueryPoint? point, string search, CancellationToken token)
    {
        preview.Validate();
        await using var session = await database.OpenAsync(token).ConfigureAwait(false);
        session.Require("creature.id", "creature.PhaseId", "creature.PhaseGroup", "creature.phaseUseFlags", "creature_template.flags_extra", "creature_template.unit_flags2");
        // A spawn's own model override wins, then the template's first model; the scale is the
        // template's scale times that model's display scale.
        string model = "(SELECT m.CreatureDisplayID FROM creature_template_model m WHERE m.CreatureID=c.id ORDER BY m.Idx LIMIT 1)";
        string display = $"COALESCE(NULLIF(c.modelid,0),{model},0)";
        string scale = "COALESCE(NULLIF(t.scale,0),1)*COALESCE((SELECT m.DisplayScale FROM creature_template_model m WHERE m.CreatureID=c.id ORDER BY m.Idx LIMIT 1),1)";
        string helper = "((COALESCE(t.flags_extra,0) & 1152)<>0 OR (COALESCE(t.unit_flags2,0) & 2)<>0)";
        string script = "COALESCE(NULLIF(c.ScriptName,''),NULLIF(t.ScriptName,''),NULLIF(t.AIName,''),'')";
        var events = await session.ReadEvents(preview, token).ConfigureAwait(false);
        var phases = await TcPhaseSource.ReadAsync(session, region, "creature", token).ConfigureAwait(false);
        await using var command = session.Command("");
        var restriction = Restrictions(session, false, preview, events, command);
        string spatial = Spatial(command, region, point, preview, "c", search, "c.id");
        string helperFilter = preview.IncludeHelpers ? "1=1" : $"NOT ({helper}) AND ({display})<>0";
        string scriptFilter = preview.IncludeScripted ? "1=1" : $"({script})=''";
        command.CommandText = $"""
            SELECT c.guid,c.id,COALESCE(t.name,'Missing template'),c.map,c.position_x,c.position_y,c.position_z,
                   c.orientation,c.spawnDifficulties,c.PhaseId,c.PhaseGroup,c.phaseUseFlags,{display},{scale},
                   {restriction.Events},{restriction.Pool},{restriction.Conditional},{script},{helper}
            FROM creature c LEFT JOIN creature_template t ON t.entry=c.id
            WHERE {spatial} AND {restriction.Predicate} AND {helperFilter} AND {scriptFilter}
            ORDER BY {WorldReadSession.Distance("c")},c.guid LIMIT @limit
            """;
        List<CreatureSpawn> spawns = [];
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            var pose = new WorldPose(reader.GetUInt16(3), reader.GetDouble(4), reader.GetDouble(5), reader.GetDouble(6), reader.GetDouble(7));
            pose.Validate();
            float modelScale = reader.GetFloat(13);
            if (!float.IsFinite(modelScale) || modelScale <= 0)
            {
                modelScale = 1;
            }

            var visibility = new SpawnVisibility(WorldReadSession.EventIds(reader.GetString(14)), reader.GetUInt32(15), reader.GetBoolean(16), reader.GetString(17), reader.GetBoolean(18));
            spawns.Add(new(Convert.ToUInt64(reader.GetValue(0)), reader.GetUInt32(1), reader.GetString(2), pose, 0, 0, reader.GetUInt32(12), modelScale, visibility, Phasing(reader, 8)));
        }

        return new(spawns.Take(preview.QueryLimit).ToArray(), spawns.Count > preview.QueryLimit, database.SourceKey, events, phases);
    }

    public async Task<GameObjectRegionResult> ReadGameObjectsAsync(CreatureRegion region, SpawnPreview preview, SpawnQueryPoint? point, string search, CancellationToken token)
    {
        preview.Validate();
        await using var session = await database.OpenAsync(token).ConfigureAwait(false);
        session.Require("gameobject.id", "gameobject.PhaseId", "gameobject.PhaseGroup", "gameobject.phaseUseFlags", "gameobject_template.displayId", "gameobject_template.size");
        var events = await session.ReadEvents(preview, token).ConfigureAwait(false);
        var phases = await TcPhaseSource.ReadAsync(session, region, "gameobject", token).ConfigureAwait(false);
        string helper = "COALESCE((t.displayId=0 OR (t.type=5 AND t.Data2<>0) OR (t.type=6 AND t.Data8<>0) OR (t.type=8 AND t.Data3<>0) OR (t.type=30 AND t.Data6<>0)),1)";
        string script = "COALESCE(NULLIF(g.ScriptName,''),NULLIF(t.ScriptName,''),NULLIF(t.AIName,''),'')";
        await using var command = session.Command("");
        var restriction = Restrictions(session, true, preview, events, command);
        string spatial = Spatial(command, region, point, preview, "g", search, "g.id");
        string helperFilter = preview.IncludeHelpers ? "1=1" : $"NOT ({helper})";
        string scriptFilter = preview.IncludeScripted ? "1=1" : $"({script})=''";
        string despawnFilter = preview.IncludeDespawned ? "1=1" : "g.spawntimesecs>=0";
        command.CommandText = $"""
            SELECT g.guid,g.id,COALESCE(t.name,'Missing template'),g.map,g.position_x,g.position_y,g.position_z,
                   g.orientation,g.spawnDifficulties,g.PhaseId,g.PhaseGroup,g.phaseUseFlags,COALESCE(t.displayId,0),COALESCE(t.size,1),
                   g.rotation0,g.rotation1,g.rotation2,g.rotation3,g.state,COALESCE(t.type,0),
                   {restriction.Events},{restriction.Pool},{restriction.Conditional},{script},{helper},(g.spawntimesecs<0)
            FROM gameobject g LEFT JOIN gameobject_template t ON t.entry=g.id
            WHERE {spatial} AND {restriction.Predicate} AND {helperFilter} AND {scriptFilter} AND {despawnFilter}
            ORDER BY {WorldReadSession.Distance("g")},g.guid LIMIT @limit
            """;
        List<GameObjectSpawn> spawns = [];
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            var pose = new WorldPose(reader.GetUInt16(3), reader.GetDouble(4), reader.GetDouble(5), reader.GetDouble(6), reader.GetDouble(7));
            pose.Validate();
            float scale = reader.GetFloat(13);
            if (!float.IsFinite(scale) || scale <= 0)
            {
                scale = 1;
            }

            var stored = new Quaternion(reader.GetFloat(14), reader.GetFloat(15), reader.GetFloat(16), reader.GetFloat(17));
            var rotation = stored;
            if (!float.IsFinite(rotation.LengthSquared()))
            {
                rotation = Quaternion.Identity;
                stored = Quaternion.Zero;
            }
            else if (rotation.LengthSquared() < 0.00001f)
            {
                rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, (float)pose.Heading);
            }
            else
            {
                rotation = Quaternion.Normalize(rotation);
            }

            var visibility = new SpawnVisibility(WorldReadSession.EventIds(reader.GetString(20)), reader.GetUInt32(21), reader.GetBoolean(22), reader.GetString(23), reader.GetBoolean(24), reader.GetBoolean(25));
            spawns.Add(new(Convert.ToUInt64(reader.GetValue(0)), reader.GetUInt32(1), reader.GetString(2), pose, 0, 0, reader.GetUInt32(12), scale, rotation, reader.GetInt32(18), reader.GetInt32(19), visibility, Phasing(reader, 8), stored));
        }

        return new(spawns.Take(preview.QueryLimit).ToArray(), spawns.Count > preview.QueryLimit, events, database.SourceKey, phases);
    }

    /// <summary>The difficulty list, phase, phase group and phase flags in four consecutive columns from <paramref name="first"/>.</summary>
    static SpawnPhasing Phasing(MySqlDataReader reader, int first) =>
        new(reader.GetString(first).Replace(" ", ""), (uint)Math.Max(0, reader.GetInt32(first + 1)), (uint)Math.Max(0, reader.GetInt32(first + 2)), reader.GetUInt32(first + 3));

    /// <summary>
    /// Bind the region, sort origin and previewed difficulty/phase, and return the spatial predicate.
    /// It mirrors <see cref="SpawnPreview.MatchesPhasing"/>.
    /// </summary>
    static string Spatial(MySqlCommand command, CreatureRegion region, SpawnQueryPoint? point, SpawnPreview preview, string alias, string search, string id)
    {
        WorldReadSession.AddRegion(command, region, point, preview.QueryLimit + 1);
        command.Parameters.AddWithValue("@difficulty", preview.DifficultyId);
        command.Parameters.AddWithValue("@phaseId", preview.PhaseId);
        string spatial = $"{alias}.map=@map AND {alias}.position_x>@minX AND {alias}.position_x<=@maxX AND {alias}.position_y>@minY AND {alias}.position_y<=@maxY";
        if (preview.RespectMasks)
        {
            spatial += $" AND (@difficulty=-1 OR FIND_IN_SET(@difficulty,REPLACE({alias}.spawnDifficulties,' ',''))>0)";
            spatial += $" AND (({alias}.PhaseId=0 AND {alias}.PhaseGroup=0) OR ({alias}.PhaseId<>0 AND {alias}.PhaseId=@phaseId))";
        }

        // A 64-yard halo accommodates quantized centers without gaps at the render-distance edge.
        command.Parameters.AddWithValue("@radius", preview.ViewDistance + 64);
        if (point != null)
        {
            spatial += $" AND {WorldReadSession.Distance(alias)}<=@radius*@radius";
        }

        if (WorldReadSession.AddSearch(command, search))
        {
            spatial += $" AND (t.name LIKE @search ESCAPE '=' OR {alias}.guid=@searchId OR {id}=@searchId)";
        }

        return spatial;
    }

    /// <summary>
    /// One indexed EXISTS expression per restriction, before sorting/LIMIT. Pools are pool_members rows
    /// (type 0 creature, 1 GameObject); a spawn is "conditional" when an object-visibility condition names its entry.
    /// </summary>
    static (string Events, string Pool, string Conditional, string Predicate) Restrictions(WorldReadSession session, bool gameObject, SpawnPreview preview, WorldEventInfo[] events, MySqlCommand command)
    {
        string alias = gameObject ? "g" : "c";
        string id = alias + ".id";
        string eventTable = gameObject ? "game_event_gameobject" : "game_event_creature";
        int poolType = gameObject ? 1 : 0;
        bool hasEvents = session.Has(eventTable + ".guid");
        if (preview.Events != EventPreviewMode.All)
        {
            session.Require(eventTable + ".guid", eventTable + ".eventEntry");
        }

        if (!preview.IncludePools)
        {
            session.Require("pool_members.type", "pool_members.spawnId", "pool_members.poolSpawnId");
        }

        if (!preview.IncludeConditional)
        {
            session.Require("conditions.SourceTypeOrReferenceId", "conditions.SourceGroup", "conditions.SourceEntry");
        }

        string conditional = session.Has("conditions.SourceEntry") ? $"EXISTS(SELECT 1 FROM conditions co WHERE co.SourceTypeOrReferenceId={ObjectVisibility} AND co.SourceGroup={(gameObject ? GameObjectType : CreatureType)} AND co.SourceEntry={id})" : "0";
        string pool = session.Has("pool_members.spawnId") ? $"COALESCE((SELECT pm.poolSpawnId FROM pool_members pm WHERE pm.type={poolType} AND pm.spawnId={alias}.guid LIMIT 1),0)" : "0";
        string eventIds = hasEvents ? $"COALESCE((SELECT GROUP_CONCAT(ev.eventEntry ORDER BY ev.eventEntry) FROM {eventTable} ev WHERE ev.guid={alias}.guid),'')" : "''";
        List<string> filters = [];
        if (!preview.IncludePools)
        {
            filters.Add($"NOT EXISTS(SELECT 1 FROM pool_members pm WHERE pm.type={poolType} AND pm.spawnId={alias}.guid)");
        }

        if (!preview.IncludeConditional)
        {
            filters.Add("NOT (" + conditional + ")");
        }

        if (preview.Events != EventPreviewMode.All)
        {
            var active = (preview.Events == EventPreviewMode.Selected ? preview.ActiveEvents : preview.Events == EventPreviewMode.Scheduled ? events.Where(e => e.Scheduled).Select(e => e.Id) : []).Distinct().ToArray();
            string positive = WorldReadSession.InList(command, active, "event");
            string negative = WorldReadSession.InList(command, active.Select(e => -e), "negativeEvent");
            filters.Add($"(NOT EXISTS(SELECT 1 FROM {eventTable} ev WHERE ev.guid={alias}.guid AND ev.eventEntry>0) OR EXISTS(SELECT 1 FROM {eventTable} ev WHERE ev.guid={alias}.guid AND ev.eventEntry IN ({positive})))");
            filters.Add($"NOT EXISTS(SELECT 1 FROM {eventTable} ev WHERE ev.guid={alias}.guid AND ev.eventEntry IN ({negative}))");
        }

        return (eventIds, pool, conditional, filters.Count > 0 ? string.Join(" AND ", filters) : "1=1");
    }

    /// <summary>Spawns of a creature entry (guid, map, server position), bounded.</summary>
    public async Task<IReadOnlyList<(ulong Guid, uint Map, double X, double Y, double Z)>> FindSpawnsAsync(uint entry, CancellationToken token)
    {
        await using var session = await database.OpenAsync(token).ConfigureAwait(false);
        await using var command = session.Command($"SELECT guid,map,position_x,position_y,position_z FROM creature WHERE id=@k0 ORDER BY guid LIMIT {DbLimits.MaxRows}");
        command.Parameters.AddWithValue("@k0", entry);
        List<(ulong, uint, double, double, double)> spawns = [];
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            spawns.Add((Convert.ToUInt64(reader.GetValue(0)), Convert.ToUInt32(reader.GetValue(1)), Convert.ToDouble(reader.GetValue(2)), Convert.ToDouble(reader.GetValue(3)), Convert.ToDouble(reader.GetValue(4))));
        }

        return spawns;
    }
}
