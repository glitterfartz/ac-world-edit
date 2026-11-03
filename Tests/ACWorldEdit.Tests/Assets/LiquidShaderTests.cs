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
        while (dir != null && !File.Exists(Path.Combine(dir, "Shaders", name)))
        {
            dir = Path.GetDirectoryName(dir)!;
        }

        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir, "Shaders", name));
    }

    [Theory]
    [InlineData("liquid.gdshader")]
    [InlineData("procwater.gdshader")]
    public void WaterShadersShareOneRippleNormal(string shader)
    {
        Assert.Contains("#include \"res://Shaders/liquid_surface.gdshaderinc\"", Shader(shader));
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
    public void AzeriteBlendsItsTwoBodyLayersByNativeDepth()
    {
        string shader = Shader("azerite.gdshader");
        // azerite_0: 1 - depth * coefficient picks between the deep and centre textures, opaque.
        Assert.Contains("1.0-COLOR.a*depth_coefficient", shader);
        Assert.Contains("mix(deep, center, blend)", shader);
        Assert.Contains("ALPHA = 1.0;", shader);
        // Both layers sample the native liquid UV; the inferred per-layer scale/angle triple tiled
        // the centre texture into fine noise, so it must not come back.
        Assert.Contains("uv * uv_scale", shader);
        Assert.DoesNotContain("radians", shader);
        Assert.DoesNotContain("deep_uv", shader);
        Assert.DoesNotContain("center_uv", shader);
        // The surface must not borrow water ripples or reflections the shipped shader does not sample.
        Assert.DoesNotContain("liquid_surface.gdshaderinc", shader);
        Assert.DoesNotContain("liquid_reflection", shader);
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
