using MySqlConnector;

namespace ACWorldEdit.Data;

/// <summary>
/// User-entered world database connection. Held in memory and in the per-user settings file, never in
/// the repo. The password is excluded from ToString so it cannot leak through logs or UI text.
/// </summary>
public sealed record DatabaseSettings(string Host, uint Port, string User, string Password, string Database)
{
    public static readonly DatabaseSettings Empty = new("127.0.0.1", 3306, "", "", "acore_world");

    public bool IsComplete => Host.Length > 0 && User.Length > 0 && Database.Length > 0;

    /// <summary>Parse the "host;port;user;password;database" form (diagnostic environment variable).</summary>
    public static DatabaseSettings Parse(string info)
    {
        var parts = info.Split(';');
        if (parts.Length != 5 || !uint.TryParse(parts[1], out uint port))
        {
            throw new InvalidDataException("Database settings must be host;port;user;password;database.");
        }

        return new DatabaseSettings(parts[0], port, parts[2], parts[3], parts[4]);
    }

    public string BuildConnectionString() => new MySqlConnectionStringBuilder
    {
        Server = Host,
        Port = Port,
        UserID = User,
        Password = Password,
        Database = Database,
        ConnectionTimeout = 5,
        DefaultCommandTimeout = 8,
        PersistSecurityInfo = false,
        AllowLoadLocalInfile = false,
        Pooling = false
    }.ConnectionString;

    public override string ToString() => $"{Host}:{Port}/{Database}";
}

/// <summary>Connection validation shared by the connection form and the layer status lines.</summary>
public static class DatabaseDiagnostics
{
    /// <summary>Names the missing fields, or null when the settings can be tried.</summary>
    public static string? Validate(DatabaseSettings settings)
    {
        var missing = new List<string>();
        if (settings.Host.Length == 0)
        {
            missing.Add("host");
        }

        if (settings.User.Length == 0)
        {
            missing.Add("user");
        }

        if (settings.Database.Length == 0)
        {
            missing.Add("database");
        }

        return missing.Count == 0 ? null : $"Enter the {string.Join(", ", missing)}.";
    }

    /// <summary>Actionable text for a failed connection. Never includes the password or provider text.</summary>
    public static string Describe(Exception error, DatabaseSettings settings) => error switch
    {
        MySqlException { ErrorCode: MySqlErrorCode.AccessDenied } => $"Access denied for user '{settings.User}'. Check the user name and password.",
        MySqlException { ErrorCode: MySqlErrorCode.UnknownDatabase } => $"Database '{settings.Database}' does not exist on that server.",
        MySqlException { ErrorCode: MySqlErrorCode.DatabaseAccessDenied } => $"User '{settings.User}' may not use database '{settings.Database}'.",
        MySqlException { ErrorCode: MySqlErrorCode.UnableToConnectToHost } => $"Cannot reach {settings.Host}:{settings.Port}. Check the host and port and that MySQL is running.",
        MySqlException mysql => $"MySQL error {mysql.Number}. Check the connection settings.",
        OperationCanceledException => $"Timed out connecting to {settings.Host}:{settings.Port}.",
        SpawnSchemaException schema => $"Connected, but this is not an AzerothCore world database: {schema.Message}",
        InvalidDataException invalid => invalid.Message,
        _ => "Could not connect. Check the host, port and that MySQL is reachable."
    };

    /// <summary>Open (and immediately dispose) a read-only session including the schema check; null on success.</summary>
    public static async Task<string?> TestAsync(DatabaseSettings settings)
    {
        string? invalid = Validate(settings);
        if (invalid != null)
        {
            return invalid;
        }

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await using var session = await new WorldDatabase(settings).OpenAsync(timeout.Token).ConfigureAwait(false);
            return null;
        }
        catch (Exception error)
        {
            return Describe(error, settings);
        }
    }
}
