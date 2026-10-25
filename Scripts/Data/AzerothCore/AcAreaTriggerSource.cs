using ACWorldEdit.Foundation;

namespace ACWorldEdit.Data;

public sealed record AreaTriggerRegionResult(AreaTriggerRecord[] Triggers, bool Truncated, string SourceKey);

/// <summary>
/// Read-only `areatrigger` snapshots with their server links (ObjectMgr::LoadAreaTriggers and the
/// areatrigger_scripts/teleport/involvedrelation/tavern loaders). Centers are limited to the
/// streamed region, ordered nearest-first and bounded; search applies before LIMIT. The client's
/// own AreaTrigger.dbc decides when a trigger fires and is not read here.
/// </summary>
public sealed class AreaTriggerSource(DatabaseSettings settings)
{
    public const int Limit = 256;
    readonly WorldDatabase database = new(settings);
    public string SourceKey => database.SourceKey;

    public async Task<AreaTriggerRegionResult> ReadAsync(CreatureRegion region, SpawnQueryPoint? point, string search, CancellationToken token)
    {
        await using var session = await database.OpenAsync(token).ConfigureAwait(false);
        session.Require("areatrigger.entry", "areatrigger.map", "areatrigger.x", "areatrigger.y", "areatrigger.z", "areatrigger.radius",
            "areatrigger.length", "areatrigger.width", "areatrigger.height", "areatrigger.orientation",
            "areatrigger_scripts.entry", "areatrigger_scripts.ScriptName", "smart_scripts.entryorguid", "smart_scripts.source_type");
        await using var command = session.Command("");
        WorldReadSession.AddRegion(command, region, point, Limit + 1);
        // Optional link tables degrade to "no link" rather than failing the whole layer.
        bool teleports = session.Has("areatrigger_teleport.ID") && session.Has("areatrigger_teleport.target_map");
        bool quests = session.Has("areatrigger_involvedrelation.id") && session.Has("areatrigger_involvedrelation.quest");
        bool taverns = session.Has("areatrigger_tavern.id") && session.Has("areatrigger_tavern.name");
        bool titles = quests && session.Has("quest_template.ID") && session.Has("quest_template.LogTitle");
        string filter = "1=1";
        if (WorldReadSession.AddSearch(command, search))
        {
            filter = "(a.entry=@searchId OR s.ScriptName LIKE @search ESCAPE '='" + (teleports ? " OR t.Name LIKE @search ESCAPE '='" : "") + (taverns ? " OR v.name LIKE @search ESCAPE '='" : "") + ")";
        }

        command.CommandText = $"""
            SELECT a.entry,a.map,a.x,a.y,a.z,a.radius,a.length,a.width,a.height,a.orientation,
                   COALESCE(s.ScriptName,''),
                   {(teleports ? "t.ID,COALESCE(t.Name,''),t.target_map,t.target_position_x,t.target_position_y,t.target_position_z,t.target_orientation" : "NULL,'',0,0,0,0,0")},
                   {(quests ? "COALESCE(q.quest,0)" : "0")},{(titles ? "COALESCE(qt.LogTitle,'')" : "''")},
                   {(taverns ? "COALESCE(v.name,'')" : "''")},
                   (SELECT COUNT(*) FROM smart_scripts ss WHERE ss.source_type=2 AND ss.entryorguid=a.entry)
            FROM areatrigger a
            LEFT JOIN areatrigger_scripts s ON s.entry=a.entry
            {(teleports ? "LEFT JOIN areatrigger_teleport t ON t.ID=a.entry" : "")}
            {(quests ? "LEFT JOIN areatrigger_involvedrelation q ON q.id=a.entry" : "")}
            {(titles ? "LEFT JOIN quest_template qt ON qt.ID=q.quest" : "")}
            {(taverns ? "LEFT JOIN areatrigger_tavern v ON v.id=a.entry" : "")}
            WHERE a.map=@map AND a.x>@minX AND a.x<=@maxX AND a.y>@minY AND a.y<=@maxY AND {filter}
            ORDER BY (POW(a.x-@x,2)+POW(a.y-@y,2)+POW(a.z-@z,2)),a.entry LIMIT @limit
            """;
        List<AreaTriggerRecord> triggers = [];
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            uint map = Convert.ToUInt32(reader.GetValue(1));
            if (map > ushort.MaxValue)
            {
                continue;
            }

            AreaTriggerTeleport? teleport = reader.IsDBNull(11) ? null
                : new(reader.GetString(12), Convert.ToUInt16(reader.GetValue(13)), reader.GetDouble(14), reader.GetDouble(15), reader.GetDouble(16), reader.GetDouble(17));
            var record = new AreaTriggerRecord(Convert.ToUInt32(reader.GetValue(0)), (ushort)map, reader.GetDouble(2), reader.GetDouble(3), reader.GetDouble(4),
                reader.GetDouble(5), reader.GetDouble(6), reader.GetDouble(7), reader.GetDouble(8), reader.GetDouble(9),
                reader.GetString(10), teleport, Convert.ToUInt32(reader.GetValue(18)), reader.GetString(19), reader.GetString(20), Convert.ToInt32(reader.GetValue(21)));
            // Rows the server could not use either (non-finite) are left out rather than drawn.
            double[] values = [record.X, record.Y, record.Z, record.Radius, record.Length, record.Width, record.Height, record.Orientation];
            if (values.All(double.IsFinite))
            {
                triggers.Add(record);
            }
        }

        bool truncated = triggers.Count > Limit;
        return new(triggers.Take(Limit).ToArray(), truncated, database.SourceKey);
    }
}
