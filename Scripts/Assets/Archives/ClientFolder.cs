namespace ACWorldEdit.Assets;

/// <summary>
/// Cheap checks that a folder looks like the right kind of client before the heavy open, so the
/// picker can explain a wrong choice instead of surfacing an archive error. The real open still
/// validates everything; nothing here reads file contents or writes anything.
/// </summary>
public static class ClientFolder
{
    /// <summary>
    /// Null when the folder looks usable for <paramref name="kind"/>; otherwise a short reason.
    /// </summary>
    public static string? Validate(ClientKind kind, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "Choose a folder.";
        }

        if (!Directory.Exists(path))
        {
            return "That folder does not exist.";
        }

        if (kind == ClientKind.Retail)
        {
            string root = path.TrimEnd('\\', '/');
            string name = Path.GetFileName(root);
            if (name.StartsWith('_') && name.EndsWith('_'))
            {
                root = Directory.GetParent(root)?.FullName ?? root;
            }

            return File.Exists(Path.Combine(root, ".build.info"))
                ? null
                : "No .build.info here. Choose the World of Warcraft install folder (the one holding .build.info) or a product folder inside it.";
        }

        // Either the Data folder or the client root above it is accepted; Resolve narrows to Data.
        return File.Exists(Path.Combine(path, "common.MPQ")) || File.Exists(Path.Combine(path, "Data", "common.MPQ"))
            ? null
            : "No common.MPQ here. Choose the 3.3.5 client's Data folder.";
    }

    /// <summary>
    /// The folder to open: a Wrath client root containing Data is narrowed to its Data folder so
    /// either selection works; everything else is unchanged.
    /// </summary>
    public static string Resolve(ClientKind kind, string path)
    {
        if (kind == ClientKind.Wrath335 && !File.Exists(Path.Combine(path, "common.MPQ")) && File.Exists(Path.Combine(path, "Data", "common.MPQ")))
        {
            return Path.Combine(path, "Data");
        }

        return path;
    }
}
