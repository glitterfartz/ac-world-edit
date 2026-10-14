using ACWorldEdit.Data;

/// <summary>
/// Assertion and environment helpers shared by the console check files (there is no test framework;
/// a failed check throws and Program reports it).
/// </summary>
public static class Test
{
    /// <summary>
    /// External worldserver.conf for the read-only database checks.
    /// </summary>
    public static string Config => Environment.GetEnvironmentVariable("ACWORLD_SERVER_CONFIG") ?? CreatureSource.DefaultConfigPath;

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
