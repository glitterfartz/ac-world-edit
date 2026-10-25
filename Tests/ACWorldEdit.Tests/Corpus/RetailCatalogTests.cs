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
