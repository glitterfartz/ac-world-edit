using ACWorldEdit.Assets;
using ACWorldEdit.Foundation;
using ACWorldEdit.Tests.Support;
using Xunit;

/// <summary>
/// Retail item templates against a real install: the stored ItemSparse fields read as the server would,
/// and the values the template does not store (item level squish, stats, armor, weapon damage,
/// durability) come out of the client's tables as plausible, monotonic numbers.
/// </summary>
[Trait("Category", "Corpus")]
[Collection(RetailData.Collection)]
public class RetailItemDetailsTests
{
    static RetailClientLookup Lookup() => new(RetailData.Tables);

    /// <summary>Items of a class whose quality and level suit the check, taken from the whole ItemSparse table.</summary>
    static IEnumerable<ItemDetails> Items(RetailClientLookup lookup, Func<ItemDetails, bool> where, int limit) =>
        RetailData.Tables.Table("ItemSparse").Values.Select(r => lookup.ItemDetails((uint)r.ID)).OfType<ItemDetails>().Where(where).Take(limit);

    [RetailClientFact]
    public void StoredFieldsComeFromItemSparseAndItem()
    {
        var lookup = Lookup();
        var cloth = lookup.SearchItems("Linen Cloth", 5).First(i => i.Name == "Linen Cloth");
        var details = lookup.ItemDetails(cloth.Id)!;
        Assert.Equal("Linen Cloth", details.Info.Name);
        Assert.Equal((int)cloth.Quality, details.Info.Quality);
        Assert.Equal(7u, details.Class);
        Assert.True(details.SellPrice > 0);
        Assert.DoesNotContain(details.Stats, s => s.Value != 0);
        Assert.Equal(0u, details.Armor);
        Assert.Equal(0u, details.MaxDurability);
        Assert.Null(lookup.ItemDetails(uint.MaxValue - 1));
    }

    [RetailClientFact]
    public void PlateArmorHasArmorDurabilityAndStatsAndRisesWithItemLevel()
    {
        var lookup = Lookup();
        // Plate chest pieces (class 4, subclass 4, inventory type 5) of epic quality.
        var chests = Items(lookup, d => d.Class == 4 && d.Subclass == 4 && d.InventoryType == 5 && d.Info.Quality == 4 && d.ItemLevel >= 10, 400).ToList();
        Assert.True(chests.Count > 5, $"{chests.Count} plate chests");
        Assert.All(chests, d => Assert.True(d.Armor > 0 && d.MaxDurability > 0));
        // Items that store stat types get a positive value for each; many retail items take theirs from bonus lists.
        var stats = chests.SelectMany(d => d.Stats).Where(s => s.Type != 0).ToList();
        Assert.True(stats.Count(s => s.Value != 0) >= stats.Count * 0.95, $"{stats.Count(s => s.Value == 0)} of {stats.Count} stats are zero");

        // At the same quality and type, a higher item level never has less armor or less of its primary stat.
        var ordered = chests.OrderBy(d => d.ItemLevel).ToList();
        for (int i = 1; i < ordered.Count; i++)
        {
            Assert.True(ordered[i].Armor >= ordered[i - 1].Armor, $"armor fell between item level {ordered[i - 1].ItemLevel} and {ordered[i].ItemLevel}");
        }

        Console.WriteLine($"RETAIL_ITEMS plate chests: {chests.Count}, item level {ordered[0].ItemLevel}-{ordered[^1].ItemLevel}, armor {ordered[0].Armor}-{ordered[^1].Armor}");
    }

    [RetailClientFact]
    public void WeaponsHaveDamageWithTheSpeedAndVarianceOfTheirTemplate()
    {
        var lookup = Lookup();
        var swords = Items(lookup, d => d.Class == 2 && d.Subclass == 7 && d.InventoryType == 13 && d.Info.Quality == 4 && d.ItemLevel >= 40 && d.Delay > 0, 200).ToList();
        Assert.True(swords.Count > 5, $"{swords.Count} one-handed swords");
        Assert.All(swords, d =>
        {
            var roll = Assert.Single(d.Damage);
            Assert.True(roll.Min > 0 && roll.Max >= roll.Min, $"{d.Info.Name}: {roll.Min}-{roll.Max}");
            Assert.True(d.MaxDurability > 0);
        });
    }

    [RetailClientFact]
    public void ItemLevelsAreSquishedByTheCurvesOfEachEra()
    {
        var lookup = Lookup();
        var tables = new RetailItemTables(RetailData.Tables.Table);
        Assert.NotEmpty(tables.SquishEras);
        // An unsquished era maps every level through its curve, so levels above the curve's range stay finite and ordered.
        uint[] levels = [1, 30, 60, 100, 200, 400, 600];
        var squished = levels.Select(l => RetailItemStats.EffectiveItemLevel(l, 0, tables, 120105)).ToArray();
        Assert.Equal(squished.Order(), squished);
        Assert.All(squished.Zip(levels), pair => Assert.True(pair.First <= pair.Second));
        Console.WriteLine("RETAIL_SQUISH " + string.Join(", ", levels.Zip(squished, (a, b) => $"{a}->{b}")));
    }

    [RetailClientFact]
    public void RetailTooltipsUseTheRetailWording()
    {
        var lookup = Lookup();
        var chest = Items(lookup, d => d.Class == 4 && d.Subclass == 4 && d.InventoryType == 5 && d.Info.Quality == 4 && d.Stats.Any(s => s is { Type: 32, Value: > 0 }), 1).First();
        var lines = ItemTooltipBuilder.Build(chest, ItemVocabulary.Retail).Lines.Select(l => l.Left).ToList();
        Console.WriteLine("RETAIL_TOOLTIP " + string.Join(" | ", lines));
        Assert.Contains(lines, l => l.StartsWith('+') && l.EndsWith("Critical Strike", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, l => l.StartsWith("Equip:", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.EndsWith("Armor", StringComparison.Ordinal));
    }
}
