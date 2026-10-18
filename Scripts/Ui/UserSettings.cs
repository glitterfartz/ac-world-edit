using Godot;
using ACWorldEdit.Assets;
using ACWorldEdit.Data;

namespace ACWorldEdit;

/// <summary>
/// Per-user settings in user://settings.cfg (outside the repo): the client profile (Wrath 3.3.5 or Retail)
/// with one folder remembered per profile, and the world database connection. The password is stored in that profile file in plain text, like worldserver.conf
/// did; it is never logged or shown. Diagnostics may override both through ACWORLD_DATA_DIR and
/// ACWORLD_DB_INFO (host;port;user;password;database) without touching the saved file.
/// </summary>
public static class UserSettings
{
    const string Path = "user://settings.cfg";

    public static string LoadDataDirectory() => SettingsResolver.DataDirectory(System.Environment.GetEnvironmentVariable(SettingsResolver.DataVariable), () => Read("client", "data_dir", ""));

    public static void SaveDataDirectory(string directory) => Write("client", "data_dir", directory);

    public static string LoadRetailDirectory() => SettingsResolver.RetailDirectory(System.Environment.GetEnvironmentVariable(SettingsResolver.RetailVariable), () => Read("client", "retail_dir", ""));

    public static void SaveRetailDirectory(string directory) => Write("client", "retail_dir", directory);

    public static ClientKind LoadProfile() => SettingsResolver.Profile(System.Environment.GetEnvironmentVariable(SettingsResolver.ProfileVariable), () => Read("client", "profile", ""));

    public static void SaveProfile(ClientKind kind) => Write("client", "profile", SettingsResolver.ProfileName(kind));

    public static DatabaseSettings LoadDatabase() => SettingsResolver.Database(System.Environment.GetEnvironmentVariable(SettingsResolver.DatabaseVariable), Read);

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
