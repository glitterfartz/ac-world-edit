using ACWorldEdit.Foundation;
using Xunit;

/// <summary>
/// SpawnPreview policy: bounds validation, phase/difficulty mask overlap, event binding rules and
/// the SQL-derived visibility descriptions. Pure logic; no database.
/// </summary>
public class SpawnPreviewTests
{
    [Fact]
    public void DefaultsAreTheNarrowSafePreview()
    {
        var preview = new SpawnPreview();
        preview.Validate();
        Assert.Equal(1u, preview.PhaseMask);
        Assert.Equal(1u, preview.SpawnMask);
        Assert.True(preview.RespectMasks);
        Assert.Equal(EventPreviewMode.Scheduled, preview.Events);
        Assert.False(preview.IncludeHelpers);
        Assert.False(preview.IncludePools);
        Assert.False(preview.IncludeConditional);
        Assert.False(preview.IncludeDespawned);
        // Script-bound spawns are included by default: SQL cannot prove them invisible.
        Assert.True(preview.IncludeScripted);
    }

    [Fact]
    public void AllStoredWidensEverythingExceptTheBudgets()
    {
        var all = SpawnPreview.AllStored;
        all.Validate();
        Assert.False(all.RespectMasks);
        Assert.Equal(EventPreviewMode.All, all.Events);
        Assert.True(all.IncludeHelpers && all.IncludePools && all.IncludeConditional && all.IncludeDespawned);
        var normal = new SpawnPreview();
        Assert.Equal(normal.QueryLimit, all.QueryLimit);
        Assert.Equal(normal.ModelLimit, all.ModelLimit);
        Assert.Equal(normal.ViewDistance, all.ViewDistance);
    }

    [Theory]
    [InlineData(60f)]
    [InlineData(600f)]
    public void ViewDistanceBoundsAreInclusive(float distance)
    {
        (new SpawnPreview { ViewDistance = distance }).Validate();
    }

