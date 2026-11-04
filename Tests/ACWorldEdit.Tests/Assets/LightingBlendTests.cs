using ACWorldEdit.Assets;
using Xunit;

namespace ACWorldEdit.Tests.Assets;

/// <summary>
/// The pure publication half: sky band blending, fog start/end and retail terms, water gradients,
/// sun specular, shaft visibility, cloud lighting and stars. These are the values the Godot node
/// pushes to shaders, so they are pinned without a renderer.
/// </summary>
public class LightingBlendTests
{
    static LightBand Band(uint packed) => LightBand.FromSamples([(0u, packed)]);
    static LightFloatBand Number(float value) => LightFloatBand.FromSamples([(0u, value)]);

    static LightProfile Profile(uint packed, LightBand? sun = null, WaterColors? water = null, WaterAlphas? alphas = null,
        FogCurve? fog = null, CloudBands? clouds = null, ShaftBands? shafts = null, RetailFog? retail = null) =>
        new(0, Band(packed), Band(packed), Band(packed), Band(packed), Band(packed), fog, water, alphas, sun, null, retail, clouds, shafts);

    static LightingSample Sample(LightProfile background, LightProfile? foreground = null, float weight = 0, double hours = 12,
        uint foregroundParams = 20)
    {
        var profiles = new Dictionary<uint, LightProfile> { [10] = background, [foregroundParams] = foreground ?? background };
        LightRegion[] regions = weight > 0
            ? [new(1, 0, 1000, 0, 0, 0, 10, 10), new(2, 0, 0, 0, 0, 4, 6, foregroundParams)]
            : [new(1, 0, 0, 0, 0, 0, 10, 10)];
        var sample = LightingSampler.Sample(new LightingRequest(hours, 5, 0, 0, regions, id => profiles.GetValueOrDefault(id)));
        Assert.NotNull(sample);
        return sample!.Value;
    }

    [Fact]
    public void SkyAndCloudColorsBlendHalfway()
    {
        var light = Sample(Profile(0x00FF0000), Profile(0x000000FF), weight: 0.5f);
        var detail = LightingBlend.Describe(light, retail: false, viewDistance: 777);

        Assert.Equal(0.5f, detail.Sky.Zenith.R, 5);
        Assert.Equal(0.5f, detail.Sky.Zenith.B, 5);
        Assert.Equal(0.5f, detail.Clouds.Sun.R, 5);
        Assert.Equal(0.5f, detail.Clouds.Sun.B, 5);
    }

    [Fact]
    public void ClassicFogUsesTheViewDistanceAndHalfStart()
    {
        var detail = LightingBlend.Describe(Sample(Profile(0x00FF0000)), retail: false, viewDistance: 777);

        Assert.Equal(777f, detail.Fog.End);
        Assert.Equal(388.5f, detail.Fog.Start, 3);
        Assert.False(detail.Fog.Retail.Present);
    }

    [Fact]
    public void RetailModernFogStartsAtTheEyeAndSpansTenThousandYards()
    {
        var detail = LightingBlend.Describe(Sample(Profile(0x00FF0000)), retail: true, viewDistance: 2500);

        Assert.Equal(LightingBlend.ModernFogSpan, detail.Fog.End);
        Assert.Equal(0f, detail.Fog.Start);
    }

    [Fact]
    public void FogDistanceComesFromTheLightWhenItHasOne()
    {
        var fog = FogCurve.FromSamples([(0u, 500f, 0.25f)]);
        var detail = LightingBlend.Describe(Sample(Profile(0x00FF0000, fog: fog)), retail: false, viewDistance: 777);

        Assert.Equal(500f, detail.Fog.End);
        Assert.Equal(125f, detail.Fog.Start, 3);
    }

