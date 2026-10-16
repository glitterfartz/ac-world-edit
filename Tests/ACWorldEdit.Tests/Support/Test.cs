using ACWorldEdit.Data;

/// <summary>
/// Assertion helpers carried over from the legacy console harness: a failed check throws, which
/// xUnit reports as a test failure.
/// </summary>
public static class Test
{
    /// <summary>
    /// ACWORLD_DB_INFO (host;port;user;password;database) for the read-only database tests.
    /// </summary>
    public static DatabaseSettings Config => DatabaseSettings.Parse(Environment.GetEnvironmentVariable("ACWORLD_DB_INFO") ?? throw new InvalidOperationException("Set ACWORLD_DB_INFO=host;port;user;password;database for database tests."));

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
