using Godot;
using ACWorldEdit.Assets;

namespace ACWorldEdit;

/// <summary>
/// Main-thread bridge from parsed grids/atlases to Godot. The shader (Shaders/liquid.gdshader) is the
/// client's water and magma pixel/vertex shaders; native frames animate over the type's period and the
/// depth gradient comes from the active light's water colors.
/// </summary>
public static class LiquidRendering
{
    public static Shader Shader { get; } = ClientShaderGlobals.Load("res://Shaders/liquid.gdshader");
    static Shader SwampShader { get; } = ClientShaderGlobals.Load("res://Shaders/swamp.gdshader");

    /// <summary>
    /// Bind horizontal atlas/category and the type's style. Supported native frames are square, so
    /// atlas aspect ratio supplies frame count. A surface without a style draws as a stock river.
    /// </summary>
    public static void Configure(ShaderMaterial material, Texture2D texture, uint kind, LiquidStyle? style = null)
    {
        style ??= kind == 1 ? LiquidStyle.Ocean : LiquidStyle.River;
        material.Shader = style.Swamp != null ? SwampShader : Shader;
        material.SetShaderParameter("albedo_tex", texture);
        material.SetShaderParameter("frames", (float)texture.GetWidth() / texture.GetHeight());
        material.SetShaderParameter("period_ms", (float)style.PeriodMs);
        if (style.Swamp is { } swamp)
        {
            material.SetShaderParameter("swamp_middle_tex", ExtraTexture(swamp.Middle));
            material.SetShaderParameter("swamp_deep_tex", ExtraTexture(swamp.Deep));
            var d = swamp.Settings.Depths;
            var s = swamp.Settings.Scales;
            material.SetShaderParameter("swamp_depths", new Vector4(d.X, d.Y, d.Z, d.W));
            material.SetShaderParameter("swamp_scales", new Vector3(s.X, s.Y, s.Z));
            material.SetShaderParameter("swamp_opacity", swamp.Settings.Opacity);
            material.SetShaderParameter("has_normals", swamp.Normals != null);
            material.SetShaderParameter("has_shore", swamp.Shore != null);
            if (swamp.Normals is { } normals)
            {
                material.SetShaderParameter("swamp_normal_tex", ExtraTexture(normals, mipmaps: false));
                material.SetShaderParameter("normal_frames", (float)normals.Width / normals.Height);
            }
            if (swamp.Shore is { } shore) material.SetShaderParameter("swamp_shore_tex", ExtraTexture(shore));
            var settings = swamp.Settings;
            material.SetShaderParameter("normal_strength", settings.NormalStrength);
            material.SetShaderParameter("specular_power", settings.SpecularPower);
            material.SetShaderParameter("refraction_strength", settings.Refraction);
            material.SetShaderParameter("wave_scale", settings.WaveScale);
            material.SetShaderParameter("reflection_strength", settings.Reflection);
            material.SetShaderParameter("flow_speed", settings.FlowSpeed);
            material.SetShaderParameter("normal_fps", settings.FramesPerSecond);
            material.SetShaderParameter("specular_strength", settings.SpecularStrength);
            material.SetShaderParameter("shore_width", settings.ShoreWidth);
            return;
        }
        // A type that names a ripple normal map (retail data) draws with the retail water model; one that does
        // not (3.3.5a data) keeps the 3.3.5a model. See Shaders/liquid.gdshader.
        material.SetShaderParameter("water_model", style.Bump != null ? 1 : 0);
        if (style.Bump != null)
        {
            material.SetShaderParameter("retail_ripple_tex", ExtraTexture(style.Bump));
        }

        material.SetShaderParameter("liquid_kind", (int)kind);
        material.SetShaderParameter("gradient", (int)style.Gradient);
        material.SetShaderParameter("depth_table", style.DepthTable);
        material.SetShaderParameter("uv_scale", style.Scale);
        material.SetShaderParameter("uv_angle", style.Rotation);
        material.SetShaderParameter("scroll_speed", new Vector2(style.ScrollU, style.ScrollV));
        material.SetShaderParameter("depth_scale", style.DepthScale);
    }

    static readonly Dictionary<(BlpTexture, bool), ImageTexture> extraTextures = [];

    /// <summary>
    /// Ripple maps and swamp depth layers, uploaded once per decoded texture and mip policy.
    /// Animated atlases omit mipmaps to keep adjacent frames separate. Shared textures live as long as the process.
    /// </summary>
    static ImageTexture ExtraTexture(BlpTexture pixels, bool mipmaps = true)
    {
        if (!extraTextures.TryGetValue((pixels, mipmaps), out var texture))
        {
            using var image = Image.CreateFromData(pixels.Width, pixels.Height, false, Image.Format.Rgba8, pixels.Rgba);
            if (mipmaps) image.GenerateMipmaps();
            texture = ImageTexture.CreateFromImage(image);
            extraTextures[(pixels, mipmaps)] = texture;
        }

        return texture;
    }

    /// <summary>
    /// Upload static colored geometry with a small culling margin for flat grids. The terrain
    /// renderer owns the mesh and shares its material/texture.
    /// </summary>
    public static ArrayMesh Mesh(ModelSurface surface, ShaderMaterial material)
    {
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Godot.Mesh.PrimitiveType.Triangles, ModelLibrary.SurfaceArrays(surface));
        mesh.SurfaceSetMaterial(0, material);
        mesh.CustomAabb = mesh.GetAabb().Grow(0.1f);
        return mesh;
    }
}
