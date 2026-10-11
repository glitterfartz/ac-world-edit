using System.Text;
namespace ACWorldEdit.Assets;

public sealed record MapInfo(int Id, string Directory, string Name);
public sealed class AssetStore : IDisposable
{
    readonly List<MpqArchive> archives = [];
    readonly object gate = new();
    public int ArchiveCount => archives.Count;
    public AssetStore(string directory) : this(directory, ClientMountProfile.StockEnUs) { }
    public AssetStore(string directory, ClientMountProfile profile)
    {
        if (!System.IO.Directory.Exists(directory)) throw new DirectoryNotFoundException(directory);
        var paths = profile.Resolve(directory);
        try { foreach (var path in paths) archives.Add(new MpqArchive(path)); }
        catch { Dispose(); throw; }
        if (archives.Count == 0) throw new FileNotFoundException("No MPQ archives found in " + directory);
    }
    public byte[] Read(string path)
    {
        lock (gate)
        {
            for (int i = archives.Count - 1; i >= 0; i--)
            { var bytes = archives[i].Read(path); if (bytes != null) return bytes; }
        }
        throw new FileNotFoundException("Asset not found: " + path);
    }
    public bool Exists(string path) { lock (gate) return archives.Any(a => a.Contains(path)); }
    public string Source(string path) { lock (gate) return archives.LastOrDefault(a => a.Contains(path))?.Path ?? "missing"; }
    public List<MapInfo> Maps()
    {
        byte[] b = Read(@"DBFilesClient/Map.dbc");
        if (Encoding.ASCII.GetString(b, 0, 4) != "WDBC") throw new InvalidDataException("Expected Wrath Map.dbc");
        int records = checked((int)MpqArchive.U(b, 4)), fields = checked((int)MpqArchive.U(b, 8));
        int stride = checked((int)MpqArchive.U(b, 12)), strings = checked(20 + records * stride);
        if (fields < 6 || stride < 24 || strings > b.Length) throw new InvalidDataException("Bad Map.dbc header");
        string Str(int offset)
        { int p = checked(strings + offset); if (p < strings || p >= b.Length) return ""; int end = Array.IndexOf(b, (byte)0, p); return end < 0 ? "" : Encoding.UTF8.GetString(b, p, end - p); }
        List<MapInfo> maps = [];
        for (int i = 0; i < records; i++)
        {
            int p = 20 + i * stride; string dir = Str((int)MpqArchive.U(b, p + 4)); string name = "";
            for (int locale = 0; locale < 16 && 5 + locale < fields; locale++)
            { name = Str((int)MpqArchive.U(b, p + (5 + locale) * 4)); if (name.Length > 0) break; }
            if (dir.Length > 0 && Exists($@"World/Maps/{dir}/{dir}.wdt")) maps.Add(new((int)MpqArchive.U(b, p), dir, name.Length == 0 ? dir : name));
        }
        return maps.OrderBy(m => m.Name).ToList();
    }
    public void Dispose() { lock (gate) { foreach (var a in archives) a.Dispose(); archives.Clear(); } }
}
