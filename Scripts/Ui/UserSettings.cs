using Godot;
using ACWorldEdit.Assets;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// Per-user settings in user://settings.cfg (outside the repo): the client profile (Wrath 3.3.5 or Retail)
/// with one folder remembered per profile, and the world database connection per profile ([database.wrath], [database.retail]). The password is stored in that profile file in plain text, like worldserver.conf
/// did; it is never logged or shown. Diagnostics may override both through ACWORLD_DATA_DIR and
/// ACWORLD_DB_INFO / ACWORLD_TC_DB_INFO (host;port;user;password;database[;hotfixes]) without touching the saved file.
/// </summary>
public static class UserSettings
{
    const string Path = "user://settings.cfg";

    public static string LoadDataDirectory() => SettingsResolver.DataDirectory(System.Environment.GetEnvironmentVariable(SettingsResolver.DataVariable), () => Read("client", "data_dir", ""));

    public static void SaveDataDirectory(string directory) => Write("client", "data_dir", directory);

    public static string LoadRetailDirectory() => SettingsResolver.RetailDirectory(System.Environment.GetEnvironmentVariable(SettingsResolver.RetailVariable), () => Read("client", "retail_dir", ""));

    public static void SaveRetailDirectory(string directory) => Write("client", "retail_dir", directory);

    /// <summary>Community listfile location (user://, or ACWORLD_LISTFILE); the file may not exist yet.</summary>
    public static string ListfilePath() => ListfileStore.PathIn(ProjectSettings.GlobalizePath("user://"), System.Environment.GetEnvironmentVariable(ListfileStore.Variable));

    /// <summary>Whether the first-run download prompt was already answered, so declining is not nagged again.</summary>
    public static bool ListfilePrompted() => Read("client", "listfile_prompted", "") == "1";

    public static void SaveListfilePrompted() => Write("client", "listfile_prompted", "1");

    public static ClientKind LoadProfile() => SettingsResolver.Profile(System.Environment.GetEnvironmentVariable(SettingsResolver.ProfileVariable), () => Read("client", "profile", ""));

    public static void SaveProfile(ClientKind kind) => Write("client", "profile", SettingsResolver.ProfileName(kind));

    public static WorldConnection LoadDatabase(WorldDialect dialect = WorldDialect.AzerothCore) =>
        SettingsResolver.Connection(dialect, System.Environment.GetEnvironmentVariable(SettingsResolver.DatabaseVariableFor(dialect)), Read);

    public static void SaveDatabase(WorldConnection connection)
    {
        var file = Open();
        string section = SettingsResolver.DatabaseSection(connection.Dialect);
        file.SetValue(section, "host", connection.World.Host);
        file.SetValue(section, "port", connection.World.Port.ToString());
        file.SetValue(section, "user", connection.World.User);
        file.SetValue(section, "password", connection.World.Password);
        file.SetValue(section, "name", connection.World.Database);
        if (connection.Hotfixes != null)
        {
            file.SetValue(section, "hotfixes", connection.Hotfixes);
        }

        file.Save(Path);
    }

    /// <summary>Saved UI scale in percent (100 when unset or out of range).</summary>
    public static int LoadUiScale() => int.TryParse(Read("ui", "scale_percent", "100"), out int percent) && percent is >= 50 and <= 300 ? percent : 100;

    public static void SaveUiScale(int percent) => Write("ui", "scale_percent", percent.ToString());

    static ConfigFile Open()
    {
        var file = new ConfigFile();
        file.Load(Path);
        MigrateLegacyDatabase(file);
        return file;
    }

