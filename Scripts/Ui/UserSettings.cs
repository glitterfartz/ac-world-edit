using Godot;
using ACWorldEdit.Data;

namespace ACWorldEdit;

/// <summary>
/// Per-user settings in user://settings.cfg (outside the repo): the client Data folder and the world
/// database connection. The password is stored in that profile file in plain text, like worldserver.conf
/// did; it is never logged or shown. Diagnostics may override both through ACWORLD_DATA_DIR and
/// ACWORLD_DB_INFO (host;port;user;password;database) without touching the saved file.
/// </summary>
public static class UserSettings
{
    const string Path = "user://settings.cfg";

    public static string LoadDataDirectory()
    {
        string? env = System.Environment.GetEnvironmentVariable("ACWORLD_DATA_DIR");
        if (!string.IsNullOrWhiteSpace(env))
        {
            return env;
        }

        return Read("client", "data_dir", "");
    }

    public static void SaveDataDirectory(string directory) => Write("client", "data_dir", directory);

    public static DatabaseSettings LoadDatabase()
    {
        string? env = System.Environment.GetEnvironmentVariable("ACWORLD_DB_INFO");
        if (!string.IsNullOrWhiteSpace(env))
        {
            try
            {
                return DatabaseSettings.Parse(env);
            }
            catch (InvalidDataException)
            {
            }
        }

        var empty = DatabaseSettings.Empty;
        return new DatabaseSettings(
            Read("database", "host", empty.Host),
            uint.TryParse(Read("database", "port", ""), out uint port) ? port : empty.Port,
            Read("database", "user", empty.User),
            Read("database", "password", empty.Password),
            Read("database", "name", empty.Database));
    }

    public static void SaveDatabase(DatabaseSettings settings)
    {
        var file = Open();
        file.SetValue("database", "host", settings.Host);
        file.SetValue("database", "port", settings.Port.ToString());
        file.SetValue("database", "user", settings.User);
        file.SetValue("database", "password", settings.Password);
        file.SetValue("database", "name", settings.Database);
        file.Save(Path);
    }

    static ConfigFile Open()
    {
        var file = new ConfigFile();
        file.Load(Path);
        return file;
    }

    static string Read(string section, string key, string fallback) => Open().GetValue(section, key, fallback).AsString();

    static void Write(string section, string key, string value)
    {
        var file = Open();
        file.SetValue(section, key, value);
        file.Save(Path);
    }
}
