using System.Text;

namespace ACWorldEdit.Assets;

/// <summary>
/// Client Map.dbc identity and display name. Directory selects WDT/ADT asset paths; Id is the
/// server database map ID.
/// </summary>
public sealed record MapInfo(int Id, string Directory, string Name);
/// <summary>
/// Read-only mounted MPQ set with explicit stock-client precedence. Reads share a lock because
/// archive readers seek mutable streams; detached decoded bytes can safely cross worker
/// boundaries.
/// </summary>
public sealed class AssetStore : IClientFiles
{
    readonly List<MpqArchive> archives = [];
    readonly object gate = new();
    public int ArchiveCount => archives.Count;
    public ClientKind Kind => ClientKind.Wrath335;
    public string Build { get; }

    /// <summary>
    /// Mount only the configured archive list. Failure closes every archive already opened; the
    /// client directory is never modified.
    /// </summary>
    public AssetStore(string directory) : this(directory, ClientMountProfile.StockEnUs)
    {
    }

    /// <summary>
    /// Mount only the configured archive list. Failure closes every archive already opened; the
    /// client directory is never modified.
    /// </summary>
    public AssetStore(string directory, ClientMountProfile profile)
    {
        if (!System.IO.Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException(directory);
        }

        var paths = profile.Resolve(directory);
        Build = profile.Build;
        try
        {
            foreach (var path in paths)
            {
                archives.Add(new MpqArchive(path));
            }
        }
        catch
        {
            Dispose();
            throw;
        }

        if (archives.Count == 0)
        {
            throw new FileNotFoundException("No MPQ archives found in " + directory);
        }
    }

    /// <summary>
    /// Resolve an asset from the highest-priority archive and return detached bytes for worker
    /// parsing. Missing assets fail rather than silently selecting a different client profile.
    /// </summary>
    public byte[] Read(string path)
    {
        if (AssetRef.IsFileReference(path))
        {
            throw new NotSupportedException("MPQ clients address files by path, not FileDataID: " + path);
        }

        lock (gate)
        {
            for (int i = archives.Count - 1; i >= 0; i--)
            {
                var bytes = archives[i].Read(path);
                if (bytes != null)
                {
                    return bytes;
                }
            }
        }

        throw new FileNotFoundException("Asset not found: " + path);
    }

    /// <summary>
    /// Check mounted archive membership under the same lock used by reads.
    /// </summary>
    public bool Exists(string path)
    {
        if (AssetRef.IsFileReference(path))
        {
            return false;
        }

        lock (gate)
        {
            return archives.Any(a => a.Contains(path));
        }
    }

    /// <summary>
    /// Report the winning archive path for validation/provenance with the same precedence as Read.
    /// </summary>
    public string Source(string path)
    {
        if (AssetRef.IsFileReference(path))
        {
            return "missing";
        }

        lock (gate)
        {
            return archives.LastOrDefault(a => a.Contains(path))?.Path ?? "missing";
        }
    }

    /// <summary>
    /// Discover maps from Map.dbc, select the first available localized name and retain only
    /// directories with a mounted WDT. Global-WMO interiors remain eligible.
    /// </summary>
    public List<MapInfo> Maps()
    {
        // Map.dbc: field 0 ID, field 1 directory, fields 5..20 localized names (one per locale).
        var table = new DbcTable(Read(@"DBFilesClient/Map.dbc"));
        List<MapInfo> maps = [];
        foreach (uint id in table.Ids)
        {
            string dir = table.Str(id, 1);
            string name = "";
            for (int locale = 0; locale < 16 && name.Length == 0; locale++)
            {
                name = table.Str(id, 5 + locale);
            }

            if (dir.Length > 0 && Exists($@"World/Maps/{dir}/{dir}.wdt"))
            {
                maps.Add(new((int)id, dir, name.Length == 0 ? dir : name));
            }
        }

        return maps.OrderBy(m => m.Name).ToList();
    }

    /// <summary>
    /// Close streams under the archive lock. Owners must first cancel and await workers that could
    /// still request assets.
    /// </summary>
    public void Dispose()
    {
        lock (gate)
        {
            foreach (var a in archives)
            {
                a.Dispose();
            }

            archives.Clear();
        }
    }
}
