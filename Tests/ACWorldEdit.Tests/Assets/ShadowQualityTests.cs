using ACWorldEdit.Assets;
using Xunit;

namespace ACWorldEdit.Tests.Assets;

/// <summary>The shadow quality names (saved in settings.cfg and accepted on the command line) and presets.</summary>
public class ShadowQualityTests
{
    [Theory]
    [InlineData("off", ShadowQuality.Off)]
    [InlineData("OFF", ShadowQuality.Off)]
    [InlineData(" Low ", ShadowQuality.Low)]
    [InlineData("medium", ShadowQuality.Medium)]
    [InlineData("High", ShadowQuality.High)]
    [InlineData("", ShadowQuality.Off)]
    [InlineData(null, ShadowQuality.Off)]
    [InlineData("ultra", ShadowQuality.Off)]
    public void ParseMapsNamesAndFallsBackToOff(string? text, ShadowQuality expected) =>
        Assert.Equal(expected, ShadowQualityPresets.Parse(text));

    [Fact]
    public void TryParseRejectsUnknownValues()
    {
        Assert.False(ShadowQualityPresets.TryParse("ultra", out var unknown));
        Assert.Equal(ShadowQuality.Off, unknown);
        Assert.True(ShadowQualityPresets.TryParse("medium", out var medium));
        Assert.Equal(ShadowQuality.Medium, medium);
    }

    [Theory]
    [InlineData(ShadowQuality.Off, 0, 0, 1)]
    [InlineData(ShadowQuality.Low, 1, 1024, 1)]
    [InlineData(ShadowQuality.Medium, 3, 1024, 5)]
    [InlineData(ShadowQuality.High, 4, 2048, 9)]
    public void PresetsSelectTheCascadeCountResolutionAndTaps(ShadowQuality quality, int cascades, int resolution, int taps)
    {
        var preset = ShadowQualityPresets.For(quality);

        Assert.Equal(cascades, preset.Cascades);
        Assert.Equal(resolution, preset.Resolution);
        Assert.Equal(taps, preset.PcfTaps);
    }

    [Fact]
    public void EnabledPresetsReachFartherWithQuality()
    {
        Assert.True(ShadowQualityPresets.For(ShadowQuality.Low).MaxDistance < ShadowQualityPresets.For(ShadowQuality.Medium).MaxDistance);
        Assert.True(ShadowQualityPresets.For(ShadowQuality.Medium).MaxDistance < ShadowQualityPresets.For(ShadowQuality.High).MaxDistance);
        Assert.Equal(0f, ShadowQualityPresets.For(ShadowQuality.Off).MaxDistance);
    }

    [Fact]
    public void NamesAreStable()
    {
        Assert.Equal("Off", ShadowQualityPresets.Name(ShadowQuality.Off));
        Assert.Equal("Low", ShadowQualityPresets.Name(ShadowQuality.Low));
        Assert.Equal("Medium", ShadowQualityPresets.Name(ShadowQuality.Medium));
        Assert.Equal("High", ShadowQualityPresets.Name(ShadowQuality.High));
    }
}
