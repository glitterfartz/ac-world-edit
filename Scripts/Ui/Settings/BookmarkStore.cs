using System.Text.Json;
using ACWorldEdit.Assets;
using ACWorldEdit.Data;
using Godot;

namespace ACWorldEdit;

/// <summary>A saved camera location: the map and server position to teleport to, the view angles
/// to restore, and the map/zone names shown in the table (resolved when the bookmark is saved).</summary>
public sealed record CameraBookmark(uint Map, double X, double Y, double Z, float Yaw, float Pitch, string MapName, string Zone);

/// <summary>
/// Camera bookmarks in user://bookmarks/ (AppData), one file per client profile: map IDs mean
/// different maps per client. Smoke runs use a throwaway folder so diagnostics never touch the
/// user's real bookmarks. A missing or corrupt file reads as no bookmarks.
/// </summary>
public static class BookmarkStore
{
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    static bool Diagnostic => OS.GetCmdlineUserArgs().Any(a => a.EndsWith("-smoke", StringComparison.Ordinal) || a.StartsWith("--client-shot", StringComparison.Ordinal));

    public static string PathFor(ClientKind kind) =>
        Diagnostic ? ProjectSettings.GlobalizePath($"res://artifacts/bookmarks/{Guid.NewGuid():N}.json")
            : System.IO.Path.Combine(ProjectSettings.GlobalizePath("user://bookmarks"), SettingsResolver.ProfileName(kind) + ".json");

    public static List<CameraBookmark> Load(string path)
    {
        try
        {
            return JsonSerializer.Deserialize<List<CameraBookmark>>(System.IO.File.ReadAllText(path), Json) ?? [];
        }
        catch (Exception)
        {
            return [];
        }
    }

    public static void Save(string path, IReadOnlyList<CameraBookmark> bookmarks)
    {
        string? folder = System.IO.Path.GetDirectoryName(path);
        if (folder != null)
        {
            System.IO.Directory.CreateDirectory(folder);
        }

        System.IO.File.WriteAllText(path, JsonSerializer.Serialize(bookmarks, Json));
    }
}
