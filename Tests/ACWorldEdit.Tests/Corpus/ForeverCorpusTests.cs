using ACWorldEdit.Assets;
using ACWorldEdit.Tests.Support;
using Xunit;

/// <summary>
/// Read-only checks against an installed WoW: Forever client (ACWORLD_RETAIL_DIR with an active
/// wowf row): the product opens from the shared install, the retail DB2 tables still match their
/// FileDataIDs and layouts, and terrain and creature assets decode. Skipped when the client,
/// CascLib.dll or the definitions are absent, or when the install has no wowf product.
/// </summary>
[Trait("Category", "Corpus")]
[Collection(RetailData.Collection)]
public class ForeverCorpusTests
{
    [ForeverClientFact]
    public void OpensTheForeverProductAndReportsItsBuild()
    {
        var files = ForeverData.Files;
        Assert.Equal(ClientKind.Retail, files.Kind);
        Assert.Equal(RetailInstall.ProductForever, files.Product);
        Assert.Equal("FOREVER", files.DisplayName);
        Assert.Matches(@"^1\.60\.1\.\d+$", files.Build);
    }

    [ForeverClientFact]
    public void ForeverIsClientOnlyUntilItsServerBranchExists()
    {
        Assert.False(ForeverData.Files.ProvidesWorldDatabase);
    }

    [ForeverClientFact]
    public void EveryKnownTableLoads()
    {
        // The product shares FileDataIDs and layouts with the retail tables, so every table the
        // viewer reads must load; some are legitimately empty in this smaller game.
        foreach (string name in Db2Tables.FileIds.Keys)
        {
            var table = ForeverData.Tables.Table(name);
            Assert.True(table.Count >= 0, name);
        }

        Assert.True(ForeverData.Tables.Table("Map").Count > 50);
        Assert.True(ForeverData.Tables.Table("AreaTable").Count > 500);
    }

    [ForeverClientFact]
    public void MapListIncludesAzeroth()
    {
        var maps = ForeverData.Tables.Maps();
        var azeroth = Assert.Single(maps, m => m.Directory.Equals("azeroth", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(0, azeroth.Id);
        Assert.NotEqual(0u, azeroth.WdtFileId);
        Assert.True(ForeverData.Files.Exists(AssetRef.FromFileId(azeroth.WdtFileId)));
        Assert.True(maps.Count > 50);
    }

    [ForeverClientFact]
    public void AzerothWdtAndATileDecode()
    {
        var azeroth = ForeverData.Tables.Maps().Single(m => m.Directory.Equals("azeroth", StringComparison.OrdinalIgnoreCase));
        var map = new WorldMap(azeroth.Directory, ForeverData.Files.Read(AssetRef.FromFileId(azeroth.WdtFileId)));
        Assert.True(map.IsSplit);
        Assert.True(map.Tiles.Count > 500);
        Assert.Equal(map.Tiles.Count, map.Files.Count);

        var key = new TileKey(32, 48); // Elwynn Forest; the same tile the retail corpus test uses.
        var tile = map.ReadTile(ForeverData.Files, key);
        Assert.Equal(256, tile.Chunks.Count);
        Assert.All(tile.Chunks, c => Assert.True(float.IsFinite(c.Height) && c.Heights.All(float.IsFinite), $"{key} chunk {c.X},{c.Y}"));
        Assert.Contains(tile.Chunks, c => c.Layers.Count > 0);

        // Some files are listed but not local in a streamed install, so a decodable layer is not
        // guaranteed. Walk a few tiles; a malformed BLP still fails below.
        int decoded = 0, missing = 0;
        var samples = new[] { key }.Concat(map.Tiles.OrderBy(t => t.X * 64 + t.Y).Where((_, i) => i % 97 == 0).Take(21)).Distinct();
        foreach (var sample in samples)
        {
            var sampled = sample == key ? tile : map.ReadTile(ForeverData.Files, sample);
            foreach (string texture in sampled.Chunks.SelectMany(c => c.Layers).Select(l => l.Texture).Where(t => t.Length > 0).Distinct())
            {
                Assert.True(AssetRef.TryParse(texture, out _), texture);
                try
                {
                    var blp = BlpTexture.Decode(ForeverData.Files.Read(texture));
                    Assert.True(blp.Width > 0 && blp.Height > 0, texture);
                    decoded++;
                }
                catch (CascReadException)
                {
                    // Not downloaded locally; the viewer draws its fallback and reports the missing file.
                    missing++;
                }
            }
        }

        // A malformed BLP fails above. Missing local files are the install's state, not a parser gap,
        // so require that the sampled references were at least examined.
        Assert.True(decoded + missing > 0, "no terrain layer texture was examined");
    }

    [ForeverClientFact]
    public void ASharedTerrainTextureReadsFromTheInstall()
    {
        // An Elwynn layer texture. In a shared install the payload may live in the live product's
        // storage; a standalone install has its own copy. Either way the BLP decodes.
        var bytes = ForeverData.Files.Read("fdid:189018");
        Assert.Equal("BLP2", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
        BlpTexture.Decode(bytes);
    }

    [ForeverClientFact]
    public void ACreatureDisplayResolvesToADecodableModel()
    {
        var catalog = new RetailCreatureCatalog(ForeverData.Tables).PrepareIndexes();
        foreach (var row in ForeverData.Tables.Table("CreatureDisplayInfo").Values)
        {
            CreatureAppearance appearance;
            try
            {
                appearance = catalog.Resolve(row.FieldAs<uint>("ID"));
            }
            catch (InvalidDataException)
            {
                continue;
            }

            Assert.StartsWith("fdid:", appearance.Path);
            var model = M2Model.Read(ForeverData.Files, appearance.Path);
            Assert.NotEmpty(model.Surfaces);
            Assert.NotEmpty(model.Bones);
            return;
        }

        Assert.Fail("no creature display resolved to a model");
    }
}
