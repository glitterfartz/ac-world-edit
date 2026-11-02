using ACWorldEdit.Assets;
using ACWorldEdit.Tests.Support;
using Xunit;

/// <summary>
/// LightFloatBand reading and cyclic blending, and the fog curve's client rules: the end never drops
/// below 10 yards, the start multiplier is clamped to -1..1 and a non-positive end means no fog data.
/// </summary>
public class FogCurveTests
{
    static DbcTable Band(params (uint Time, float Value)[] keys)
    {
        var row = new uint[34];
        row[1] = (uint)keys.Length;
        for (int i = 0; i < keys.Length; i++)
        {
            row[2 + i] = keys[i].Time;
            row[18 + i] = BitConverter.SingleToUInt32Bits(keys[i].Value);
        }

        return new DbcTable(new DbcBuilder(34).Row(row).Build());
    }

    [Fact]
    public void SingleSampleIsConstant()
    {
        var band = LightFloatBand.Read(Band((0, 6500f)), 0);
        Assert.Equal(6500f, band.At(0));
        Assert.Equal(6500f, band.At(1700));
    }

    [Fact]
    public void BlendsLinearlyBetweenSamples()
    {
        var band = LightFloatBand.Read(Band((0, 100f), (1000, 300f)), 0);
        Assert.Equal(200f, band.At(500), 3);
    }

    [Fact]
    public void WrapsFromTheLastSampleBackToTheFirst()
    {
        var band = LightFloatBand.Read(Band((0, 100f), (1000, 300f)), 0);
        // Halfway between tick 1000 (300) and the first sample one day later (100).
        Assert.Equal(200f, band.At(1000 + (LightBand.CycleLength - 1000) / 2), 3);
    }

    [Fact]
    public void EmptyBandIsZero()
    {
        Assert.Equal(0f, LightFloatBand.Read(Band(), 0).At(5));
    }

    [Fact]
    public void FogEndBelowTenYardsIsRaisedToTen()
    {
        var fog = FogCurve.FromBands(LightFloatBand.Read(Band((0, 0.8f)), 0), LightFloatBand.Read(Band((0, 0.5f)), 0));
        Assert.Equal((10f, 0.5f), fog.At(0));
    }

    [Fact]
    public void StartMultiplierIsClamped()
    {
        var fog = FogCurve.FromBands(LightFloatBand.Read(Band((0, 6500f)), 0), LightFloatBand.Read(Band((0, 3f)), 0));
        Assert.Equal((6500f, 1f), fog.At(0));
    }

    [Fact]
    public void NonPositiveEndMeansNoFogData()
    {
        var fog = FogCurve.FromBands(LightFloatBand.Read(Band((0, 0f)), 0), LightFloatBand.Read(Band((0, 0.5f)), 0));
        Assert.Null(fog.At(0));
    }

    [Fact]
    public void RetailSamplesBlendEndAndScalerTogether()
    {
        var fog = FogCurve.FromSamples([(0u, 200f, 0.2f), (1000u, 400f, 0.6f)]);
        var (end, startMul) = fog.At(500)!.Value;
        Assert.Equal(300f, end, 3);
        Assert.Equal(0.4f, startMul, 3);
    }

    [Theory]
    [InlineData("proceduralOceanDepthTex", LiquidGradient.Ocean)]
    [InlineData("proceduralRiverDepthTex", LiquidGradient.River)]
    [InlineData("proceduralWmoWaterTex", LiquidGradient.Wmo)]
    [InlineData("", LiquidGradient.River)]
    public void GradientIsNamedByTheTypesSecondTexture(string name, LiquidGradient expected)
    {
        Assert.Equal(expected, LiquidStyle.GradientFor(name));
    }
}
