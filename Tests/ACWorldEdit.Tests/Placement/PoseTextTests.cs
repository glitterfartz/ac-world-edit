using ACWorldEdit.Foundation;
using Xunit;

/// <summary>
/// Heading text and typed-pose merging used by the creature overlay's Details fields.
/// </summary>
public class PoseTextTests
{
    static readonly WorldPose Pose = new(0, -8913.23456789, -133.909123456, 53.4000001, 1.2345678901);

    [Theory]
    [InlineData(0.0, "0.000 rad · 0.0° N")]
    [InlineData(Math.PI / 2, "1.571 rad · 90.0° W")]
    [InlineData(Math.PI, "3.142 rad · 180.0° S")]
    [InlineData(3 * Math.PI / 2, "4.712 rad · 270.0° E")]
    [InlineData(Math.PI / 4, "0.785 rad · 45.0° NW")]
    public void HeadingTextNamesTheCompassPoint(double heading, string expected)
    {
        Assert.Equal(expected, PoseText.HeadingText(heading));
    }

    [Fact]
    public void HeadingTextWrapsNegativeAndLargeHeadingsButEchoesTheRawRadians()
    {
        Assert.Equal("-1.571 rad · 270.0° E", PoseText.HeadingText(-Math.PI / 2));
        Assert.Equal("7.854 rad · 90.0° W", PoseText.HeadingText(5 * Math.PI / 2));
    }

    [Fact]
    public void HeadingTextRoundsToTheNearestEighthTurnAndWrapsTheLastSector()
    {
        // 359 degrees is nearer to N than to NE, and index 8 wraps back to N.
        Assert.EndsWith("359.0° N", PoseText.HeadingText(359 * Math.PI / 180));
        Assert.EndsWith("22.0° N", PoseText.HeadingText(22 * Math.PI / 180));
        Assert.EndsWith("23.0° NW", PoseText.HeadingText(23 * Math.PI / 180));
    }

    [Fact]
    public void HeadingTextIsCultureInvariant()
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            Assert.Equal("1.571 rad · 90.0° W", PoseText.HeadingText(Math.PI / 2));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void TypedFieldsMatchingTheRoundedTextKeepTheExactStoredValues()
    {
        // The fields display F4 text; retyping those values must not rewrite the stored doubles.
        var typed = PoseText.Typed(Pose, Math.Round(Pose.X, 4), Math.Round(Pose.Y, 4), Math.Round(Pose.Z, 4), Math.Round(Pose.Heading * 180 / Math.PI, 4));
        Assert.Equal(Pose, typed);
    }

    [Fact]
    public void ChangingOneFieldLeavesTheOthersExact()
    {
        var typed = PoseText.Typed(Pose, Math.Round(Pose.X, 4), -100, Math.Round(Pose.Z, 4), Math.Round(Pose.Heading * 180 / Math.PI, 4));
        Assert.Equal(-100, typed.Y);
        Assert.Equal(Pose.X, typed.X);
        Assert.Equal(Pose.Z, typed.Z);
        Assert.Equal(Pose.Heading, typed.Heading);
        Assert.Equal(Pose.Map, typed.Map);
    }

    [Fact]
    public void ADifferentHeadingIsNormalizedIntoTheServerRange()
    {
        Assert.Equal(3 * Math.PI / 2, PoseText.Typed(Pose, Pose.X, Pose.Y, Pose.Z, -90).Heading, 9);
        Assert.Equal(Math.PI / 2, PoseText.Typed(Pose, Pose.X, Pose.Y, Pose.Z, 450).Heading, 9);
        Assert.InRange(PoseText.Typed(Pose, Pose.X, Pose.Y, Pose.Z, -1e-12).Heading, 0, 2 * Math.PI);
    }

    [Fact]
    public void ToleranceIsHalfAUnitInTheFourthDecimal()
    {
        Assert.Equal(Pose.X, PoseText.Typed(Pose, Pose.X + 4e-5, Pose.Y, Pose.Z, 70.7).X);
        Assert.Equal(Pose.X + 6e-5, PoseText.Typed(Pose, Pose.X + 6e-5, Pose.Y, Pose.Z, 70.7).X);
    }

    [Fact]
    public void NonFiniteInputsPassThroughSoValidationCanRejectThem()
    {
        var typed = PoseText.Typed(Pose, double.NaN, Pose.Y, Pose.Z, 10);
        Assert.Throws<ArgumentException>(() => typed.Validate());
        Assert.Throws<ArgumentException>(() => PoseText.Typed(Pose, Pose.X, Pose.Y, Pose.Z, double.PositiveInfinity).Validate());
    }
}
