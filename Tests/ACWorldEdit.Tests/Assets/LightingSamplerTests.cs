using ACWorldEdit.Assets;
using Xunit;

namespace ACWorldEdit.Tests.Assets;

/// <summary>
/// The pure lighting sampler: clock wrap, default-light precedence, nearest-light blend, the
/// one-yard falloff floor and the "keep the last frame" null results.
/// </summary>
public class LightingSamplerTests
{
    static LightBand Band(uint packed) => LightBand.FromSamples([(0u, packed)]);

    static LightProfile Profile(uint packed) => new(0, Band(packed), Band(packed), Band(packed), Band(packed), Band(packed));

    static readonly Dictionary<uint, LightProfile> Profiles = new()
    {
        [10] = Profile(0x00FF0000), // red background
        [20] = Profile(0x000000FF), // blue foreground
        [30] = Profile(0x0000FF00)  // green foreground
    };

    static LightProfile? Lookup(uint id) => Profiles.GetValueOrDefault(id);

    static LightRegion Region(uint id, uint paramsId, float x, float falloffStart, float falloffEnd, bool isDefault = false) =>
        new(id, 0, x, 0, 0, falloffStart, falloffEnd, paramsId, isDefault);

    [Fact]
    public void AdvanceWrapsAtTwentyFourHours()
    {
        Assert.Equal(1.0, LightingSampler.Advance(23, 120, 1), 6);
        Assert.Equal(23.5, LightingSampler.Advance(23, 30, 1), 6);
        Assert.Equal(0.0, LightingSampler.Advance(23.5, 60, 0.5), 6);
    }

    [Fact]
    public void NoUsableBackgroundReturnsNull()
    {
        var request = new LightingRequest(12, 0, 0, 0, [Region(5, 99, 0, 0, 10)], Lookup);
        Assert.Null(LightingSampler.Sample(request));
    }

    [Fact]
    public void TheDefaultRegionIsTheBackground()
    {
        LightRegion[] regions =
        [
            Region(9, 10, 1000, 0, 10, isDefault: true),
            Region(1, 20, 1000, 0, 10)
        ];

        var sample = LightingSampler.Sample(new LightingRequest(12, 0, 0, 0, regions, Lookup));

        Assert.NotNull(sample);
        Assert.Equal(9u, sample!.Value.BackgroundId);
        Assert.Equal(10u, sample.Value.BackgroundParamsId);
        Assert.Equal(10u, sample.Value.ActiveParamsId);
        Assert.Contains("background=9/params 10", sample.Value.ActiveLights);
    }

    [Fact]
    public void LightOneIsTheFallbackBackground()
    {
        LightRegion[] regions = [Region(1, 10, 1000, 0, 10), Region(7, 20, 5000, 0, 10)];

        var sample = LightingSampler.Sample(new LightingRequest(12, 0, 0, 0, regions, Lookup));

        Assert.NotNull(sample);
        Assert.Equal(1u, sample!.Value.BackgroundId);
    }

    [Fact]
    public void TheNearestCoveringLightBlendsOverTheBackground()
    {
        LightRegion[] regions =
        [
            Region(1, 10, 1000, 0, 10),
            Region(2, 20, 0, 0, 10),
            Region(3, 30, 100, 0, 10)
        ];

        var sample = LightingSampler.Sample(new LightingRequest(12, 5, 0, 0, regions, Lookup));

        Assert.NotNull(sample);
        Assert.Equal(2u, sample!.Value.Foreground!.Id);
        Assert.Equal(0.5f, sample.Value.Weight);
        Assert.Equal(0.5f, sample.Value.Diffuse.R, 5);
        Assert.Equal(0f, sample.Value.Diffuse.G, 5);
        Assert.Equal(0.5f, sample.Value.Diffuse.B, 5);
        // At half weight the background still owns the skybox slot.
        Assert.Equal(10u, sample.Value.ActiveParamsId);
    }

    [Fact]
    public void StrictDistanceKeepsTheFirstLightInFileOrderOnATie()
    {
        LightRegion[] regions =
        [
            Region(1, 10, 1000, 0, 10),
            Region(2, 20, 3, 0, 10),
            Region(3, 30, 3, 0, 10)
        ];

        var sample = LightingSampler.Sample(new LightingRequest(12, 0, 0, 0, regions, Lookup));

        Assert.NotNull(sample);
        Assert.Equal(2u, sample!.Value.Foreground!.Id);
    }

    [Fact]
    public void FalloffUsesAtLeastAOneYardSpan()
    {
        var request = new LightingRequest(12, 5.25f, 0, 0, [Region(1, 10, 1000, 0, 10), Region(2, 20, 0, 5, 5.5f)], Lookup);

        var sample = LightingSampler.Sample(request);

        Assert.NotNull(sample);
        Assert.Equal(0.25f, sample!.Value.Weight, 5);
    }

    [Fact]
    public void AnUncoveredCameraKeepsTheBackground()
    {
        var request = new LightingRequest(12, 50, 0, 0, [Region(1, 10, 1000, 0, 10), Region(2, 20, 0, 0, 10)], Lookup);

        var sample = LightingSampler.Sample(request);

        Assert.NotNull(sample);
        Assert.Null(sample!.Value.Foreground);
        Assert.Null(sample.Value.ForegroundProfile);
        Assert.Equal(0f, sample.Value.Weight);
        Assert.Contains("foreground=/params", sample.Value.ActiveLights);
    }
}
