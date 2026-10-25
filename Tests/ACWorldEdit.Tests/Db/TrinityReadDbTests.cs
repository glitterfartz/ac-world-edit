using ACWorldEdit.Assets;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;
using ACWorldEdit.Tests.Support;
using Xunit;

/// <summary>
/// Live, read-only checks of the TrinityCore sources against ACWORLD_TC_DB_INFO: spawns around
/// Goldshire, graveyards with their team conditions, game_tele, and area triggers read from the
/// retail client's AreaTrigger.db2. Only SELECTs inside read-only transactions; nothing is written.
/// </summary>
[Trait("Category", "Db")]
public class TrinityReadDbTests
{
    // Elwynn Forest around Goldshire, the neighborhood the viewer smokes use.
    static readonly CreatureRegion Elwynn = new(0, 31, 33, 48, 50);
    static readonly SpawnQueryPoint Goldshire = new(-9464.7, 62.8, 56.2);
    static readonly SpawnPreview Normal = new() { Dialect = WorldDialect.TrinityCore };
    // Westfall and the Deadmines door, well inside the Eastern Kingdoms.
    static readonly CreatureRegion Westfall = new(0, 26, 30, 51, 55);

    static IDBSource Open(IClientLookup? lookup = null) => DBSource.Open(Environments.TrinityConnection()!, lookup);

    [TcDbFact]
    public async Task CreaturesNearGoldshireCarryTheirDifficultyAndPhase()
    {
        var result = await Open().ReadCreaturesAsync(Elwynn, Normal, Goldshire, "", CancellationToken.None);
        Assert.NotEmpty(result.Spawns);
        Assert.True(result.Spawns.Count <= Normal.QueryLimit);
        double previous = 0;
        foreach (var spawn in result.Spawns)
        {
            Assert.Equal(0, spawn.Pose.Map);
            Assert.InRange(spawn.Pose.X, Elwynn.MinServerX, Elwynn.MaxServerX);
            Assert.InRange(spawn.Pose.Y, Elwynn.MinServerY, Elwynn.MaxServerY);
            double distance = Math.Pow(spawn.Pose.X - Goldshire.X, 2) + Math.Pow(spawn.Pose.Y - Goldshire.Y, 2) + Math.Pow(spawn.Pose.Z - Goldshire.Z, 2);
            Assert.True(distance >= previous, "creatures are not nearest-first");
            previous = distance;
            // The SQL filter and the executable spec agree.
            Assert.True(Normal.MatchesPhasing(spawn.Phasing!.Difficulties, spawn.Phasing.PhaseId, spawn.Phasing.PhaseGroup), $"guid {spawn.Guid} should not pass the filter");
        }

        var dughan = result.Spawns.Single(s => s.Guid == 280689);
        Assert.Equal(240u, dughan.TemplateId);
        Assert.Equal("Marshal Dughan", dughan.Name);
        Assert.NotEqual(0u, dughan.DisplayId);
        Assert.Equal("0", dughan.Phasing!.Difficulties);
        Assert.Equal(0u, dughan.Phasing.PhaseId);
        Assert.Equal(-9465.52, dughan.Pose.X, 1);
    }

    [TcDbFact]
    public async Task ADifferentDifficultyOrPhaseChangesWhatIsShown()
    {
        var source = Open();
        var open = await source.ReadCreaturesAsync(Elwynn, Normal, Goldshire, "", CancellationToken.None);
        var heroic = await source.ReadCreaturesAsync(Elwynn, Normal with { DifficultyId = 2 }, Goldshire, "", CancellationToken.None);
        var any = await source.ReadCreaturesAsync(Elwynn, Normal with { DifficultyId = SpawnPreview.AnyDifficulty }, Goldshire, "", CancellationToken.None);
        Assert.DoesNotContain(heroic.Spawns, s => s.Guid == 280689);
        Assert.All(heroic.Spawns, s => Assert.Contains("2", s.Phasing!.Difficulties.Split(',')));
        Assert.True(any.Spawns.Count >= open.Spawns.Count);
        Assert.Contains(any.Spawns, s => s.Guid == 280689);
    }

    [TcDbFact]
    public async Task SearchFindsACreatureByNameAndByGuid()
    {
        var source = Open();
        var byName = await source.ReadCreaturesAsync(Elwynn, Normal, Goldshire, "Marshal Dughan", CancellationToken.None);
        Assert.Contains(byName.Spawns, s => s.Guid == 280689);
        Assert.All(byName.Spawns, s => Assert.Contains("Dughan", s.Name));
        var byGuid = await source.ReadCreaturesAsync(Elwynn, Normal, null, "280689", CancellationToken.None);
        Assert.Equal(280689ul, Assert.Single(byGuid.Spawns).Guid);
    }

    [TcDbFact]
    public async Task GameObjectsKeepTheirStoredRotation()
    {
        var result = await Open().ReadGameObjectsAsync(Elwynn, Normal, Goldshire, "", CancellationToken.None);
        Assert.NotEmpty(result.Spawns);
        Assert.All(result.Spawns, s =>
        {
            Assert.NotNull(s.Phasing);
            Assert.NotNull(s.StoredRotation);
            Assert.True(Normal.MatchesPhasing(s.Phasing!.Difficulties, s.Phasing.PhaseId, s.Phasing.PhaseGroup));
        });
        // The stored rotation is the raw columns; the drawn one is its normalization.
        var placement = GameObjectPlacement.From(result.Spawns[0]);
        Assert.Equal(result.Spawns[0].StoredRotation!.Value.Z, (float)placement.RotZ);
    }

