using ACWorldEdit.Assets;
using Xunit;

namespace ACWorldEdit.Tests.Assets;

/// <summary>
/// Retail liquids and light bands without a client: MH2O layers in a split root file, LiquidObject and
/// vertex-format resolution, the path listfile and retail LightData samples.
/// </summary>
public class RetailLiquidTests
{
    // Types: 1 = water (material 10, LVF 0), 2 = magma (material 11, LVF 3). Object 100 is magma.
    static readonly RetailLiquids Tables = RetailLiquids.From(
        [(1, "XTextures\\river\\lake_a.%d.blp", 0, 10), (2, "XTextures\\lava\\lava.%d.blp", 2, 11)],
        [(100, 2)],
        [(10, 0), (11, 3)]);

    /// <summary>2x2 cells (3x3 vertices): heights 10..18, UV pairs (index, 255 - index), depth 0, 127, 255 per row.</summary>
    static byte[] Format3Vertices()
    {
        using var m = new MemoryStream();
        using var w = new BinaryWriter(m);
        for (int i = 0; i < 9; i++)
        {
            w.Write(10f + i);
        }

        for (int i = 0; i < 9; i++)
        {
            w.Write((ushort)i);
            w.Write((ushort)(255 - i));
        }

        for (int i = 0; i < 9; i++)
        {
            w.Write((byte)(i % 3 * 127));
        }

        return m.ToArray();
    }

    static TerrainTile Read(byte[] liquid, RetailLiquids? tables) =>
        RetailTerrain.Read(new(0, 0), SyntheticSplitTerrain.Root(new() { Liquid = liquid }), SyntheticSplitTerrain.TextureFile(), false, null, tables);

    [Fact]
    public void RetailFormat3LayerReadsHeightsUvAndDepth()
    {
        var tile = Read(SyntheticSplitTerrain.Mh2o(1, 3, 0, 2, 2, Format3Vertices()), Tables);

        var surface = Assert.Single(tile.Liquids);
        Assert.Equal(1u, surface.LiquidType);
        Assert.Equal(9, surface.Positions.Length);
        Assert.Equal(10f, surface.Positions[0].Y);
        Assert.Equal(18f, surface.Positions[8].Y);
        Assert.Equal(1 / 255f, surface.Uv[1].X, 4);
        Assert.Equal(254 / 255f, surface.Uv[1].Y, 4);
        Assert.Equal(0f, surface.Colors![0].W);
        Assert.Equal(127 / 255f, surface.Colors[1].W, 4);
        Assert.Equal(4 * 6, surface.Indices.Length);
    }

    [Fact]
    public void LiquidObjectIdSelectsTypeAndVertexFormat()
    {
        // 100 is >= 42, so it names a LiquidObject: magma, whose material has LVF 3.
        var tile = Read(SyntheticSplitTerrain.Mh2o(1, 100, 0, 2, 2, Format3Vertices()), Tables);

        var surface = Assert.Single(tile.Liquids);
        Assert.Equal(2u, surface.LiquidType);
        Assert.Equal(18f, surface.Positions[8].Y);
    }

    [Fact]
    public void FlatLayerWithoutVertexDataUsesTheStoredLevel()
    {
        var tile = Read(SyntheticSplitTerrain.Mh2o(1, 2, 42.5f, 3, 1, null), Tables);

        var surface = Assert.Single(tile.Liquids);
        Assert.All(surface.Positions, p => Assert.Equal(42.5f, p.Y));
        Assert.All(surface.Colors!, c => Assert.Equal(1f, c.W));
    }

    [Fact]
    public void TileWithoutLiquidTablesHasNoLiquids()
    {
        Assert.Empty(Read(SyntheticSplitTerrain.Mh2o(1, 3, 0, 2, 2, Format3Vertices()), null).Liquids);
    }

