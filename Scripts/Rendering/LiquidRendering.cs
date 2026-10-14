using Godot;
using ACWorldEdit.Assets;

namespace ACWorldEdit;

/// <summary>
/// Main-thread bridge from parsed grids/atlases to Godot. Native frames animate in the shader;
/// tint, opacity response and view-angle blending remain approximations.
/// </summary>
public static class LiquidRendering
{
    public static Shader Shader { get; } = GD.Load<Shader>("res://Shaders/liquid.gdshader");

    /// <summary>
    /// Bind horizontal atlas/category. Supported native frames are square, so atlas aspect ratio
    /// supplies frame count.
    /// </summary>
    public static void Configure(ShaderMaterial material, Texture2D texture, uint kind)
    {
        material.SetShaderParameter("albedo_tex", texture);
        material.SetShaderParameter("frames", (float)texture.GetWidth() / texture.GetHeight());
        material.SetShaderParameter("liquid_kind", (int)kind);
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
