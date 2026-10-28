using ACWorldEdit.Assets;
using ACWorldEdit.Foundation;
using ACWorldEdit.Tests.Support;
using Xunit;

/// <summary>
/// The retail creature and GameObject display catalogs against a real install: displays resolve to
/// model references that exist in CASC, and hotfix rows replace client rows.
/// </summary>
[Trait("Category", "Corpus")]
[Collection(RetailData.Collection)]
public class RetailCatalogTests
{
    [RetailClientFact]
    public void CreatureDisplaysResolveToModelsThatExist()
    {
        var catalog = new RetailCreatureCatalog(RetailData.Tables);
        var displays = RetailData.Tables.Table("CreatureDisplayInfo");
        int resolved = 0, missing = 0;
        foreach (var display in displays.Values.Take(400))
        {
            try
            {
                var appearance = catalog.Resolve((uint)display.ID);
                Assert.True(AssetRef.TryParse(appearance.Path, out uint file));
                Assert.True(RetailData.Files.Exists(AssetRef.FromFileId(file)), $"display {display.ID}: model {file} is missing from CASC");
                Assert.Equal(3, appearance.Textures.Length);
                Assert.True(appearance.Scale > 0);
                resolved++;
            }
            catch (InvalidDataException)
            {
                // Displays without a model are legitimate (invisible, placeholder rows).
                missing++;
            }
        }

        Assert.True(resolved > missing, $"{resolved} resolved, {missing} without a model");
    }

    [RetailClientFact]
    public void UnknownCreatureDisplaysFail()
    {
        var catalog = new RetailCreatureCatalog(RetailData.Tables);
        Assert.Throws<InvalidDataException>(() => catalog.Resolve(uint.MaxValue - 1));
    }

    [RetailClientFact]
    public void AHotfixRowReplacesTheClientRow()
    {
        var catalog = new RetailCreatureCatalog(RetailData.Tables);
        uint id = (uint)RetailData.Tables.Table("CreatureDisplayInfo").Keys.First();
        catalog.Override(id, new CreatureDisplayOverride(123456, [654321, 0], 2f));
        var appearance = catalog.Resolve(id);
        Assert.Equal(AssetRef.FromFileId(123456), appearance.Path);
        Assert.Equal([AssetRef.FromFileId(654321), "", ""], appearance.Textures);
        Assert.Equal(2f, appearance.Scale);
    }

    [RetailClientFact]
    public void AHumanoidHotfixRowKeepsItsBakedSkinAndCustomization()
    {
        var catalog = new RetailCreatureCatalog(RetailData.Tables);
        var extras = RetailData.Tables.Table("CreatureDisplayInfoExtra");
        var display = RetailData.Tables.Table("CreatureDisplayInfo").Values
            .First(d => d.FieldAs<int>("ExtendedDisplayInfoID") is int extra && extra != 0 && extras.ContainsKey(extra) && catalog.Resolve((uint)d.ID).BakedTexture != null);
        var client = catalog.Resolve((uint)display.ID);
        Assert.True(AssetRef.TryParse(client.Path, out uint model));

        catalog.Override((uint)display.ID, new CreatureDisplayOverride(model, [0, 0, 0], 1.5f, display.FieldAs<int>("ExtendedDisplayInfoID")));
        var hotfixed = catalog.Resolve((uint)display.ID);

        Assert.Equal(1.5f, hotfixed.Scale);
        Assert.Equal(client.BakedTexture, hotfixed.BakedTexture);
        Assert.Equal(client.Geosets, hotfixed.Geosets);
        Assert.Equal(client.HairTexture, hotfixed.HairTexture);
        Assert.Equal(client.EyeTexture, hotfixed.EyeTexture);
    }

    [RetailClientFact]
    public void DisplayHotfixesNameTheirModelFromTheClientOrTheirOwnModelData()
    {
        var catalog = new RetailCreatureCatalog(RetailData.Tables);
        uint id = (uint)RetailData.Tables.Table("CreatureDisplayInfo").Keys.First();
        var model = RetailData.Tables.Table("CreatureModelData").Values.First(r => r.FieldAs<uint>("FileDataID") != 0);
        catalog.Apply([new(id, (uint)model.ID, 3f, [111, 0, 0])], new Dictionary<uint, uint>());
        var fromClient = catalog.Resolve(id);
        Assert.Equal(AssetRef.FromFileId(model.FieldAs<uint>("FileDataID")), fromClient.Path);
        Assert.Equal(3f, fromClient.Scale);
        Assert.Equal(AssetRef.FromFileId(111), fromClient.Textures[0]);

        // A model data row the hotfixes carry themselves wins over the client's.
        catalog.Apply([new(id, (uint)model.ID, 1f, [])], new Dictionary<uint, uint> { [(uint)model.ID] = 555 });
        Assert.Equal(AssetRef.FromFileId(555), catalog.Resolve(id).Path);

        // A model nobody knows leaves the display without one.
        catalog.Apply([new(id, uint.MaxValue - 1, 1f, [])], new Dictionary<uint, uint>());
        Assert.Throws<InvalidDataException>(() => catalog.Resolve(id));
    }

