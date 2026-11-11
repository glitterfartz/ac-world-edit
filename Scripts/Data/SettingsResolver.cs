using ACWorldEdit.Assets;
using ACWorldEdit.Foundation;

namespace ACWorldEdit.Data;

/// <summary>
/// The precedence rules behind UserSettings, free of the Godot config file: an ACWORLD_DATA_DIR /
/// ACWORLD_RETAIL_DIR / ACWORLD_CLIENT_PROFILE / ACWORLD_DB_INFO / ACWORLD_TC_DB_INFO override wins over the saved profile settings, and an unusable DB override falls
/// back to the saved values rather than failing. The saved file is read through a callback so the
/// profile is only touched when no override applies.
/// </summary>
public static class SettingsResolver
{
    public const string DataVariable = "ACWORLD_DATA_DIR";
    public const string DatabaseVariable = "ACWORLD_DB_INFO";
    public const string TrinityDatabaseVariable = "ACWORLD_TC_DB_INFO";
    public const string AcworldVariable = "ACWORLD_ACWORLD_INFO";
    public const string TrinityAcworldVariable = "ACWORLD_TC_ACWORLD_INFO";
    public const string RetailVariable = "ACWORLD_RETAIL_DIR";
    public const string ProfileVariable = "ACWORLD_CLIENT_PROFILE";

    public static string DataDirectory(string? environmentValue, Func<string> saved) =>
        !string.IsNullOrWhiteSpace(environmentValue) ? environmentValue : saved();

    public static string RetailDirectory(string? environmentValue, Func<string> saved) =>
        !string.IsNullOrWhiteSpace(environmentValue) ? environmentValue : saved();

    /// <summary>
    /// True for the client-only WoW: Forever product. It has no private-server branch, so the viewer
    /// opens it without a database; the folder may be the install root (with ACWORLD_CLIENT_PRODUCT)
    /// or its _forever_ product folder.
    /// </summary>
    public static bool ClientOnlyProduct(string retailDirectory) =>
        RetailInstall.ProductFor(retailDirectory) == RetailInstall.ProductForever;

    /// <summary>
    /// The client profile to open: the environment override, else the saved choice. Anything other
    /// than "retail" selects the Wrath 3.3.5 profile, so a damaged value never blocks startup.
    /// </summary>
    public static ClientKind Profile(string? environmentValue, Func<string> saved) =>
        ParseProfile(!string.IsNullOrWhiteSpace(environmentValue) ? environmentValue : saved());

    public static ClientKind ParseProfile(string? value) =>
        string.Equals(value?.Trim(), ProfileName(ClientKind.Retail), StringComparison.OrdinalIgnoreCase) ? ClientKind.Retail : ClientKind.Wrath335;

    public static string ProfileName(ClientKind kind) => kind == ClientKind.Retail ? "retail" : "wrath";

    /// <summary>Saved settings live in [database.wrath] / [database.retail], one section per client profile.</summary>
    public static string DatabaseSection(WorldDialect dialect) => dialect == WorldDialect.TrinityCore ? "database.retail" : "database.wrath";

    /// <summary>Change-history overrides live in [acworld.wrath] / [acworld.retail]; absent means same server as the world.</summary>
    public static string AcworldSection(WorldDialect dialect) => dialect == WorldDialect.TrinityCore ? "acworld.retail" : "acworld.wrath";

    /// <summary>The section settings were saved in before client profiles existed.</summary>
    public const string LegacyDatabaseSection = "database";

    static readonly string[] LegacyDatabaseKeys = ["host", "port", "user", "password", "name"];

    /// <summary>
    /// The values to copy from the pre-profile [database] section into [database.wrath]: every legacy
    /// key that has a value there and none in the profile section. The caller copies them, then
    /// deletes [database].
    /// </summary>
    public static IReadOnlyList<(string Key, string Value)> LegacyDatabaseMigration(Func<string, string?> legacy, Func<string, bool> profileHas) =>
        LegacyDatabaseKeys.Where(key => !profileHas(key)).Select(key => (Key: key, Value: legacy(key))).Where(pair => pair.Value != null).Select(pair => (pair.Key, pair.Value!)).ToList();

