using ACWorldEdit.Assets;
using Xunit;

namespace ACWorldEdit.Tests.Assets;

/// <summary>
/// The CPU mirror of the client's liquid slot frame selection must match the decompiled
/// Midnight render path: static slots pin frame 0, animated slots cycle count frames over
/// the rate with the last frame wrapping to frame 0 and the fraction as the blend.
/// </summary>
public class LiquidAnimPhaseTests
{
    [Fact]
    public void StartsOnFirstTwoFramesWithNoBlend()
    {
        Assert.Equal((0, 1, 0f), LiquidAnimPhase.Sequence(0, 3000, 30));
    }

    [Fact]
    public void MidCyclePicksTheMiddlePair()
    {
        var (first, second, blend) = LiquidAnimPhase.Sequence(1500, 3000, 30);

        Assert.Equal(15, first);
        Assert.Equal(16, second);
        Assert.Equal(0f, blend, 5);
    }

    [Fact]
    public void LastFrameWrapsItsPairToFrameZero()
    {
        var (first, second, blend) = LiquidAnimPhase.Sequence(2999, 3000, 30);

        Assert.Equal(29, first);
        Assert.Equal(0, second);
        Assert.Equal(0.99f, blend, 2);
    }

    [Fact]
    public void FullCycleRestarts()
    {
        Assert.Equal((0, 1, 0f), LiquidAnimPhase.Sequence(3000, 3000, 30));
    }

    [Fact]
    public void ZeroRateBehavesAsOneMillisecond()
    {
        Assert.Equal((0, 1, 0f), LiquidAnimPhase.Sequence(5, 0, 30));
    }

    [Fact]
    public void SingleFrameBlendsTowardItself()
    {
        var (first, second, blend) = LiquidAnimPhase.Sequence(250, 1000, 1);

        Assert.Equal(0, first);
        Assert.Equal(0, second);
        Assert.Equal(0.25f, blend, 5);
    }

    [Fact]
    public void EmptySequenceSelectsNothing()
    {
        Assert.Equal((0, 0, 0f), LiquidAnimPhase.Sequence(1500, 3000, 0));
    }
}
