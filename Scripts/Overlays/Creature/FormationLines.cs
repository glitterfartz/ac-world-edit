using Godot;

namespace ACWorldEdit;

/// <summary>Leader-to-member link lines for the streamed formations, rebuilt from marker positions.</summary>
internal sealed class FormationLines(Node3D owner)
{
    static readonly Color LinkColor = new(0.45f, 0.85f, 1f, 0.8f);
    static readonly StandardMaterial3D LineMaterial = new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        VertexColorUseAsAlbedo = true,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        NoDepthTest = true,
        RenderPriority = 10
    };

    MeshInstance3D? lines;

    public void Rebuild(IEnumerable<(Vector3 Leader, Vector3 Member)> pairs)
    {
        var list = pairs.ToList();
        if (list.Count == 0)
        {
            Clear();
            return;
        }

        var vertices = new Vector3[list.Count * 2];
        var colors = new Color[list.Count * 2];
        for (int i = 0; i < list.Count; i++)
        {
            vertices[i * 2] = list[i].Leader + Vector3.Up;
            vertices[i * 2 + 1] = list[i].Member + Vector3.Up;
            colors[i * 2] = colors[i * 2 + 1] = LinkColor;
        }

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices;
        arrays[(int)Mesh.ArrayType.Color] = colors;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Lines, arrays);
        if (lines == null || !GodotObject.IsInstanceValid(lines))
        {
            lines = new MeshInstance3D
            {
                MaterialOverride = LineMaterial,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
            };
            owner.AddChild(lines);
        }

        lines.Mesh = mesh;
        lines.Visible = true;
    }

    public void SetVisible(bool visible)
    {
        if (lines != null)
        {
            lines.Visible = visible;
        }
    }

    public void Clear()
    {
        if (lines != null)
        {
            lines.QueueFree();
            lines = null;
        }
    }
}
