namespace ACWorldEdit.Assets;

/// <summary>
/// Archive allowlist and ordering for one client build/locale. Later archives override earlier
/// ones; backups and HD overlays are not discovered implicitly.
/// </summary>
public sealed record ClientMountProfile(string Build, string Locale, IReadOnlyList<string> Archives)
{
    public static ClientMountProfile StockEnUs { get; } = new("3.3.5.12340", "enUS", Array.AsReadOnly(new[] { "common.MPQ", "common-2.MPQ", "expansion.MPQ", "lichking.MPQ", "enUS/base-enUS.MPQ", "enUS/locale-enUS.MPQ", "enUS/speech-enUS.MPQ", "enUS/expansion-locale-enUS.MPQ", "enUS/expansion-speech-enUS.MPQ", "enUS/lichking-locale-enUS.MPQ", "enUS/lichking-speech-enUS.MPQ", "patch.MPQ", "enUS/patch-enUS.MPQ", "patch-2.MPQ", "enUS/patch-enUS-2.MPQ", "patch-3.MPQ", "enUS/patch-enUS-3.MPQ" }));

    /// <summary>
    /// Resolve required archives inside the supplied directory and fail on missing files. Returned
    /// order is part of the lookup contract.
    /// </summary>
    public string[] Resolve(string directory)
    {
        if (Build != "3.3.5.12340" || Locale != "enUS")
        {
            throw new NotSupportedException($"Unsupported client profile {Build}/{Locale}; only stock 3.3.5.12340/enUS is currently configured.");
        }

        string root = Path.GetFullPath(directory);
        var paths = Archives.Select(relative =>
        {
            if (Path.IsPathRooted(relative))
            {
                throw new InvalidDataException("Mount paths must be relative.");
            }

            string path = Path.GetFullPath(Path.Combine(root, relative));
            if (!path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Mount escapes client data root: " + relative);
            }

            if (!File.Exists(path))
            {
                throw new FileNotFoundException("Required profile archive missing: " + relative, path);
            }

            return path;
        }).ToArray();
        if (paths.Length == 0 || paths.Distinct(StringComparer.OrdinalIgnoreCase).Count() != paths.Length)
        {
            throw new InvalidDataException("Mount profile must contain unique archives.");
        }

        return paths;
    }
}
