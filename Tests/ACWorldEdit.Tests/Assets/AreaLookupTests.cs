using ACWorldEdit.Assets;
using ACWorldEdit.Tests.Support;
using Xunit;

/// <summary>
/// AreaLookup over a synthetic AreaTable (id, map, parent, name at field 11), so no client data is
/// needed. Field offsets are the ones the lookup documents, not independently client-verified here.
/// </summary>
public class AreaLookupTests
{
    static AreaLookup Lookup()
    {
        var dbc = new DbcBuilder(12);
        dbc.Row(12, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, dbc.Str("Elwynn Forest"))
            .Row(87, 0, 12, 0, 0, 0, 0, 0, 0, 0, 0, dbc.Str("Goldshire"))
            .Row(88, 0, 12, 0, 0, 0, 0, 0, 0, 0, 0, dbc.Str("Northshire Valley"))
            .Row(14, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, dbc.Str("Durotar"))
            .Row(99, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0)
            .Row(100, 0, 4242, 0, 0, 0, 0, 0, 0, 0, 0, dbc.Str("Orphan"));
        return AreaLookup.FromTable(new DbcTable(dbc.Build()));
    }

    [Fact]
    public void ContainsAndMapOfUseTheTable()
    {
        var areas = Lookup();
        Assert.True(areas.Contains(12));
        Assert.False(areas.Contains(1));
        Assert.Equal(1u, areas.MapOf(14));
        Assert.Equal(0u, areas.MapOf(12));
        Assert.Null(areas.MapOf(1));
    }

    [Fact]
    public void DescribeResolvesZoneAndParent()
    {
        var areas = Lookup();
        Assert.Equal(("Elwynn Forest", (string?)null), areas.Describe(12));
        Assert.Equal(("Goldshire", (string?)"Elwynn Forest"), areas.Describe(87));
    }

    [Fact]
    public void DescribeFallsBackToNumericPlaceholders()
    {
        var areas = Lookup();
        Assert.Equal(("Area 1", (string?)null), areas.Describe(1));
        Assert.Equal(("Area 99", (string?)null), areas.Describe(99));
        // A parent ID that is not in the table is dropped rather than shown as a number.
        Assert.Equal(("Orphan", (string?)null), areas.Describe(100));
    }

    [Fact]
    public void SearchMatchesNameCaseInsensitivelyAndById()
    {
        var areas = Lookup();
        Assert.Equal([12u], areas.Search("elwynn", null).Select(m => m.Id));
        Assert.Equal([88u], areas.Search("  NORTHSHIRE ", null).Select(m => m.Id));
        Assert.Equal(14u, areas.Search("14", null)[0].Id);
        Assert.Empty(areas.Search("nowhere", null));
    }

    [Fact]
    public void SearchOrdersExactIdThenPreferredMapThenZonesBeforeSubzones()
    {
        var areas = Lookup();
        var all = areas.Search("", preferredMap: 1);
        Assert.Equal(1u, all[0].Map);
        // Within map 0, zones (no parent) come before subzones; names break ties.
        var map0 = all.Where(m => m.Map == 0).Select(m => m.Id).ToArray();
        Assert.True(Array.IndexOf(map0, 12u) < Array.IndexOf(map0, 87u));
        Assert.True(Array.IndexOf(map0, 87u) < Array.IndexOf(map0, 88u));
        Assert.Equal(87u, areas.Search("87", null)[0].Id);
    }

    [Fact]
    public void SearchIsBounded()
    {
        Assert.Equal(2, Lookup().Search("", null, limit: 2).Count);
    }
}
