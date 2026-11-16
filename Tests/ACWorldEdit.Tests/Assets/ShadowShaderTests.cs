using System.Numerics;
using ACWorldEdit.Assets;
using Xunit;

namespace ACWorldEdit.Tests.Assets;

/// <summary>
/// The caster shader is the only thing that writes cascade maps, and the receiver trusts its three
/// channels as exact 8-bit digits. These read the shader source because the packing lives in GLSL.
/// </summary>
public class ShadowShaderTests
{
    static string RepoFile(params string[] parts)
    {
        string dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine([dir, .. parts])))
        {
            dir = Path.GetDirectoryName(dir)!;
        }

        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine([dir, .. parts]));
    }

    [Fact]
    public void CasterDrawsUnshadedDepthAndNeverBlends()
    {
        string shader = RepoFile("Shaders", "shadow", "shadow_caster.gdshader");

        Assert.Contains("render_mode unshaded", shader);
        Assert.Contains("depth_draw_opaque", shader);
        Assert.Contains("fog_disabled", shader);
        // blend_mode is a uniform now, so name the actual blend render modes that must stay absent.
        Assert.DoesNotContain("blend_add", shader);
        Assert.DoesNotContain("blend_mix", shader);
        Assert.DoesNotContain("blend_mul", shader);
        Assert.DoesNotContain("depth_draw_never", shader);
    }

    [Fact]
    public void CasterSkinsAndAlphaTestsFromTheSharedInclude()
    {
        string shader = RepoFile("Shaders", "shadow", "shadow_caster.gdshader");
        string include = RepoFile("Shaders", "shared", "client_skinning.gdshaderinc");

        Assert.Contains("client_skinning.gdshaderinc", shader);
        Assert.Contains("client_skin(VERTEX, NORMAL)", shader);
        Assert.Contains("albedo_tex", shader);
        Assert.Contains("blend_mode", shader);
        Assert.Contains("discard", shader);
        Assert.Contains("caster_uv", shader);
        Assert.Contains("uv_row0", shader);
        Assert.Contains("uv_row1", shader);

        Assert.Contains("animation_palette", include);
        Assert.Contains("CUSTOM0", include);
        Assert.Contains("CUSTOM1", include);
        Assert.Contains("creature_clock", include);
        Assert.Contains("bone_matrix", include);
    }

    [Fact]
    public void ModelShaderUsesTheSharedSkinningInclude()
    {
        string model = RepoFile("Shaders", "model", "model.gdshader");

        Assert.Contains("client_skinning.gdshaderinc", model);
        Assert.Contains("client_skin(VERTEX, NORMAL)", model);
    }

    [Fact]
    public void CasterUsesTheMultiMeshWorldTransformAndOffsetsAlongTheNormal()
    {
        string shader = RepoFile("Shaders", "shadow", "shadow_caster.gdshader");

        Assert.Contains("MODEL_MATRIX * vec4(VERTEX, 1.0)", shader);
        Assert.Contains("MODEL_NORMAL_MATRIX", shader);
        Assert.Contains("client_shadow_normal_offset", shader);
    }

    [Fact]
    public void CasterPacksTwentyFourBitsWithTheSameConstantsAsCSharp()
    {
        string shader = RepoFile("Shaders", "shadow", "shadow_caster.gdshader");

        Assert.Contains("16777215.0", shader);
        Assert.Contains("255u", shader);
        Assert.Contains("8u", shader);
        Assert.Contains("16u", shader);
        Assert.Contains("ALPHA = 1.0;", shader);
        // The range comes from the global, whose defaults are ShadowDepthPacking.Min/Max.
        Assert.Contains("client_shadow_depth_range.x", shader);
        Assert.Contains("client_shadow_depth_range.y", shader);
    }

    [Fact]
    public void PackingRoundTripsTheShadersDigitLayout()
    {
        // The shader shifts 8 and 16 bits into r, g and b; the C# pack must be the same layout.
        float depth = 123.5f;
        Vector3 rgb = ShadowDepthPacking.Pack(depth);
        uint bits = (uint)MathF.Round(rgb.X * 255f) | (uint)MathF.Round(rgb.Y * 255f) << 8 | (uint)MathF.Round(rgb.Z * 255f) << 16;

        Assert.InRange(bits, 0u, ShadowDepthPacking.Levels);
        Assert.InRange(ShadowDepthPacking.Unpack(rgb), depth - ShadowDepthPacking.MaxError, depth + ShadowDepthPacking.MaxError);
    }
}
