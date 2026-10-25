using ACWorldEdit.Foundation;

namespace ACWorldEdit.Data;

/// <summary>
/// Everything needed to reach one world database: the connection, the optional hotfixes database
/// name (TrinityCore only) and the schema dialect, which follows the client kind.
/// </summary>
public sealed record WorldConnection(DatabaseSettings World, string? Hotfixes, WorldDialect Dialect)
{
    public const string DefaultHotfixes = "hotfixes";

    public static readonly WorldConnection Empty = new(DatabaseSettings.Empty, null, WorldDialect.AzerothCore);

    /// <summary>Blank settings with the database names a stock install of that server uses.</summary>
    public static WorldConnection DefaultFor(WorldDialect dialect) =>
        dialect == WorldDialect.TrinityCore ? new(DatabaseSettings.DefaultFor(dialect), DefaultHotfixes, dialect) : Empty;

    /// <summary>
    /// Parse "host;port;user;password;database". TrinityCore also accepts a sixth part naming the
    /// hotfixes database, which defaults to "hotfixes" when it is left out or blank.
    /// </summary>
    public static WorldConnection Parse(string info, WorldDialect dialect)
    {
        if (dialect != WorldDialect.TrinityCore)
        {
            return new(DatabaseSettings.Parse(info), null, dialect);
        }

        var parts = info.Split(';');
        if (parts.Length != 6)
        {
            return new(DatabaseSettings.Parse(info), DefaultHotfixes, dialect);
        }

        string hotfixes = parts[5].Trim();
        return new(DatabaseSettings.Parse(string.Join(';', parts[..5])), hotfixes.Length > 0 ? hotfixes : DefaultHotfixes, dialect);
    }

    /// <summary>The same server and credentials pointed at the hotfixes database, or null when there is none.</summary>
    public DatabaseSettings? HotfixesSettings => string.IsNullOrWhiteSpace(Hotfixes) ? null : World with { Database = Hotfixes.Trim() };
}
