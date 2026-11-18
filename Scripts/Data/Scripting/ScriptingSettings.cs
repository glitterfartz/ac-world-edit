using ACWorldEdit.Assets;

namespace ACWorldEdit.Data.Scripting;

/// <summary>
/// The scripting settings, free of the Godot config file: the target host, auto-export and last
/// document are global (<c>[scripting]</c>); the workspace and export directory are per client
/// profile (<c>[scripting.wrath]</c>, <c>[scripting.retail]</c>). Paths are returned as saved
/// (<c>user://…</c>); the UI globalises them.
/// </summary>
public static class ScriptingSettings
{
    public const string Section = "scripting";
    public const string TargetHostDefault = "ale-azerothcore";

    public static string ProfileSection(ClientKind kind) => Section + "." + SettingsResolver.ProfileName(kind);

    public static string DefaultWorkspace(ClientKind kind) =>
        "user://scripts/" + SettingsResolver.ProfileName(kind);

    public static string TargetHost(Func<string, string, string, string> read) =>
        Blank(read(Section, "target_host", "")) ?? TargetHostDefault;

    public static bool AutoExport(Func<string, string, string, string> read) =>
        IsOn(read(Section, "auto_export", ""));

    public static string LastDocument(Func<string, string, string, string> read) =>
        read(Section, "last_document", "");

    public static string Workspace(ClientKind kind, Func<string, string, string, string> read) =>
        Blank(read(ProfileSection(kind), "workspace", "")) ?? DefaultWorkspace(kind);

    public static string ExportDirectory(ClientKind kind, Func<string, string, string, string> read) =>
        read(ProfileSection(kind), "export_dir", "");

    /// <summary>Whether a saved target host names a known scripting host.</summary>
    public static bool KnownHost(string host) =>
        host is "ale-azerothcore" or "eluna-trinitycore";

    static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    static bool IsOn(string? value) =>
        value is not null && (value.Equals("1", StringComparison.Ordinal)
            || value.Equals("true", StringComparison.OrdinalIgnoreCase)
            || value.Equals("yes", StringComparison.OrdinalIgnoreCase));
}
