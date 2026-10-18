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
    public void EveryMapWdtParsesAndOneTilePerTerrainMapDecodes()
    {
        var failures = new SortedDictionary<string, List<string>>();
        void Fail(string kind, string map)
        {
            if (!failures.TryGetValue(kind, out var list))
            {
                failures[kind] = list = [];
            }

            list.Add(map);
        }

        int terrainMaps = 0, checkedLayers = 0;
        foreach (var info in RetailData.Tables.Maps().Where(m => m.WdtFileId != 0))
        {
            WorldMap map;
            try
            {
                map = new WorldMap(info.Directory, RetailData.Files.Read(AssetRef.FromFileId(info.WdtFileId)));
            }
            catch (Exception e)
            {
                // Known gap until the retail WMO milestone: global-WMO placements use FileDataIDs, not MWMO names.
                Fail(e.GetType().Name + ": " + e.Message, info.Directory);
                continue;
            }

            if (map.Tiles.Count == 0)
            {
                continue;
            }

            terrainMaps++;
            var key = map.Tiles.OrderBy(k => k.X * 64 + k.Y).ElementAt(map.Tiles.Count / 2);
            try
            {
                var tile = map.ReadTile(RetailData.Files, key);
                foreach (string texture in tile.Chunks.SelectMany(c => c.Layers).Select(l => l.Texture).Where(t => t.Length > 0).Distinct())
                {
                    BlpTexture.Decode(RetailData.Files.Read(texture));
                }

                checkedLayers += CheckUncompressedAlphaSpans(map, key);
            }
            catch (Exception e)
            {
                Fail("tile " + e.GetType().Name + ": " + e.Message, info.Directory);
            }
        }

        Assert.True(terrainMaps > 20, $"only {terrainMaps} terrain maps");
        // Only maps whose sole content is a global WMO (placed by FileDataID) are an accepted gap until the WMO milestone.
        var unexpected = failures.Where(f => !f.Key.Contains("placement", StringComparison.OrdinalIgnoreCase) && !f.Key.Contains("neither terrain nor a global WMO", StringComparison.Ordinal)).ToList();
        Assert.True(unexpected.Count == 0, string.Join("; ", unexpected.Select(f => $"{f.Key}: {string.Join(",", f.Value)}")));
        Assert.True(checkedLayers > 0, "no uncompressed alpha layer was checked");
    }

    /// <summary>
    /// The map's BigAlpha flag decides 4096- versus 2048-byte uncompressed alpha; the span between a
    /// layer's offset and the next layer (or the end of MCAL) must agree. Returns layers checked.
    /// </summary>
    static int CheckUncompressedAlphaSpans(WorldMap map, TileKey key)
    {
        var bytes = RetailData.Files.Read(AssetRef.FromFileId(map.Files[key].Textures));
        int checkedLayers = 0;
        foreach (var chunk in Chunks.Read(bytes).Where(c => c.Tag == "MCNK"))
        {
            var subs = Chunks.Read(bytes, chunk.Data, chunk.Data + chunk.Size).ToList();
            var layers = subs.First(s => s.Tag == "MCLY");
            var alpha = subs.FirstOrDefault(s => s.Tag == "MCAL");
            int count = layers.Size / 16;
            for (int i = 1; i < count; i++)
            {
                uint flags = MpqArchive.U(bytes, layers.Data + i * 16 + 4);
                if ((flags & 0x100) == 0 || (flags & 0x200) != 0)
                {
                    continue;
                }

                int offset = (int)MpqArchive.U(bytes, layers.Data + i * 16 + 8);
                int next = i + 1 < count ? (int)MpqArchive.U(bytes, layers.Data + (i + 1) * 16 + 8) : alpha.Size;
                Assert.True(next - offset == (map.BigAlpha ? 4096 : 2048), $"{map.Name} {key}: uncompressed alpha span {next - offset} with BigAlpha={map.BigAlpha}");
                checkedLayers++;
            }
        }

        return checkedLayers;
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
