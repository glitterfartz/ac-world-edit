using Godot;
using ACWorldEdit.Assets;
namespace ACWorldEdit;

public static class LiquidRendering
{
    public static Shader Shader { get; } = GD.Load<Shader>("res://Shaders/liquid.gdshader");
    public static void Configure(ShaderMaterial material,Texture2D texture,uint kind)
    { material.SetShaderParameter("albedo_tex",texture); material.SetShaderParameter("frames",(float)texture.GetWidth()/texture.GetHeight()); material.SetShaderParameter("liquid_kind",(int)kind); }
    public static ArrayMesh Mesh(ModelSurface surface,ShaderMaterial material)
    {
        var arrays=new Godot.Collections.Array(); arrays.Resize((int)Godot.Mesh.ArrayType.Max);
        arrays[(int)Godot.Mesh.ArrayType.Vertex]=surface.Positions.Select(v=>new Vector3(v.X,v.Y,v.Z)).ToArray();
        arrays[(int)Godot.Mesh.ArrayType.Normal]=surface.Normals.Select(v=>new Vector3(v.X,v.Y,v.Z)).ToArray();
        arrays[(int)Godot.Mesh.ArrayType.TexUV]=surface.Uv.Select(v=>new Vector2(v.X,v.Y)).ToArray();
        arrays[(int)Godot.Mesh.ArrayType.Color]=surface.Colors!.Select(v=>new Color(v.X,v.Y,v.Z,v.W)).ToArray();
        arrays[(int)Godot.Mesh.ArrayType.Index]=surface.Indices;
        var mesh=new ArrayMesh(); mesh.AddSurfaceFromArrays(Godot.Mesh.PrimitiveType.Triangles,arrays); mesh.SurfaceSetMaterial(0,material); mesh.CustomAabb=mesh.GetAabb().Grow(0.1f); return mesh;
    }
}
