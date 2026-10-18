using ACWorldEdit.Data;
using Xunit;

namespace ACWorldEdit.Tests.Drafts;

/// <summary>Start-location choice from game_tele rows.</summary>
public class TeleportPickTests
{
    static TeleportLocation Tele(uint id, ushort map) => new(id, $"T{id}", map, 0, 0, 0, 0);

    static HashSet<ushort> Maps(params ushort[] ids) => ids.ToHashSet();

    [Fact]
    public void PicksOnlyFromMapsTheClientHas()
    {
        TeleportLocation[] rows = [Tele(1, 0), Tele(2, 1), Tele(3, 0), Tele(4, 530)];
        for (int seed = 0; seed < 50; seed++)
        {
            Assert.Contains(TeleportSource.Pick(rows, Maps(0, 1), new Random(seed))!.Map, new ushort[] { 0, 1 });
        }
    }

    [Fact]
    public void ReachesEveryCandidateOnAnyMap()
    {
        TeleportLocation[] rows = [Tele(1, 0), Tele(2, 1), Tele(3, 530), Tele(4, 99)];
        var seen = Enumerable.Range(0, 200).Select(s => TeleportSource.Pick(rows, Maps(0, 1, 530), new Random(s))!.Id).ToHashSet();
        Assert.Equal(new uint[] { 1, 2, 3 }, seen.OrderBy(i => i));
    }

    [Fact]
    public void NullWhenNoRowIsOnAvailableMaps()
    {
        Assert.Null(TeleportSource.Pick([], Maps(0), new Random(1)));
        Assert.Null(TeleportSource.Pick([Tele(1, 1)], Maps(0), new Random(1)));
    }
}
