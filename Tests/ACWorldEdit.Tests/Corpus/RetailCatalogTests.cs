using ACWorldEdit.Assets;
using ACWorldEdit.Tests.Support;
using Xunit;

/// <summary>
/// The retail creature and GameObject display catalogs against a real install: displays resolve to
/// model references that exist in CASC, and hotfix rows replace client rows.
/// </summary>
[Trait("Category", "Corpus")]
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
    public void ConcurrentCallsAreSafe()
    {
        var lookup = Lookup();
        Parallel.For(0, 8, i => Assert.NotEmpty(lookup.SearchItems("a", 3)));
        Assert.NotEmpty(lookup.AreaTriggers());
    }
}
