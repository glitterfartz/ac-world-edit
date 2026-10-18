using ACWorldEdit.Data;
using Xunit;

namespace ACWorldEdit.Tests.Drafts;

/// <summary>Start-location choice on Eastern Kingdoms from game_tele rows.</summary>
public class TeleportPickTests
{
    static TeleportLocation Tele(uint id, ushort map) => new(id, $"T{id}", map, 0, 0, 0, 0);

    [Fact]
    public void PicksOnlyFromTheRequestedMap()
    {
        TeleportLocation[] rows = [Tele(1, 0), Tele(2, 1), Tele(3, 0), Tele(4, 530)];
        for (int seed = 0; seed < 50; seed++)
        {
            Assert.Equal((ushort)0, TeleportSource.PickOnMap(rows, 0, new Random(seed))!.Map);
        }
    }

    [Fact]
    public void ReachesEveryCandidateAcrossSeeds()
    {
        TeleportLocation[] rows = [Tele(1, 0), Tele(2, 0), Tele(3, 0)];
        var seen = Enumerable.Range(0, 100).Select(s => TeleportSource.PickOnMap(rows, 0, new Random(s))!.Id).ToHashSet();
        Assert.Equal(3, seen.Count);
    }

    [Fact]
    public void NullWhenNoRowIsOnTheMap()
    {
        Assert.Null(TeleportSource.PickOnMap([], 0, new Random(1)));
        Assert.Null(TeleportSource.PickOnMap([Tele(1, 1)], 0, new Random(1)));
    }
}
