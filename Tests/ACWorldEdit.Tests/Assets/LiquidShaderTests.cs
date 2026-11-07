using Xunit;

namespace ACWorldEdit.Tests.Assets;

/// <summary>
/// The retail water shaders share one ripple normal and pick their underwater face per fragment, which
/// lives in GLSL rather than in C# routing, so these checks read the shader source.
/// </summary>
public class LiquidShaderTests
{
    static string Shader(string name)
    {
        string dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "Shaders", "liquid", name)))
        {
            dir = Path.GetDirectoryName(dir)!;
        }

        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir, "Shaders", "liquid", name));
    }

    [Theory]
    [InlineData("liquid.gdshader")]
    [InlineData("procwater.gdshader")]
    public void WaterShadersShareOneRippleNormal(string shader)
    {
        Assert.Contains("#include \"res://Shaders/liquid/liquid_surface.gdshaderinc\"", Shader(shader));
    }

    [Fact]
    public void SharedRippleNormalHasBothFaces()
    {
        string include = Shader("liquid_surface.gdshaderinc");
        // Above the surface the pairs blend by camera distance; procwaterbelow folds both at full strength.
        Assert.Contains("vec2 liquid_ripple(", include);
        Assert.Contains("min(0.2 + distance * 0.001, 1.0)", include);
        Assert.Contains("near_sum * 2.0 + far_sum * 2.0 * ripple_scale", include);
    }

    [Fact]
    public void UndersideIsSelectedPerFragmentFromTheEyeSideOfTheSurface()
    {
        // One material covers every patch of a type, so the eye's side must be tested per fragment
        // against the interpolated surface height rather than a per-material camera flag.
        Assert.Contains("INV_VIEW_MATRIX[3].y < world_position.y", Shader("liquid.gdshader"));
        Assert.Contains("INV_VIEW_MATRIX[3].y < world_position.y", Shader("procwater.gdshader"));
    }

    [Fact]
    public void OnlyTheAboveSurfaceSideReflects()
    {
        // The shipped below shaders take no reflection input; a second call would mirror scenery onto
        // the underside, which the client never shows.
        Assert.Equal(1, Count(Shader("procwater.gdshader"), "liquid_reflection("));
        Assert.DoesNotContain("liquid_reflection", Shader("liquid.gdshader"));
    }

    [Fact]
    public void AzeriteRampsItsThreeBodyLayersAndWarpsThemWithTheAnimatedNormals()
    {
        string shader = Shader("azerite.gdshader");
        // Material 18 body: azeritedeep (gold marble), azeritetranscolor (yellow
        // transition) and azeritecentercolor (blue shallow fringe), weighted by native depth.
        Assert.Contains("texture(deep_tex", shader);
        Assert.Contains("texture(center_tex", shader);
        Assert.Contains("texture(transcolor_tex", shader);
        Assert.Contains("COLOR.a * depth_coefficient", shader);
        Assert.Contains("center_weight", shader);
        Assert.Contains("trans_weight", shader);
        Assert.Contains("ALPHA = 1.0;", shader);
        // azerite_1 masks the blend with azeriteblend, and procazerite crossfades two
        // frames of the animated normal sequence before warping every layer UV.
        Assert.Contains("texture(blend_tex", shader);
        Assert.Contains("normal_frame(", shader);
        Assert.Contains("warp", shader);
        // The pools are self-lit, so the shader adds an emissive term rather than only
        // raising the albedo.
        Assert.Contains("emission_strength", shader);
        // procazerite samples a reflection (t7); the swamp/water ripple include is still
        // not shared, and the inferred per-layer scale/angle triple is not applied.
        Assert.Contains("liquid_reflection(", shader);
        Assert.DoesNotContain("liquid_surface.gdshaderinc", shader);
        Assert.DoesNotContain("albedo_tex", shader);
        Assert.DoesNotContain("radians", shader);
        Assert.DoesNotContain("uv_scale", shader);
    }

    [Fact]
    public void LeyUndersidePicksProcOrMediumByShorePresence()
    {
        // Both shipped below variants draw in the same fragment shader, picked by the
        // eye's side of the surface; proc vs medium is picked by shore-texture presence
        // (void rows name none), matching the has_shore uniform the router binds.
        string shader = Shader("leyline.gdshader");
        Assert.Contains("INV_VIEW_MATRIX[3].y < world_position.y", shader);
        // procleylinebelow_0: same warped body mixed halfway toward the shore texture.
        Assert.Contains("mix(body, shore_below.rgb, 0.5)", shader);
        // mediumleylinebelow_0 fallback for shoreless types: deep-color view-angle blend.
        Assert.Contains("mix(deep_color, body, k)", shader);
    }

    [Fact]
    public void FelSpecularAndShoreFollowTheBytecode()
    {
        string shader = Shader("fel.gdshader");
        // procfel_2 specular (slot 2): same masked add as the emissive overlay,
        // scaled by native depth.
        Assert.Contains("texture(specular_tex", shader);
        Assert.Contains("smask", shader);
        Assert.Contains("COLOR.a * specular_strength", shader);
        // procfel_2/3 shore (slot 3): depth-masked lerp toward the shore texel.
        Assert.Contains("texture(shore_tex", shader);
        Assert.Contains("shore_factor", shader);
        Assert.Contains("view_depth(SCREEN_UV", shader);
    }

    static int Count(string text, string value)
    {
        int count = 0;
        for (int at = text.IndexOf(value, StringComparison.Ordinal); at >= 0; at = text.IndexOf(value, at + value.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }
}
