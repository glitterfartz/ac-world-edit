using ACWorldEdit.Foundation;
using Xunit;

namespace ACWorldEdit.Tests.Drafts;

/// <summary>The creature template page descriptors stay consistent with the table catalogs they write to.</summary>
public class CreaturePageTests
{
    public static TheoryData<WorldDialect> Dialects => new() { WorldDialect.AzerothCore, WorldDialect.TrinityCore };

    [Theory]
    [MemberData(nameof(Dialects))]
    public void EveryFieldIsABoundColumnInAGroupOfThePage(WorldDialect dialect)
    {
        var schema = WorldSchemas.For(dialect);
        var page = schema.TemplatePage;
        var groups = page.Groups.Select(g => g.Id).ToHashSet();
        foreach (var field in page.Fields)
        {
            Assert.True(field.Table.Has(field.Column), $"{field.Table.Name}.{field.Column} is not a column");
            Assert.Contains(field.Group, groups);
            Assert.Same(schema.Tables.Find(field.Table.Name), field.Table);
            Assert.True(field.Kind != FieldKind.Choice || field.Options is { Length: > 0 }, $"{field.Column} has no options");
            Assert.True(field.Row == RowBinding.Template || page.Difficulty?.Table == field.Table, $"{field.Column} binds to a difficulty row the page lacks");
        }

        Assert.Equal(page.Fields.Length, page.Fields.Select(f => (f.Table.Name, f.Column)).Distinct().Count());
    }

    [Fact]
    public void TheAzerothCorePageCoversEveryEditableTemplateColumn()
    {
        var covered = AcCreaturePage.Page.Fields.Select(f => (f.Table.Name, f.Column)).ToHashSet();
        foreach (var column in AcTables.CreatureDetails.Columns.Where(c => c.Name != "entry"))
        {
            Assert.Contains(("creature_details", column.Name), covered);
        }

        Assert.Contains(("creature_template", "npcflag"), covered);
        Assert.Contains(("creature_template", "subname"), covered);
    }

    [Fact]
    public void TheTrinityCorePageCoversEveryTemplateAndDifficultyColumn()
    {
        var covered = TcCreaturePage.Page.Fields.Select(f => (f.Table.Name, f.Column)).ToHashSet();
        foreach (var column in TcTables.CreatureTemplate.Columns.Where(c => c.Name != "entry"))
        {
            Assert.Contains(("creature_template", column.Name), covered);
        }

        foreach (var column in TcTables.CreatureDifficulty.Columns.Where(c => !TcTables.CreatureDifficulty.Key.Contains(c.Name)))
        {
            Assert.Contains(("creature_template_difficulty", column.Name), covered);
        }
    }

    [Fact]
    public void BothDialectsShareTheGroupOrder()
    {
        Assert.Equal(AcCreaturePage.Page.Groups.Select(g => g.Id), TcCreaturePage.Page.Groups.Select(g => g.Id));
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public void EveryTemplateAndStatsFieldExplainsItself(WorldDialect dialect)
    {
        var schema = WorldSchemas.For(dialect);
        foreach (var field in schema.TemplatePage.Fields.Concat(schema.StatsPage.Fields))
        {
            string where = $"{dialect} {field.Table.Name}.{field.Column}";
            Assert.False(string.IsNullOrWhiteSpace(field.Tip), $"{where} has no tooltip");
            Assert.True(field.Tip.Length >= 25, $"{where} tooltip is too short: {field.Tip}");
            Assert.Contains(' ', field.Tip);
            Assert.DoesNotContain(field.Tip, new[] { "On or off.", "One of the listed values.", "Not used.", "Raw value." });
        }
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public void EveryInteractionFieldExplainsItself(WorldDialect dialect)
    {
        var schema = WorldSchemas.For(dialect);
        var fields = schema.VendorPage.Fields
            .Concat(schema.TrainerPage.Spells.Fields)
            .Concat(schema.GossipPage.Options.Fields);
        foreach (var field in fields)
        {
            string where = $"{dialect} {field.Table.Name}.{field.Column}";
            Assert.False(string.IsNullOrWhiteSpace(field.Tip), $"{where} has no tooltip");
            Assert.True(field.Tip.Length >= 20, $"{where} tooltip is too short: {field.Tip}");
            Assert.Contains(' ', field.Tip);
        }
    }
}
