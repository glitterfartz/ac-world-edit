using Godot;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// Where the local draft files live (user:// profile, or a throwaway folder for diagnostics) and
/// the shared content draft opened from there. The draft logic itself is Foundation's ContentDrafts.
/// </summary>
public static class ContentDraftFiles
{
    /// <summary>
    /// Absolute path of a local draft file. Any "*-smoke" or "--client-shot*" run gets a unique
    /// throwaway folder under artifacts/ so diagnostics never open or lock the user's real drafts.
    /// </summary>
    public static string LocalPath(string file)
    {
        bool smoke = OS.GetCmdlineUserArgs().Any(a => a.EndsWith("-smoke", StringComparison.Ordinal) || a.StartsWith("--client-shot", StringComparison.Ordinal));
        return ProjectSettings.GlobalizePath(smoke ? $"res://artifacts/drafts/{Guid.NewGuid():N}/{file}" : $"user://drafts/{file}");
    }

    public static ContentDrafts Open() => new(LocalPath("interactions.json"));
}
