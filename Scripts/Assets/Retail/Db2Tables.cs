using DBCD;
using DBCD.Providers;

namespace ACWorldEdit.Assets;

/// <summary>
/// Retail client tables (DB2/WDC5) read through DBCD with WoWDBDefs layouts. Column names and types
/// come from the definitions in the Data/WoWDBDefs submodule (CC BY-SA 4.0, unmodified, pinned by its commit),
/// never from guesses: a table or build the definitions do not describe fails with a clear message.
/// Tables are loaded on first use and cached. Table may be called from any thread (loads are
/// serialised); a loaded table is read-only.
/// </summary>
public sealed class Db2Tables
{
    /// <summary>
    /// FileDataIDs of the DB2 files, found by matching each file's WDC5 layout hash against the
    /// definitions on build 12.1.5.70448. They are stable across builds and shared with the WoW:
    /// Forever product (same install, same FileDataIDs), but a table that moves fails to match its
    /// layout and is reported rather than misread.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, uint> FileIds = new Dictionary<string, uint>
    {
        ["Map"] = 1349477,
        ["AreaTable"] = 1353545,
        ["Light"] = 1375579,
        ["LightData"] = 1375580,
        ["LightParams"] = 1334669,
        ["LightSkybox"] = 1308501,
        ["VolumeFogCondition"] = 3587230,
        ["LightShaft"] = 5350589,
        ["LightParamsLightShaft"] = 5350588,
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
        ["Path"] = 801732,
        ["PathNode"] = 801733,
        ["PathProperty"] = 801735,
        ["Location"] = 801707,
        ["BroadcastText"] = 841604,
        ["Item"] = 841626,
        ["ItemSparse"] = 1572924,
        ["SpellName"] = 1990283,
        ["ContentTuning"] = 1962930,
        ["ContentTuningXExpected"] = 2976765,
        ["ExpectedStat"] = 1937326,
        ["ExpectedStatMod"] = 1969773,
        ["MythicPlusSeason"] = 2400282,
        ["MapDifficulty"] = 1367868,
        ["CurrencyTypes"] = 1095531,
        ["ItemExtendedCost"] = 801681,
        ["PlayerCondition"] = 1045411,
        ["ItemBonusList"] = 1805807,
        ["SkillLine"] = 1240935,
        ["Difficulty"] = 1352127,
        ["Languages"] = 1135325,
        ["GossipNPCOption"] = 4278503,
        // Editor reference names: currencies, skills, factions, mails, emotes, races and quest text.
        ["SpellMisc"] = 1003144,
        ["QuestInfo"] = 1120960,
        ["QuestSort"] = 1134585,
        ["MailTemplate"] = 1240344,
        ["ChrRaces"] = 1305311,
        ["CharTitles"] = 1349054,
        ["Emotes"] = 1343602,
        ["Faction"] = 1361972,
        // What an item template does not store: its level squish and the tables its stats, armor, damage and durability come from.
        ["ItemSquishEra"] = 7322706,
        ["Curve"] = 892585,
        ["CurvePoint"] = 892586,
        ["RandPropPoints"] = 1310245,
        ["ItemArmorQuality"] = 1283021,
        ["ItemArmorTotal"] = 1283022,
        ["ItemArmorShield"] = 1277741,
        ["ArmorLocation"] = 1284818,
        ["ItemDamageOneHand"] = 1277743,
        ["ItemDamageOneHandCaster"] = 1277739,
        ["ItemDamageTwoHand"] = 1277738,
        ["ItemDamageTwoHandCaster"] = 1277742,
        ["ItemDamageAmmo"] = 1277740,
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
    /// Load one table. DBCD resolves the layout from the file's own hash, so a build newer than the
    /// pinned definitions still loads when the layout is unchanged; an unknown table, an unreadable
    /// layout or a missing definition becomes an InvalidDataException that names the table and build.
    /// </summary>
    public IDBCDStorage Table(string name)
    {
        lock (tables)
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
    }

    /// <summary>Complete Map table, including rows without selectable client terrain.</summary>
    public Dictionary<int, int> NavigationParents() => Table("Map").Values
        .Select(row => (Id: row.ID, Parent: row.FieldAs<int>("ParentMapID"), Cosmetic: row.FieldAs<int>("CosmeticParentMapID")))
        .Where(row => row.Parent >= 0 || row.Cosmetic >= 0)
        .ToDictionary(row => row.Id, row => row.Parent >= 0 ? row.Parent : row.Cosmetic);

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
