using System.Numerics;
using ACWorldEdit.Assets;
using Xunit;

namespace ACWorldEdit.Tests.Assets;

/// <summary>Upload-time batch merging: which batches join, and that joined geometry draws the same triangles.</summary>
public class SurfaceMergeTests
{
    static ModelSurface Triangle(string texture, int blend = 0, float x = 0, bool skinned = true) => new(
        [new(x, 0, 0), new(x + 1, 0, 0), new(x, 1, 0)],
        [Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ],
        [Vector2.Zero, Vector2.UnitX, Vector2.UnitY],
        [0, 2, 1],
        texture,
        blend,
        false,
        skinned ? [1, 0, 0, 0, 2, 0, 0, 0, 3, 0, 0, 0] : null,
        skinned ? [1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0] : null);

    static (ModelSurface[], SurfaceMaterial[]) Merge(params ModelSurface[] surfaces) =>
        SurfaceMerge.Merge(surfaces, surfaces.Select(_ => SurfaceMaterial.Default).ToArray());

    /// <summary>World-space triangles of a surface, for comparing geometry independent of vertex layout.</summary>
    static IEnumerable<(Vector3, Vector3, Vector3)> Triangles(ModelSurface s) =>
        Enumerable.Range(0, s.Indices.Length / 3).Select(t => (s.Positions[s.Indices[t * 3]], s.Positions[s.Indices[t * 3 + 1]], s.Positions[s.Indices[t * 3 + 2]]));

    [Fact]
    public void OpaqueBatchesWithOneMaterialJoinWithTheirTriangles()
    {
        var a = Triangle("A.blp");
        var b = Triangle("a.BLP", x: 5);
        var (surfaces, materials) = Merge(a, b);
        var merged = Assert.Single(surfaces);
        Assert.Single(materials);
        Assert.Equal(Triangles(a).Concat(Triangles(b)), Triangles(merged));
        Assert.Equal(6, merged.Positions.Length);
        Assert.Equal([1, 0, 0, 0, 2, 0, 0, 0, 3, 0, 0, 0, 1, 0, 0, 0, 2, 0, 0, 0, 3, 0, 0, 0], merged.Bones);
        Assert.Equal(24, merged.Weights!.Length);
    }

    [Fact]
    public void TranslucentAnimatedAndDifferentBatchesKeepTheirOwnSurfaceAndOrder()
    {
        var opaque = Triangle("a.blp");
        var add = Triangle("a.blp", blend: 3);
        var other = Triangle("b.blp");
        var alphaKey = Triangle("a.blp", blend: 1);
        var animated = Triangle("a.blp");
        var opaque2 = Triangle("a.blp", x: 2);
        ModelSurface[] input = [opaque, add, other, alphaKey, animated, opaque2];
        var materials = input.Select(_ => SurfaceMaterial.Default).ToArray();
        materials[4] = materials[4] with { Column = 0, UvFrames = 30 };
        var (surfaces, _) = SurfaceMerge.Merge(input, materials);
        Assert.Equal(5, surfaces.Length);
        Assert.Equal(6, surfaces[0].Indices.Length);
        Assert.Same(add, surfaces[1]);
        Assert.Same(other, surfaces[2]);
        Assert.Same(alphaKey, surfaces[3]);
        Assert.Same(animated, surfaces[4]);
    }

    [Fact]
    public void BatchesSharingVertexArraysOnlyJoinIndices()
    {
        var a = Triangle("a.blp", skinned: false);
        var b = a with { Indices = [1, 2, 0] };
        var merged = Assert.Single(Merge(a, b).Item1);
        Assert.Equal(3, merged.Positions.Length);
        Assert.Equal([0, 2, 1, 1, 2, 0], merged.Indices);
        Assert.Null(merged.Bones);
    }

    [Fact]
    public void DifferentStaticColorsOrSkinningDoNotJoin()
    {
        var materials = new[] { SurfaceMaterial.Default, SurfaceMaterial.Default with { Color = new(1, 0, 0, 1) } };
        Assert.Equal(2, SurfaceMerge.Merge([Triangle("a.blp"), Triangle("a.blp")], materials).Surfaces.Length);
        Assert.Equal(2, Merge(Triangle("a.blp"), Triangle("a.blp", skinned: false)).Item1.Length);
    }
}
