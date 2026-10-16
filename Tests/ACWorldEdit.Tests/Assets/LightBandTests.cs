using ACWorldEdit.Assets;
using ACWorldEdit.Foundation;
using ACWorldEdit.Tests.Support;
using Xunit;

/// <summary>
/// LightBand keyframe reading and cyclic interpolation, plus the Light.dbc position conversion.
/// These pin the CURRENT math (a 2880-tick cycle, linear per-channel blend, 1/36-yard positions with
/// a tile-grid-corner origin); they do not establish client-verified Light.dbc behavior.
/// </summary>
public class LightBandTests
{
    static DbcTable Band(params (uint Time, uint Rgb)[] keys)
    {
        var dbc = new DbcBuilder(34);
        var row = new uint[34];
        row[1] = (uint)keys.Length;
        for (int i = 0; i < keys.Length; i++)
        {
            row[2 + i] = keys[i].Time;
            row[18 + i] = keys[i].Rgb;
        }

        return new DbcTable(dbc.Row(row).Build());
    }

    static void Near((float R, float G, float B) actual, float r, float g, float b)
    {
        Assert.Equal(r, actual.R, 3);
        Assert.Equal(g, actual.G, 3);
        Assert.Equal(b, actual.B, 3);
    }

    [Fact]
    public void PackedColorsUnpackAsRedGreenBlue()
    {
        var band = LightBand.Read(Band((0, 0x00FF8000)), 0);
        Near(band.Rgb(0), 1f, 128 / 255f, 0f);
        Near(band.Rgb(1500), 1f, 128 / 255f, 0f);
    }

    [Fact]
    public void EmptyBandIsWhite()
    {
        Near(LightBand.Read(Band(), 0).Rgb(1234), 1, 1, 1);
    }

    [Fact]
    public void SampleCountAboveSixteenIsClamped()
    {
        var dbc = new DbcBuilder(34);
        var row = new uint[34];
        row[1] = 40;
        row[2] = 0;
        row[18] = 0x00FF0000;
        // Reading must not run past the row's 16 sample slots.
        var band = LightBand.Read(new DbcTable(dbc.Row(row).Build()), 0);
        Assert.True(float.IsFinite(band.Rgb(100).R));
    }

    [Fact]
    public void BlendsLinearlyBetweenKeyframes()
    {
        var band = LightBand.Read(Band((0, 0x00000000), (1000, 0x00FF0000)), 0);
        Near(band.Rgb(0), 0, 0, 0);
        Near(band.Rgb(500), 0.5f, 0, 0);
        Near(band.Rgb(999.999), 1, 0, 0);
        Near(band.Rgb(1000), 1, 0, 0);
    }

    [Fact]
    public void FinalSegmentWrapsBackToTheFirstKeyframeAcrossMidnight()
    {
        var band = LightBand.Read(Band((0, 0x00000000), (1000, 0x00FF0000)), 0);
        // From tick 1000 (red) to tick 2880 (back to black at the next cycle's first sample).
        double midpoint = 1000 + (2880 - 1000) / 2.0;
        Near(band.Rgb(midpoint), 0.5f, 0, 0);
        Near(band.Rgb(2880 - 1e-6), 0, 0, 0);
    }

    [KnownBug("KB-003", "A tick earlier than the first keyframe falls through every segment and returns the last color, instead of blending through the wrap segment (last keyframe to first + 2880); the colour jumps at the first keyframe. Real bands usually start at tick 0, so this only matters for bands whose first time is above 0.")]
    public void TicksBeforeTheFirstKeyframeBelongToTheWrapSegment()
    {
        var band = LightBand.Read(Band((500, 0x000000FF), (1500, 0x00FF0000)), 0);
        // Tick 0 is between the last keyframe (1500) and the first of the next cycle (500 + 2880).
        float f = (float)((0 - 1500 + 2880) / (500.0 + 2880 - 1500));
        Near(band.Rgb(0), f, 0, 1 - f);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(700)]
    [InlineData(2879.5)]
    public void NegativeAndOverflowingTicksWrapByWholeCycles(double tick)
    {
        var band = LightBand.Read(Band((0, 0x00000000), (1000, 0x00FF0000)), 0);
        var baseline = band.Rgb(tick);
        Near(band.Rgb(tick + 2880), baseline.R, baseline.G, baseline.B);
        Near(band.Rgb(tick - 2880 * 3), baseline.R, baseline.G, baseline.B);
    }

    [Fact]
    public void DuplicateTimesDoNotDivideByZero()
    {
        var band = LightBand.Read(Band((100, 0x00000000), (100, 0x00FF0000)), 0);
        var value = band.Rgb(100);
        Assert.True(float.IsFinite(value.R) && float.IsFinite(value.G) && float.IsFinite(value.B));
    }

    [Fact]
    public void RowOutsideTheTableIsRejected()
    {
        Assert.Throws<InvalidDataException>(() => LightBand.Read(Band((0, 0)), 5));
    }

    [Fact]
    public void LightPositionConversionPinsTheCurrentScaleAndOrigin()
    {
        double center = 32.0 * WorldCoordinates.TileSize;
        // dbc (x, y, z) -> server (center - z/36, center - x/36, y/36) -> view (-sy, sz, -sx).
        var (x, y, z) = LightCoordinates.ToView(36 * 100, 36 * 7, 36 * 40);
        Assert.Equal(-(center - 100), x, 6);
        Assert.Equal(7, y, 9);
        Assert.Equal(-(center - 40), z, 6);
    }

    [Fact]
    public void TileGridCornerOriginMapsToTheMapCenterOffset()
    {
        double center = 32.0 * WorldCoordinates.TileSize;
        var (x, y, z) = LightCoordinates.ToView(0, 0, 0);
        Assert.Equal(-center, x, 6);
        Assert.Equal(0, y);
        Assert.Equal(-center, z, 6);
    }
}
