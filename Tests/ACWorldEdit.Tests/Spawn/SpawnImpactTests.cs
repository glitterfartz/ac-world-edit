using ACWorldEdit.Foundation;
using Xunit;

namespace ACWorldEdit.Tests.Spawn;

public class SpawnImpactTests
{
    static readonly SpawnImpactItem[] Items = [new(new SpawnReference("pool_creature", "guid", "pool membership"), 2)];

    [Fact]
    public void OffersCleanupOnlyWhenThereIsSomethingToRemove()
    {
        Assert.Contains("Delete anyway?", SpawnImpact.Prompt("Hogger (5)", Items));
        Assert.DoesNotContain("Delete anyway?", SpawnImpact.Prompt("Hogger (5)", Items, offersCleanup: true));
        Assert.Contains("remove the rows that name it too", SpawnImpact.Prompt("Hogger (5)", Items, offersCleanup: true));
        Assert.Contains("• 2 pool membership (pool_creature)", SpawnImpact.Prompt("Hogger (5)", Items));
    }
}
