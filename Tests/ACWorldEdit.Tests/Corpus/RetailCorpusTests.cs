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

    static WorldMap Azeroth()
    {
        var azeroth = RetailData.Tables.Maps().Single(m => m.Directory.Equals("azeroth", StringComparison.OrdinalIgnoreCase));
        return new WorldMap(azeroth.Directory, RetailData.Files.Read(AssetRef.FromFileId(azeroth.WdtFileId)));
    }

    [RetailClientFact]
    public void AzerothWdtListsSplitTileFiles()
    {
        var map = Azeroth();
        Assert.True(map.IsSplit);
        Assert.True(map.BigAlpha);
        Assert.Equal(map.Tiles.Count, map.Files.Count);
        Assert.All(map.Files.Values, f => Assert.True(RetailData.Files.Exists(AssetRef.FromFileId(f.Root)) && RetailData.Files.Exists(AssetRef.FromFileId(f.Textures))));
    }

    [RetailClientFact]
    public void SampledAzerothTilesDecodeWithTexturesAndAlpha()
    {
        var map = Azeroth();
        // Elwynn plus a spread of other tiles, so both compressed and uncompressed alpha and varying MDID sizes occur.
        var keys = new[] { new TileKey(32, 48) }.Concat(map.Tiles.OrderBy(k => k.X * 64 + k.Y).Where((_, i) => i % 97 == 0)).Distinct().ToList();
        Assert.True(keys.Count >= 8);
        foreach (var key in keys)
        {
            var tile = map.ReadTile(RetailData.Files, key);
            Assert.Equal(256, tile.Chunks.Count);
            Assert.All(tile.Chunks, c => Assert.True(float.IsFinite(c.Height) && c.Heights.All(float.IsFinite), $"{key} chunk {c.X},{c.Y}"));
            Assert.Contains(tile.Chunks, c => c.Layers.Count > 0);
            foreach (var layer in tile.Chunks.SelectMany(c => c.Layers).Skip(1).Where(l => l.Alpha.Length > 0).Take(50))
            {
                Assert.Equal(4096, layer.Alpha.Length);
            }

            foreach (string texture in tile.Chunks.SelectMany(c => c.Layers).Select(l => l.Texture).Distinct())
            {
                Assert.True(AssetRef.TryParse(texture, out _), texture);
                var blp = BlpTexture.Decode(RetailData.Files.Read(texture));
                Assert.True(blp.Width > 0 && blp.Height > 0, texture);
            }
        }
    }

    [RetailClientFact]
    public void ElwynnChunksSitAtTheirServerPositions()
    {
        // Chunk (x, y) of tile (tx, ty) starts at world (17066.67 - 533.33*ty - 33.33*y, 17066.67 - 533.33*tx - 33.33*x).
        var tile = Azeroth().ReadTile(RetailData.Files, new(32, 48));
        double Tile = 1600.0 / 3.0, Chunk = Tile / 16, Origin = 32 * Tile;
        foreach (var c in tile.Chunks.Where(c => (c.X + c.Y) % 5 == 0))
        {
            Assert.InRange(c.ServerX, Origin - (48 + 1) * Tile + 1, Origin - 48 * Tile + 1);
            Assert.InRange(c.ServerY, Origin - (32 + 1) * Tile + 1, Origin - 32 * Tile + 1);
            Assert.Equal(Origin - 48 * Tile - c.Y * Chunk, c.ServerX, 0.5);
            Assert.Equal(Origin - 32 * Tile - c.X * Chunk, c.ServerY, 0.5);
        }
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
