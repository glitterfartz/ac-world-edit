using System.Numerics;
using ACWorldEdit.Assets;
using Xunit;

namespace ACWorldEdit.Tests.Assets;

/// <summary>What a model keeps in memory after upload (<see cref="ModelData.ForProbes"/>) and how its size is estimated.</summary>
public class ModelDataTests
{
    static ModelSurface Batch(Vector3[] positions, uint? liquid = null, bool skinned = false) => new(
        positions,
        [.. positions.Select(_ => Vector3.UnitZ)],
        [.. positions.Select(_ => Vector2.Zero)],
        [0, 1, 2],
        "a.blp",
        0,
        false,
        skinned ? [.. positions.SelectMany(_ => new[] { 1, 0, 0, 0 })] : null,
        skinned ? [.. positions.SelectMany(_ => new[] { 1f, 0, 0, 0 })] : null,
        [.. positions.Select(_ => Vector4.One)],
        LiquidType: liquid);

    static ModelData Model(params ModelSurface[] surfaces) => new(
        surfaces,
        [new ModelBone(-1, Vector3.Zero, new([], [], 0, 0), new([], [], 0, 0), new([], [], 0, 0))],
        1000,
        [new ModelDoodad("doodad.m2", Vector3.One, Quaternion.Identity, 1)],
        ["a warning"],
        [5, 6]);

    [Fact]
    public void WmoKeepsPositionsIndicesAndLiquidTypeAndDropsTheOtherChannels()
    {
        Vector3[] positions = [Vector3.Zero, Vector3.UnitX, Vector3.UnitY];
        var kept = Model(Batch(positions, liquid: 7)).ForProbes(keepSurfaces: true);
        var surface = Assert.Single(kept.Surfaces);
        Assert.Same(positions, surface.Positions);
        Assert.Equal([0, 1, 2], surface.Indices);
        Assert.Equal(7u, surface.LiquidType);
        Assert.Empty(surface.Normals);
        Assert.Empty(surface.Uv);
        Assert.Null(surface.Colors);
        Assert.Null(surface.Bones);
        Assert.Null(surface.Weights);
        Assert.Empty(kept.Bones);
        Assert.Empty(kept.Warnings);
    }

    [Fact]
    public void BatchesSharingAPositionsArrayStillShareItAfterwards()
    {
        Vector3[] shared = [Vector3.Zero, Vector3.UnitX, Vector3.UnitY];
        var kept = Model(Batch(shared), Batch(shared)).ForProbes(keepSurfaces: true);
        Assert.Same(kept.Surfaces[0].Positions, kept.Surfaces[1].Positions);
    }

    [Fact]
    public void DoodadsAndAnimationIdsSurviveForBothModelKinds()
    {
        foreach (bool wmo in new[] { true, false })
        {
            var kept = Model(Batch([Vector3.Zero, Vector3.UnitX, Vector3.UnitY])).ForProbes(wmo);
            Assert.Equal("doodad.m2", Assert.Single(kept.Doodads).Path);
            Assert.Equal(new ushort[] { 5, 6 }, kept.AnimationIds);
            Assert.Equal(1000u, kept.Duration);
        }
    }

    [Fact]
    public void M2KeepsNoSurfaces()
    {
        Assert.Empty(Model(Batch([Vector3.Zero, Vector3.UnitX, Vector3.UnitY])).ForProbes(keepSurfaces: false).Surfaces);
    }

    [Fact]
    public void EstimatedBytesCountsASharedPositionsArrayOnce()
    {
        Vector3[] shared = [Vector3.Zero, Vector3.UnitX, Vector3.UnitY];
        var one = Model(Batch(shared)).EstimatedBytes;
        var two = Model(Batch(shared), Batch(shared)).EstimatedBytes;
        var separate = Model(Batch(shared), Batch([.. shared])).EstimatedBytes;
        Assert.Equal(one + 3 * 4, two);
        Assert.Equal(one * 2, separate);
    }

    [Fact]
    public void EstimatedBytesDropsWhenChannelsAreStripped()
    {
        var full = Model(Batch([Vector3.Zero, Vector3.UnitX, Vector3.UnitY], skinned: true));
        // Three vertices: 12 B positions + indices 12 B stay; normals, UVs, colors and skinning go.
        Assert.Equal(48L, full.ForProbes(keepSurfaces: true).EstimatedBytes);
        Assert.True(full.EstimatedBytes > full.ForProbes(keepSurfaces: true).EstimatedBytes);
        Assert.Equal(0L, full.ForProbes(keepSurfaces: false).EstimatedBytes);
    }
}
