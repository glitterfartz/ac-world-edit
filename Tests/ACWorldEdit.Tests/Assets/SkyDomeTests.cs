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
    public void StarsShowAtNightAndFadeOutAtDawnAndDusk()
    {
        Assert.Equal(1f, SkyDome.StarFade(0f), 3);
        Assert.Equal(1f, SkyDome.StarFade(0.125f), 3);
        Assert.Equal(1f / 255f, SkyDome.StarFade(SkyDome.Noon), 4);
        Assert.Equal(1f, SkyDome.StarFade(1f), 3);
        Assert.InRange(SkyDome.StarFade(0.15f), 0.2f, 0.8f);
    }

    [Fact]
    public void SunGlareGrowsAsTheCameraTurnsTowardTheSun()
    {
        // Looking away (cosine 0.7 or less) the quad is 3 wide at half alpha; straight at the sun it is 20 wide at full alpha.
        var away = SkyDome.SunGlare(SkyDome.Noon, 0.2f);
        var at = SkyDome.SunGlare(SkyDome.Noon, 1f);
        Assert.Equal(3f, away.Scale, 3);
        Assert.Equal(0.5f, away.Alpha, 3);
        Assert.Equal(20f, at.Scale, 3);
        Assert.Equal(1f, at.Alpha, 3);
        // No sun glare at night.
        Assert.Equal(0f, SkyDome.SunGlare(0f, 1f).Alpha, 3);
    }

    [Fact]
    public void MoonGlareIsTwiceTheMoonQuadAndOnlyShowsAtNight()
    {
        var glare = SkyDome.MoonGlare(0f, 1f, 1.75f);
        Assert.Equal(3.5f, glare.Scale, 3);
        Assert.Equal(1f, glare.Alpha, 3);
        Assert.Equal(0.1f, SkyDome.MoonGlare(0.05f, 0f, 1.75f).Alpha, 3);
        Assert.Equal(0f, SkyDome.MoonGlare(SkyDome.Noon, 1f, 1.75f).Alpha, 3);
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
        // Level 192 against the threshold byte 101 ((1 - 0.6) * 255 truncates) is index 91: 1 - 0.96^(91 * 0.6015625).
        Assert.Equal(1f - MathF.Pow(0.96f, 91f * 0.6015625f), SkyDome.CloudAlpha(1f, 0.6f), 4);
        Assert.True(SkyDome.CloudAlpha(1.5f, 0.6f) > SkyDome.CloudAlpha(1f, 0.6f));
        Assert.True(SkyDome.CloudAlpha(1f, 0.8f) > SkyDome.CloudAlpha(1f, 0.6f));
    }

    [Fact]
    public void LightTravelsDownwardOnAFixedBearingAndSteepensAtNoon()
    {
        var (nx, ny, nz) = SkyDome.LightDirection(0.5f);
        var (dx, dy, dz) = SkyDome.LightDirection(0.25f);

        Assert.Equal(MathF.Cos(2.2165682f), nz, 5);
        Assert.Equal(MathF.Cos(1.9198623f), dz, 5);
        Assert.True(nz < dz && dz < 0);
        // Same bearing all day: the horizontal parts stay in proportion (azimuth 225 degrees, so x equals y).
        Assert.Equal(nx, ny, 5);
        Assert.Equal(dx, dy, 5);
        Assert.True(nx < 0);
        Assert.Equal(1f, nx * nx + ny * ny + nz * nz, 5);
        // The elevation table wraps from 0.75 back to 0 (midnight is as steep as noon).
        Assert.Equal(nz, SkyDome.LightDirection(0f).Z, 5);
        Assert.Equal((dx + SkyDome.LightDirection(0.75f).X) / 2, SkyDome.LightDirection(0.25f).X, 5);
    }

    [Fact]
    public void ShaftReachScalesWithTheLightShaftRadius()
    {
        Assert.Equal(1f, SkyDome.ShaftReach(64f));
        Assert.Equal(0.5f, SkyDome.ShaftReach(32f));
        Assert.Equal(0f, SkyDome.ShaftReach(0f));
        Assert.Equal(1f, SkyDome.ShaftReach(500f));
    }

    [Fact]
    public void ShaftsFadeInAsTheSunClearsTheHorizon()
    {
        Assert.Equal(0f, SkyDome.ShaftSunVisibility(-0.2f));
        Assert.Equal(1f, SkyDome.ShaftSunVisibility(0.5f));
        Assert.True(SkyDome.ShaftSunVisibility(0.05f) is > 0f and < 1f);
        Assert.True(SkyDome.ShaftSunVisibility(0.1f) > SkyDome.ShaftSunVisibility(0.0f));
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
