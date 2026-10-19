using ACWorldEdit.Foundation;
using Xunit;

/// <summary>Tooltip text of items: line order, stat wording and the class/race restriction masks.</summary>

public class ItemTooltipBuilderTests
{
    static ItemDetails Item(Func<ItemDetails, ItemDetails>? change = null)
    {
        var item = new ItemDetails(new ItemInfo(1, "Test Blade", 3, 0, 0), 2, 7, 13, 2, 0, 20, 15, 0, -1, -1, 0, [], [(10f, 20f, 0)], 2600, 0, 0, [0, 0, 0, 0, 0, 0], 60, [0, 0, 0], "", 12345);
        return change?.Invoke(item) ?? item;
    }

    static string[] Texts(ItemDetails item) => ItemTooltipBuilder.Build(item).Lines.Select(l => l.Right.Length > 0 ? $"{l.Left}|{l.Right}" : l.Left).ToArray();

    [Fact]
    public void WeaponShowsSlotKindDamageSpeedAndDps()
    {
        var lines = Texts(Item());
        Assert.Equal("Test Blade", lines[0]);
        Assert.Contains("One-Hand|Sword", lines);
        Assert.Contains("10 - 20 Damage|Speed 2.60", lines);
        Assert.Contains("(5.8 damage per second)", lines);
        Assert.Contains("Binds when equipped", lines);
        Assert.Contains("Sell Price: 1g 23s 45c", lines);
    }

    [Fact]
    public void TitleCarriesQuality()
    {
        var title = ItemTooltipBuilder.Build(Item()).Lines[0];
        Assert.Equal(TooltipTone.Title, title.Tone);
        Assert.Equal(3, title.Quality);
    }

    [Fact]
    public void PrimaryStatsArePlainAndRatingsAreGreenEquipLines()
    {
        var tooltip = ItemTooltipBuilder.Build(Item(i => i with { Stats = [(7, 12), (32, 9)] }));
        Assert.Contains(tooltip.Lines, l => l.Left == "+12 Stamina" && l.Tone == TooltipTone.Normal);
        Assert.Contains(tooltip.Lines, l => l.Left == "Equip: Improves critical strike rating by 9." && l.Tone == TooltipTone.Good);
    }

    [Fact]
    public void ArmorShowsMaterialAndFullClassAndRaceMasksAreHidden()
    {
        var lines = Texts(Item(i => i with { Class = 4, Subclass = 4, InventoryType = 1, Armor = 500, Damage = [], AllowableClass = 1535, AllowableRace = 1650 }));
        Assert.Contains("Head|Plate", lines);
        Assert.Contains("500 Armor", lines);
        Assert.DoesNotContain(lines, l => l.StartsWith("Classes") || l.StartsWith("Races"));
    }

    [Fact]
    public void RestrictedClassesAreListed()
    {
        var lines = Texts(Item(i => i with { AllowableClass = 0x3 }));
        Assert.Contains("Classes: Warrior, Paladin", lines);
    }

    [Theory]
    [InlineData(0, "0c")]
    [InlineData(5, "5c")]
    [InlineData(120, "1s 20c")]
    [InlineData(10000, "1g")]
    [InlineData(12345, "1g 23s 45c")]
    public void CoinsOmitZeroParts(long copper, string expected) => Assert.Equal(expected, ItemTooltipBuilder.Coins(copper));
}