    [Theory]
    [InlineData(59.9f)]
    [InlineData(600.1f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void ViewDistanceOutOfRangeIsRejected(float distance)
    {
        Assert.Throws<ArgumentException>(() => (new SpawnPreview { ViewDistance = distance }).Validate());
    }

    [Fact]
    public void LimitsAreBounded()
    {
        (new SpawnPreview { QueryLimit = 1, ModelLimit = 1 }).Validate();
        (new SpawnPreview { QueryLimit = 768, ModelLimit = 256 }).Validate();
        Assert.Throws<ArgumentException>(() => (new SpawnPreview { QueryLimit = 0 }).Validate());
        Assert.Throws<ArgumentException>(() => (new SpawnPreview { QueryLimit = 769 }).Validate());
        Assert.Throws<ArgumentException>(() => (new SpawnPreview { ModelLimit = 0 }).Validate());
        Assert.Throws<ArgumentException>(() => (new SpawnPreview { ModelLimit = 257 }).Validate());
    }

    [Fact]
    public void ActiveEventsAreBounded()
    {
        (new SpawnPreview { ActiveEvents = [1, 65535] }).Validate();
        Assert.Throws<ArgumentException>(() => (new SpawnPreview { ActiveEvents = [0] }).Validate());
        Assert.Throws<ArgumentException>(() => (new SpawnPreview { ActiveEvents = [65536] }).Validate());
        Assert.Throws<ArgumentException>(() => (new SpawnPreview { ActiveEvents = [-1] }).Validate());
        (new SpawnPreview { ActiveEvents = Enumerable.Range(1, 128).ToArray() }).Validate();
        Assert.Throws<ArgumentException>(() => (new SpawnPreview { ActiveEvents = Enumerable.Range(1, 129).ToArray() }).Validate());
        Assert.Throws<ArgumentException>(() => (new SpawnPreview { Events = (EventPreviewMode)99 }).Validate());
    }

    [Theory]
    [InlineData(1u, 1u, 1u, 1u, true)]
    [InlineData(0b11u, 1u, 1u, 1u, true)]
    [InlineData(2u, 1u, 1u, 1u, false)]
    [InlineData(1u, 2u, 1u, 1u, false)]
    [InlineData(0u, 1u, 1u, 1u, false)]
    [InlineData(uint.MaxValue, uint.MaxValue, 1u, 1u, true)]
    public void MasksMatchWhenAnyBitOverlaps(uint phase, uint spawn, uint wantPhase, uint wantSpawn, bool expected)
    {
        var preview = new SpawnPreview { PhaseMask = wantPhase, SpawnMask = wantSpawn };
        Assert.Equal(expected, preview.MatchesMasks(phase, spawn));
    }

    [Fact]
    public void MasksAreIgnoredWhenNotRespected()
    {
        Assert.True((new SpawnPreview { RespectMasks = false }).MatchesMasks(0, 0));
    }

    [Fact]
    public void ScheduledEventsUseTheSchedule()
    {
        var preview = new SpawnPreview();
        Assert.True(preview.MatchesEvents([], []));
        Assert.True(preview.MatchesEvents([5], [5]));
        Assert.False(preview.MatchesEvents([5], [6]));
        Assert.False(preview.MatchesEvents([5], []));
        Assert.True(preview.MatchesEvents([5, 6], [6]));
    }

    [Fact]
    public void NegativeBindingsHideWhileTheEventIsActive()
    {
        var preview = new SpawnPreview();
        Assert.False(preview.MatchesEvents([-5], [5]));
        Assert.True(preview.MatchesEvents([-5], [6]));
        Assert.True(preview.MatchesEvents([-5], []));
        // A positive binding that is active does not rescue a spawn removed by another active event.
        Assert.False(preview.MatchesEvents([5, -6], [5, 6]));
        Assert.True(preview.MatchesEvents([5, -6], [5]));
    }

    [Fact]
    public void ZeroBindingsAreNeutral()
    {
        var preview = new SpawnPreview();
        Assert.True(preview.MatchesEvents([0], []));
        Assert.False(preview.MatchesEvents([0, 5], []));
    }

    [Fact]
    public void NoneModeTreatsNothingAsActive()
    {
        var preview = new SpawnPreview { Events = EventPreviewMode.None };
        Assert.True(preview.MatchesEvents([], [5]));
        Assert.False(preview.MatchesEvents([5], [5]));
        Assert.True(preview.MatchesEvents([-5], [5]));
    }

    [Fact]
    public void SelectedModeUsesTheSelectionNotTheSchedule()
    {
        var preview = new SpawnPreview { Events = EventPreviewMode.Selected, ActiveEvents = [7] };
        Assert.True(preview.MatchesEvents([7], []));
        Assert.False(preview.MatchesEvents([5], [5]));
        Assert.False(preview.MatchesEvents([-7], []));
    }

    [Fact]
    public void AllModeAcceptsEverything()
    {
        var preview = new SpawnPreview { Events = EventPreviewMode.All };
        Assert.True(preview.MatchesEvents([5, -6], [6]));
    }

    [Theory]
    [InlineData(0, 0, 32, 32, 32)]
    [InlineData(63.9, 63.9, 63.9, 32, 32)]
    [InlineData(64, 64, 64, 96, 96)]
    [InlineData(-0.1, -0.1, -0.1, -32, -32)]
    [InlineData(-64, -65, -128, -32, -96)]
    public void QueryPointQuantizesToTheCenterOfA64YardCell(double x, double y, double z, double cx, double cy)
    {
        var point = SpawnQueryPoint.FromPose(new WorldPose(0, x, y, z, 0));
        Assert.Equal(cx, point.X);
        Assert.Equal(cy, point.Y);
    }

    [Fact]
    public void QueryPointIsStableWithinACellAndChangesAcrossCells()
    {
        var a = SpawnQueryPoint.FromPose(new WorldPose(0, 10, 10, 10, 0));
        Assert.Equal(a, SpawnQueryPoint.FromPose(new WorldPose(0, 63, 1, 40, 5)));
        Assert.NotEqual(a, SpawnQueryPoint.FromPose(new WorldPose(0, 64, 10, 10, 0)));
    }

    [Fact]
    public void VisibilityDescriptionJoinsOnlyTheActiveRestrictions()
    {
        Assert.Equal("", new SpawnVisibility([], 0, false, "", false).Description);
        Assert.Equal("Events 5, 9 · Pool 3 · SQL conditions · Script-bound (runtime unknown) · Hidden helper · Initially despawned",
            new SpawnVisibility([5, 9], 3, true, "npc_script", true, true).Description);
        Assert.Equal("Pool 3", new SpawnVisibility([], 3, false, "", false).Description);
        Assert.Equal("Script-bound (runtime unknown)", new SpawnVisibility([], 0, false, "x", false).Description);
    }
}