    public static string DatabaseVariableFor(WorldDialect dialect) => dialect == WorldDialect.TrinityCore ? TrinityDatabaseVariable : DatabaseVariable;

    public static string AcworldVariableFor(WorldDialect dialect) => dialect == WorldDialect.TrinityCore ? TrinityAcworldVariable : AcworldVariable;

    /// <summary>
    /// The connection for a dialect: its environment override (ACWORLD_DB_INFO, or ACWORLD_TC_DB_INFO for
    /// TrinityCore), else the saved profile section. read(section, key, fallback) returns a saved value
    /// or the fallback.
    /// </summary>
    public static WorldConnection Connection(WorldDialect dialect, string? environmentValue, Func<string, string, string, string> read)
    {
        if (!string.IsNullOrWhiteSpace(environmentValue))
        {
            try
            {
                return WorldConnection.Parse(environmentValue, dialect);
            }
            catch (InvalidDataException)
            {
            }
        }

        var empty = DatabaseSettings.DefaultFor(dialect);
        string section = DatabaseSection(dialect);
        string Read(string key, string fallback) => read(section, key, fallback);

        var world = new DatabaseSettings(
            Read("host", empty.Host),
            uint.TryParse(Read("port", ""), out uint port) ? port : empty.Port,
            Read("user", empty.User),
            Read("password", empty.Password),
            Read("name", empty.Database));
        return new WorldConnection(world, dialect == WorldDialect.TrinityCore ? Read("hotfixes", WorldConnection.DefaultHotfixes) : null, dialect);
    }

    /// <summary>
    /// The change-history database for a dialect: its environment override, else the saved
    /// [acworld.*] section, else null meaning "same server as the world" (database `acworld`).
    /// read(section, key, fallback) returns a saved value or the fallback; hasSection reports whether
    /// the user ever saved an override.
    /// </summary>
    public static DatabaseSettings? AcworldOverride(WorldDialect dialect, string? environmentValue, Func<string, string, string, string> read, Func<string, bool> hasSection) =>
        AcworldOverride(dialect, environmentValue, read, hasSection, world: null);

    /// <summary>Whether a history override means "same server, database name only", so only the name should be saved.</summary>
    public static bool AcworldNameOnly(DatabaseSettings world, DatabaseSettings acworld) =>
        acworld.Host == world.Host && acworld.Port == world.Port &&
        acworld.User == world.User && acworld.Password == world.Password;

    /// <summary>
    /// As above, but resolves a name-only section against the current world settings. A section with
    /// no server fields (or with the world's host/port/user) means "same server, this database
    /// name", so it follows the world credentials instead of freezing a copy of them.
    /// </summary>
    public static DatabaseSettings? AcworldOverride(
        WorldDialect dialect,
        string? environmentValue,
        Func<string, string, string, string> read,
        Func<string, bool> hasSection,
        DatabaseSettings? world)
    {
        if (!string.IsNullOrWhiteSpace(environmentValue))
        {
            try
            {
                return DatabaseSettings.Parse(environmentValue);
            }
            catch (InvalidDataException)
            {
            }
        }

        string section = AcworldSection(dialect);
        if (!hasSection(section))
        {
            return null;
        }

        string Read(string key, string fallback) => read(section, key, fallback);
        string host = Read("host", "");
        string user = Read("user", "");
        string name = Read("name", AcworldStore.DefaultDatabase);
        bool hasServer = host.Length > 0 || user.Length > 0;
        if (!hasServer)
        {
            // Name-only override: keep the database name, follow the current world server.
            return world == null ? null : world with { Database = name };
        }

        uint port = uint.TryParse(Read("port", ""), out uint parsedPort) ? parsedPort : 3306;
        string password = Read("password", "");
        if (world != null && host == world.Host && port == world.Port && user == world.User)
        {
            // Older versions saved a full copy for a name-only override. Its server still matches
            // the world, so treat it as name-only and pick up any changed world credentials.
            return world with { Database = name };
        }

        string savedName = name.Length == 0 ? AcworldStore.DefaultDatabase : name;
        return new DatabaseSettings(host, port, user, password, savedName);
    }
}