    /// <summary>Settings saved before client profiles lived in [database]; move them into [database.wrath] once.</summary>
    static void MigrateLegacyDatabase(ConfigFile file)
    {
        if (!file.HasSection(SettingsResolver.LegacyDatabaseSection))
        {
            return;
        }

        string profile = SettingsResolver.DatabaseSection(WorldDialect.AzerothCore);
        var moves = SettingsResolver.LegacyDatabaseMigration(
            key => file.HasSectionKey(SettingsResolver.LegacyDatabaseSection, key) ? file.GetValue(SettingsResolver.LegacyDatabaseSection, key).AsString() : null,
            key => file.HasSectionKey(profile, key));
        foreach (var (key, value) in moves)
        {
            file.SetValue(profile, key, value);
        }

        file.EraseSection(SettingsResolver.LegacyDatabaseSection);
        file.Save(Path);
    }

    static string Read(string section, string key, string fallback) => Open().GetValue(section, key, fallback).AsString();

    static void Write(string section, string key, string value)
    {
        var file = Open();
        file.SetValue(section, key, value);
        file.Save(Path);
    }
}

/// <summary>
/// Where the local draft files live (user:// profile, or a throwaway folder for diagnostics) and
/// the shared content draft opened from there. Each client profile has its own folder, so a Wrath
/// and a retail viewer can run side by side without fighting over the single-writer locks. The draft
/// logic itself is Foundation's ContentDrafts.
/// </summary>
public static class ContentDraftFiles
{
    static ClientKind profile = ClientKind.Wrath335;

    static bool Diagnostic => OS.GetCmdlineUserArgs().Any(a => a.EndsWith("-smoke", StringComparison.Ordinal) || a.StartsWith("--client-shot", StringComparison.Ordinal));

    /// <summary>Files and folders that sat directly in user://drafts before drafts were split by profile.</summary>
    static readonly string[] LegacyEntries = ["creature-positions.json", "gameobject-positions.json", "graveyards.json", "waypoints.json", "interactions.json", "sources.json", "exported.json", "exports"];

    /// <summary>
    /// Draft files of this client profile from now on. The first call after an upgrade moves the
    /// files that predate the split into this profile's folder, so earlier drafts stay with the
    /// profile in use.
    /// </summary>
    public static void UseProfile(ClientKind kind)
    {
        profile = kind;
        sources = null;
        if (!Diagnostic)
        {
            MigrateLegacy();
        }
    }

    static void MigrateLegacy()
    {
        string root = ProjectSettings.GlobalizePath("user://drafts");
        string folder = System.IO.Path.Combine(root, SettingsResolver.ProfileName(profile));
        foreach (string name in LegacyEntries)
        {
            string from = System.IO.Path.Combine(root, name);
            string to = System.IO.Path.Combine(folder, name);
            try
            {
                if (File.Exists(from) && !File.Exists(to))
                {
                    Directory.CreateDirectory(folder);
                    File.Move(from, to);
                }
                else if (Directory.Exists(from) && !Directory.Exists(to))
                {
                    Directory.CreateDirectory(folder);
                    Directory.Move(from, to);
                }
            }
            catch (IOException)
            {
                // Held open by an older viewer; the next launch moves it.
            }
        }
    }

    /// <summary>
    /// Absolute path of a local draft file. Any "*-smoke" or "--client-shot*" run gets a unique
    /// throwaway folder under artifacts/ so diagnostics never open or lock the user's real drafts.
    /// </summary>
    public static string LocalPath(string file) =>
        ProjectSettings.GlobalizePath(Diagnostic ? $"res://artifacts/drafts/{Guid.NewGuid():N}/{file}" : $"user://drafts/{SettingsResolver.ProfileName(profile)}/{file}");

    static SourceDialects? sources;

    /// <summary>The dialect of every database source drafts were made against (sources.json).</summary>
    public static SourceDialects Sources => sources ??= new SourceDialects(LocalPath("sources.json"));

    /// <summary>The content draft, validating each source's rows against that source's tables.</summary>
    public static ContentDrafts Open() => new(LocalPath("interactions.json"), source => Sources.SchemaFor(source).Tables);

    /// <summary>Point an open content draft at the current profile's file.</summary>
    public static void Rebind(ContentDrafts drafts) => drafts.Rebind(LocalPath("interactions.json"));
}
