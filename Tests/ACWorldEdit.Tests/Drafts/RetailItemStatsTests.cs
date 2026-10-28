using ACWorldEdit.Foundation;
using Xunit;

public class RetailItemStatsTests
{
    /// <summary>Tables with one row per item level, filled from a few dictionaries.</summary>
    sealed class FakeTables : IRetailItemTables
    {
        public float[]? ArmorQuality(uint itemLevel) => [0, 0, 1f, 2f, 3f, 4f, 5f, 6f];
        public ArmorFactors? ArmorTotal(uint itemLevel) => new(10f, 20f, 30f, 40f);
        public ArmorFactors? ArmorLocation(uint inventoryType) => inventoryType == 5 ? new(1f, 1f, 1f, 0.5f) : null;
        public float[]? ShieldArmor(uint itemLevel) => [0, 0, 100f, 200f, 300f, 400f, 500f, 600f];
        public float[]? Dps(ItemDpsTable table, uint itemLevel) => table == ItemDpsTable.OneHand ? [0, 0, 5f, 10f, 20f, 30f, 40f, 50f] : null;
        public RandomPropertyPoints? RandomPoints(uint itemLevel) => new([1000f, 500f, 300f, 800f, 100f], [900f, 450f, 270f, 700f, 90f], [800f, 400f, 240f, 600f, 80f]);
        public (int Type, IReadOnlyList<(float X, float Y)> Points)? Curve(uint curveId) => curveId == 1 ? (0, new[] { (1f, 1f), (101f, 51f) }) : null;
        public IReadOnlyList<ItemSquishEra> SquishEras { get; } = [new(1, 100000, 0, 0), new(2, 110000, 1, 0), new(3, 130000, 1, 0)];
    }

    static readonly FakeTables Tables = new();

    [Theory]
    [InlineData(0, 1, 1f)]      // below the first point holds the first value
    [InlineData(0, 51, 26f)]    // linear midpoint
    [InlineData(0, 500, 51f)]   // beyond the last point holds the last value
    [InlineData(3, 51, 26f)]    // cosine passes through the midpoint
    [InlineData(4, 51, 1f)]     // constant mode: between points reads zero
    [InlineData(5, 51, 1f)]     // step holds the previous point
    public void CurveValueInterpolatesLikeTheServer(int type, float x, float expected)
    {
        float actual = RetailItemStats.CurveValue(type, [(1f, 1f), (101f, 51f)], x);
        Assert.Equal(type == 4 && x > 1 && x < 101 ? 0f : expected, actual, 0.001f);
    }

    [Fact]
    public void ItemLevelIsSquishedOnlyByErasAfterTheItemsOwnUpToTheClientPatch()
    {
        // Era 1 has no curve; era 2 (patch 11.0) halves; era 3 (13.0) is beyond a 12.1 client.
        Assert.Equal(26u, RetailItemStats.EffectiveItemLevel(51, 0, Tables, 120105));
        // An item already squished by era 2 skips it and every earlier era.
        Assert.Equal(51u, RetailItemStats.EffectiveItemLevel(51, 2, Tables, 120105));
        Assert.Equal(51u, RetailItemStats.EffectiveItemLevel(51, 0, Tables, 90000));
    }

    [Fact]
    public void ArmorMultipliesQualityTotalAndSlotAndRoundsHalfUp()
    {
        // Epic (4) plate chest: 3 * 40 * 0.5 = 60. A robe uses the chest slot.
        Assert.Equal(60u, RetailItemStats.Armor(4, 4, 5, 4, 100, Tables));
        Assert.Equal(60u, RetailItemStats.Armor(4, 4, 20, 4, 100, Tables));
        // Slots the table does not list, and non-armor subclasses, have none.
        Assert.Equal(0u, RetailItemStats.Armor(4, 4, 1, 4, 100, Tables));
        Assert.Equal(0u, RetailItemStats.Armor(4, 0, 5, 4, 100, Tables));
        // Shields read the shield table; heirlooms count as rare; artifacts and up have no budget.
        Assert.Equal(300u, RetailItemStats.Armor(4, 6, 14, 4, 100, Tables));
        Assert.Equal(RetailItemStats.Armor(4, 4, 5, 3, 100, Tables), RetailItemStats.Armor(4, 4, 5, 7, 100, Tables));
        Assert.Equal(0u, RetailItemStats.Armor(4, 4, 5, 8, 100, Tables));
    }

    [Fact]
    public void WeaponDpsPicksTheTableOfTheSlotAndDamageSpreadsByVariance()
    {
        Assert.Equal(20f, RetailItemStats.Dps(2, 7, 13, 4, 100, 0, Tables));
        // Caster weapons use another table, which this fake does not have.
        Assert.Equal(0f, RetailItemStats.Dps(2, 7, 13, 4, 100, 0x200, Tables));
        Assert.Equal(0f, RetailItemStats.Dps(4, 7, 13, 4, 100, 0, Tables));

        // 20 dps at 2.0 s is 40 average; variance 0.5 spreads 30..50.
        var (min, max) = RetailItemStats.Damage(20f, 2000, 0.5f);
        Assert.Equal(30f, min, 0.001f);
        Assert.Equal(50f, max);
        Assert.Equal((0f, 0f), RetailItemStats.Damage(0f, 2000, 0.5f));
    }

    [Fact]
    public void StatValueIsAShareOfTheSlotBudgetTruncated()
    {
        // Epic chest budget is 1000; 25.55% of it is 255.5, truncated to 255. A weapon uses index 3 (800).
        Assert.Equal(255, RetailItemStats.StatValue(2555, 100, 4, 5, 4, Tables));
        Assert.Equal(800, RetailItemStats.StatValue(10000, 100, 4, 13, 7, Tables));
        // Common items and slots without a budget have none.
        Assert.Equal(0, RetailItemStats.StatValue(10000, 100, 1, 5, 4, Tables));
        Assert.Equal(0, RetailItemStats.StatValue(10000, 100, 4, 18, 0, Tables));
    }

    [Fact]
    public void DurabilityComesFromQualitySlotAndALowLevelPenalty()
    {
        uint full = RetailItemStats.MaxDurability(4, 4, 5, 4, 100);
        Assert.True(full > 0 && full % 5 == 0);
        Assert.True(RetailItemStats.MaxDurability(4, 4, 5, 4, 10) < full);
        Assert.Equal(0u, RetailItemStats.MaxDurability(7, 0, 0, 1, 100));
    }

    [Fact]
    public void RetailTooltipsWordStatsWithoutEquipPrefix()
    {
        var info = new ItemInfo(1, "Test Plate", 4, 0, 0);
        var item = new ItemDetails(info, 4, 4, 5, 1, 0, 100, 60, 0, -1, -1, 0, [(7, 50), (32, 20), (40, 10)], [], 0, 300, 0, [], 0, [], "", 0);
        var lines = ItemTooltipBuilder.Build(item, ItemVocabulary.Retail).Lines.Select(l => l.Left).ToList();
        Assert.Contains("+50 Stamina", lines);
        Assert.Contains("+20 Critical Strike", lines);
        Assert.Contains("+10 Versatility", lines);
        Assert.DoesNotContain(lines, l => l.StartsWith("Equip:", StringComparison.Ordinal));
    }
}
