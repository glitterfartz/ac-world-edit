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

    /// <summary>
    /// Bind horizontal atlas/category and the type's style. Supported native frames are square, so
    /// atlas aspect ratio supplies frame count. A surface without a style draws as a stock river.
    /// </summary>
    public static void Configure(ShaderMaterial material, Texture2D texture, uint kind, LiquidStyle? style = null)
    {
        style ??= kind == 1 ? LiquidStyle.Ocean : LiquidStyle.River;
        // A type that names a ripple normal map (retail data) draws with the retail water model; one that does
        // not (3.3.5a data) keeps the 3.3.5a model. See Shaders/liquid.gdshader.
        material.SetShaderParameter("water_model", style.Bump != null ? 1 : 0);
        if (style.Bump != null)
        {
            material.SetShaderParameter("retail_ripple_tex", BumpTexture(style.Bump));
        }

        material.SetShaderParameter("albedo_tex", texture);
        material.SetShaderParameter("frames", (float)texture.GetWidth() / texture.GetHeight());
        material.SetShaderParameter("liquid_kind", (int)kind);
        material.SetShaderParameter("gradient", (int)style.Gradient);
        material.SetShaderParameter("depth_table", style.DepthTable);
        material.SetShaderParameter("period_ms", (float)style.PeriodMs);
        material.SetShaderParameter("uv_scale", style.Scale);
        material.SetShaderParameter("uv_angle", style.Rotation);
        material.SetShaderParameter("depth_scale", style.DepthScale);
    }

    static readonly Dictionary<BlpTexture, ImageTexture> bumpTextures = [];

    /// <summary>
    /// The ripple normal map as a mip-mapped texture, uploaded once per decoded map. It is a handful of
    /// small textures, so they live as long as the process.
    /// </summary>
    static ImageTexture BumpTexture(BlpTexture bump)
    {
        if (!bumpTextures.TryGetValue(bump, out var texture))
        {
            using var image = Image.CreateFromData(bump.Width, bump.Height, false, Image.Format.Rgba8, bump.Rgba);
            image.GenerateMipmaps();
            texture = ImageTexture.CreateFromImage(image);
            bumpTextures[bump] = texture;
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
