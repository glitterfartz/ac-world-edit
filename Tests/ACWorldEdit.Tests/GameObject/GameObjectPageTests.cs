using ACWorldEdit.Foundation;
using Xunit;

namespace ACWorldEdit.Tests.GameObject;

/// <summary>The empty-data descriptors behind the GameObject window: groups, fields, ranges and fallbacks.</summary>
public class GameObjectPageTests
{
    [Fact]
    public void TemplatePageHasTheSharedFieldsAndOnlyTheDialectsOwnColumns()
    {
        var ac = GameObjectPage.TemplatePage(WorldDialect.AzerothCore);
        Assert.Equal(["identity", "scaling", "appearance", "links"], ac.Groups.Select(g => g.Id));
        Assert.Contains(ac.Fields, f => f.Column == "name" && f.Kind == FieldKind.Text);
        Assert.Contains(ac.Fields, f => f.Column == "displayId" && f.Kind == FieldKind.Whole);
        Assert.Contains(ac.Fields, f => f.Column == "size" && f.Kind == FieldKind.Decimal);
        Assert.Contains(ac.Fields, f => f.Column == "faction" && f.Reference == "FactionTemplate");
        Assert.Contains(ac.Fields, f => f.Column == "artkit3");
        Assert.DoesNotContain(ac.Fields, f => f.Column == "artkit4");
        Assert.DoesNotContain(ac.Fields, f => f.Column == "ContentTuningId");

        var tc = GameObjectPage.TemplatePage(WorldDialect.TrinityCore);
        Assert.Contains(tc.Fields, f => f.Column == "ContentTuningId");
        Assert.Contains(tc.Fields, f => f.Column == "RequiredLevel");
        Assert.Contains(tc.Fields, f => f.Column == "artkit4");
        Assert.Contains(tc.Fields, f => f.Column == "flags" && f.Kind == FieldKind.Flags);
    }

    [Fact]
    public void DoorTypePageCarriesLockAutoCloseAndBroadcastReferences()
    {
        var ac = GameObjectPage.TypePage(WorldDialect.AzerothCore, 0);
        var lockField = ac.Fields.Single(f => f.Column == "Data1");
        Assert.Equal("Lock", lockField.Reference);
        Assert.Equal(FieldKind.Whole, lockField.Kind);

        var autoClose = ac.Fields.Single(f => f.Column == "Data2");
        Assert.Equal(65536d, autoClose.Scale);

        var openText = ac.Fields.Single(f => f.Column == "Data4");
        Assert.Equal("BroadcastText", openText.Reference);

        var tc = GameObjectPage.TypePage(WorldDialect.TrinityCore, 0);
        var tcAutoClose = tc.Fields.Single(f => f.Column == "Data2");
        Assert.Equal(FieldKind.Whole, tcAutoClose.Kind);
        Assert.Contains("milliseconds", tcAutoClose.Tip);
        Assert.Contains("3000", tcAutoClose.Tip);
    }

    [Fact]
    public void TrapAndChestTypesUseTheirOwnKindsAndRanges()
    {
        var trap = GameObjectPage.TypePage(WorldDialect.AzerothCore, 6);
        var type = trap.Fields.Single(f => f.Column == "Data4");
        Assert.Equal(FieldKind.Choice, type.Kind);
        Assert.Equal(3, type.Options!.Length);

        var tcChest = GameObjectPage.TypePage(WorldDialect.TrinityCore, 3);
        var maxRestock = tcChest.Fields.Single(f => f.Column == "Data5");
        Assert.Equal(FieldKind.Whole, maxRestock.Kind);
        Assert.Equal(65535ul, maxRestock.Max);
    }

    [Fact]
    public void UnusedAndRawPagesShowTheRemainingColumns()
    {
        var unused = GameObjectPage.UnusedPage(WorldDialect.TrinityCore, 3);
        Assert.NotEmpty(unused.Fields);
        Assert.All(unused.Fields, f => Assert.StartsWith("Data", f.Column));

        var rawAc = GameObjectPage.RawPage(WorldDialect.AzerothCore);
        Assert.Equal(24, rawAc.Fields.Length);
        var rawTc = GameObjectPage.RawPage(WorldDialect.TrinityCore);
        Assert.Equal(35, rawTc.Fields.Length);
    }

    [Fact]
    public void AnUnknownTypeFallsBackToTheRawPage()
    {
        var page = GameObjectPage.TypePage(WorldDialect.AzerothCore, 200);
        Assert.Equal(24, page.Fields.Length);
        Assert.All(page.Fields, f => Assert.Equal(FieldKind.Whole, f.Kind));
    }
}
