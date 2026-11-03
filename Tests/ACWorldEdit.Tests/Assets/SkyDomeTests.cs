using ACWorldEdit.Assets;
using Xunit;

namespace ACWorldEdit.Tests.Assets;

/// <summary>
/// The client's sky tables: the highlight curve, the sun path and the dome's ring geometry, without a client.
/// </summary>
public class SkyDomeTests
{
    [Fact]
    public void HighlightPeaksAtSunriseAndSunsetAndVanishesBetween()
    {
        Assert.Equal(1f, SkyDome.Highlight(0.27083334f), 4);
        Assert.Equal(1f, SkyDome.Highlight(0.89583331f), 4);
        Assert.Equal(0f, SkyDome.Highlight(0.5f), 4);
        Assert.Equal(0.5f, SkyDome.Highlight((0.125f + 0.27083334f) / 2), 3);
    }

    [Fact]
    public void InterpolationWrapsPastMidnightAndClampsItsInput()
    {
        // Between the last key (0.9993, 0) and the first (0.125, 0) the value stays 0; the clamp keeps 1.5 at the end.
        Assert.Equal(0f, SkyDome.Highlight(0.05f), 4);
        Assert.Equal(SkyDome.Highlight(1f), SkyDome.Highlight(1.5f), 4);
        Assert.Equal(SkyDome.Highlight(0f), SkyDome.Highlight(-3f), 4);
    }

    [Fact]
    public void SunIsNearlyOverheadAtNoonAndOnTheHorizonAtSunriseAndSunset()
    {
        var (x, y, z) = SkyDome.SunDirection(SkyDome.Noon);
        Assert.Equal(MathF.Cos(0.087266468f), z, 4);
        Assert.Equal(1f, MathF.Sqrt(x * x + y * y + z * z), 4);

        // The sun sits 10 degrees below the horizon at sunrise and sunset, always on the 45 degree bearing.
        var (_, _, sunriseZ) = SkyDome.SunDirection(SkyDome.Sunrise);
        var (sunsetX, sunsetY, sunsetZ) = SkyDome.SunDirection(SkyDome.Sunset);
        Assert.Equal(MathF.Cos(1.7453293f), sunriseZ, 4);
        Assert.Equal(sunsetZ, sunriseZ, 4);
        Assert.Equal(sunsetX, sunsetY, 4);
    }

    [Fact]
    public void RingsRunFromTheZenithDownToTheHorizonAtHeightZero()
    {
        var top = SkyDome.Ring(0);
        var horizon = SkyDome.Ring(5);
        Assert.Equal(0f, top.Radius, 5);
        Assert.Equal(1f - MathF.Cos(MathF.PI / 4), top.Height, 5);
        Assert.Equal(0f, horizon.Height, 5);
        for (int i = 1; i < 6; i++)
        {
            Assert.True(SkyDome.Ring(i).Radius > SkyDome.Ring(i - 1).Radius);
            Assert.True(SkyDome.Ring(i).Height < SkyDome.Ring(i - 1).Height);
        }
    }
}