    [RetailClientFact]
    public void GameObjectDisplaysResolveToModelsThatExist()
    {
        var catalog = new RetailGameObjectCatalog(RetailData.Tables);
        int resolved = 0;
        foreach (var display in RetailData.Tables.Table("GameObjectDisplayInfo").Values.Take(400))
        {
            try
            {
                string path = catalog.Resolve((uint)display.ID);
                Assert.True(AssetRef.TryParse(path, out uint file));
                Assert.True(RetailData.Files.Exists(AssetRef.FromFileId(file)), $"display {display.ID}: model {file} is missing from CASC");
                resolved++;
            }
            catch (InvalidDataException)
            {
            }
        }

        Assert.True(resolved > 100, $"only {resolved} GameObject displays resolved");
    }

    [RetailClientFact]
    public void AGameObjectHotfixRowReplacesTheClientRow()
    {
        var catalog = new RetailGameObjectCatalog(RetailData.Tables);
        uint id = (uint)RetailData.Tables.Table("GameObjectDisplayInfo").Keys.First();
        catalog.Override(id, 777);
        Assert.Equal(AssetRef.FromFileId(777), catalog.Resolve(id));
    }
}

[Trait("Category", "Corpus")]
[Collection(RetailData.Collection)]
public class RetailClientLookupTests
{
    static RetailClientLookup Lookup() => new(RetailData.Tables);

    [RetailClientFact]
    public void AreaTriggersCarryPositionsAndShapes()
    {
        var triggers = Lookup().AreaTriggers();
        Assert.True(triggers.Count > 1000);
        Assert.Contains(triggers, t => t.Map == 0 && (t.Radius > 0 || t.BoxLength > 0));
        Assert.All(triggers, t => Assert.True(t.ShapeType <= 8, $"trigger {t.Id} shape {t.ShapeType}"));
        Assert.Contains(triggers, t => t.X != 0 || t.Y != 0);
    }

    [RetailClientFact]
    public void PolygonTriggersTakeTheirCornersFromTheirPath()
    {
        var lookup = Lookup();
        var polygons = lookup.AreaTriggers().Where(t => t.ShapeType == (byte)AreaTriggerShape.Polygon).ToList();
        Assert.NotEmpty(polygons);
        var resolved = polygons.Select(t => lookup.Polygon(t.ShapeId)).OfType<ClientPolygon>().ToList();
        Console.WriteLine($"AREATRIGGER_POLYGONS {polygons.Count} polygon triggers, {resolved.Count} with corners, {resolved.Count(p => p.Height != null)} with a height, {polygons.Count(t => t.X == 0 && t.Y == 0)} placed at the map origin");
        Assert.True(resolved.Count > polygons.Count / 2, $"only {resolved.Count} of {polygons.Count} polygons have a path");
        // The path origin is dropped, so corners are the offsets of the remaining (3 or more) nodes.
        Assert.All(resolved, p => Assert.True(p.Vertices.Count >= 3 && p.Vertices.All(v => float.IsFinite(v.X) && float.IsFinite(v.Y)) && float.IsFinite(p.Ground)));
        Assert.All(resolved.Where(p => p.Height != null), p => Assert.True(p.Height >= 0.02f));
        Assert.Null(lookup.Polygon(uint.MaxValue - 1));
    }

    [RetailClientFact]
    public void ItemsSpellsAndBroadcastTextResolveByIdAndByName()
    {
        var lookup = Lookup();

        var item = lookup.SearchItems("Linen Cloth", 5).FirstOrDefault(i => i.Name == "Linen Cloth");
        Assert.NotNull(item);
        Assert.NotEqual(0u, item.IconFileId);
        Assert.Equal(item.Name, lookup.Item(item.Id)!.Name);
        Assert.Equal(item.Id, Assert.Single(lookup.SearchItems(item.Id.ToString(), 5)).Id);

        var (spellId, spellName) = lookup.SearchSpells("Fireball", 20).First(s => s.Name == "Fireball");
        Assert.Equal(spellName, lookup.SpellName(spellId));
        Assert.Null(lookup.SpellName(uint.MaxValue - 1));

        var (textId, text) = lookup.SearchBroadcastText("Greetings", 5).First();
        Assert.Equal(text, lookup.BroadcastText(textId) ?? lookup.BroadcastText(textId, true));
        Assert.Null(lookup.BroadcastText(uint.MaxValue - 1));
    }

