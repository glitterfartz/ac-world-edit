using ACWorldEdit.Foundation;
using Xunit;

namespace ACWorldEdit.Tests.Drafts;

/// <summary>The viewport animation follows the addon row with numeric AnimationData clips (names
/// do not exist on modern clients): dead always plays Death, else the emote's clip when the model
/// has it, else the seat clip when mounted, else the stance clip, else Stand.</summary>
public class SpawnAnimationTests
{
    static bool All(ushort id) => true;
    static bool None(ushort id) => false;

    static Func<ushort, bool> Only(params ushort[] ids)
    {
        var set = new HashSet<ushort>(ids);
        return set.Contains;
    }

    [Fact]
    public void DeadAlwaysPlaysDeath()
    {
        Assert.Equal(SpawnAppearance.AnimDeath, SpawnAppearance.RiderClip(7, null, false, All));
        Assert.Equal(SpawnAppearance.AnimDeath, SpawnAppearance.RiderClip(7, 67, true, All));
    }

    [Fact]
    public void IdleWithNothingIsStand()
    {
        Assert.Equal(SpawnAppearance.AnimStand, SpawnAppearance.RiderClip(0, null, false, All));
        Assert.Equal(SpawnAppearance.AnimStand, SpawnAppearance.MountClip(false, All));
        Assert.Equal(SpawnAppearance.AnimStand, SpawnAppearance.MountClip(true, None));
    }

    [Fact]
    public void EmoteClipWinsOverSeatAndStanceWhenPresent()
    {
        Assert.Equal(67, SpawnAppearance.RiderClip(0, 67, true, Only(67, 91)));
        Assert.Equal(67, SpawnAppearance.RiderClip(3, 67, false, Only(67, 100)));
    }

    [Fact]
    public void MissingEmoteClipFallsThroughToSeatThenStance()
    {
        // Wave (67) not on the model: a mounted rider sits (91), then stance sleep (100), then Stand.
        Assert.Equal(91, SpawnAppearance.RiderClip(0, 67, true, Only(91)));
        Assert.Equal(91, SpawnAppearance.RiderClip(3, 67, true, Only(91)));
        Assert.Equal(100, SpawnAppearance.RiderClip(3, 67, false, Only(100)));
        Assert.Equal(SpawnAppearance.AnimStand, SpawnAppearance.RiderClip(3, 67, false, None));
    }

    [Fact]
    public void StandClipsFollowTheVerifiedIds()
    {
        Assert.Equal(SpawnAppearance.AnimStand, SpawnAppearance.StandClip(0));
        Assert.Equal(97, SpawnAppearance.StandClip(1));
        Assert.Equal(102, SpawnAppearance.StandClip(2));
        Assert.Equal(100, SpawnAppearance.StandClip(3));
        Assert.Equal(SpawnAppearance.AnimDeath, SpawnAppearance.StandClip(7));
        Assert.Equal(115, SpawnAppearance.StandClip(8));
        Assert.Equal(SpawnAppearance.AnimStand, SpawnAppearance.StandClip(9));
    }

    [Fact]
    public void MountClipNeedsFlightAndTheFlyClip()
    {
        Assert.Equal(135, SpawnAppearance.MountClip(true, Only(135)));
        Assert.Equal(SpawnAppearance.AnimStand, SpawnAppearance.MountClip(true, None));
        Assert.Equal(SpawnAppearance.AnimStand, SpawnAppearance.MountClip(false, All));
    }

    [Fact]
    public void StandStateReadsTheDialectColumn()
    {
        var tc = Row.Of(("StandState", 3u), ("emote", 0u));
        Assert.Equal(3u, SpawnAppearance.StandStateOf(WorldDialect.TrinityCore, tc));
        var ac = Row.Of(("bytes1", 0x0103u), ("emote", 0u));
        Assert.Equal(3u, SpawnAppearance.StandStateOf(WorldDialect.AzerothCore, ac));
    }

    [Fact]
    public void EmoteReadsTheEmoteColumn()
    {
        Assert.Equal(69u, SpawnAppearance.EmoteOf(Row.Of(("emote", 69u))));
    }

    [Fact]
    public void MountReadsDisplayAndCreatureColumns()
    {
        var tc = Row.Of(("mount", 14337u), ("MountCreatureID", 123u));
        Assert.Equal((14337u, 123u), SpawnAppearance.MountOf(WorldDialect.TrinityCore, tc));
        var tcNone = Row.Of(("mount", 0u), ("MountCreatureID", 0u));
        Assert.Equal((0u, 0u), SpawnAppearance.MountOf(WorldDialect.TrinityCore, tcNone));
        var ac = Row.Of(("mount", 14337u));
        Assert.Equal((14337u, 0u), SpawnAppearance.MountOf(WorldDialect.AzerothCore, ac));
    }
}
