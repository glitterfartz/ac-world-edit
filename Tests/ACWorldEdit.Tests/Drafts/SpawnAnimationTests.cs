using ACWorldEdit.Foundation;
using Xunit;

namespace ACWorldEdit.Tests.Drafts;

/// <summary>The viewport animation follows the addon row: dead always plays Death, else the emote's
/// animation when the client names one, else the stance's, else Stand.</summary>
public class SpawnAnimationTests
{
    static ushort? ByName(IReadOnlyDictionary<string, ushort> map, string name) =>
        map.TryGetValue(name, out ushort id) ? id : null;

    [Fact]
    public void DeadAlwaysPlaysDeath()
    {
        Assert.Equal(SpawnAppearance.AnimDeath, SpawnAppearance.AnimationFor(7, 0, _ => null, _ => null));
        Assert.Equal(SpawnAppearance.AnimDeath, SpawnAppearance.AnimationFor(7, 69, _ => (ushort?)70, _ => "Wave"));
    }

    [Fact]
    public void IdleWithNothingIsStand()
    {
        Assert.Equal(SpawnAppearance.AnimStand, SpawnAppearance.AnimationFor(0, 0, _ => null, _ => null));
    }

    [Fact]
    public void EmoteNameMatchWinsOverStance()
    {
        var anims = new Dictionary<string, ushort> { ["Wave"] = 70, ["Sleep"] = 5 };
        Assert.Equal(70, SpawnAppearance.AnimationFor(3, 69, name => ByName(anims, name), emote => emote == 69 ? "Wave" : null));
    }

    [Fact]
    public void StanceNameMatchPlaysWhenEmoteIsUnknown()
    {
        var anims = new Dictionary<string, ushort> { ["Sleep"] = 5 };
        Assert.Equal(5, SpawnAppearance.AnimationFor(3, 69, name => ByName(anims, name), _ => "Something unknown"));
        Assert.Equal(5, SpawnAppearance.AnimationFor(3, 0, name => ByName(anims, name), _ => null));
    }

    [Fact]
    public void UnknownNamesFallBackToStand()
    {
        Assert.Equal(SpawnAppearance.AnimStand, SpawnAppearance.AnimationFor(3, 0, _ => null, _ => null));
        Assert.Equal(SpawnAppearance.AnimStand, SpawnAppearance.AnimationFor(0, 69, _ => null, _ => "Wave"));
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

    [Fact]
    public void SeatAnimationPrefersMountAndNeedsTheClip()
    {
        var anims = new Dictionary<string, ushort> { ["Mount"] = 12, ["Sit"] = 8 };
        var all = new HashSet<ushort> { 12, 8 };
        Assert.Equal((ushort)12, SpawnAppearance.SeatAnimation(name => ByName(anims, name), all.Contains));
        Assert.Equal((ushort)8, SpawnAppearance.SeatAnimation(name => ByName(anims, name), id => id == 8));
        Assert.Null(SpawnAppearance.SeatAnimation(name => ByName(anims, name), _ => false));
        Assert.Null(SpawnAppearance.SeatAnimation(_ => null, all.Contains));
    }

    [Fact]
    public void FlyAnimationPrefersFlyAndNeedsTheClip()
    {
        var anims = new Dictionary<string, ushort> { ["Fly"] = 30, ["Hover"] = 31 };
        var all = new HashSet<ushort> { 30, 31 };
        Assert.Equal((ushort)30, SpawnAppearance.FlyAnimation(name => ByName(anims, name), all.Contains));
        Assert.Equal((ushort)31, SpawnAppearance.FlyAnimation(name => ByName(anims, name), id => id == 31));
        Assert.Null(SpawnAppearance.FlyAnimation(name => ByName(anims, name), _ => false));
    }
}
