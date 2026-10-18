using ACWorldEdit.Assets;
using Xunit;

namespace ACWorldEdit.Tests.Assets;

/// <summary>
/// Split (retail) ADT reader and MAID WDT parsing over synthetic files; the layout facts come from a
/// real 12.1.5 install (RetailCorpusTests repeats the reads against it).
/// </summary>
public class SplitTerrainTests
{
    static SyntheticSplitTerrain.Options Alpha(byte[] alpha, uint flags = 0x100) => new() { LayerAlpha = alpha, LayerFlags = flags };

    static TerrainTile Read(SyntheticSplitTerrain.Options? options = null, bool bigAlpha = true) =>
        RetailTerrain.Read(new(3, 4), SyntheticSplitTerrain.Root(options), SyntheticSplitTerrain.TextureFile(options), bigAlpha);

    [Fact]
    public void ReadsGeometryFromSubChunksBehindTheHeader()
    {
        var tile = Read();
        Assert.Equal(new TileKey(3, 4), tile.Key);
        Assert.Equal(256, tile.Chunks.Count);
        var chunk = tile.Chunks.Single(c => c.X == 5 && c.Y == 2);
        Assert.Equal(100f + 2 * 16 + 5, chunk.Height);
        Assert.Equal(77u, chunk.AreaId);
        Assert.Equal(144f, chunk.Heights[144]);
        Assert.NotNull(chunk.Normals);
        Assert.Equal(435, chunk.Normals!.Length);
        Assert.Empty(tile.Objects);
        Assert.Empty(tile.Liquids);
    }

    [Fact]
    public void LayersUseFileDataIdsFromMdid()
    {
        var chunk = Read().Chunks[0];
        var layer = Assert.Single(chunk.Layers);
        Assert.Equal("fdid:1000", layer.Texture);
        Assert.Empty(layer.Alpha);
    }

    [Fact]
    public void UncompressedBigAlphaIsCopiedAs64By64()
    {
        byte[] alpha = new byte[4096];
        for (int i = 0; i < alpha.Length; i++)
        {
            alpha[i] = (byte)i;
        }

        var layers = Read(Alpha(alpha)).Chunks[0].Layers;
        Assert.Equal(2, layers.Count);
        Assert.Equal("fdid:1001", layers[1].Texture);
        Assert.Equal(alpha, layers[1].Alpha);
    }

    [Fact]
    public void CompressedAlphaIsExpanded()
    {
        // 64 runs of "fill 64 bytes with 200" cover the whole 64-by-64 map.
        byte[] rle = Enumerable.Range(0, 64).SelectMany(_ => new byte[] { 0x80 | 64, 200 }).ToArray();
        var layers = Read(Alpha(rle, 0x300)).Chunks[0].Layers;
        Assert.All(layers[1].Alpha, v => Assert.Equal(200, v));
    }

    [Fact]
    public void HighResolutionHolesRemoveSingleCells()
    {
        var holed = new SyntheticSplitTerrain.Options { ChunkFlags = 0x18000, HighHoles = 1UL << (3 * 8 + 5) };
        var chunk = Read(holed).Chunks[0];
        Assert.Equal(1UL << 29, chunk.CellHoles);
        Assert.Equal(768 - 12, TerrainGeometry.Build(chunk).Indices.Length);
        Assert.Equal(768 - 48, TerrainGeometry.Build(new TerrainChunk { Holes = 1 }).Indices.Length);
    }

    [Fact]
    public void HoleBitsOnlyApplyWithTheHighResolutionFlag()
    {
        var chunk = Read(new SyntheticSplitTerrain.Options { ChunkFlags = 0x8000, HighHoles = 0xFFFF }).Chunks[0];
        Assert.Null(chunk.HighHoles);
        Assert.Equal(0UL, chunk.CellHoles);
    }

    [Fact]
    public void CoarseHolesExpandToCellBlocks()
    {
        // Coarse bit 5 is column 1, row 1: cells (2..3, 2..3).
        var chunk = new TerrainChunk { Holes = 1u << 5 };
        ulong expected = (1UL << (2 * 8 + 2)) | (1UL << (2 * 8 + 3)) | (1UL << (3 * 8 + 2)) | (1UL << (3 * 8 + 3));
        Assert.Equal(expected, chunk.CellHoles);
    }

    [Fact]
    public void TerrainHeightsHonorHighResolutionHoles()
    {
        var options = new SyntheticSplitTerrain.Options { ChunkFlags = 0x18000, HighHoles = 1UL };
        var heights = new TerrainHeights(Read(options));
        float cell = TerrainTile.ChunkSize / 8;
        Assert.Null(heights.Probe(cell * 0.5f, cell * 0.5f));
        Assert.NotNull(heights.Probe(cell * 1.5f, cell * 0.5f));
    }

    [Fact]
    public void LayersWithoutAnMdidListFailClosed()
    {
        Assert.Throws<InvalidDataException>(() => Read(new SyntheticSplitTerrain.Options { SkipMdid = true }));
    }

    [Fact]
    public void UntexturedTilesWithEmptyMdidAndLayersAreValid()
    {
        var tile = Read(new SyntheticSplitTerrain.Options { Textures = [], NoLayers = true });
        Assert.All(tile.Chunks, c => Assert.Empty(c.Layers));
    }

