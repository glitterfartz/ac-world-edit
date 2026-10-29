using System.Numerics;

namespace ACWorldEdit.Assets;

/// <summary>
/// Upload-time draw-call reduction: opaque batches (blend 0 or alpha-tested 1) that share every
/// material input are drawn as one surface. Depth testing makes the opaque pass order-free, so the
/// result is pixel-identical. Translucent, liquid, hidden and animated batches keep their own
/// surfaces and their relative order. WMO batches are already merged by <see cref="WmoModel"/>;
/// this mostly joins M2 submeshes that share a texture.
/// </summary>
public static class SurfaceMerge
{
    /// <summary>
    /// Merge eligible surfaces in place of the first member of each group. The source arrays are
    /// not modified; ground probes keep reading the unmerged <see cref="ModelData"/>.
    /// </summary>
    public static (ModelSurface[] Surfaces, SurfaceMaterial[] Materials) Merge(ModelSurface[] surfaces, SurfaceMaterial[] materials)
    {
        var groups = new Dictionary<MergeKey, List<int>>();
        var order = new List<object>();
        for (int i = 0; i < surfaces.Length; i++)
        {
            var s = surfaces[i];
            var m = materials[i];
            if (s.Blend > 1 || s.LiquidType != null || m.Hidden || m.Column >= 0 || s.Indices.Length == 0)
            {
                order.Add(i);
                continue;
            }

            var key = new MergeKey(s.Texture.ToLowerInvariant(), s.Blend, s.TwoSided, s.Unlit, s.ColorMode, s.Bones != null && s.Weights != null, s.Colors != null, m.Color, m.UvRow0, m.UvRow1, s.Layers?.Signature ?? "");
            if (!groups.TryGetValue(key, out var members))
            {
                groups[key] = members = [];
                order.Add(members);
            }

            members.Add(i);
        }

        List<ModelSurface> merged = [];
        List<SurfaceMaterial> appearance = [];
        foreach (var item in order)
        {
            if (item is int single)
            {
                merged.Add(surfaces[single]);
                appearance.Add(materials[single]);
            }
            else
            {
                var members = (List<int>)item;
                merged.Add(members.Count == 1 ? surfaces[members[0]] : Join(members.Select(i => surfaces[i]).ToArray()));
                appearance.Add(materials[members[0]]);
            }
        }

        return (merged.ToArray(), appearance.ToArray());
    }

    readonly record struct MergeKey(string Texture, int Blend, bool TwoSided, bool Unlit, VertexColorMode ColorMode, bool Skinned, bool Colored, Vector4 Color, Vector3 UvRow0, Vector3 UvRow1, string Layers);

    /// <summary>
    /// Concatenate vertex channels once per distinct source array (batches of one WMO group or M2
    /// share them) and offset each member's indices.
    /// </summary>
    static ModelSurface Join(ModelSurface[] members)
    {
        var offsets = new Dictionary<Vector3[], int>(ReferenceEqualityComparer.Instance);
        List<ModelSurface> sources = [];
        int vertexCount = 0;
        foreach (var s in members)
        {
            if (offsets.TryAdd(s.Positions, vertexCount))
            {
                sources.Add(s);
                vertexCount += s.Positions.Length;
            }
        }

        var first = members[0];
        bool skinned = first.Bones != null && first.Weights != null;
        var positions = new Vector3[vertexCount];
        var normals = new Vector3[vertexCount];
        var uv = new Vector2[vertexCount];
        int[]? bones = skinned ? new int[vertexCount * 4] : null;
        float[]? weights = skinned ? new float[vertexCount * 4] : null;
        Vector4[]? colors = first.Colors != null ? new Vector4[vertexCount] : null;
        Vector2[]? uv2 = first.Layers?.Uv2 != null ? new Vector2[vertexCount] : null;
        float[]? alpha2 = first.Layers?.Alpha2 != null ? new float[vertexCount] : null;
        foreach (var s in sources)
        {
            int at = offsets[s.Positions];
            s.Positions.CopyTo(positions, at);
            s.Normals.CopyTo(normals, at);
            s.Uv.CopyTo(uv, at);
            s.Bones?.CopyTo(bones!, at * 4);
            s.Weights?.CopyTo(weights!, at * 4);
            s.Colors?.CopyTo(colors!, at);
            s.Layers?.Uv2?.CopyTo(uv2!, at);
            s.Layers?.Alpha2?.CopyTo(alpha2!, at);
        }

        var indices = new int[members.Sum(s => s.Indices.Length)];
        int n = 0;
        foreach (var s in members)
        {
            int at = offsets[s.Positions];
            foreach (int index in s.Indices)
            {
                indices[n++] = index + at;
            }
        }

        return first with
        {
            Positions = positions,
            Normals = normals,
            Uv = uv,
            Indices = indices,
            Bones = bones,
            Weights = weights,
            Colors = colors,
            Layers = first.Layers == null ? null : first.Layers with { Uv2 = uv2, Alpha2 = alpha2 }
        };
    }
}
