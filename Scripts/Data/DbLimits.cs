using MySqlConnector;

namespace ACWorldEdit.Data;

/// <summary>Row and search bounds shared by the editors and every dialect's sources.</summary>
public static class DbLimits
{
    public const int MaxRows = 500, MaxSearch = 50;

    /// <summary>
    /// Yards around a graveyard searched for spirit healers (they usually stand within ~10 yd).
    /// </summary>
    public const double HealerRadius = 40;
}

/// <summary>
/// A read that cannot run for a reason the user can act on (an editor the dialect lacks, missing client
/// data, incomplete settings). Its message is written for the status bar and is shown as is.
/// </summary>
public sealed class DatabaseFeatureException(string message) : Exception(message);

public static class DbErrors
{
    /// <summary>User-facing text for a failed read; never includes provider text or credentials.</summary>
    public static string Safe(Exception error) => error switch
    {
        SpawnSchemaException schema => schema.Message,
        DatabaseFeatureException feature => feature.Message,
        MySqlException => "Database unavailable. Check Scene → Database connection…",
        InvalidDataException => "Check the database connection settings, or the spawn data is unsupported.",
        _ => "Could not load spawns. Check the connection and database format."
    };
}