    [Fact]
    public void WrathRejectsTheRetailOnlyFormat()
    {
        // The Wrath reader keeps its limit of formats 0-2; format 3 only exists in retail files.
        var root = SyntheticSplitTerrain.Root(new() { Liquid = SyntheticSplitTerrain.Mh2o(1, 3, 0, 2, 2, Format3Vertices()) });
        Assert.Throws<InvalidDataException>(() => LiquidData.Terrain(root));
    }

    [Fact]
    public void LayerBelowTheObjectRangeKeepsItsTypeAndFormat()
    {
        Assert.Equal((1u, 2), Tables.Layer(1, 2));
        Assert.Equal((2u, 3), Tables.Layer(1, 100));
        // An object without a row (ocean layers store 42) keeps the layer's type and is depth-only.
        Assert.Equal((1u, 2), Tables.Layer(1, 999));
        Assert.Equal((2u, 2), Tables.Layer(2, 42));
    }

    [Fact]
    public void TypeLookupExposesPatternAndKind()
    {
        Assert.Equal("XTextures\\lava\\lava.%d.blp", Tables.Pattern(2));
        Assert.Equal(2u, Tables.Kind(2));
        Assert.Equal(0u, Tables.Kind(77));
        Assert.Throws<InvalidDataException>(() => Tables.Pattern(77));
    }

    [Fact]
    public void WaterTypesExposeTheirRippleNormalMap()
    {
        var withBump = RetailLiquids.From(
            [(1, "XTextures\\river\\lake_a.%d.blp", 0, 10)], [], [(10, 0)],
            [(1, "XTextures\\ocean\\newoceanbump.blp")]);

        Assert.Equal("XTextures/ocean/newoceanbump.blp", withBump.Bump(1));
        Assert.Null(withBump.Bump(2));
        Assert.Null(Tables.Bump(1));
    }

    [Fact]
    public void MagmaTypesExposeTheirScrollSpeeds()
    {
        var tables = RetailLiquids.From(
            [(3, "XTextures\\lava\\lava.%d.blp", 2, 2)], [], [(2, 1)],
            scrolls: [(3, 0.025f, 0.1f)]);

        Assert.Equal((0.025f, 0.1f), tables.Scroll(3));
        Assert.Equal((0f, 0f), tables.Scroll(99));
    }

