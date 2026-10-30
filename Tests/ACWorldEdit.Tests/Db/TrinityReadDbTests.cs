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
[Collection(RetailData.Collection)]
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
    public async Task GossipReferenceChecksResolveMenusAndExactBroadcastIds()
    {
        var source = Open();
        var snapshot = await source.ReadInteractionAsync(295, 0, [], rows => rows, null, CancellationToken.None);
        uint menu = snapshot.Rows.All(source.Schema.GossipPage.Options.Table).Select(r => r.U("MenuID")).First();
        Assert.True(await source.MenuExistsAsync(menu, CancellationToken.None));
        Assert.False(await source.MenuExistsAsync(uint.MaxValue, CancellationToken.None));
        uint broadcast = snapshot.Rows.All(source.Schema.GossipPage.TextTable)
            .SelectMany(r => Enumerable.Range(0, 8).Select(i => r.U($"BroadcastTextID{i}"))).First(id => id != 0);
        var match = await source.SearchBroadcastAsync(broadcast.ToString(System.Globalization.CultureInfo.InvariantCulture), CancellationToken.None);
        Assert.Equal(broadcast, Assert.Single(match).Id);
        Assert.Empty(await source.SearchBroadcastAsync(uint.MaxValue.ToString(), CancellationToken.None));
    }

    [TcDbFact]
    public async Task QuestSnapshotReadsTheTemplateAndItsObjectives()
    {
        var source = Open();
        var snapshot = await source.ReadQuestAsync(176, [], rows => rows, null, CancellationToken.None);
        var quest = snapshot.Rows.Get(TcTables.QuestTemplate, "176");
        Assert.NotNull(quest);
        Assert.Contains("Hogger", quest.T("LogTitle"));
        var objective = Assert.Single(snapshot.Rows.All(TcTables.QuestObjectives), r => r.U("QuestID") == 176);
        Assert.Equal(TcQuestRules.Monster, objective.U("Type"));
        Assert.Equal("Hogger", snapshot.Names.Creature(objective.U("ObjectID")));
        Assert.True(snapshot.MaxQuestId >= 176);
        Assert.True(snapshot.MaxObjectiveId >= objective.U("ID"));
        Assert.Empty(TcQuestRules.Check(176, snapshot.Rows, snapshot.Names, null, null).Where(i => i.Error));
        Assert.Contains(await source.SearchQuestsAsync("Hogger", CancellationToken.None), q => q.Id == 176);
        Assert.Contains(await source.SearchAsync(SmartParamKind.Creature, "Hogger", CancellationToken.None), c => c.Id == 448);
    }

    [TcDbFact]
    public async Task BehaviorSnapshotReadsHoggersRulesLinkAndSpeech()
    {
        var source = Open();
        var smart = source.Schema.Smart;
        var snapshot = await source.ReadBehaviorAsync(448, 0, [], rows => rows, null, CancellationToken.None);
        var scope = new SmartScope(448, SmartScope.Creature);
        var rules = SmartRuleEdits.RulesOf(SmartCatalog.Trinity, snapshot.Rows, scope);
        Assert.Equal("Hogger", snapshot.CreatureName);
        Assert.Equal(4, rules.Count);
        Assert.Contains(rules, r => r.Event == SmartCatalog.LinkEvent && r.Id != 0);
        Assert.Equal("SmartAI", snapshot.Rows.Get(smart.CreatureAi, "448")!.T("AIName"));
        Assert.Empty(smart.Check(scope, rules, snapshot.Names, snapshot.Rows.Get(smart.CreatureAi, "448"), 448).Where(i => i.Error));

        // Every rule reads as a sentence (no unknown types) and its row round-trips the TrinityCore table.
        foreach (var rule in rules)
        {
            rule.Row.Validate(smart.Scripts);
            Assert.DoesNotContain("(unknown)", smart.Describe(rule, snapshot.Names, 448), StringComparison.Ordinal);
        }

        // Speech: the lines carry their SoundPlayType and the broadcast texts they link are named.
        var lines = snapshot.Rows.All(smart.CreatureText).Where(t => t.U("CreatureID") == 448).ToList();
        Assert.NotEmpty(lines);
        lines.ForEach(line => line.Validate(smart.CreatureText));
        Assert.Contains("Grrrr", (snapshot.Names.Broadcasts.TryGetValue(1870, out var broadcast) ? broadcast : ""), StringComparison.Ordinal);
        Assert.Contains("bones to gnaw", snapshot.Names.Text(448, 0) ?? "", StringComparison.Ordinal);
    }

    [TcDbFact]
    public async Task BehaviorSnapshotLoadsTimedActionListsAndSeesDraftedRules()
    {
        var source = Open();
        var smart = source.Schema.Smart;
        // Scourge Plague Spreader runs timed action lists; they come with the snapshot.
        var snapshot = await source.ReadBehaviorAsync(25349, 0, [], rows => rows, null, CancellationToken.None);
        Assert.Contains(snapshot.Rows.All(smart.Scripts), r => r.U("source_type") == SmartScope.ActionList);
        var used = await source.UsedActionListsAsync(2534900, 2534999, CancellationToken.None);
        Assert.All(used, id => Assert.InRange(id, 2534900, 2534999));

        // A drafted rule on another creature is loaded by its key even though it is not in the database.
        var drafted = smart.NewRow(new SmartScope(448, SmartScope.Creature), 40, 4, 1, 1, "draft");
        var withDraft = await source.ReadBehaviorAsync(448, 0, [(smart.Scripts, smart.Scripts.KeyOf(drafted))], rows => rows, null, CancellationToken.None);
        Assert.Null(withDraft.Rows.Get(smart.Scripts, smart.Scripts.KeyOf(drafted)));
        await Assert.ThrowsAsync<InvalidDataException>(() => source.ReadBehaviorAsync(4_000_000_000, 0, [], rows => rows, null, CancellationToken.None));
    }

    [TcDbFact]
    public async Task WaypointPathsComeFromTheNodeTableWithThePathsMoveType()
    {
        var result = await Open().ReadWaypointsAsync(Elwynn, Goldshire, CancellationToken.None);
        // A Goldshire creature (guid 280672) walks path 22453760, whose waypoint_path row says walk.
        var path = Assert.Single(result.Paths, p => p.Id == 22453760);
        Assert.Equal(WorldDialect.TrinityCore, path.Dialect);
        Assert.Equal(92, path.Nodes.Length);
        Assert.Contains(path.Users, u => u.Guid == 280672 && u.Walks);
        Assert.All(path.Nodes, n => Assert.True(n.MoveType == 0 && n.Velocity == 0 && !n.Smooth && n.Action == 0));
        Assert.Equal(path.Nodes.Select(n => n.Point).Order(), path.Nodes.Select(n => n.Point));
    }

    [TcDbFact]
    public async Task MenuSearchFindsMenusByIdAndByTheTextOfTheirGreeting()
    {
        var source = Open();
        var snapshot = await source.ReadInteractionAsync(295, 0, [], rows => rows, null, CancellationToken.None);
        uint menu = snapshot.Rows.All(source.Schema.GossipPage.Options.Table).Select(r => r.U("MenuID")).First();
        var byId = await source.SearchMenusAsync(menu.ToString(System.Globalization.CultureInfo.InvariantCulture), CancellationToken.None);
        Assert.Equal(menu, byId[0].Id);
        Assert.True(byId.Count <= DbLimits.MaxSearch);

        string greeting = byId[0].Name;
        Assert.NotEmpty(greeting);
        var byText = await source.SearchMenusAsync(greeting[..Math.Min(greeting.Length, 25)], CancellationToken.None);
        Assert.Contains(byText, m => m.Id == menu);
        Assert.True(byText.Count <= DbLimits.MaxSearch);
    }

    [TcDbFact]
    public async Task BroadcastSearchFindsTextFromTheHotfixesTable()
    {
        var source = Open();
        var byText = await source.SearchBroadcastAsync("fresh meat", CancellationToken.None);
        Assert.Contains(byText, b => b.Id == 1870);
        Assert.Equal("Grrrr... fresh meat!", Assert.Single(await source.SearchBroadcastAsync("1870", CancellationToken.None)).Name);
        Assert.Empty(await source.SearchBroadcastAsync("", CancellationToken.None));
    }

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
    public async Task BattlegroundLinksAreLockedAndPlainTeamLinksAreNot()
    {
        var goldshire = (await Open().ReadGraveyardsAsync(Elwynn, Goldshire, "", CancellationToken.None)).Sites.Single(s => s.Record.Id == 106);
        Assert.False(goldshire.IsLocked(12));
        Assert.Equal(0, goldshire.TeleportUses);
        // Snowfall in Alterac Valley: its zone link is decided by world states and else-groups.
        var alterac = await Open().ReadGraveyardsAsync(new CreatureRegion(30, 30, 33, 30, 33), null, "", CancellationToken.None);
        var snowfall = alterac.Sites.Single(s => s.Record.Id == 169);
        Assert.True(snowfall.IsLocked(2597));
    }

    static async Task<TcCreatureView> Creature(uint entry)
    {
        var snapshot = await Open().ReadInteractionAsync(entry, 0, [], rows => rows, null, CancellationToken.None);
        return TcCreatureView.Build(entry, snapshot.Rows, snapshot.Lookup);
    }

    [TcDbFact]
    public async Task TheGoldshireInnkeeperHasHisMenuTextsAndVendorItems()
    {
        var farley = await Creature(295);
        Assert.Equal("Innkeeper Farley", farley.Name);
        Assert.Contains("Innkeeper", TcCreatureView.DescribeNpcFlags(farley.NpcFlags));
        Assert.Contains("Vendor", TcCreatureView.DescribeNpcFlags(farley.NpcFlags));
        Assert.Equal(2, farley.Difficulties.Length);
        var menu = Assert.Single(farley.Menus, m => m.Root);
        Assert.NotEmpty(menu.Texts);
        // The menu text comes from hotfixes.broadcast_text (no client data in this test).
        Assert.Contains(menu.Texts.SelectMany(t => t.Variants), v => !string.IsNullOrEmpty(v.Text));
        Assert.Equal(12, farley.Vendor.Length);
        Assert.All(farley.Vendor, v => Assert.Equal(1u, v.Type));
    }

    [TcDbFact]
    public async Task ATrainerIsBoundToTheGossipOptionThatOpensIt()
    {
        var belle = await Creature(2329);
        var trainer = Assert.Single(belle.Trainers);
        Assert.Equal(877u, trainer.TrainerId);
        Assert.Equal((5855u, 0u), (trainer.MenuId, trainer.OptionId));
        Assert.False(trainer.Missing);
        Assert.Equal(9, trainer.Spells.Length);
        var option = belle.Menus.Single(m => m.MenuId == 5855).Options.Single(o => o.OptionId == 0);
        Assert.Equal(877u, option.TrainerId);
    }

    [TcDbFact]
    public async Task QuestLinksCarryTheirTitles()
    {
        var dughan = await Creature(240);
        Assert.Equal(5, dughan.Starts.Length);
        Assert.All(dughan.Starts, q => Assert.False(string.IsNullOrEmpty(q.Info?.Title)));
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
    public async Task TheHotfixesMaximumIdsNumberNewBroadcastTexts()
    {
        var source = new TcHotfixSource(Environments.TrinityConnection()!.HotfixesSettings);
        var (broadcast, hotfix) = await source.ReadMaxIdsAsync(CancellationToken.None);
        Assert.True(broadcast > 0);
        Assert.True(hotfix > 0);
        // A text the tool writes sits above every row the database already has.
        Assert.Empty((await source.ReadBroadcastTextsAsync([broadcast + 1], CancellationToken.None)));
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
    public async Task TheHotfixesItemSparseOverridesReadByIdAndSkipUnlistedItems()
    {
        var source = new TcHotfixSource(Environments.TrinityConnection()!.HotfixesSettings);
        var items = await source.ReadItemOverridesAsync([165657u, 2589u], CancellationToken.None);
        Assert.Equal("Free T-Shirt", items[165657].Name);
        Assert.Equal(3, items[165657].Quality);
        Assert.False(items.ContainsKey(2589));
    }

    [TcDbFact]
    public async Task TheHotfixesCreatureDisplaysReadWithTheirModelFiles()
    {
        var source = new TcHotfixSource(Environments.TrinityConnection()!.HotfixesSettings);
        var (displays, modelFiles) = await source.ReadCreatureDisplaysAsync(CancellationToken.None);
        Assert.NotEmpty(displays);
        Assert.All(displays, d =>
        {
            Assert.True(d.Id > 0);
            Assert.Equal(3, d.TextureFileIds.Length);
        });
        Assert.All(modelFiles, pair => Assert.True(pair.Key > 0));
    }

    [TcRetailFact]
    public async Task HotfixedHumanoidDisplaysKeepTheirSkinAndCustomization()
    {
        var (displays, modelFiles) = await new TcHotfixSource(Environments.TrinityConnection()!.HotfixesSettings).ReadCreatureDisplaysAsync(CancellationToken.None);
        var extras = RetailData.Tables.Table("CreatureDisplayInfoExtra");
        var humanoids = displays.Where(d => d.ExtraId != 0).ToList();
        Assert.NotEmpty(humanoids);
        // The hotfixes database has no creature_display_info_extra rows, so each extra must come from the client.
        var unknown = humanoids.Where(d => !extras.ContainsKey(d.ExtraId)).Select(d => $"{d.Id}→{d.ExtraId}").ToList();
        Assert.True(unknown.Count == 0, "client has no extra for hotfixed displays " + string.Join(", ", unknown));
        var catalog = new RetailCreatureCatalog(RetailData.Tables);
        catalog.Apply(displays, modelFiles);
        foreach (var display in humanoids)
        {
            var appearance = catalog.Resolve(display.Id);
            Assert.NotNull(appearance.BakedTexture);
            Assert.NotNull(appearance.Geosets);
        }
    }

    /// <summary>The tiles around a server point, the region shape the viewer queries.</summary>
    static CreatureRegion Around(ushort map, Point3 center)
    {
        var (x, y) = WorldCoordinates.Tile(center);
        return new(map, x - 1, x + 1, y - 1, y + 1);
    }

    // Stormwind: spawned trigger 290 (a box running custom template 188's SmartAI) and client trigger
    // 1125 (Cathedral of Light, SmartTrigger with a linked chain).
    static readonly Point3 StormwindSpawn = new(-8321.21, 288.127, 156.834);

    [TcRetailFact]
    public async Task ServerSpawnedTriggersLoadWithTheirShapeAndTemplateRules()
    {
        var source = Open(new RetailClientLookup(RetailData.Tables));
        var result = await source.ReadAreaTriggersAsync(Around(0, StormwindSpawn), new(StormwindSpawn.X, StormwindSpawn.Y, StormwindSpawn.Z), "", CancellationToken.None);
        var spawn = result.Triggers.Single(t => t.IsSpawn && t.SpawnId == 290);
        Assert.Equal(AreaTriggerShape.Box, spawn.Volume);
        Assert.Equal(188u, spawn.Entry);
        Assert.True(spawn.IsCustom);
        Assert.Equal(AreaTriggerKind.SmartAI, spawn.Kind);
        Assert.Equal(1, spawn.SmartRows);
        Assert.True(spawn.Length > 0 && spawn.Width > 0 && spawn.Height > 0);
        Assert.True(spawn.Contains(StormwindSpawn));
        Assert.Contains("Stormwind", spawn.Comment);
        Assert.Equal(new AreaTriggerOwner(188, SmartScope.AreaTriggerEntityCustom, 290), spawn.Owner);
        Assert.Contains(result.Triggers, t => !t.IsSpawn);
        // Nearest first across both kinds.
        Assert.Equal(result.Triggers.OrderBy(t => Math.Pow(t.X - StormwindSpawn.X, 2) + Math.Pow(t.Y - StormwindSpawn.Y, 2) + Math.Pow(t.Z - StormwindSpawn.Z, 2)), result.Triggers);
        var bySpawn = await source.ReadAreaTriggersAsync(Around(0, StormwindSpawn), null, "290", CancellationToken.None);
        Assert.Contains(bySpawn.Triggers, t => t.SpawnId == 290);
        var byComment = await source.ReadAreaTriggersAsync(Around(0, StormwindSpawn), null, "Conversation trigger 10963", CancellationToken.None);
        Assert.Equal(290ul, Assert.Single(byComment.Triggers, t => t.IsSpawn).SpawnId);
    }

    [TcRetailFact]
    public async Task ClientPolygonsAndCylindersLoadInTheShapeTheServerTests()
    {
        var lookup = new RetailClientLookup(RetailData.Tables);
        var source = Open(lookup);
        foreach (var shape in new[] { AreaTriggerShape.Polygon, AreaTriggerShape.Cylinder })
        {
            var row = lookup.AreaTriggers().First(t => t.ShapeType == (byte)shape && t.Map <= ushort.MaxValue && (shape != AreaTriggerShape.Polygon || lookup.Polygon(t.ShapeId) != null) && (shape != AreaTriggerShape.Cylinder || (t.Radius > 0 && t.BoxHeight > 0)));
            // A polygon is found where its corners are, not at its position (often the map origin).
            var polygon = shape == AreaTriggerShape.Polygon ? lookup.Polygon(row.ShapeId)! : null;
            var center = polygon == null ? new Point3(row.X, row.Y, row.Z)
                : new Point3(row.X + polygon.Vertices.Average(v => v.X), row.Y + polygon.Vertices.Average(v => v.Y), row.Z + polygon.Ground);
            var result = await source.ReadAreaTriggersAsync(Around((ushort)row.Map, center), null, row.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), CancellationToken.None);
            var trigger = Assert.Single(result.Triggers, t => !t.IsSpawn);
            Assert.Equal(shape, trigger.Volume);
            if (polygon != null)
            {
                Assert.Equal(polygon.Vertices.Count, trigger.Vertices.Count);
                Assert.True(double.IsNegativeInfinity(trigger.Bottom));
                Assert.Equal(polygon.Height ?? double.PositiveInfinity, trigger.Top, 4);
                Assert.Equal(center.X, trigger.Focus.X, 2);
                Assert.Equal(center.Y, trigger.Focus.Y, 2);
                Assert.Equal(center.Z, trigger.Focus.Z, 2);
            }
            else
            {
                Assert.Equal(row.Radius, trigger.Radius, 4);
                Assert.Equal((0.0, (double)row.BoxHeight), (trigger.Bottom, trigger.Top));
                Assert.True(trigger.Contains(new(row.X, row.Y, row.Z + row.BoxHeight / 2)));
                Assert.False(trigger.Contains(new(row.X, row.Y, row.Z - 1)));
            }
        }
    }

    [TcRetailFact]
    public async Task ClientTriggerRulesReadWithTheirScriptName()
    {
        var source = Open(new RetailClientLookup(RetailData.Tables));
        var snapshot = await source.ReadAreaTriggerBehaviorAsync(new(1125), [], rows => rows, null, CancellationToken.None);
        var scope = new SmartScope(1125, SmartScope.AreaTrigger);
        var rules = SmartRuleEdits.RulesOf(SmartCatalog.Trinity, snapshot.Rows, scope);
        Assert.Equal(3, rules.Count);
        Assert.Contains(rules, r => r.Event == SmartCatalog.LinkEvent);
        Assert.Equal(AreaTriggerRecord.SmartTrigger, snapshot.ScriptName);
        Assert.Equal("AreaTrigger 1125", snapshot.CreatureName);
        var smart = SmartCatalog.Trinity;
        var issues = smart.Check(scope, rules, snapshot.Names, null, 1125)
            .Concat(smart.CheckAreaTriggerScript(new(1125), snapshot.Rows.Get(smart.AreaTriggerScripts, "1125"), "", true)).ToList();
        Assert.True(issues.All(i => !i.Error), string.Join("; ", issues.Select(i => i.Message)));
        await Assert.ThrowsAsync<InvalidDataException>(() => source.ReadAreaTriggerBehaviorAsync(new(uint.MaxValue - 1), [], rows => rows, null, CancellationToken.None));
    }

    [TcDbFact]
    public async Task SpawnedTriggerRulesReadWithTheSpawnsScriptName()
    {
        var owner = new AreaTriggerOwner(188, SmartScope.AreaTriggerEntityCustom, 290);
        var snapshot = await Open().ReadAreaTriggerBehaviorAsync(owner, [], rows => rows, null, CancellationToken.None);
        var rules = SmartRuleEdits.RulesOf(SmartCatalog.Trinity, snapshot.Rows, new SmartScope(188, SmartScope.AreaTriggerEntityCustom));
        Assert.Single(rules);
        var smart = SmartCatalog.Trinity;
        var spawn = snapshot.Rows.Get(smart.AreaTriggerAi!, "290");
        Assert.Equal(AreaTriggerRecord.SmartEntity, spawn!.T("ScriptName"));
        Assert.Empty(smart.CheckAreaTriggerScript(owner, spawn, snapshot.ScriptName, true));
        // The live rule links to a rule that does not exist (a data defect the editor reports); the
        // AreaTrigger-specific checks accept it.
        var issues = smart.Check(new(188, SmartScope.AreaTriggerEntityCustom), rules, snapshot.Names, null, 188).ToList();
        Assert.DoesNotContain(issues, i => i.Message.Contains("only accepts", StringComparison.Ordinal) || i.Message.Contains("targets Self", StringComparison.Ordinal));
        // The template is custom: the non-custom template 188 is a different one (or none).
        await Assert.ThrowsAsync<InvalidDataException>(() => Open().ReadAreaTriggerBehaviorAsync(new(uint.MaxValue - 1, SmartScope.AreaTriggerEntity, 290), [], rows => rows, null, CancellationToken.None));
    }

    [TcDbFact]
    public async Task AreaTriggersNeedTheClientLookup()
    {
        var error = await Assert.ThrowsAsync<DatabaseFeatureException>(() => Open(null).ReadAreaTriggersAsync(Elwynn, null, "", CancellationToken.None));
        Assert.Contains("retail client", error.Message);
    }

    [TcDbFact]
    public async Task AnUnreadableHotfixesDatabaseLeavesTheClientTriggers()
    {
        var connection = Environments.TrinityConnection()! with { Hotfixes = "no_such_hotfixes_database" };
        var lookup = new OneTrigger(new ClientAreaTrigger(78, 0, -11208f, 1672f, 24f, 5f, 0, 0, 0, 0, 0, 0, 0, 0));
        var result = await DBSource.Open(connection, lookup).ReadAreaTriggersAsync(Westfall, null, "", CancellationToken.None);
        var trigger = Assert.Single(result.Triggers);
        Assert.Equal(78u, trigger.Entry);
        Assert.Equal("Deadmines Entrance Target", trigger.Teleport!.Name);
    }

    /// <summary>A client lookup holding a single trigger, so the test needs no retail client.</summary>
    sealed class OneTrigger(ClientAreaTrigger trigger) : IClientLookup
    {
        public IReadOnlyList<ClientAreaTrigger> AreaTriggers() => [trigger];
        public ClientPolygon? Polygon(uint pathId) => null;
        public string? BroadcastText(uint id, bool female = false) => null;
        public IReadOnlyList<(uint Id, string Text)> SearchBroadcastText(string term, int limit) => [];
        public ClientItem? Item(uint id) => null;
        public IReadOnlyList<ClientItem> SearchItems(string term, int limit) => [];
        public ItemDetails? ItemDetails(uint id) => null;
        public string? SpellName(uint id) => null;
        public IReadOnlyList<(uint Id, string Name)> SearchSpells(string term, int limit) => [];
        public bool Exists(ClientRecord kind, uint id) => false;
        public TcContentTuning? ContentTuning(uint id) => null;
        public TcExpectedStat? ExpectedStat(uint level, int expansion) => null;
        public TcExpectedStatMod? ExpectedStatMod(uint id) => null;
        public IReadOnlyList<TcContentTuningMod> ContentTuningMods(uint contentTuningId) => [];
        public int? MilestoneSeason(int mythicPlusSeasonId) => null;
        public uint MapDifficultyTuning(uint mapId, int difficultyId) => 0;
        public uint AreaTuning(uint areaId) => 0;
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
