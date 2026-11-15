using System.Numerics;
using ACWorldEdit.Assets;
using Xunit;

namespace ACWorldEdit.Tests.Assets;

/// <summary>
/// The caster pass writes sun-space depth as 24 bits in an RGB texel; these check the round trip the
/// receiver depends on and the white "far / lit" clear value.
/// </summary>
public class ShadowDepthPackingTests
{
    [Theory]
    [InlineData(-32768f)]
    [InlineData(-20000f)]
    [InlineData(-1000.5f)]
    [InlineData(0f)]
    [InlineData(17.25f)]
    [InlineData(9000f)]
    [InlineData(32768f)]
    public void RoundTripStaysWithinOne24BitStep(float depth)
    {
        Assert.InRange(ShadowDepthPacking.Unpack(ShadowDepthPacking.Pack(depth)), depth - ShadowDepthPacking.MaxError, depth + ShadowDepthPacking.MaxError);
    }

    [Fact]
    public void ValuesBeyondTheRangeClampToItsEnds()
    {
        Assert.Equal(new Vector3(0f), ShadowDepthPacking.Pack(-40000f));
        Assert.Equal(Vector3.One, ShadowDepthPacking.Pack(40000f));
        Assert.Equal(ShadowDepthPacking.Min, ShadowDepthPacking.Unpack(Vector3.Zero));
        Assert.Equal(ShadowDepthPacking.Max, ShadowDepthPacking.Unpack(Vector3.One));
    }

    [Fact]
    public void WhiteIsTheFarLitClearValue()
    {
        Assert.Equal(Vector3.One, ShadowDepthPacking.Pack(ShadowDepthPacking.Max));
        Assert.Equal(ShadowDepthPacking.Max, ShadowDepthPacking.Unpack(new Vector3(1f, 1f, 1f)));
    }
}
