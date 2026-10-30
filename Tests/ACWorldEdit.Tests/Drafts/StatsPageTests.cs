using ACWorldEdit.Foundation;
using Xunit;

namespace ACWorldEdit.Tests.Drafts;

/// <summary>The stats page of each dialect is data on the schema, like the template page.</summary>
public class StatsPageTests
{
    public static IEnumerable<object[]> Dialects => [[WorldDialect.AzerothCore], [WorldDialect.TrinityCore]];

    [Theory]
    [MemberData(nameof(Dialects))]
    public void EveryFieldIsAColumnOfACatalogTableAndEveryGroupHasFields(WorldDialect dialect)
    {
        var schema = WorldSchemas.For(dialect);
        var page = schema.StatsPage;
        Assert.NotEmpty(page.Fields);
        Assert.All(page.Fields, f =>
        {
            Assert.Same(schema.Tables.Find(f.Table.Name), f.Table);
            Assert.True(f.Table.Has(f.Column), f.Column);
        });
        Assert.All(page.Groups, g => Assert.NotEmpty(page.In(g.Id)));
        Assert.Equal(["levels", "multipliers"], page.Groups.Select(g => g.Id));
        // The four stat multipliers exist in both dialects.
        foreach (string column in new[] { "HealthModifier", "ManaModifier", "ArmorModifier", "DamageModifier" })
        {
            Assert.Contains(page.Fields, f => f.Column == column);
        }
    }

    static RowSet Rows(uint min = 20, uint max = 22, uint exp = 2, float health = 1.5f)
    {
        var rows = new RowSet();
        rows.Set(AcTables.CreatureStats, Row.Of(("entry", 7u), ("minlevel", min), ("maxlevel", max), ("exp", exp), ("unit_class", 1u),
            ("HealthModifier", health), ("ManaModifier", 1f), ("ArmorModifier", 1f), ("DamageModifier", 1f)));
        return rows;
    }

    [Fact]
    public void AValidAzerothCoreStatsRowHasNoIssues() => Assert.Empty(AcStatsPage.Check(Rows(), 7));

    [Theory]
    [InlineData(0u, 5u, 0u, 1f, "between 1 and")]
    [InlineData(5u, 256u, 0u, 1f, "between 1 and")]
    [InlineData(30u, 20u, 0u, 1f, "cannot exceed")]
    [InlineData(5u, 6u, 3u, 1f, "expansion tier")]
    [InlineData(5u, 6u, 0u, -1f, "multiplier")]
    public void TheStatsRulesRefuseWhatTheEditorUsedToRefuse(uint min, uint max, uint exp, float health, string message)
    {
        var issues = AcStatsPage.Check(Rows(min, max, exp, health), 7).ToList();
        Assert.Contains(issues, i => i.Error && i.Message.Contains(message));
    }

    [Fact]
    public void ACreatureWithoutAStatsRowHasNothingToCheck() => Assert.Empty(AcStatsPage.Check(new RowSet(), 7));
}