    [Fact]
    public void StylesThatScrollDifferentlyDoNotShareAMaterialKey()
    {
        var still = LiquidStyle.River;
        var scrolling = LiquidStyle.River with { ScrollU = 0.025f };
        var faster = LiquidStyle.River with { ScrollU = 0.025f, ScrollV = 0.1f };

        Assert.NotEqual(still.CacheKey, scrolling.CacheKey);
        Assert.NotEqual(scrolling.CacheKey, faster.CacheKey);
        Assert.Equal(scrolling.CacheKey, (LiquidStyle.River with { ScrollU = 0.025f }).CacheKey);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SwampLayersAreSharedAndMissingLayersKeepTheBaseFallback(bool missingDeep)
    {
        using var files = new SwampFiles(missingDeep);
        var settings = new RetailLiquids.SwampSettings("middle.blp", "deep.blp",
            new(0, 0.04f, 0.04f, 0.05f), new(0.1f, 0.3f, 2), 1);
        RetailLiquids.Register(files, RetailLiquids.From(
            [(1225, "shallow.blp", 2, 14), (899, "shallow.blp", 2, 14)], [], [(14, 3)],
            swamps: [(1225, settings), (899, settings with { Opacity = 0.93f })]));
        var textures = LiquidTextures.For(files);
        var first = textures.Style(1225);
        var second = textures.Style(899);
        Assert.Equal(0, first.ScrollU);
        Assert.Equal(0, first.ScrollV);
        Assert.Same(textures.Decode(1225), textures.Decode(899));
        if (missingDeep)
        {
            Assert.Null(first.Swamp);
            Assert.Null(second.Swamp);
        }
        else
        {
            Assert.NotNull(first.Swamp);
            Assert.NotNull(second.Swamp);
            Assert.Same(first.Swamp.Middle, second.Swamp.Middle);
            Assert.Same(first.Swamp.Deep, second.Swamp.Deep);
            Assert.Same(first.Swamp, textures.Style(1225).Swamp);
            Assert.NotEqual(first.CacheKey, second.CacheKey);
            Assert.NotEqual(first.CacheKey, LiquidStyle.River.CacheKey);
        }
    }

    [Fact]
    public void SwampAnimationFramesAndShoreAreSharedWithoutMergingDifferentSettings()
    {
        using var files = new SwampFiles(false);
        var settings = new RetailLiquids.SwampSettings("middle.blp", "deep.blp", new(0, 0.04f, 0.04f, 0.05f), new(1), 1,
            NormalPattern: "normal_%d.blp", Shore: "shore.blp");
        RetailLiquids.Register(files, RetailLiquids.From(
            [(1, "shallow.blp", 2, 14), (2, "shallow.blp", 2, 14)], [], [(14, 3)],
            swamps: [(1, settings), (2, settings with { SpecularStrength = 0 })]));
        var textures = LiquidTextures.For(files);
        var first = textures.Style(1);
        var second = textures.Style(2);
        Assert.NotNull(first.Swamp!.Normals);
        Assert.NotNull(first.Swamp.Shore);
        Assert.Equal(2, first.Swamp.Normals.Width / first.Swamp.Normals.Height);
        Assert.Same(first.Swamp.Normals, second.Swamp!.Normals);
        Assert.Same(first.Swamp.Shore, second.Swamp.Shore);
        Assert.NotEqual(first.CacheKey, second.CacheKey);
    }

    sealed class SwampFiles(bool missingDeep) : IClientFiles
    {
        public ClientKind Kind => ClientKind.Retail;
        public string Build => "synthetic";
        public bool Exists(string path) => path is "shallow.blp" or "middle.blp" or "shore.blp" or "normal_1.blp" or "normal_2.blp" || path == "deep.blp" && !missingDeep;
        public string Source(string path) => "synthetic";
        public void Dispose() { }
        public byte[] Read(string path)
        {
            if (!Exists(path)) throw new FileNotFoundException(path);
            byte[] b = new byte[152];
            "BLP2"u8.CopyTo(b);
            b[8] = 3; // One uncompressed BGRA texel.
            b[9] = 8;
            BitConverter.GetBytes(1).CopyTo(b, 12);
            BitConverter.GetBytes(1).CopyTo(b, 16);
            BitConverter.GetBytes(148).CopyTo(b, 20);
            BitConverter.GetBytes(4).CopyTo(b, 84);
            b[151] = 255;
            return b;
        }
    }

    [Fact]
    public void ListfileResolvesIgnoringCaseAndSeparators()
    {
        string file = Path.Combine(Path.GetTempPath(), "listfile-" + Guid.NewGuid().ToString("N") + ".csv");
        File.WriteAllLines(file, ["219901;xtextures/river/lake_a.1.blp", "bad line", "x;xtextures/none.blp", "219855;xtextures/ocean/ocean_h.1.blp"]);
        try
        {
            var list = PathListfile.Load(file);

            Assert.Equal(2, list.Count);
            Assert.Equal(219901u, list.Resolve("XTextures\\river\\Lake_A.1.blp"));
            Assert.Null(list.Resolve("xtextures/river/lake_a.2.blp"));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void LightSamplesAreSortedAndInterpolated()
    {
        var band = LightBand.FromSamples([(1440, 0x0000FF), (0, 0xFF0000)]);

        var start = band.Rgb(0);
        var mid = band.Rgb(720);
        var end = band.Rgb(1440);
        Assert.Equal((1f, 0f, 0f), start);
        Assert.Equal(0.5f, mid.R, 3);
        Assert.Equal(0.5f, mid.B, 3);
        Assert.Equal((0f, 0f, 1f), end);
    }

    [Fact]
    public void NoLightSamplesGiveWhite()
    {
        Assert.Equal((1f, 1f, 1f), LightBand.FromSamples([]).Rgb(1000));
    }
}
