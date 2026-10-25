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

public static class DbErrors
{
    /// <summary>User-facing text for a failed read; never includes provider text or credentials.</summary>
    public static string Safe(Exception error) => error switch
    {
        SpawnSchemaException schema => schema.Message,
        MySqlException => "Database unavailable. Check Scene → Database connection…",
        InvalidDataException => "Check the database connection settings, or the spawn data is unsupported.",
        _ => "Could not load spawns. Check the connection and database format."
    };
}
