using System.Data;
using System.Text.RegularExpressions;
using ACWorldEdit.Foundation;
using MySqlConnector;

namespace ACWorldEdit.Data;
/// <summary>External credentials are held only in memory. Each session owns a read-only snapshot.</summary>
public sealed class WorldDatabase
{
    readonly string connectionString;
    public string SourceKey { get; }

    public WorldDatabase(string configPath)
    {
        string? info = File.ReadLines(configPath).Select(line => Regex.Match(line, "^\\s*WorldDatabaseInfo\\s*=\\s*\"([^\"]*)\"")).FirstOrDefault(match => match.Success)?.Groups[1].Value;
        var parts = info?.Split(';');
        if (parts == null || parts.Length != 5 || !uint.TryParse(parts[1], out uint port))
        {
            throw new InvalidDataException("Unsupported external world database setting.");
        }

        connectionString = new MySqlConnectionStringBuilder
        {
            Server = parts[0],
            Port = port,
            UserID = parts[2],
            Password = parts[3],
            Database = parts[4],
            ConnectionTimeout = 5,
            DefaultCommandTimeout = 8,
            PersistSecurityInfo = false,
            AllowLoadLocalInfile = false,
            Pooling = false
        }.ConnectionString;
        SourceKey = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"{parts[0]}:{port}/{parts[4]}")));
    }

    public async Task<WorldReadSession> OpenAsync(CancellationToken token)
    {
        var connection = new MySqlConnection(connectionString);
        try
        {
            await connection.OpenAsync(token).ConfigureAwait(false);
            var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, true, token).ConfigureAwait(false);
            var session = new WorldReadSession(connection, transaction);
            try
            {
                await session.ReadSchema(token).ConfigureAwait(false);
                return session;
            }
            catch
            {
                await session.DisposeAsync();
                throw;
            }
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }
}

public sealed class SpawnSchemaException(string message) : Exception(message);
/// <summary>
/// Fixed-schema query helpers. Missing visibility metadata fails closed for conservative filters;
/// SQL identifiers come from code, and all camera/settings/search values remain parameters.
/// </summary>
public sealed class WorldReadSession(MySqlConnection connection, MySqlTransaction transaction) : IAsyncDisposable
{
    readonly HashSet<string> columns = new(StringComparer.OrdinalIgnoreCase);
    public bool Has(string field) => columns.Contains(field);
    public MySqlCommand Command(string sql) => new(sql, connection, transaction);
    public void Require(params string[] fields)
    {
        foreach (string field in fields)
        {
            if (!Has(field))
            {
                throw new SpawnSchemaException($"Required database metadata unavailable: {field}.");
            }
        }
    }

