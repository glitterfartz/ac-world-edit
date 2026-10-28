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
    public const string RetailVariable = "ACWORLD_RETAIL_DIR";
    public const string ProfileVariable = "ACWORLD_CLIENT_PROFILE";

    public static string DataDirectory(string? environmentValue, Func<string> saved) =>
        !string.IsNullOrWhiteSpace(environmentValue) ? environmentValue : saved();

    public static string RetailDirectory(string? environmentValue, Func<string> saved) =>
        !string.IsNullOrWhiteSpace(environmentValue) ? environmentValue : saved();

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
}
