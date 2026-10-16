namespace ACWorldEdit.Data;

/// <summary>
/// The precedence rules behind UserSettings, free of the Godot config file: an ACWORLD_DATA_DIR /
/// ACWORLD_DB_INFO override wins over the saved profile settings, and an unusable DB override falls
/// back to the saved values rather than failing. The saved file is read through a callback so the
/// profile is only touched when no override applies.
/// </summary>
public static class SettingsResolver
{
    public const string DataVariable = "ACWORLD_DATA_DIR";
    public const string DatabaseVariable = "ACWORLD_DB_INFO";

    public static string DataDirectory(string? environmentValue, Func<string> saved) =>
        !string.IsNullOrWhiteSpace(environmentValue) ? environmentValue : saved();

    /// <summary>
    /// read(section, key, fallback) returns a saved value or the fallback.
    /// </summary>
    public static DatabaseSettings Database(string? environmentValue, Func<string, string, string, string> read)
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

        var empty = DatabaseSettings.Empty;
        return new DatabaseSettings(
            read("database", "host", empty.Host),
            uint.TryParse(read("database", "port", ""), out uint port) ? port : empty.Port,
            read("database", "user", empty.User),
            read("database", "password", empty.Password),
            read("database", "name", empty.Database));
    }
}
