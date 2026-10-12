using ACWorldEdit.Foundation;
using MySqlConnector;

namespace ACWorldEdit.Data;
/// <summary>Bounded SELECT-only creature snapshots; visibility predicates run before nearest-first LIMIT.</summary>
public sealed class CreatureSource
{
    public const string DefaultConfigPath = @"X:\AzerothCore\azerothcore-wotlk\build\bin\Debug\configs\worldserver.conf";
    public const int MaximumSpawns = 768;
    readonly WorldDatabase database;
    public CreatureSource(string configPath) => database = new(configPath);
    public Task<CreatureRegionResult> ReadAsync(CreatureRegion region, CancellationToken token) => ReadAsync(region, new SpawnPreview(), null, "", token);
    public async Task<CreatureRegionResult> ReadAsync(CreatureRegion region, SpawnPreview preview, SpawnQueryPoint? point, string search, CancellationToken token)
    {
        preview.Validate();
        await using var session = await database.OpenAsync(token).ConfigureAwait(false);
        string id = session.Has("creature.id1") ? "c.id1" : "c.id";
        string display = session.Has("creature_template_model.CreatureDisplayID") ? $"COALESCE((SELECT m.CreatureDisplayID FROM creature_template_model m WHERE m.CreatureID={id} ORDER BY m.Idx LIMIT 1),0)" : "COALESCE(NULLIF(t.modelid1,0),NULLIF(t.modelid2,0),NULLIF(t.modelid3,0),t.modelid4,0)";
        if (session.Has("creature.modelid"))
        {
            display = $"COALESCE(NULLIF(c.modelid,0),{display})";
        }

        string scale = session.Has("creature_template_model.DisplayScale") ? $"COALESCE((SELECT m.DisplayScale FROM creature_template_model m WHERE m.CreatureID={id} ORDER BY m.Idx LIMIT 1),1)" : "COALESCE(NULLIF(t.scale,0),1)";
        if (!preview.IncludeHelpers)
        {
            session.Require("creature_template.flags_extra", "creature_template.unit_flags2");
        }

        string helper = session.Has("creature_template.flags_extra") ? "((COALESCE(t.flags_extra,0) & 1152)<>0 OR (COALESCE(t.unit_flags2,0) & 2)<>0)" : "0";
        string spawnScript = session.Has("creature.ScriptName") ? "NULLIF(c.ScriptName,'')" : "NULL";
        string templateScript = session.Has("creature_template.ScriptName") ? "NULLIF(t.ScriptName,'')" : "NULL";
        string ai = session.Has("creature_template.AIName") ? "NULLIF(t.AIName,'')" : "NULL";
        string script = $"COALESCE({spawnScript},{templateScript},{ai},'')";
        var events = await session.ReadEvents(preview, token).ConfigureAwait(false);
        await using var command = session.Command("");
        var restriction = session.Restrictions(false, id, preview, events, command);
        string spatial = WorldReadSession.Spatial(command, region, point, preview, "c", search, id);
        string helperFilter = preview.IncludeHelpers ? "1=1" : $"NOT ({helper}) AND ({display})<>0";
        string scriptFilter = preview.IncludeScripted ? "1=1" : $"({script})=''";
        command.CommandText = $"""
            SELECT c.guid,{id},COALESCE(t.name,'Missing template'),c.map,c.position_x,c.position_y,c.position_z,
                   c.orientation,c.spawnMask,c.phaseMask,{display},{scale},
                   {restriction.Events},{restriction.Pool},{restriction.Conditional},{script},{helper}
            FROM creature c LEFT JOIN creature_template t ON t.entry={id}
            WHERE {spatial} AND {restriction.Predicate} AND {helperFilter} AND {scriptFilter}
            ORDER BY {WorldReadSession.Distance("c")},c.guid LIMIT @limit
            """;
        List<CreatureSpawn> spawns = [];
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            var pose = new WorldPose(reader.GetUInt16(3), reader.GetDouble(4), reader.GetDouble(5), reader.GetDouble(6), reader.GetDouble(7));
            pose.Validate();
            float modelScale = reader.GetFloat(11);
            if (!float.IsFinite(modelScale) || modelScale <= 0)
            {
                modelScale = 1;
            }

            var visibility = new SpawnVisibility(WorldReadSession.EventIds(reader.GetString(12)), reader.GetUInt32(13), reader.GetBoolean(14), reader.GetString(15), reader.GetBoolean(16));
            spawns.Add(new(reader.GetUInt32(0), reader.GetUInt32(1), reader.GetString(2), pose, reader.GetUInt32(8), reader.GetUInt32(9), reader.GetUInt32(10), modelScale, visibility));
        }

        return new(spawns.Take(preview.QueryLimit).ToArray(), spawns.Count > preview.QueryLimit, database.SourceKey, events);
    }

    public static string SafeError(Exception error) => error switch
    {
        SpawnSchemaException schema => schema.Message,
        MySqlException mysql => $"Database unavailable (MySQL {mysql.Number}). Check connection settings.",
        IOException => "Cannot read world configuration. Check the connection settings path.",
        InvalidDataException => "Unsupported database configuration or spawn data.",
        _ => "Could not load spawns. Check the connection and database format."
    };
}
