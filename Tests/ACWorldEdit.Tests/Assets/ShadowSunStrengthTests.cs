using System.Numerics;
using ACWorldEdit.Assets;
using Xunit;

namespace ACWorldEdit.Tests.Assets;

/// <summary>The sun strength that scales the receiver: zero at night, full for a bright high sun.</summary>
public class ShadowSunStrengthTests
{
    static readonly Vector3 White = Vector3.One;

    [Fact]
    public void AHighBrightSunReachesFullStrength()
    {
        Assert.Equal(1f, ShadowSunStrength.Compute(0.9f, White), 3);
    }

    [Fact]
    public void ABelowHorizonSunIsDark()
    {
        Assert.Equal(0f, ShadowSunStrength.Compute(-0.9f, White), 3);
    }

    [Theory]
    [InlineData(-0.02f, 0f)]
    [InlineData(0.02f, 0.198f)]
    [InlineData(0.7f, 1f)]
    public void TheHorizonBandsTheFade(float sunUp, float expected)
    {
        Assert.Equal(expected, ShadowSunStrength.Compute(sunUp, White), 3);
    }

    [Fact]
    public void ADarkDiffuseBandWeakensTheShadow()
    {
        Assert.Equal(0.25f, ShadowSunStrength.Compute(1f, new Vector3(0.125f, 0.125f, 0.125f)), 3);
    }
}
