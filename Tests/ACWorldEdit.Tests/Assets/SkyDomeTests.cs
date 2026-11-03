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
    public void SunQuadIsTwiceAsWideAtSunriseAndSunsetAsAtNoon()
    {
        Assert.Equal(2f, SkyDome.Sun(0.25f).Scale, 3);
        Assert.Equal(1f, SkyDome.Sun(SkyDome.Noon).Scale, 3);
        Assert.Equal(2f, SkyDome.Sun(0.875f).Scale, 3);
    }

    [Fact]
    public void MoonIsHighAtMidnightAndBelowTheHorizonAtNoon()
    {
        var midnight = SkyDome.Moon(0f);
        Assert.Equal(MathF.Cos(0.61086524f), midnight.Direction.Z, 4);
        Assert.Equal(1.75f, midnight.Scale, 3);
        Assert.True(SkyDome.Moon(SkyDome.Noon).Direction.Z < 0);
        // One and a half times the base scale while it is low, at moonrise and moonset.
        Assert.Equal(1.5f * 1.75f, SkyDome.Moon(SkyDome.Moonset).Scale, 3);
    }

    [Fact]
    public void SecondMoonFollowsItsOwnSlowerCycle()
    {
        Assert.True(SkyDome.SecondMoon(0f).Direction.Z > 0);
        Assert.True(SkyDome.SecondMoon(SkyDome.Noon).Direction.Z < 0);
        var (x, y, z) = SkyDome.SecondMoon(0.05f).Direction;
        Assert.Equal(1f, MathF.Sqrt(x * x + y * y + z * z), 4);
    }

    [Fact]
    public void CloudLayerFadesOutTowardTheHorizon()
    {
        Assert.Equal(1f, SkyDome.CloudEdgeAlpha(0f), 4);
        Assert.Equal(1f, SkyDome.CloudEdgeAlpha(0.2f * MathF.PI), 4);
        Assert.Equal(0.5f, SkyDome.CloudEdgeAlpha(0.23f * MathF.PI), 4);
        Assert.Equal(0f, SkyDome.CloudEdgeAlpha(0.245f * MathF.PI), 4);
        Assert.Equal(0f, SkyDome.CloudEdgeAlpha(0.25f * MathF.PI), 4);
    }

    [Fact]
    public void CloudOpacityRisesWithTheNoiseAboveTheDensityThreshold()
    {
        // No density means nothing is above the threshold; thin noise at the default density stays clear.
        Assert.Equal(0f, SkyDome.CloudAlpha(1f, 0f), 4);
        Assert.Equal(0f, SkyDome.CloudAlpha(-1f, 0.6f), 4);
        // Level 192 against the threshold 102 is index 90: 1 - 0.96^(90 * 0.59765625).
        Assert.Equal(1f - MathF.Pow(0.96f, 90f * 0.59765625f), SkyDome.CloudAlpha(1f, 0.6f), 4);
        Assert.True(SkyDome.CloudAlpha(1.5f, 0.6f) > SkyDome.CloudAlpha(1f, 0.6f));
        Assert.True(SkyDome.CloudAlpha(1f, 0.8f) > SkyDome.CloudAlpha(1f, 0.6f));
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
