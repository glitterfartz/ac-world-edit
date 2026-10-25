using System.Numerics;
using ACWorldEdit.Foundation;

namespace ACWorldEdit.Data;
/// <summary>Read-only database GO snapshots. SQL membership, masks and default despawn state precede LIMIT.</summary>
public sealed class AcGameObjectSource(WorldDatabase database)
{
    public AcGameObjectSource(DatabaseSettings settings) : this(new WorldDatabase(settings))
    {
    }

    public async Task<GameObjectRegionResult> ReadAsync(CreatureRegion region, SpawnPreview preview, SpawnQueryPoint? point, string search, CancellationToken token)
    {
        preview.Validate();
        await using var session = await database.OpenAsync(token).ConfigureAwait(false);
        session.Require("gameobject.id", "gameobject_template.displayId", "gameobject_template.size");
        var events = await session.ReadEvents(preview, token).ConfigureAwait(false);
        string helper = "COALESCE((t.displayId=0 OR (t.type=5 AND t.Data2<>0) OR (t.type=6 AND t.Data8<>0) OR (t.type=8 AND t.Data3<>0) OR (t.type=30 AND t.Data6<>0)),1)";
        string scriptName = session.Has("gameobject_template.ScriptName") ? "NULLIF(t.ScriptName,'')" : "NULL";
        string aiName = session.Has("gameobject_template.AIName") ? "NULLIF(t.AIName,'')" : "NULL";
        string script = $"COALESCE({scriptName},{aiName},'')";
        await using var command = session.Command("");
        var restriction = session.Restrictions(true, "g.id", preview, events, command);
        string spatial = WorldReadSession.Spatial(command, region, point, preview, "g", search, "g.id");
        string helperFilter = preview.IncludeHelpers ? "1=1" : $"NOT ({helper})";
        string scriptFilter = preview.IncludeScripted ? "1=1" : $"({script})=''";
        string despawnFilter = preview.IncludeDespawned ? "1=1" : "g.spawntimesecs>=0";
        command.CommandText = $"""
            SELECT g.guid,g.id,COALESCE(t.name,'Missing template'),g.map,g.position_x,g.position_y,g.position_z,
                   g.orientation,g.spawnMask,g.phaseMask,COALESCE(t.displayId,0),COALESCE(t.size,1),
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
            float scale = reader.GetFloat(11);
            if (!float.IsFinite(scale) || scale <= 0)
            {
                scale = 1;
            }

            var rotation = new Quaternion(reader.GetFloat(12), reader.GetFloat(13), reader.GetFloat(14), reader.GetFloat(15));
            if (!float.IsFinite(rotation.LengthSquared()))
            {
                rotation = Quaternion.Identity;
            }
            else if (rotation.LengthSquared() < 0.00001f)
            {
                rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, (float)pose.Heading);
            }
            else
            {
                rotation = Quaternion.Normalize(rotation);
            }

            var visibility = new SpawnVisibility(WorldReadSession.EventIds(reader.GetString(18)), reader.GetUInt32(19), reader.GetBoolean(20), reader.GetString(21), reader.GetBoolean(22), reader.GetBoolean(23));
            spawns.Add(new(Convert.ToUInt64(reader.GetValue(0)), reader.GetUInt32(1), reader.GetString(2), pose, reader.GetUInt32(8), reader.GetUInt32(9), reader.GetUInt32(10), scale, rotation, reader.GetInt32(16), reader.GetInt32(17), visibility));
        }

        return new(spawns.Take(preview.QueryLimit).ToArray(), spawns.Count > preview.QueryLimit, events, database.SourceKey);
    }
}
