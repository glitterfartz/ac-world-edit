using ACWorldEdit.Assets;
using Xunit;

namespace ACWorldEdit.Tests.Assets;

/// <summary>The pure terrain window and ray tests the viewer delegates to.</summary>
public class TileWindowTests
{
    [Fact]
    public void WantedKeepsOnlyMapTilesInTheSquare()
    {
        HashSet<TileKey> map = [new(32, 32), new(33, 32), new(32, 33), new(34, 32)];

        var wanted = TileWindow.Wanted(map, new TileKey(32, 32), 1);

        Assert.Equal(3, wanted.Count);
        Assert.Contains(new TileKey(32, 32), wanted);
        Assert.DoesNotContain(new TileKey(34, 32), wanted);
    }

    [Fact]
    public void KeepLoadedAddsOneHysteresisRing()
    {
        var center = new TileKey(32, 32);

        Assert.True(TileWindow.KeepLoaded(new TileKey(34, 32), center, 1));
        Assert.True(TileWindow.KeepLoaded(new TileKey(34, 34), center, 1));
        Assert.False(TileWindow.KeepLoaded(new TileKey(35, 32), center, 1));
    }

    [Fact]
    public void NextMissingIsNearestAndSkipsLoadedAndFailed()
    {
        TileKey[] wanted = [new(40, 40), new(32, 33), new(31, 32), new(34, 32)];

        var next = TileWindow.NextMissing(wanted, key => key == new TileKey(32, 33), key => key == new TileKey(31, 32), new TileKey(32, 32));

        Assert.Equal(new TileKey(34, 32), next);
    }

    [Fact]
    public void NextMissingReturnsNullWhenNothingIsLeft()
    {
        TileKey[] wanted = [new(32, 33)];

        Assert.Null(TileWindow.NextMissing(wanted, _ => true, _ => false, new TileKey(32, 32)));
    }
}

public class RayBoxTests
{
    static readonly System.Numerics.Vector3 Min = new(-1, -1, -1);
    static readonly System.Numerics.Vector3 Max = new(1, 1, 1);

    [Fact]
    public void BoxHitReturnsTheEntryParameter()
    {
        var hit = RayBox.BoxHit(Min, Max, new System.Numerics.Vector3(0, 0, -5), new System.Numerics.Vector3(0, 0, 1));

        Assert.Equal(4f, hit!.Value, 5);
    }

    [Fact]
    public void BoxMissAndParallelOutsideReturnNull()
    {
        Assert.Null(RayBox.BoxHit(Min, Max, new System.Numerics.Vector3(5, 5, -5), new System.Numerics.Vector3(0, 0, 1)));
        Assert.Null(RayBox.BoxHit(Min, Max, new System.Numerics.Vector3(2, 0, -5), new System.Numerics.Vector3(0, 0, 1)));
    }

    [Fact]
    public void ParallelInsideCountsAsAZeroEntry()
    {
        Assert.Equal(0f, RayBox.BoxHit(Min, Max, new System.Numerics.Vector3(0, 0, 0), new System.Numerics.Vector3(0, 1, 0))!.Value);
    }

    [Fact]
    public void SphereHitFrontInsideAndBehind()
    {
        Assert.Equal(4f, RayBox.SphereHit(new System.Numerics.Vector3(0, 0, 0), 1, new System.Numerics.Vector3(0, 0, -5), new System.Numerics.Vector3(0, 0, 1))!.Value, 5);
        Assert.Equal(0f, RayBox.SphereHit(new System.Numerics.Vector3(0, 0, 0), 1, new System.Numerics.Vector3(0, 0, 0), new System.Numerics.Vector3(1, 0, 0))!.Value);
        Assert.Null(RayBox.SphereHit(new System.Numerics.Vector3(0, 0, 0), 1, new System.Numerics.Vector3(0, 0, -5), new System.Numerics.Vector3(0, 0, -1)));
        Assert.Null(RayBox.SphereHit(new System.Numerics.Vector3(0, 0, 0), 1, new System.Numerics.Vector3(5, 5, 0), new System.Numerics.Vector3(0, 0, 1)));
    }
}
