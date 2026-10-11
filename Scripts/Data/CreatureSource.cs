using System.Data;
using System.Text.RegularExpressions;
using ACWorldEdit.Foundation;
using MySqlConnector;
namespace ACWorldEdit.Data;

/// <summary>Fixed SELECT-only adapter. Credentials are read in memory from external core configuration.</summary>
public sealed class CreatureSource
{
    public const string DefaultConfigPath = @"X:\AzerothCore\azerothcore-wotlk\build\bin\Debug\configs\worldserver.conf";
    public const int MaximumSpawns = 2000;
    readonly string connectionString;
    public CreatureSource(string configPath)
    {
        string? info = File.ReadLines(configPath).Select(line => Regex.Match(line, "^\\s*WorldDatabaseInfo\\s*=\\s*\"([^\"]*)\""))
            .FirstOrDefault(match => match.Success)?.Groups[1].Value;
        var parts = info?.Split(';');
        if (parts == null || parts.Length != 5 || !uint.TryParse(parts[1], out uint port))
            throw new InvalidDataException("Expected a quoted WorldDatabaseInfo setting with host;port;user;password;database.");
        connectionString = new MySqlConnectionStringBuilder
        {
            Server = parts[0], Port = port, UserID = parts[2], Password = parts[3], Database = parts[4],
            ConnectionTimeout = 5, DefaultCommandTimeout = 10, PersistSecurityInfo = false,
            AllowLoadLocalInfile = false, Pooling = false
        }.ConnectionString;
    }
    public async Task<CreatureRegionResult> ReadAsync(CreatureRegion region, CancellationToken token)
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync(token).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, true, token).ConfigureAwait(false);
        await using var command = new MySqlCommand("""
            SELECT c.guid,c.id,COALESCE(t.name,'Missing template'),c.map,c.position_x,c.position_y,c.position_z,
                   c.orientation,c.spawnMask,c.phaseMask
            FROM creature c LEFT JOIN creature_template t ON t.entry=c.id
            WHERE c.map=@map AND c.position_x>@minX AND c.position_x<=@maxX
                            AND c.position_y>@minY AND c.position_y<=@maxY
            ORDER BY c.guid LIMIT @limit
            """, connection, transaction);
        command.Parameters.AddWithValue("@map", region.Map);
        command.Parameters.AddWithValue("@minX", region.MinServerX); command.Parameters.AddWithValue("@maxX", region.MaxServerX);
        command.Parameters.AddWithValue("@minY", region.MinServerY); command.Parameters.AddWithValue("@maxY", region.MaxServerY);
        command.Parameters.AddWithValue("@limit", MaximumSpawns + 1);
        var spawns = new List<CreatureSpawn>();
        await using (var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                var pose = new WorldPose(reader.GetUInt16(3), reader.GetDouble(4), reader.GetDouble(5), reader.GetDouble(6), reader.GetDouble(7));
                pose.Validate();
                spawns.Add(new(reader.GetUInt32(0), reader.GetUInt32(1), reader.GetString(2), pose, reader.GetUInt32(8), reader.GetUInt32(9)));
            }
        }
        // No source writes: read-only transaction is rolled back, even on cancellation/errors via disposal.
        await transaction.RollbackAsync(token).ConfigureAwait(false);
        return new(spawns.Take(MaximumSpawns).ToArray(), spawns.Count > MaximumSpawns);
    }
    public static string SafeError(Exception error) => error switch
    {
        MySqlException mysql => $"Database connection/query failed (MySQL {mysql.Number}). Check server availability and world configuration.",
        IOException => "Cannot read worldserver configuration. Check its path and file access.",
        InvalidDataException => "World database configuration or creature data has an unsupported format.",
        _ => "Could not load creatures. Check database configuration and schema compatibility."
    };
}