    [TcDbFact]
    public async Task GoldshireGraveyardIsAllianceByItsTeamCondition()
    {
        var result = await Open().ReadGraveyardsAsync(Elwynn, Goldshire, "", CancellationToken.None);
        Assert.NotEmpty(result.Sites);
        var goldshire = result.Sites.Single(s => s.Record.Id == 106);
        Assert.Equal("Elwynn Forest, Goldshire", goldshire.Record.Comment);
        var link = Assert.Single(goldshire.Record.Links, l => l.GhostZone == 12);
        Assert.Equal(GraveyardLink.Alliance, link.Faction);
        // Every link's team is one the editor understands, and only linked world_safe_locs rows appear.
        Assert.All(result.Sites, s =>
        {
            Assert.NotEmpty(s.Record.Links);
            Assert.All(s.Record.Links, l => Assert.Contains(l.Faction, new ushort[] { 0, 67, 469 }));
        });
    }

    [TcDbFact]
    public async Task SafeLocationsThatAreNotGraveyardsAreLeftOut()
    {
        // Deadmines Exit Front Target (3601) is a world_safe_locs row of an area trigger, not a graveyard.
        var result = await Open().ReadGraveyardsAsync(Westfall, null, "", CancellationToken.None);
        Assert.DoesNotContain(result.Sites, s => s.Record.Id == 3601);
    }

    [TcDbFact]
    public async Task TeleportsComeFromGameTele()
    {
        var rows = await Open().ReadTeleportsAsync(CancellationToken.None);
        Assert.True(rows.Length > 1000);
        Assert.Contains(rows, r => r.Map == 0);
        Assert.Contains(rows, r => r.Map == 1);
        Assert.All(rows, r => Assert.NotEmpty(r.Name));
    }

    [TcDbFact]
    public async Task TheHotfixesAreaTriggerTableReadsAndIsEmptyOrWellFormed()
    {
        var source = new TcHotfixSource(Environments.TrinityConnection()!.HotfixesSettings);
        Assert.True(source.Available);
        var rows = await source.ReadAreaTriggersAsync(CancellationToken.None);
        Assert.All(rows, r => Assert.True(r.Id > 0));
    }

    [TcDbFact]
    public async Task AreaTriggersNeedTheClientLookup()
    {
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => Open(null).ReadAreaTriggersAsync(Elwynn, null, "", CancellationToken.None));
        Assert.Contains("retail client", error.Message);
    }

    [TcRetailFact]
    public async Task TheDeadminesEntranceTriggerTeleportsToItsSafeLocation()
    {
        var lookup = new RetailClientLookup(RetailData.Tables);
        var result = await Open(lookup).ReadAreaTriggersAsync(Westfall, null, "", CancellationToken.None);
        var trigger = result.Triggers.Single(t => t.Entry == 78);
        Assert.Equal(AreaTriggerKind.Teleport, trigger.Kind);
        Assert.Equal("Deadmines Entrance Target", trigger.Teleport!.Name);
        Assert.Equal(36, trigger.Teleport.Map);
        Assert.Equal(-14.5732, trigger.Teleport.X, 2);
        Assert.Equal(155u, trigger.QuestId);
        // The client's own position puts it at the Westfall entrance.
        Assert.InRange(trigger.X, -11300, -11100);
        Assert.InRange(trigger.Y, 1600, 1750);
        Assert.True(trigger.Radius > 0 || (trigger.Length > 0 && trigger.Width > 0 && trigger.Height > 0));
    }

    [TcRetailFact]
    public async Task AreaTriggerSearchFindsByIdAndByLinkedName()
    {
        var lookup = new RetailClientLookup(RetailData.Tables);
        var source = Open(lookup);
        var byId = await source.ReadAreaTriggersAsync(Westfall, null, "78", CancellationToken.None);
        Assert.Equal(78u, Assert.Single(byId.Triggers).Entry);
        var byName = await source.ReadAreaTriggersAsync(Westfall, null, "Deadmines Entrance", CancellationToken.None);
        Assert.Contains(byName.Triggers, t => t.Entry == 78);
        Assert.All(byName.Triggers, t => Assert.Contains("Deadmines", t.Teleport?.Name ?? ""));
    }

    [TcRetailFact]
    public async Task EveryReturnedTriggerLiesInsideTheRegion()
    {
        var lookup = new RetailClientLookup(RetailData.Tables);
        var result = await Open(lookup).ReadAreaTriggersAsync(Elwynn, Goldshire, "", CancellationToken.None);
        Assert.NotEmpty(result.Triggers);
        Assert.All(result.Triggers, t =>
        {
            Assert.Equal(0, t.Map);
            Assert.InRange(t.X, Elwynn.MinServerX, Elwynn.MaxServerX);
            Assert.InRange(t.Y, Elwynn.MinServerY, Elwynn.MaxServerY);
        });
    }
}