    [RetailClientFact]
    public void CreatureScalingTablesReadByLevelExpansionAndContentTuning()
    {
        var lookup = Lookup();

        // ExpectedStat is keyed by its Lvl relation column: a normal level must resolve for the current expansion.
        var stat = lookup.ExpectedStat(10, 11) ?? lookup.ExpectedStat(10, -2);
        Assert.NotNull(stat);
        Assert.True(stat.CreatureHealth > 0 && stat.CreatureAutoAttackDps > 0 && stat.CreatureArmor > 0);
        Assert.True((lookup.ExpectedStat(60, 11) ?? lookup.ExpectedStat(60, -2))!.CreatureHealth > stat.CreatureHealth);

        // The four class modifier rows TrinityCore picks by unit class.
        for (uint id = 1; id <= 4; id++)
        {
            Assert.NotNull(lookup.ExpectedStatMod(id));
        }

        // ContentTuningXExpected is grouped by its ContentTuningID relation column.
        Assert.Null(lookup.ContentTuning(uint.MaxValue - 1));
        var grouped = Enumerable.Range(1, 3000).Select(i => (uint)i).Where(i => lookup.ContentTuningMods(i).Count > 0).ToArray();
        Assert.NotEmpty(grouped);
        var tuning = Enumerable.Range(1, 3000).Select(i => lookup.ContentTuning((uint)i)).First(t => t != null && t.MaxLevel > 0);
        Assert.InRange(tuning!.MinLevel, 0, 123);
    }

    [RetailClientFact]
    public void ConcurrentCallsAreSafe()
    {
        var lookup = Lookup();
        Parallel.For(0, 8, i => Assert.NotEmpty(lookup.SearchItems("a", 3)));
        Assert.NotEmpty(lookup.AreaTriggers());
    }
}

/// <summary>Retail creature models carry their own animation: chunked M2s no longer fall back to the reference pose.</summary>
[Trait("Category", "Corpus")]
[Collection(RetailData.Collection)]
public class RetailAnimationTests
{
    [RetailClientFact]
    public void RetailCreatureModelsReadAndMostStandAnimated()
    {
        var catalog = new RetailCreatureCatalog(RetailData.Tables);
        int read = 0, animated = 0, failed = 0;
        foreach (var display in RetailData.Tables.Table("CreatureDisplayInfo").Values.Take(300))
        {
            CreatureAppearance appearance;
            try
            {
                appearance = catalog.Resolve((uint)display.ID);
            }
            catch (InvalidDataException)
            {
                continue;
            }

            try
            {
                var model = M2Model.Read(RetailData.Files, appearance.Path, appearance.Textures);
                read++;
                if (model.Duration > 0 && model.Bones.Any(b => b.Rotation.Times.Length > 1 || b.Translation.Times.Length > 1))
                {
                    animated++;
                }
            }
            catch (Exception e) when (e is InvalidDataException or NotSupportedException or FileNotFoundException)
            {
                failed++;
            }
        }

        Assert.True(read > 100, $"{read} models read, {failed} failed");
        Assert.True(animated * 2 > read, $"only {animated} of {read} models play a Stand animation");
    }

    /// <summary>
    /// Humanoid displays draw a character model whose skeleton lives in a .skel file, with a baked skin
    /// and customization choices that pick the head, hair and eyes.
    /// </summary>
    [RetailClientFact]
    public void HumanoidDisplaysAnimateWithBakedSkinHairAndEyes()
    {
        var catalog = new RetailCreatureCatalog(RetailData.Tables);
        var extras = RetailData.Tables.Table("CreatureDisplayInfoExtra");
        var humanoids = RetailData.Tables.Table("CreatureDisplayInfo").Values
            .Where(d => d.FieldAs<int>("ExtendedDisplayInfoID") is int extra && extra != 0 && extras.ContainsKey(extra))
            .Select(d => catalog.Resolve((uint)d.ID))
            .Where(a => a.BakedTexture != null)
            .Take(5)
            .ToList();

        Assert.NotEmpty(humanoids);
        foreach (var appearance in humanoids)
        {
            Assert.NotNull(appearance.Geosets);
            Assert.Equal(2, appearance.Geosets.Parse()[32]);
            Assert.NotNull(appearance.HairTexture);
            Assert.NotNull(appearance.EyeTexture);

            var model = M2Model.Read(RetailData.Files, appearance.Path, appearance.Textures, appearance.BakedTexture, hair: appearance.HairTexture, eyes: appearance.EyeTexture, geosets: appearance.Geosets);
            Assert.True(model.Bones.Length > 100, $"{appearance.Path}: {model.Bones.Length} bones");
            Assert.True(model.Duration > 0, $"{appearance.Path}: no Stand animation");
            Assert.Contains(model.Bones, b => b.Rotation.Times.Length > 1);
            Assert.DoesNotContain(model.Warnings, w => w.StartsWith("Unresolved replaceable texture"));
            Assert.Contains(model.Surfaces, s => s.Texture == appearance.BakedTexture);
            Assert.Contains(model.Surfaces, s => s.Texture == appearance.HairTexture);
            Assert.Contains(model.Surfaces, s => s.Texture == appearance.EyeTexture);
        }
    }

    [RetailClientFact]
    public void ReferenceTablesAnswerExistenceChecks()
    {
        var lookup = new RetailClientLookup(RetailData.Tables);
        Assert.True(lookup.Exists(ClientRecord.Difficulty, 1));
        Assert.True(lookup.Exists(ClientRecord.SkillLine, 164));
        Assert.True(lookup.Exists(ClientRecord.Spell, 2575));
        Assert.True(lookup.Exists(ClientRecord.Item, 2589));
        foreach (var kind in Enum.GetValues<ClientRecord>())
        {
            Assert.False(lookup.Exists(kind, 4000000000u), kind.ToString());
        }
    }
}
