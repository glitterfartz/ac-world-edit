using ACWorldEdit.Data;

/// <summary>
/// Assertion and environment helpers shared by the console check files (there is no test framework;
/// a failed check throws and Program reports it).
/// </summary>
public static class Test
{
    /// <summary>
    /// ACWORLD_DB_INFO (host;port;user;password;database) for the read-only database checks.
    /// </summary>
    public static DatabaseSettings Config => DatabaseSettings.Parse(Environment.GetEnvironmentVariable("ACWORLD_DB_INFO") ?? throw new InvalidOperationException("Set ACWORLD_DB_INFO=host;port;user;password;database for database checks."));

    public static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception(message);
        }
    }

    /// <summary>
    /// Pass only when the action is rejected with InvalidDataException (the draft/validation error).
    /// </summary>
    public static void Rejects(Action action, string message)
    {
        try
        {
            action();
        }
        catch (InvalidDataException)
        {
            return;
        }

        throw new Exception(message);
    }
}
