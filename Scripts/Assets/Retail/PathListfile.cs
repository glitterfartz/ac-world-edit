namespace ACWorldEdit.Assets;

/// <summary>
/// Path to FileDataID map for the few retail files the client tables name by path (liquid texture
/// sequences). Reads "id;path" lines of the community listfile format; Data/liquid-listfile.csv is a
/// trimmed copy of it. Lookups ignore case and treat '\' as '/'.
/// </summary>
public sealed class PathListfile
{
    public const string FileName = "liquid-listfile.csv";

    readonly Dictionary<string, uint> ids = new(StringComparer.OrdinalIgnoreCase);

    public int Count => ids.Count;

    public static PathListfile Load(string file)
    {
        var result = new PathListfile();
        foreach (string line in File.ReadLines(file))
        {
            int split = line.IndexOf(';');
            if (split > 0 && uint.TryParse(line.AsSpan(0, split), out uint id))
            {
                result.ids[Normalize(line[(split + 1)..])] = id;
            }
        }

        return result;
    }

    public uint? Resolve(string path) => ids.TryGetValue(Normalize(path), out uint id) ? id : null;

    static string Normalize(string path) => path.Trim().Replace('\\', '/');

    /// <summary>
    /// Find the listfile: beside the assembly, then Data/ in the nearest ancestor (the repo during
    /// development). Null when absent; retail liquids then draw without textures.
    /// </summary>
    public static string? Find(params string?[] directories)
    {
        foreach (string? directory in directories)
        {
            for (var current = string.IsNullOrEmpty(directory) ? null : new DirectoryInfo(directory); current != null; current = current.Parent)
            {
                foreach (string candidate in new[] { Path.Combine(current.FullName, FileName), Path.Combine(current.FullName, "Data", FileName) })
                {
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
            }
        }

        return null;
    }
}
