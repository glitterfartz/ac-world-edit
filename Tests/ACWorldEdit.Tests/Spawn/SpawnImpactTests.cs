using ACWorldEdit.Foundation;
using Xunit;

namespace ACWorldEdit.Tests.Spawn;

public class SpawnImpactTests
{
    static readonly SpawnImpactItem[] Items = [new(new SpawnReference("pool_creature", "guid", "pool membership"), 2)];
    static readonly SpawnPathUse[] Paths = [new(5, 700, 8, 0)];

    [Fact]
    public void OffersCleanupOnlyWhenThereIsSomethingToRemove()
    {
        Assert.Contains("Delete anyway?", SpawnImpact.Prompt("Hogger (5)", Items));
        Assert.DoesNotContain("Delete anyway?", SpawnImpact.Prompt("Hogger (5)", Items, offersCleanup: true));
        Assert.Contains("remove what depends on it too", SpawnImpact.Prompt("Hogger (5)", Items, offersCleanup: true));
        Assert.Contains("• 2 pool membership (pool_creature)", SpawnImpact.Prompt("Hogger (5)", Items));
    }

    [Fact]
    public void AnUnusedPathIsOfferedForRemovalOrWarnedAbout()
    {
        string offered = SpawnImpact.Prompt("Hogger (5)", [], Paths, offersCleanup: true);
        Assert.Contains("waypoint path 700 (8 nodes) that nothing else uses", offered);
        Assert.DoesNotContain("stays in the database unused", offered);

        string warned = SpawnImpact.Prompt("Hogger (5)", [], Paths);
        Assert.Contains("stays in the database unused", warned);
        Assert.Contains("Delete anyway?", warned);
    }
}
