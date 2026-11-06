using ACWorldEdit.Foundation;
using Xunit;

namespace ACWorldEdit.Tests.GameObject;

/// <summary>The GameObject template rules: ranges, addon money, quest items and unknown types.</summary>
public class GameObjectRulesTests
{
    static TableSchema Template(WorldDialect dialect) => WorldSchemas.For(dialect).Tables.Find("gameobject_template");
    static TableSchema Addon(WorldDialect dialect) => WorldSchemas.For(dialect).Tables.Find("gameobject_template_addon");
    static TableSchema Items(WorldDialect dialect) => WorldSchemas.For(dialect).Tables.Find("gameobject_questitem");

    static Row TemplateRow(WorldDialect dialect, int type = 0, uint data1 = 0, uint data4 = 0)
    {
        var table = Template(dialect);
        var row = new Row(table.Columns.Select(c => new KeyValuePair<string, string>(c.Name, c.Kind == ColumnKind.Text ? "" : "0")));
        row = row.With("entry", 1u).With("type", (uint)type).With("name", "Test object").With("size", 1f);
        row = row.With("Data1", data1).With("Data4", data4);
        return row;
    }

    static RowSet Rows(WorldDialect dialect, Row template)
    {
        var rows = new RowSet();
        rows.Set(Template(dialect), template);
        return rows;
    }

    static Row AddonRow(WorldDialect dialect, uint min, uint max)
    {
        var table = Addon(dialect);
        var row = new Row(table.Columns.Select(c => new KeyValuePair<string, string>(c.Name, "0")));
        return row.With("entry", 1u).With("mingold", min).With("maxgold", max);
    }

    [Fact]
    public void AValidDoorHasNoErrors()
    {
        var issues = GameObjectRules.Check(WorldDialect.AzerothCore, Rows(WorldDialect.AzerothCore, TemplateRow(WorldDialect.AzerothCore)), 1).ToArray();
        Assert.DoesNotContain(issues, i => i.Error);
    }

    [Fact]
    public void ADataValueOutsideTheTypeRangeIsAnError()
    {
        var rows = Rows(WorldDialect.AzerothCore, TemplateRow(WorldDialect.AzerothCore, type: 6, data4: 9));
        var issue = Assert.Single(GameObjectRules.Check(WorldDialect.AzerothCore, rows, 1).Where(i => i.Error));
        Assert.Contains("Trap type", issue.Message);
        Assert.Contains("one of the listed", issue.Message);
    }

    [Fact]
    public void MinimumMoneyCannotExceedMaximumMoney()
    {
        var dialect = WorldDialect.AzerothCore;
        var rows = Rows(dialect, TemplateRow(dialect));
        rows.Set(Addon(dialect), AddonRow(dialect, 500, 100));
        Assert.Contains(GameObjectRules.Check(dialect, rows, 1), i => i.Error && i.Message.Contains("minimum money"));
    }

    [Fact]
    public void SixQuestItemsAreAllowedAndSevenAreNot()
    {
        var dialect = WorldDialect.AzerothCore;
        var rows = Rows(dialect, TemplateRow(dialect));
        for (uint i = 0; i < 6; i++)
        {
            rows.Set(Items(dialect), Row.Of(("GameObjectEntry", 1u), ("Idx", i), ("ItemId", 100u)));
        }

        Assert.DoesNotContain(GameObjectRules.Check(dialect, rows, 1), i => i.Error);
        rows.Set(Items(dialect), Row.Of(("GameObjectEntry", 1u), ("Idx", 6u), ("ItemId", 100u)));
        Assert.Contains(GameObjectRules.Check(dialect, rows, 1), i => i.Error && i.Message.Contains("six quest items"));
    }

    [Fact]
    public void AnUnknownTypeIsAWarningNotAnError()
    {
        var issue = Assert.Single(GameObjectRules.Check(WorldDialect.AzerothCore, Rows(WorldDialect.AzerothCore, TemplateRow(WorldDialect.AzerothCore, type: 200)), 1));
        Assert.False(issue.Error);
        Assert.Contains("no field catalogue", issue.Message);
    }

    [Fact]
    public void ACppScriptIsAWarning()
    {
        var dialect = WorldDialect.TrinityCore;
        var row = TemplateRow(dialect).With("ScriptName", "my_script");
        var issue = Assert.Single(GameObjectRules.Check(dialect, Rows(dialect, row), 1));
        Assert.False(issue.Error);
        Assert.Contains("my_script", issue.Message);
    }
}
