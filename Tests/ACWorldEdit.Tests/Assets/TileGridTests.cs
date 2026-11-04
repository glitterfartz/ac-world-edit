using ACWorldEdit.Assets;
using Xunit;

namespace ACWorldEdit.Tests.Assets;

/// <summary>
/// Pins the two tile-grid semantics the viewer relies on: the raw conversion (which may leave
/// 0..63) and the clamped one used by the tile entry fields.
/// </summary>
public class TileGridTests
{
    const float Size = TerrainTile.Size;

    [Fact]
    public void TheViewOriginIsTile32()
    {
        Assert.Equal(new TileKey(32, 32), TileGrid.FromView(0, 0));
    }

    [Fact]
    public void ATileEdgeBelongsToTheNextTile()
    {
        Assert.Equal(new TileKey(33, 32), TileGrid.FromView(Size, 0));
        Assert.Equal(new TileKey(32, 33), TileGrid.FromView(0, Size));
    }

    [Fact]
    public void JustBeforeAnEdgeStaysInThePreviousTile()
    {
        Assert.Equal(new TileKey(32, 32), TileGrid.FromView(Size - 0.01f, Size - 0.01f));
    }

    [Fact]
    public void NegativeViewSpaceCanLeaveTheGrid()
    {
        Assert.Equal(new TileKey(31, 31), TileGrid.FromView(-1, -1));
        Assert.Equal(new TileKey(0, 0), TileGrid.FromView(-32 * Size, -32 * Size));
        Assert.Equal(new TileKey(-1, -1), TileGrid.FromView(-32 * Size - 1, -32 * Size - 1));
    }

    [Fact]
    public void TheFarEdgeCanLeaveTheGrid()
    {
        Assert.Equal(new TileKey(63, 63), TileGrid.FromView(31 * Size + 1, 31 * Size + 1));
        Assert.Equal(new TileKey(64, 64), TileGrid.FromView(32 * Size + 1, 32 * Size + 1));
    }

    [Fact]
    public void ClampBoundsBothAxes()
    {
        Assert.Equal(new TileKey(0, 0), TileGrid.Clamp(new TileKey(-4, -9)));
        Assert.Equal(new TileKey(63, 63), TileGrid.Clamp(new TileKey(64, 99)));
        Assert.Equal(new TileKey(34, 49), TileGrid.Clamp(new TileKey(34, 49)));
    }

    [Fact]
    public void LocalCoordinatesRoundTrip()
    {
        var key = new TileKey(34, 49);
        float x = (key.X - TileGrid.Radius) * Size + 123.5f;
        float z = (key.Y - TileGrid.Radius) * Size + 7.25f;
        Assert.Equal(key, TileGrid.FromView(x, z));
        Assert.Equal(123.5f, TileGrid.LocalX(x, key));
        Assert.Equal(7.25f, TileGrid.LocalZ(z, key));
        Assert.Equal(0f, TileGrid.LocalX((key.X - TileGrid.Radius) * Size, key));
    }
}
