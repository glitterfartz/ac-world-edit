using System.Numerics;
using ACWorldEdit.Assets;
using Xunit;

namespace ACWorldEdit.Tests.Assets;

/// <summary>
/// How a multi-texture batch's shader picks its combiner and texture coordinates: the 0x8000 table, the
/// legacy shader_id decode and the WMO material shader map, against https://wowdev.wiki/M2/.skin and /WMO.
/// </summary>
public class ModelLayersTests
{
    [Fact]
    public void TableShaderZeroIsAnEnvironmentMappedReflection()
    {
        var (combiner, coords, edgeFade) = M2Combiners.Resolve(0x8000, 2)!.Value;

        Assert.Equal(LayerCombiner.OpaqueMod2xNAAlpha, combiner);
        Assert.Equal([LayerCoord.T1, LayerCoord.Env], coords);
        Assert.False(edgeFade);
    }

    [Fact]
    public void TableShaderWithEdgeFadeVertexShaderFades()
    {
        var (combiner, coords, edgeFade) = M2Combiners.Resolve(0x8015, 2)!.Value;

        Assert.Equal(LayerCombiner.ModMod, combiner);
        Assert.Equal([LayerCoord.T1, LayerCoord.T2], coords);
        Assert.True(edgeFade);
    }

    [Fact]
    public void ExtraUnitsBeyondTheVertexShaderReadTheFirstSet()
    {
        // Diffuse_T1 names one coordinate set; a three-unit batch with it reads the first set for all.
        var (_, coords, _) = M2Combiners.Resolve(0x8012, 3)!.Value;

        Assert.Equal([LayerCoord.T1, LayerCoord.T1, LayerCoord.T1], coords);
    }

    [Theory]
    // 0x70 bits set: Mod first operation; low three bits 4 pick Mod2x; bit 0x4000 reads the second UV set.
    [InlineData(0x4014, LayerCombiner.ModMod2x, LayerCoord.T2)]
    // No 0x70 bits: Opaque first operation; 6 picks Mod2xNA; bit 0x8 maps the second unit to the environment.
    [InlineData(0x000E, LayerCombiner.OpaqueMod2xNA, LayerCoord.Env)]
    [InlineData(0x0007, LayerCombiner.OpaqueAddAlpha, LayerCoord.T1)]
    [InlineData(0x0001, LayerCombiner.OpaqueMod, LayerCoord.T1)]
    public void LegacyShaderIdsDecodeTheirOperations(int shaderId, LayerCombiner expected, LayerCoord second)
    {
        var (combiner, coords, _) = M2Combiners.Resolve(shaderId, 2)!.Value;

        Assert.Equal(expected, combiner);
        Assert.Equal(second, coords[1]);
    }

    [Theory]
    [InlineData(0x801A, 2)] // guild tabard shaders need per-guild colors
    [InlineData(0x8000, 1)] // single-texture batches never reach the combiners
    [InlineData(0x8000, 5)] // more units than the viewer has samplers for
    [InlineData(0x80FF, 2)] // past the end of the table
    [InlineData(0x0000, 2)] // Wrath stores 0 and the client derives the combiner at run time
    public void UnimplementedShadersResolveToNull(int shaderId, int textures) =>
        Assert.Null(M2Combiners.Resolve(shaderId, textures));

    [Fact]
    public void EveryCombinerHasACaseInTheModelShader()
    {
        // The GLSL switch is keyed by the enum's values; a member the shader lacks would silently draw the base texture.
        string dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "Shaders", "model.gdshader")))
        {
            dir = Path.GetDirectoryName(dir)!;
        }

        Assert.NotNull(dir);
        string shader = File.ReadAllText(Path.Combine(dir, "Shaders", "model.gdshader"));
        foreach (var combiner in Enum.GetValues<LayerCombiner>().Where(c => c != LayerCombiner.None))
        {
            Assert.Matches($@"case {(int)combiner}:", shader);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(16)]
    public void PlainWmoShadersNeedNoLayers(int shader)
    {
        var info = WmoCombiners.Resolve(shader)!.Value;

        Assert.Equal(LayerCombiner.None, info.Combiner);
        Assert.False(info.Opaque);
    }

    [Fact]
    public void OpaqueWmoShaderForcesAnOpaqueBatch()
    {
        var info = WmoCombiners.Resolve(4)!.Value;

        Assert.Equal(LayerCombiner.None, info.Combiner);
        Assert.True(info.Opaque);
    }

    [Fact]
    public void TwoLayerOpaqueBlendsTwoTextures()
    {
        var info = WmoCombiners.Resolve(13)!.Value;

        Assert.Equal(LayerCombiner.WmoTwoLayerDiffuseOpaque, info.Combiner);
        Assert.Equal(2, info.Units);
        Assert.True(info.Opaque);
    }

    [Fact]
    public void MaskedEnvMetalNeedsThreeTexturesAndAnEnvironmentUnit()
    {
        var info = WmoCombiners.Resolve(11)!.Value;

        Assert.Equal(3, info.Units);
        Assert.Equal(LayerCoord.WmoEnv, info.Coords[1]);
    }

    [Fact]
    public void UnknownWmoShadersResolveToNull() => Assert.Null(WmoCombiners.Resolve(99));

    [Fact]
    public void LayerSignatureIgnoresTextureCaseAndVertexData()
    {
        var a = new SurfaceLayers(LayerCombiner.ModMod, ["A/B.blp"], [LayerCoord.T1, LayerCoord.T2], [Vector2.Zero]);
        var b = new SurfaceLayers(LayerCombiner.ModMod, ["a/b.blp"], [LayerCoord.T1, LayerCoord.T2], [Vector2.One, Vector2.One]);
        var c = new SurfaceLayers(LayerCombiner.ModMod2x, ["a/b.blp"], [LayerCoord.T1, LayerCoord.T2]);

        Assert.Equal(a.Signature, b.Signature);
        Assert.NotEqual(a.Signature, c.Signature);
    }
}