    public async Task ReadSchema(CancellationToken token)
    {
        await using var command = Command("""
            SELECT CONCAT(TABLE_NAME,'.',COLUMN_NAME) FROM information_schema.COLUMNS
            WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME IN
            ('creature','creature_template','creature_template_model','gameobject','gameobject_template',
             'game_event','game_event_creature','game_event_gameobject','pool_creature','pool_gameobject','conditions',
             'game_graveyard','graveyard_zone','gossip_menu','gossip_menu_option','npc_text','broadcast_text',
             'creature_queststarter','creature_questender','quest_template','npc_vendor','item_template','trainer',
             'trainer_spell','creature_default_trainer','npc_spellclick_spells','smart_scripts','points_of_interest',
             'quest_template_addon','quest_request_items','quest_offer_reward','creature_text','gameobject_template',
             'areatrigger','areatrigger_scripts','areatrigger_teleport','areatrigger_involvedrelation','areatrigger_tavern')
            """);
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            columns.Add(reader.GetString(0));
        }
    }

    public async Task<WorldEventInfo[]> ReadEvents(SpawnPreview preview, CancellationToken token)
    {
        if (!Has("game_event.eventEntry"))
        {
            if (preview.Events == EventPreviewMode.Scheduled)
            {
                Require("game_event.eventEntry");
            }

            return [];
        }

        Require("game_event.start_time", "game_event.end_time", "game_event.occurence", "game_event.length");
        string ordinary = Has("game_event.world_event") ? "world_event=0" : "1=1";
        await using var command = Command($"""
            SELECT eventEntry,description,
                   ({ordinary} AND start_time<NOW() AND end_time>NOW() AND occurence>0 AND length>0
                    AND MOD(TIMESTAMPDIFF(SECOND,start_time,NOW()),NULLIF(occurence*60,0))<length*60)
            FROM game_event ORDER BY eventEntry LIMIT 1025
            """);
        List<WorldEventInfo> events = [];
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            if (events.Count == 1024)
            {
                throw new SpawnSchemaException("Too many event definitions to preview safely.");
            }

            events.Add(new(reader.GetInt32(0), reader.GetString(1), !reader.IsDBNull(2) && reader.GetBoolean(2)));
        }

        return events.ToArray();
    }

    /// <summary>One indexed EXISTS expression per restriction, before sorting/LIMIT. No join duplicates.</summary>
    public (string Events, string Pool, string Conditional, string Predicate) Restrictions(bool gameObject, string id, SpawnPreview preview, WorldEventInfo[] events, MySqlCommand command)
    {
        string alias = gameObject ? "g" : "c";
        string eventTable = gameObject ? "game_event_gameobject" : "game_event_creature";
        string poolTable = gameObject ? "pool_gameobject" : "pool_creature";
        bool hasEvents = Has(eventTable + ".guid"), hasPools = Has(poolTable + ".guid");
        if (preview.Events != EventPreviewMode.All)
        {
            Require(eventTable + ".guid", eventTable + ".eventEntry");
        }

        if (!preview.IncludePools)
        {
            Require(poolTable + ".guid", poolTable + ".pool_entry");
        }

        if (!preview.IncludeConditional)
        {
            Require("conditions.SourceTypeOrReferenceId", "conditions.SourceGroup", "conditions.SourceEntry", "conditions.SourceId");
        }

        string conditional = Has("conditions.SourceId") ? $"EXISTS(SELECT 1 FROM conditions co WHERE co.SourceEntry={id} AND ((co.SourceTypeOrReferenceId=30 AND co.SourceGroup={(gameObject ? 1 : 0)} AND co.SourceId IN (0,{alias}.guid))" + (gameObject ? "))" : " OR co.SourceTypeOrReferenceId=29))") : "0";
        string pool = hasPools ? $"COALESCE((SELECT pc.pool_entry FROM {poolTable} pc WHERE pc.guid={alias}.guid LIMIT 1),0)" : "0";
        string eventIds = hasEvents ? $"COALESCE((SELECT GROUP_CONCAT(ev.eventEntry ORDER BY ev.eventEntry) FROM {eventTable} ev WHERE ev.guid={alias}.guid),'')" : "''";
        List<string> filters = [];
        if (!preview.IncludePools)
        {
            filters.Add($"NOT EXISTS(SELECT 1 FROM {poolTable} pc WHERE pc.guid={alias}.guid)");
        }

        if (!preview.IncludeConditional)
        {
            filters.Add("NOT (" + conditional + ")");
        }

        if (preview.Events != EventPreviewMode.All)
        {
            var active = (preview.Events == EventPreviewMode.Selected ? preview.ActiveEvents : preview.Events == EventPreviewMode.Scheduled ? events.Where(e => e.Scheduled).Select(e => e.Id) : []).Distinct().ToArray();
            string positive = Parameters(command, active, "event");
            string negative = Parameters(command, active.Select(e => -e), "negativeEvent");
            filters.Add($"(NOT EXISTS(SELECT 1 FROM {eventTable} ev WHERE ev.guid={alias}.guid AND ev.eventEntry>0) OR EXISTS(SELECT 1 FROM {eventTable} ev WHERE ev.guid={alias}.guid AND ev.eventEntry IN ({positive})))");
            filters.Add($"NOT EXISTS(SELECT 1 FROM {eventTable} ev WHERE ev.guid={alias}.guid AND ev.eventEntry IN ({negative}))");
        }

        return (eventIds, pool, conditional, filters.Count > 0 ? string.Join(" AND ", filters) : "1=1");
    }

    static string Parameters(MySqlCommand command, IEnumerable<int> values, string prefix)
    {
        List<string> names = [];
        foreach (int value in values)
        {
            string name = "@" + prefix + names.Count;
            names.Add(name);
            command.Parameters.AddWithValue(name, value);
        }

        return names.Count == 0 ? "0" : string.Join(",", names);
    }

    /// <summary>
    /// Bind the streamed region (@map, @minX..@maxY), the nearest-first sort origin (@x, @y, @z; the
    /// region center when there is no query point yet) and @limit. Values only, never SQL text.
    /// </summary>
    public static void AddRegion(MySqlCommand command, CreatureRegion region, SpawnQueryPoint? point, int limit)
    {
        command.Parameters.AddWithValue("@map", region.Map);
        command.Parameters.AddWithValue("@minX", region.MinServerX);
        command.Parameters.AddWithValue("@maxX", region.MaxServerX);
        command.Parameters.AddWithValue("@minY", region.MinServerY);
        command.Parameters.AddWithValue("@maxY", region.MaxServerY);
        command.Parameters.AddWithValue("@x", point?.X ?? (region.MinServerX + region.MaxServerX) / 2);
        command.Parameters.AddWithValue("@y", point?.Y ?? (region.MinServerY + region.MaxServerY) / 2);
        command.Parameters.AddWithValue("@z", point?.Z ?? 0);
        command.Parameters.AddWithValue("@limit", limit);
    }

    /// <summary>
    /// Bind a user search term as parameters: trimmed and capped at 80 characters, '=' escapes LIKE
    /// wildcards, and a numeric term also binds as an exact ID. Returns whether a filter is wanted
    /// (the term is not empty).
    /// </summary>
    public static bool AddSearch(MySqlCommand command, string term, string like = "@search", string id = "@searchId")
    {
        term = term.Trim();
        if (term.Length > 80)
        {
            term = term[..80];
        }

        command.Parameters.AddWithValue(like, "%" + term.Replace("=", "==").Replace("%", "=%").Replace("_", "=_") + "%");
        command.Parameters.AddWithValue(id, uint.TryParse(term, out uint number) ? number : 0);
        return term.Length > 0;
    }

    public static string Spatial(MySqlCommand command, CreatureRegion region, SpawnQueryPoint? point, SpawnPreview preview, string alias, string search, string id)
    {
        AddRegion(command, region, point, preview.QueryLimit + 1);
        command.Parameters.AddWithValue("@phase", preview.PhaseMask);
        command.Parameters.AddWithValue("@spawn", preview.SpawnMask);
        string spatial = $"{alias}.map=@map AND {alias}.position_x>@minX AND {alias}.position_x<=@maxX AND {alias}.position_y>@minY AND {alias}.position_y<=@maxY";
        if (preview.RespectMasks)
        {
            spatial += $" AND ({alias}.phaseMask & @phase)<>0 AND ({alias}.spawnMask & @spawn)<>0";
        }

        // A 64-yard halo accommodates quantized centers without gaps at the render-distance edge.
        command.Parameters.AddWithValue("@radius", preview.ViewDistance + 64);
        if (point != null)
        {
            spatial += $" AND {Distance(alias)}<=@radius*@radius";
        }

        if (AddSearch(command, search))
        {
            spatial += $" AND (t.name LIKE @search ESCAPE '=' OR {alias}.guid=@searchId OR {id}=@searchId)";
        }

        return spatial;
    }

    public static string Distance(string alias) => $"(POW({alias}.position_x-@x,2)+POW({alias}.position_y-@y,2)+POW({alias}.position_z-@z,2))";
    public static int[] EventIds(string text) => text.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToArray();
    public async ValueTask DisposeAsync()
    {
        await transaction.DisposeAsync().ConfigureAwait(false); // Uncommitted read-only snapshots roll back.
        await connection.DisposeAsync().ConfigureAwait(false);
    }
}
