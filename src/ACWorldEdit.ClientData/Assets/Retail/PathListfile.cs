namespace ACWorldEdit.Assets;

/// <summary>
/// Path to FileDataID map for the retail files the client names by path (old-style WDT placements,
/// WMO textures and groups, liquid texture sequences). Reads "id;path" lines of the community listfile
/// (<see cref="ListfileStore"/> downloads it). The full listfile has millions of paths, so entries are
/// keyed by a 64-bit hash of the normalized path instead of keeping every string. Lookups ignore case
/// and treat '\' as '/'.
/// </summary>
public sealed class PathListfile
{
    readonly Dictionary<ulong, uint> ids;

    PathListfile(int capacity)
    {
        ids = new(capacity);
    }

    public int Count => ids.Count;

    public static PathListfile Load(string file)
    {
        // Lines average roughly 50 bytes; sizing up front avoids rehashing millions of entries.
        var result = new PathListfile((int)Math.Min(new FileInfo(file).Length / 50, int.MaxValue / 2));
        foreach (string line in File.ReadLines(file))
        {
            int split = line.IndexOf(';');
            if (split > 0 && uint.TryParse(line.AsSpan(0, split), out uint id))
            {
                result.ids[Key(line.AsSpan(split + 1))] = id;
            }
        }

        return result;
    }

    public uint? Resolve(string path) => ids.TryGetValue(Key(path), out uint id) ? id : null;

    /// <summary>
    /// FNV-1a over the trimmed, lower-cased path with '\' folded to '/'. At a few million entries a
    /// 64-bit key makes a false match vanishingly unlikely.
    /// </summary>
    static ulong Key(ReadOnlySpan<char> path)
    {
        ulong hash = 14695981039346656037;
        foreach (char c in path.Trim())
        {
            hash = (hash ^ (c == '\\' ? '/' : char.ToLowerInvariant(c))) * 1099511628211;
        }

        return hash;
    }
}
