using System.Data;
using System.Text.RegularExpressions;
using ACWorldEdit.Foundation;
using MySqlConnector;

namespace ACWorldEdit.Data;

/// <summary>
/// Fixed SELECT-only regional adapter. External credentials stay in memory and are never logged;
/// read-only transactions never apply editor drafts.
/// </summary>
public sealed class CreatureSource
{
    public const string DefaultConfigPath = @"X:\AzerothCore\azerothcore-wotlk\build\bin\Debug\configs\worldserver.conf";
    public const int MaximumSpawns = 2000;
    readonly string connectionString;
    readonly string sourceKey;
    /// <summary>
    /// Read only WorldDatabaseInfo and derive a non-secret source identity. Disable pooling/local
    /// infile and bound connection/query timeouts.
    /// </summary>
    public CreatureSource(string configPath)
    {
        string? info = File.ReadLines(configPath).Select(line => Regex.Match(line, "^\\s*WorldDatabaseInfo\\s*=\\s*\"([^\"]*)\"")).FirstOrDefault(match => match.Success)?.Groups[1].Value;
        var parts = info?.Split(';');
        if (parts == null || parts.Length != 5 || !uint.TryParse(parts[1], out uint port))
        {
            throw new InvalidDataException("Expected a quoted WorldDatabaseInfo setting with host;port;user;password;database.");
        }

        connectionString = new MySqlConnectionStringBuilder
        {
            Server = parts[0],
            Port = port,
            UserID = parts[2],
            Password = parts[3],
            Database = parts[4],
            ConnectionTimeout = 5,
            DefaultCommandTimeout = 10,
            PersistSecurityInfo = false,
            AllowLoadLocalInfile = false,
            Pooling = false
        }.ConnectionString;
        sourceKey = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"{parts[0]}:{port}/{parts[4]}")));
    }

    /// <summary>
    /// Read a bounded coherent snapshot across old/current display schemas. Enforce map/position
    /// filters; event, pool, phase and script visibility are currently not simulated.
    /// </summary>
    public async Task<CreatureRegionResult> ReadAsync(CreatureRegion region, CancellationToken token)
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync(token).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, true, token).ConfigureAwait(false);
        // Support both the user's older database and current AzerothCore's model table/id1 schema.
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using (var schema = new MySqlCommand("SELECT CONCAT(TABLE_NAME,'.',COLUMN_NAME) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME IN ('creature','creature_template','creature_template_model')", connection, transaction))
        {
            await using (var reader = await schema.ExecuteReaderAsync(token).ConfigureAwait(false))
            {
                while (await reader.ReadAsync(token).ConfigureAwait(false))
                {
                    columns.Add(reader.GetString(0));
                }
            }
        }

        string id = columns.Contains("creature.id1") ? "c.id1" : "c.id";
        string display = columns.Contains("creature_template_model.CreatureDisplayID") ? $"COALESCE((SELECT m.CreatureDisplayID FROM creature_template_model m WHERE m.CreatureID={id} ORDER BY m.Idx LIMIT 1),0)" : "COALESCE(NULLIF(t.modelid1,0),NULLIF(t.modelid2,0),NULLIF(t.modelid3,0),t.modelid4,0)";
        if (columns.Contains("creature.modelid"))
        {
            display = $"COALESCE(NULLIF(c.modelid,0),{display})";
        }

        string scale = columns.Contains("creature_template_model.DisplayScale") ? $"COALESCE((SELECT m.DisplayScale FROM creature_template_model m WHERE m.CreatureID={id} ORDER BY m.Idx LIMIT 1),1)" : "COALESCE(NULLIF(t.scale,0),1)";
        await using var command = new MySqlCommand($"""
            SELECT c.guid,{id},COALESCE(t.name,'Missing template'),c.map,c.position_x,c.position_y,c.position_z,
                   c.orientation,c.spawnMask,c.phaseMask,{display},{scale}
            FROM creature c LEFT JOIN creature_template t ON t.entry={id}
            WHERE c.map=@map AND c.position_x>@minX AND c.position_x<=@maxX
                            AND c.position_y>@minY AND c.position_y<=@maxY
            ORDER BY c.guid LIMIT @limit
            """, connection, transaction);
        command.Parameters.AddWithValue("@map", region.Map);
        command.Parameters.AddWithValue("@minX", region.MinServerX);
        command.Parameters.AddWithValue("@maxX", region.MaxServerX);
        command.Parameters.AddWithValue("@minY", region.MinServerY);
        command.Parameters.AddWithValue("@maxY", region.MaxServerY);
        command.Parameters.AddWithValue("@limit", MaximumSpawns + 1);
        var spawns = new List<CreatureSpawn>();
        await using (var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                var pose = new WorldPose(reader.GetUInt16(3), reader.GetDouble(4), reader.GetDouble(5), reader.GetDouble(6), reader.GetDouble(7));
                pose.Validate();
                float modelScale = reader.GetFloat(11);
                if (!float.IsFinite(modelScale) || modelScale <= 0)
                {
                    modelScale = 1;
                }

                spawns.Add(new(reader.GetUInt32(0), reader.GetUInt32(1), reader.GetString(2), pose, reader.GetUInt32(8), reader.GetUInt32(9), reader.GetUInt32(10), modelScale));
            }
        }

        // No source writes: read-only transaction is rolled back, even on cancellation/errors via disposal.
        await transaction.RollbackAsync(token).ConfigureAwait(false);
        return new(spawns.Take(MaximumSpawns).ToArray(), spawns.Count > MaximumSpawns, sourceKey);
    }

    /// <summary>
    /// Translate provider/configuration failures without exposing passwords, connection strings or
    /// raw provider text.
    /// </summary>
    public static string SafeError(Exception error) => error switch
    {
        MySqlException mysql => $"Database connection/query failed (MySQL {mysql.Number}). Check server availability and world configuration.",
        IOException => "Cannot read worldserver configuration. Check its path and file access.",
        InvalidDataException => "World database configuration or creature data has an unsupported format.",
        _ => "Could not load creatures. Check database configuration and schema compatibility."
    };
}
