using MySqlConnector;

namespace ACWorldEdit.Data;

/// <summary>
/// Actionable errors for the editor's own change-history database. The history must degrade
/// gracefully: when the MySQL user may not create the database (error 1044 is the common one on
/// hosted servers), the window says so and staging continues locally instead of failing silently.
/// Passwords never appear in these messages.
/// </summary>
public static class AcworldDiagnostics
{
    /// <summary>Human-readable reason a history operation failed, or null when there is none.</summary>
    public static string Describe(Exception error, DatabaseSettings acworld) => error switch
    {
        DatabaseFeatureException feature => feature.Message,
        MySqlException mysql => DescribeCode(mysql.Number, acworld),
        OperationCanceledException => $"Timed out talking to the history database at {acworld.Host}:{acworld.Port}.",
        _ => $"Could not reach the history database at {acworld.Host}:{acworld.Port}. Check the history connection."
    };

    /// <summary>Human-readable text for a MySQL server error number against the history database.</summary>
    public static string DescribeCode(int number, DatabaseSettings acworld) => number switch
    {
        1044 => $"MySQL user '{acworld.User}' may not use database '{acworld.Database}' (error 1044). " +
            $"Ask the admin to create it once and grant access, or pick a database you already own in the connection form:\n" +
            $"CREATE DATABASE `{Escape(acworld.Database)}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;\n" +
            $"GRANT ALL PRIVILEGES ON `{Escape(acworld.Database)}`.* TO '{acworld.User}'@'%';",
        1045 => $"Access denied for user '{acworld.User}' on the history connection (error 1045). Check the history user and password.",
        1049 => $"History database '{acworld.Database}' does not exist and could not be created (error 1049). Create it once or pick one you own in the connection form.",
        1142 => $"MySQL user '{acworld.User}' may not change database '{acworld.Database}' (error 1142). " +
            $"It needs table privileges there, e.g. GRANT ALL PRIVILEGES ON `{Escape(acworld.Database)}`.* TO '{acworld.User}'@'%';",
        _ => $"MySQL error {number} on the history database '{acworld.Database}'. Check the history connection."
    };

    static string Escape(string name) => name.Replace("`", "``", StringComparison.Ordinal);
}
