using ACWorldEdit.Assets;

namespace ACWorldEdit;

/// <summary>Client open: the Wrath MPQ and retail CASC halves.</summary>
public sealed partial record ClientServices
{
    public static ClientServices Open(ClientKind kind, string directory, string? listfile = null, Action<string>? warn = null) =>
        kind == ClientKind.Retail ? OpenRetail(directory, listfile, warn) : OpenWrath(directory, warn);

    /// <summary>
    /// Stock 3.3.5.12340 MPQs. Failure disposes the partly opened store.
    /// </summary>
    public static ClientServices OpenWrath(string directory, Action<string>? warn = null)
    {
        var store = new AssetStore(ClientFolder.Resolve(ClientKind.Wrath335, directory));
        try
        {
            var maps = store.Maps();
            return new ClientServices(store, maps, ClientLighting.TryLoad(store, warn), AreaLookup.TryLoadWrath(store), true, $"WRATH {store.Build} · {store.ArchiveCount} archives · {maps.Count} maps", new CreatureCatalog(store), new GameObjectCatalog(store));
        }
        catch
        {
            store.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Retail CASC storage, served by a TrinityCore world database. Lighting and liquids are loaded
    /// from the DB2 tables; area names are not yet and stay null.
    /// </summary>
    public static ClientServices OpenRetail(string directory, string? listfile = null, Action<string>? warn = null)
    {
        string definitions = Db2Tables.FindDefinitions(System.Environment.GetEnvironmentVariable("ACWORLD_DBD_DIR"), AppContext.BaseDirectory)
            ?? throw new FileNotFoundException(DependencyCheck.DefinitionsMissing);
        CascFiles files;
        try
        {
            files = new CascFiles(directory);
        }
        catch (DllNotFoundException)
        {
            throw new FileNotFoundException(DependencyCheck.CascLibMissing + " The Wrath profile does not need it.");
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
            return new ClientServices(files, maps, ClientLighting.TryLoadRetail(tables, warn), null, true, $"RETAIL {files.Build} · {maps.Count} maps", new RetailCreatureCatalog(tables).PrepareIndexes(), new RetailGameObjectCatalog(tables), new RetailClientLookup(tables, id => files.Exists("fdid:" + id.ToString(System.Globalization.CultureInfo.InvariantCulture))), tables.NavigationParents());
        }
        catch
        {
            // An in-flight listfile load only reads its own file, so it can finish unobserved.
            files.Dispose();
            throw;
        }
    }
}
