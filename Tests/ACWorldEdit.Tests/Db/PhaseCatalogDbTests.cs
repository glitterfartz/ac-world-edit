using ACWorldEdit.Data;
using ACWorldEdit.Foundation;
using ACWorldEdit.Tests.Support;
using Xunit;
using static Test;

/// <summary>Live, read-only checks that a region read counts its spawns per phase whatever the preview's phase filter is.</summary>
[Trait("Category", "Db")]
public class PhaseCatalogDbTests
{
    // Elwynn Forest around Goldshire: almost everything is in phase 1, a few spawns are in other phases or all of them.
    static readonly CreatureRegion Elwynn = new(0, 31, 33, 48, 50);

    [DbFact]
    public async Task AzerothCoreCountsEachPhaseOfTheRegionAndTheSpawnsInEveryPhase()
    {
        var source = AcSource(Config);
        var normal = await source.ReadCreaturesAsync(Elwynn, new SpawnPreview(), null, "", default);
        var all = await source.ReadCreaturesAsync(Elwynn, SpawnPreview.AllStored, null, "", default);
        var catalog = Assert.IsType<PhaseCatalog>(normal.Phases);

        Assert.True(all.Phases!.Phases.SequenceEqual(catalog.Phases), "the phase counts do not depend on the preview");
        var normalWorld = Assert.Single(catalog.Phases, p => p.Id == 1);
        Assert.True(normalWorld.Spawns > 100, "Goldshire has far more than 100 spawns in the normal world");
        Assert.All(catalog.Phases, p => Assert.InRange(p.Id, 1u, 32u));
        Assert.Equal(catalog.Phases.Select(p => p.Id).Order(), catalog.Phases.Select(p => p.Id));
        Assert.NotNull((await source.ReadGameObjectsAsync(Elwynn, new SpawnPreview(), null, "", default)).Phases);
    }

    [TcDbFact]
    public async Task TrinityCoreNamesThePhasesOfTheRegionAndSkipsUnphasedSpawns()
    {
        var source = DBSource.Open(Environments.TrinityConnection()!);
        // The Twilight Highlands tile where the Quest Zone-Specific phases hold hundreds of spawns.
        var region = new CreatureRegion(648, 26, 26, 30, 30);
        var catalog = Assert.IsType<PhaseCatalog>((await source.ReadCreaturesAsync(region, new SpawnPreview { Dialect = WorldDialect.TrinityCore }, null, "", default)).Phases);

        Assert.DoesNotContain(catalog.Phases, p => p.Id == 0);
        var first = Assert.Single(catalog.Phases, p => p.Id == 170);
        Assert.Equal("Quest Zone-Specific 01", first.Name);
        Assert.True(first.Spawns > 100);
        Assert.Contains(catalog.Phases, p => p.Id == 171 && p.Name == "Quest Zone-Specific 02");
    }
}