    [Fact]
    public void RetailFogTermsBlendAndFallBackToTheFourPointFiveDensity()
    {
        var height = new FogHeightLayer(Number(10), Number(2), Number(3), [Number(0), Number(0), Number(0), Number(0)], Band(0x0000FF00), Band(0x000000FF));
        var retail = new RetailFog(Number(0), Band(0x00FF0000), Number(300), height, Band(0x00000000), Number(0));
        var detail = LightingBlend.Describe(Sample(Profile(0x00FF0000, retail: retail)), retail: true, viewDistance: 2500);

        Assert.True(detail.Fog.Retail.Present);
        Assert.Equal(4.5f, detail.Fog.Retail.Density);
        Assert.Equal(1f, detail.Fog.Retail.EndColor.R);
        Assert.Equal(300f, detail.Fog.Retail.EndDistance);
        Assert.Equal(0f, detail.Fog.Retail.Layered); // all-zero coefficients leave the main fog everywhere
        Assert.Equal(0f, detail.Fog.Retail.SunStrength); // black sun colour: no scattering
    }

    [Fact]
    public void WaterGradientsCarryColorAndAlpha()
    {
        var water = new WaterColors(Band(0x00FF0000), Band(0x0000FF00), Band(0x000000FF), Band(0x00FFFFFF));
        var alphas = new WaterAlphas(0.5f, 0.6f, 0.7f, 0.8f);
        var detail = LightingBlend.Describe(Sample(Profile(0x00FF0000, water: water, alphas: alphas)), retail: false, viewDistance: 777);

        Assert.Equal(1f, detail.Water.OceanShallow.Color.R);
        Assert.Equal(0.7f, detail.Water.OceanShallow.Alpha); // OceanShallow is the third alphas argument
        Assert.Equal(0.8f, detail.Water.OceanDeep.Alpha);
        Assert.Equal(0.5f, detail.Water.RiverShallow.Alpha);
    }

    [Fact]
    public void SunSpecularUsesTheSunBandOrWhite()
    {
        var withBand = LightingBlend.Describe(Sample(Profile(0x00FF0000, sun: Band(0x0000FF00))), retail: false, viewDistance: 777);
        var without = LightingBlend.Describe(Sample(Profile(0x00FF0000)), retail: false, viewDistance: 777);

        Assert.Equal(1f, withBand.SunSpecular.G);
        Assert.Equal(1f, without.SunSpecular.R);
        Assert.Equal(1f, without.SunSpecular.G);
        Assert.Equal(1f, without.SunSpecular.B);
    }

    [Fact]
    public void ShaftsShowOnlyAboveTheFloorAndUseTheSunReach()
    {
        var shafts = new ShaftBands(Band(0x00FFFFFF), Number(100), Number(2));
        var noon = LightingBlend.Describe(Sample(Profile(0x00FF0000, shafts: shafts), hours: 12), retail: false, viewDistance: 777);
        var midnight = LightingBlend.Describe(Sample(Profile(0x00FF0000, shafts: shafts), hours: 0), retail: false, viewDistance: 777);

        Assert.True(noon.Shafts.Visible);
        Assert.True(noon.Shafts.Amount > 0.001f);
        Assert.Equal(SkyDome.ShaftReach(100), noon.Shafts.Reach);
        Assert.False(midnight.Shafts.Visible);
    }

    [Fact]
    public void CloudLightFollowsTheMoonAtNightAndTheSunByDay()
    {
        var noon = LightingBlend.Describe(Sample(Profile(0x00FF0000), hours: 12), retail: false, viewDistance: 777);
        var midnight = LightingBlend.Describe(Sample(Profile(0x00FF0000), hours: 0), retail: false, viewDistance: 777);

        var sun = SkyDome.Sun(0.5f).Direction;
        var moon = SkyDome.Moon(0f).Direction;
        Assert.Equal(sun.X, noon.Clouds.LightX);
        Assert.Equal(moon.X, midnight.Clouds.LightX);
    }

    [Fact]
    public void StarsFadeByDayAndShowAtNight()
    {
        var noon = LightingBlend.Describe(Sample(Profile(0x00FF0000), hours: 12), retail: false, viewDistance: 777);
        var midnight = LightingBlend.Describe(Sample(Profile(0x00FF0000), hours: 0), retail: false, viewDistance: 777);

        Assert.True(noon.StarsFade < 0.01f);
        Assert.Equal(1f, midnight.StarsFade, 5);
    }
}
