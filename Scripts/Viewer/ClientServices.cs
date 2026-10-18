using ACWorldEdit.Assets;

namespace ACWorldEdit;

/// <summary>
/// Everything the viewer needs from one opened client, chosen once at load time so no parser has to
/// branch on the client version. Lighting and area names are null where a client family does not
/// provide them yet; the creature and GameObject catalogs read 3.3.5 DBC tables and are null for clients without database-backed spawns; DatabaseEnabled is false when the world database cannot describe that client.
/// Open runs on a worker: it reads archives and tables but creates no Godot resources.
/// </summary>
public sealed record ClientServices(IClientFiles Files, List<MapInfo> Maps, ClientLighting? Lighting, AreaLookup? Areas, bool DatabaseEnabled, string Description, CreatureCatalog? Creatures = null, GameObjectCatalog? GameObjects = null)
{
    public static ClientServices Open(ClientKind kind, string directory, string? listfile = null) =>
        kind == ClientKind.Retail ? OpenRetail(directory, listfile) : OpenWrath(directory);

    /// <summary>
    /// Stock 3.3.5.12340 MPQs. Failure disposes the partly opened store.
    /// </summary>
    public static ClientServices OpenWrath(string directory)
    {
        var store = new AssetStore(ClientFolder.Resolve(ClientKind.Wrath335, directory));
        try
        {
            var maps = store.Maps();
            return new ClientServices(store, maps, ClientLighting.TryLoad(store), AreaLookup.TryLoad(store), true, $"WRATH {store.Build} · {store.ArchiveCount} archives · {maps.Count} maps", new CreatureCatalog(store), new GameObjectCatalog(store));
        }
        catch
        {
            store.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Retail CASC storage. The world database targets a different server version, so database
    /// features are off. Lighting and liquids are loaded from the DB2 tables; area names are not yet
    /// and stay null.
    /// </summary>
    public static ClientServices OpenRetail(string directory, string? listfile = null)
    {
        string definitions = Db2Tables.FindDefinitions(System.Environment.GetEnvironmentVariable("ACWORLD_DBD_DIR"), AppContext.BaseDirectory)
            ?? throw new FileNotFoundException("Retail table definitions (Data/definitions) were not found next to the game.");
        CascFiles files;
        try
        {
            files = new CascFiles(directory);
        }
        catch (DllNotFoundException)
        {
            throw new FileNotFoundException("CascLib.dll was not found; see Tools/get_casclib.py. The Wrath profile does not need it.");
        }

        try
        {
            // The listfile holds millions of entries and parses independently of the DB2 tables, so
            // it loads alongside the map table; the resolver is installed only afterwards because
            // the liquid tables resolve texture paths through it.
            var listing = listfile != null && File.Exists(listfile) ? Task.Run(() => PathListfile.Load(listfile)) : null;
            var tables = new Db2Tables(files, definitions);
            var maps = tables.Maps();
            // Paths (old-style placements, WMO textures and groups, liquid texture sequences) map to
            // FileDataIDs through the downloaded community listfile; without it they stay unreadable.
            if (listing != null)
            {
                files.PathResolver = listing.GetAwaiter().GetResult().Resolve;
            }

            RetailLiquids.Register(files, RetailLiquids.Load(tables));
            return new ClientServices(files, maps, ClientLighting.TryLoadRetail(tables), null, false, $"RETAIL {files.Build} · {maps.Count} maps");
        }
        catch
        {
            // An in-flight listfile load only reads its own file, so it can finish unobserved.
            files.Dispose();
            throw;
        }
    }
}
