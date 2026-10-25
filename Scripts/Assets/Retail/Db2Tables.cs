using DBCD;
using DBCD.Providers;

namespace ACWorldEdit.Assets;

/// <summary>
/// Retail client tables (DB2/WDC5) read through DBCD with WoWDBDefs layouts. Column names and types
/// come from the definitions in the Data/WoWDBDefs submodule (CC BY-SA 4.0, unmodified, pinned by its commit),
/// never from guesses: a table or build the definitions do not describe fails with a clear message.
/// Tables are loaded on first use and cached; the instance is not thread-safe.
/// </summary>
public sealed class Db2Tables
{
    /// <summary>
    /// FileDataIDs of the DB2 files, found by matching each file's WDC5 layout hash against the
    /// definitions on build 12.1.5.70448. They are stable across builds, but a table that moves
    /// fails to match its layout and is reported rather than misread.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, uint> FileIds = new Dictionary<string, uint>
    {
        ["Map"] = 1349477,
        ["AreaTable"] = 1353545,
        ["Light"] = 1375579,
        ["LightData"] = 1375580,
        ["LightParams"] = 1334669,
        ["LightSkybox"] = 1308501,
        ["LiquidType"] = 1371380,
        ["LiquidMaterial"] = 1132538,
        ["LiquidObject"] = 1308058,
        // The ones below were taken from the community listfile (dbfilesclient/<name>.db2) and each
        // one is checked by the corpus test that loads every table against its layout hash.
        ["CreatureDisplayInfo"] = 1108759,
        ["CreatureModelData"] = 1365368,
        ["CreatureDisplayInfoExtra"] = 1264997,
        ["TextureFileData"] = 982459,
        ["CreatureDisplayInfoOption"] = 3692043,
        ["ChrCustomizationElement"] = 3512765,
        ["ChrCustomizationGeoset"] = 3456171,
        ["ChrCustomizationMaterial"] = 3459652,
        ["ChrCustomizationChoice"] = 3450554,
        ["ChrCustomizationOption"] = 3384247,
        ["ChrModelTextureLayer"] = 3548976,
        ["GameObjectDisplayInfo"] = 1266277,
        ["AreaTrigger"] = 1354401,
        ["AreaTriggerActionSet"] = 1352378,
        ["BroadcastText"] = 841604,
        ["Item"] = 841626,
        ["ItemSparse"] = 1572924,
        ["SpellName"] = 1990283,
        ["ContentTuning"] = 1962930,
    };

    readonly CascFiles files;
    readonly DBCD.DBCD database;
    readonly Dictionary<string, IDBCDStorage> tables = [];

    public string Build { get; }

    public Db2Tables(CascFiles files, string definitionsDirectory)
    {
        this.files = files;
        Build = files.Build;
        database = new DBCD.DBCD(new Provider(files), new FilesystemDBDProvider(definitionsDirectory));
    }

    /// <summary>
    /// Find the definitions folder: ACWORLD_DBD_DIR, then a definitions folder beside the assembly,
    /// then Data/WoWDBDefs/definitions in the nearest ancestor (the repo during development).
    /// </summary>
    public static string? FindDefinitions(string? overridePath, params string?[] directories)
    {
        if (!string.IsNullOrWhiteSpace(overridePath) && Directory.Exists(overridePath))
        {
            return overridePath;
        }

        foreach (string? directory in directories)
        {
            for (var current = string.IsNullOrEmpty(directory) ? null : new DirectoryInfo(directory); current != null; current = current.Parent)
            {
                string beside = Path.Combine(current.FullName, "definitions");
                if (File.Exists(Path.Combine(beside, "Map.dbd")))
                {
                    return beside;
                }

                string repo = Path.Combine(current.FullName, "Data", "WoWDBDefs", "definitions");
                if (File.Exists(Path.Combine(repo, "Map.dbd")))
                {
                    return repo;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Load one table. A missing definition for this build, an unknown table or an unreadable layout
    /// becomes an InvalidDataException that names the table and build.
    /// </summary>
    public IDBCDStorage Table(string name)
    {
        if (tables.TryGetValue(name, out var cached))
        {
            return cached;
        }

        if (!FileIds.ContainsKey(name))
        {
            throw new ArgumentException("Unknown client table: " + name);
        }

        try
        {
            var table = database.Load(name, Build, Locale.EnUS);
            tables[name] = table;
            return table;
        }
        catch (Exception e) when (e is not IOException and not OperationCanceledException)
        {
            throw new InvalidDataException($"Table {name} cannot be read for build {Build}: {e.Message}", e);
        }
    }

    /// <summary>
    /// Maps that have a WDT. Retail names a map's world file by FileDataID (Map.WdtFileDataID); a
    /// map is listed only when that file exists, so instances without terrain data do not appear.
    /// </summary>
    public List<MapInfo> Maps()
    {
        var maps = new List<MapInfo>();
        foreach (var row in Table("Map").Values)
        {
            uint wdt = row.FieldAs<uint>("WdtFileDataID");
            string directory = row.Field<string>("Directory") ?? "";
            if (wdt == 0 || directory.Length == 0 || !files.Exists(AssetRef.FromFileId(wdt)))
            {
                continue;
            }

            string name = row.Field<string>("MapName_lang") ?? "";
            // InstanceType 0 is an open-world map, which is what "continent" means for choosing a start location.
            maps.Add(new MapInfo(row.ID, directory, name.Length == 0 ? directory : name, wdt, row.FieldAs<int>("InstanceType") == 0));
        }

        return maps.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Provider that serves DB2 bytes by FileDataID; encrypted sections without a key are zero-filled
    /// and reported through CascFiles.PartialDb2Files.
    /// </summary>
    sealed class Provider(CascFiles files) : IDBCProvider
    {
        public Stream StreamForTableName(string tableName, string build)
        {
            if (!FileIds.TryGetValue(tableName, out uint id))
            {
                throw new FileNotFoundException("No FileDataID is known for table " + tableName);
            }

            return new MemoryStream(files.ReadAvailableDb2(id), false);
        }
    }
}
