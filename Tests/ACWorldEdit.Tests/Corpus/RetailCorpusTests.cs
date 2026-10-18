using ACWorldEdit.Assets;
using ACWorldEdit.Tests.Support;
using Xunit;

/// <summary>
/// Read-only checks against a real retail install (ACWORLD_RETAIL_DIR): CASC access by FileDataID,
/// DB2 tables through the pinned definitions, and the map list. Skipped when the client, CascLib.dll
/// or the definitions are absent. Visual output is not covered.
/// </summary>
[Trait("Category", "Corpus")]
public class RetailCorpusTests
{
    [RetailClientFact]
    public void OpensTheInstallAndReportsItsBuild()
    {
        var files = RetailData.Files;
        Assert.Equal(ClientKind.Retail, files.Kind);
        Assert.Matches(@"^\d+\.\d+\.\d+\.\d+$", files.Build);
    }

    [RetailClientFact]
    public void EveryKnownTableLoadsWithRows()
    {
        foreach (string name in Db2Tables.FileIds.Keys)
        {
            var table = RetailData.Tables.Table(name);
            Assert.True(table.Count > 0, name + " has no rows");
        }
    }

    [RetailClientFact]
    public void UnknownTablesAreRefused()
    {
        Assert.Throws<ArgumentException>(() => RetailData.Tables.Table("NoSuchTable"));
    }

    [RetailClientFact]
    public void MapListIncludesTheEasternKingdoms()
    {
        var maps = RetailData.Tables.Maps();
        var azeroth = Assert.Single(maps, m => m.Directory.Equals("azeroth", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(0, azeroth.Id);
        Assert.NotEqual(0u, azeroth.WdtFileId);
        Assert.True(RetailData.Files.Exists(AssetRef.FromFileId(azeroth.WdtFileId)));
        Assert.True(maps.Count > 50);
    }

    [RetailClientFact]
    public void TheExistingWdtParserReadsARetailWdt()
    {
        var azeroth = RetailData.Tables.Maps().Single(m => m.Directory.Equals("azeroth", StringComparison.OrdinalIgnoreCase));
        var map = new WorldMap(azeroth.Directory, RetailData.Files.Read(AssetRef.FromFileId(azeroth.WdtFileId)));
        Assert.True(map.Tiles.Count > 100);
        Assert.Contains(new TileKey(32, 48), map.Tiles);
    }

    [RetailClientFact]
    public void PathsFailClosedWithoutAResolver()
    {
        var files = RetailData.Files;
        files.PathResolver = null;
        Assert.Throws<FileNotFoundException>(() => files.Read("world/maps/azeroth/azeroth.wdt"));
        Assert.False(files.Exists("world/maps/azeroth/azeroth.wdt"));
        Assert.Throws<InvalidDataException>(() => files.Read("fdid:12x"));
    }

    [RetailClientFact]
    public void PathsReadThroughTheResolver()
    {
        var files = RetailData.Files;
        var azeroth = RetailData.Tables.Maps().Single(m => m.Directory.Equals("azeroth", StringComparison.OrdinalIgnoreCase));
        try
        {
            files.PathResolver = path => path == "world/maps/azeroth/azeroth.wdt" ? azeroth.WdtFileId : null;
            Assert.Equal(files.Read(AssetRef.FromFileId(azeroth.WdtFileId)), files.Read("world/maps/azeroth/azeroth.wdt"));
            Assert.Equal("casc:" + files.Build, files.Source("world/maps/azeroth/azeroth.wdt"));
            Assert.Equal("missing", files.Source("world/maps/other.wdt"));
        }
        finally
        {
            files.PathResolver = null;
        }
    }

    [RetailClientFact]
    public void MissingFileIdsReportAsMissing()
    {
        Assert.False(RetailData.Files.Exists(AssetRef.FromFileId(uint.MaxValue)));
        Assert.Throws<CascReadException>(() => RetailData.Files.Read(AssetRef.FromFileId(uint.MaxValue)));
    }
}
