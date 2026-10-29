using System.Data;
using ACWorldEdit.Foundation;
using MySqlConnector;

namespace ACWorldEdit.Data;
/// <summary>External credentials are held only in memory. Each session owns a read-only snapshot.</summary>
public sealed class WorldDatabase
{
    readonly string connectionString;
    // Column list read once per WorldDatabase; a settings change builds a new instance.
    string[]? schema;
    public string SourceKey { get; }

    readonly WorldDialect dialect;

    public WorldDatabase(DatabaseSettings settings) : this(settings, WorldDialect.AzerothCore)
    {
    }

    public WorldDatabase(WorldConnection connection) : this(connection.World, connection.Dialect)
    {
    }

    WorldDatabase(DatabaseSettings settings, WorldDialect dialect)
    {
        if (!settings.IsComplete)
        {
            throw new DatabaseFeatureException("Enter the world database host, user and database name.");
        }

        this.dialect = dialect;
        connectionString = settings.BuildConnectionString();
        // The dialect is hashed in for TrinityCore only, so AzerothCore keys (and the drafts saved
        // under them) stay valid while TrinityCore keys can never collide with them.
        string identity = $"{settings.Host}:{settings.Port}/{settings.Database}" + (dialect == WorldDialect.AzerothCore ? "" : $"#{dialect}");
        SourceKey = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(identity)));
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
                if (schema == null)
                {
                    await session.ReadSchema(dialect, token).ConfigureAwait(false);
                    schema = session.Columns;
                }
                else
                {
                    session.LoadSchema(schema);
                }

                session.RequireDialect(dialect);
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
    public string[] Columns => columns.ToArray();
    public void LoadSchema(IEnumerable<string> known) => columns.UnionWith(known);
    public MySqlCommand Command(string sql) => new(sql, connection, transaction);

    /// <summary>
    /// Bind distinct values as @prefix0.. parameters and return the comma-separated names for an
    /// IN (...) list. An empty list yields NULL, because IN (NULL) matches nothing.
    /// </summary>
    public static string InList<T>(MySqlCommand command, IEnumerable<T> values, string prefix, int limit = int.MaxValue)
    {
        var names = values.Distinct().Take(limit).Select((value, i) =>
        {
            command.Parameters.AddWithValue($"@{prefix}{i}", value);
            return $"@{prefix}{i}";
        }).ToArray();
        return names.Length == 0 ? "NULL" : string.Join(',', names);
    }
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

    /// <summary>
    /// The tables whose columns the editors probe, per dialect. These are fixed names from code, never
    /// user input, so they can be written into the statement.
    /// </summary>
    public static string[] SchemaTables(WorldDialect dialect) => dialect == WorldDialect.TrinityCore ? TrinityTables : AzerothTables;

    static readonly string[] AzerothTables =
    [
        "creature", "creature_template", "creature_template_model", "gameobject", "gameobject_template",
        "game_event", "game_event_creature", "game_event_gameobject", "pool_creature", "pool_gameobject", "conditions",
        "game_graveyard", "graveyard_zone", "game_tele", "gossip_menu", "gossip_menu_option", "npc_text", "broadcast_text",
        "creature_queststarter", "creature_questender", "quest_template", "npc_vendor", "item_template", "trainer",
        "trainer_spell", "creature_default_trainer", "npc_spellclick_spells", "smart_scripts", "points_of_interest",
        "quest_template_addon", "quest_request_items", "quest_offer_reward", "creature_text", "creature_classlevelstats",
        "areatrigger", "areatrigger_scripts", "areatrigger_teleport", "areatrigger_involvedrelation", "areatrigger_tavern",
        "creature_addon", "creature_template_addon", "waypoint_data",
        "linked_respawn", "spawn_group", "game_event_model_equip", "creature_formations", "gameobject_addon"
    ];

    // Tables TrinityCore replaced (pool_members, world_safe_locs, waypoint_path(_node),
    // creature_template_difficulty, quest_objectives, creature_trainer) stand in for the AzerothCore
    // ones; the AC-only mirrors (broadcast_text, item_template, game_graveyard, ...) are not listed.
    static readonly string[] TrinityTables =
    [
        "creature", "creature_template", "creature_template_model", "creature_template_difficulty", "creature_template_gossip", "gameobject", "gameobject_template",
        "game_event", "game_event_creature", "game_event_gameobject", "pool_members", "conditions",
        "world_safe_locs", "graveyard_zone", "game_tele", "gossip_menu", "gossip_menu_option", "npc_text",
        "creature_queststarter", "creature_questender", "quest_template", "quest_objectives", "npc_vendor", "trainer",
        "trainer_spell", "creature_trainer", "npc_spellclick_spells", "smart_scripts", "points_of_interest",
        "quest_template_addon", "quest_request_items", "quest_offer_reward", "creature_text", "creature_classlevelstats",
        "areatrigger", "areatrigger_scripts", "areatrigger_teleport", "areatrigger_involvedrelation", "areatrigger_tavern",
        "areatrigger_template", "areatrigger_create_properties", "areatrigger_create_properties_polygon_vertex",
        "creature_addon", "creature_template_addon", "waypoint_path", "waypoint_path_node",
        "linked_respawn", "spawn_group", "game_event_model_equip", "creature_formations", "gameobject_addon", "phase_name"
    ];

    /// <summary>
    /// Fail fast when the database is the wrong flavour, by columns that exist in that dialect and
    /// not in the other: TrinityCore's difficulty list and safe locations, AzerothCore's spawn mask
    /// and graveyard table.
    /// </summary>
    public void RequireDialect(WorldDialect dialect)
    {
        if (dialect == WorldDialect.TrinityCore)
        {
            Require("creature.spawnDifficulties", "world_safe_locs.ID");
        }
        else
        {
            Require("creature.spawnMask", "game_graveyard.ID");
        }
    }

    public async Task ReadSchema(WorldDialect dialect, CancellationToken token)
    {
        await using var command = Command("""
            SELECT CONCAT(TABLE_NAME,'.',COLUMN_NAME) FROM information_schema.COLUMNS
            WHERE TABLE_SCHEMA=DATABASE() AND FIND_IN_SET(TABLE_NAME,@tables)
            """);
        command.Parameters.AddWithValue("@tables", string.Join(',', SchemaTables(dialect)));
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
            string positive = InList(command, active, "event");
            string negative = InList(command, active.Select(e => -e), "negativeEvent");
            filters.Add($"(NOT EXISTS(SELECT 1 FROM {eventTable} ev WHERE ev.guid={alias}.guid AND ev.eventEntry>0) OR EXISTS(SELECT 1 FROM {eventTable} ev WHERE ev.guid={alias}.guid AND ev.eventEntry IN ({positive})))");
            filters.Add($"NOT EXISTS(SELECT 1 FROM {eventTable} ev WHERE ev.guid={alias}.guid AND ev.eventEntry IN ({negative}))");
        }

        return (eventIds, pool, conditional, filters.Count > 0 ? string.Join(" AND ", filters) : "1=1");
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