    [Fact]
    public void LayerPointingAtAZeroFileIdKeepsAnEmptyName()
    {
        var options = new SyntheticSplitTerrain.Options { Textures = [1000, 0], LayerAlpha = new byte[4096] };
        Assert.Equal("", Read(options).Chunks[0].Layers[1].Texture);
    }

    [Fact]
    public void LayerTextureIndexOutOfRangeIsRejected()
    {
        var options = new SyntheticSplitTerrain.Options { LayerAlpha = new byte[4096], LayerTexture = 9 };
        Assert.Throws<InvalidDataException>(() => Read(options));
    }

    [Fact]
    public void LayersBeyondTheFourthAreCountedAndNotKept()
    {
        var options = new SyntheticSplitTerrain.Options { LayerAlpha = new byte[4096], ExtraLayers = 3 };
        var chunk = Read(options).Chunks[0];
        Assert.Equal(4, chunk.Layers.Count);
        Assert.Equal(1, chunk.DroppedLayers);
    }

    [Fact]
    public void MoreThanEightLayersAreRejected()
    {
        var options = new SyntheticSplitTerrain.Options { LayerAlpha = new byte[4096], ExtraLayers = 8 };
        Assert.Throws<InvalidDataException>(() => Read(options));
    }

    [Fact]
    public void ShortChunkListsAreRejected()
    {
        Assert.Throws<InvalidDataException>(() => Read(new SyntheticSplitTerrain.Options { ChunkCount = 255 }));
    }

    [Fact]
    public void UnsupportedVersionIsRejected()
    {
        Assert.Throws<NotSupportedException>(() => Read(new SyntheticSplitTerrain.Options { Version = 17 }));
    }

    [Fact]
    public void WdtMaidMapsTilesToFileIds()
    {
        var map = new WorldMap("fixture", SyntheticSplitTerrain.Wdt(0x3CA, (32, 48, 10, 11, 12), (33, 48, 20, 21, 0)));
        Assert.True(map.IsSplit);
        Assert.Equal(2, map.Tiles.Count);
        Assert.Equal(new TileFiles(10, 11, 12), map.Files[new(32, 48)]);
        Assert.Equal(new TileFiles(20, 21, 0), map.Files[new(33, 48)]);
        Assert.Equal(0, map.IncompleteTiles);
    }

    [Fact]
    public void WdtMaidDropsTilesWithoutRootOrTextures()
    {
        var map = new WorldMap("fixture", SyntheticSplitTerrain.Wdt(0x3CA, (1, 1, 10, 0, 0), (2, 2, 0, 11, 0), (3, 3, 10, 11, 0)));
        Assert.Equal([new TileKey(3, 3)], map.Tiles);
        Assert.Equal(2, map.IncompleteTiles);
    }

    [Theory]
    [InlineData(0x3CAu, true)]
    [InlineData(0x04u, true)]
    [InlineData(0x02u, false)]
    public void RetailBigAlphaComesFromTheHeightTexturingOrBigAlphaFlag(uint flags, bool expected)
    {
        Assert.Equal(expected, new WorldMap("fixture", SyntheticSplitTerrain.Wdt(flags, (1, 1, 10, 11, 0))).BigAlpha);
    }

    [Fact]
    public void ReadTileRoutesSplitMapsThroughFileDataIds()
    {
        var map = new WorldMap("fixture", SyntheticSplitTerrain.Wdt(0x3CA, (3, 4, 10, 11, 0)));
        var files = new FakeFiles(new()
        {
            ["fdid:10"] = SyntheticSplitTerrain.Root(),
            ["fdid:11"] = SyntheticSplitTerrain.TextureFile()
        });
        var tile = map.ReadTile(files, new(3, 4));
        Assert.Equal(256, tile.Chunks.Count);
        Assert.Equal(["fdid:10", "fdid:11"], files.Requested.Order());
    }

    [Fact]
    public void ReadTileRoutesUnsplitMapsThroughThePath()
    {
        byte[] main = new byte[64 * 64 * 8];
        main[0] = 1;
        var map = new WorldMap("fixture", [.. new byte[] { 78, 73, 65, 77 }, .. BitConverter.GetBytes(main.Length), .. main]);
        Assert.False(map.IsSplit);
        var files = new FakeFiles(new() { [map.TilePath(new(1, 2))] = SyntheticTerrain.Tile() });
        Assert.Equal(256, map.ReadTile(files, new(1, 2)).Chunks.Count);
        Assert.Equal([map.TilePath(new(1, 2))], files.Requested);
    }

    sealed class FakeFiles(Dictionary<string, byte[]> contents) : IClientFiles
    {
        public List<string> Requested { get; } = [];

        public ClientKind Kind => ClientKind.Retail;

        public string Build => "0.0.0.0";

        public byte[] Read(string path)
        {
            Requested.Add(path);
            return contents.TryGetValue(path, out var bytes) ? bytes : throw new FileNotFoundException(path);
        }

        public bool Exists(string path) => contents.ContainsKey(path);

        public string Source(string path) => Exists(path) ? "fake" : "missing";

        public void Dispose()
        {
        }
    }
}
