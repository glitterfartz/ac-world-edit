using ACWorldEdit.Foundation;
using Xunit;

namespace ACWorldEdit.Tests.GameObject;

/// <summary>The row arithmetic behind the type selector and the optional addon row.</summary>
public class GameObjectEditsTests
{
    static Row Template(WorldDialect dialect, int type)
    {
        var table = WorldSchemas.For(dialect).Tables.Find("gameobject_template");
        var row = new Row(table.Columns.Select(c => new KeyValuePair<string, string>(c.Name, "0")));
        return row.With("entry", 1u).With("type", (uint)type).With("name", "Test object").With("size", 1f);
    }

    [Fact]
    public void ChangingTheTypeResetsEveryDataColumn()
    {
        var dialect = WorldDialect.AzerothCore;
        var row = Template(dialect, 0).With("Data1", 43).With("Data2", 196608).With("Data3", 1);
        var edit = GameObjectEdits.ChangeType(dialect, row, 3);
        Assert.Equal("gameobject_template", edit.Table);
        Assert.Equal(3u, edit.After!.U("type"));
        for (int i = 0; i <= 23; i++)
        {
            Assert.Equal("0", edit.After.T("Data" + i));
        }
    }

    [Fact]
    public void ChangingToChairUsesTheCuratedDefaults()
    {
        var dialect = WorldDialect.AzerothCore;
        var edit = GameObjectEdits.ChangeType(dialect, Template(dialect, 0), 7);
        Assert.Equal(1u, edit.After!.U("Data0")); // slots
        Assert.Equal(1u, edit.After.U("Data1"));  // seat height
        Assert.Equal(0u, edit.After.U("Data2"));  // onlyCreatorUse
    }

    [Fact]
    public void ANewAddonRowHasEveryColumnAndTheEntry()
    {
        var dialect = WorldDialect.TrinityCore;
        var table = WorldSchemas.For(dialect).Tables.Find("gameobject_template_addon");
        var row = GameObjectEdits.NewAddon(dialect, 42);
        Assert.Equal(table.Columns.Length, row.Values.Count);
        Assert.All(table.Columns, c => Assert.True(row.Values.ContainsKey(c.Name)));
        Assert.Equal(42u, row.U("entry"));
    }

    [Fact]
    public void AQuestItemRowUsesTheSchemaKeys()
    {
        var row = GameObjectEdits.NewQuestItem(WorldDialect.AzerothCore, 7, 2, 100);
        Assert.Equal(7u, row.U("GameObjectEntry"));
        Assert.Equal(2u, row.U("Idx"));
        Assert.Equal(100u, row.U("ItemId"));
    }

    [Fact]
    public void TheTypeEditExportsAnUpdateWithoutVerifiedBuild()
    {
        var dialect = WorldDialect.AzerothCore;
        var row = Template(dialect, 0).With("Data1", 43);
        var edit = GameObjectEdits.ChangeType(dialect, row, 3);
        var scripts = SqlExport.Build("src", [], [edit], DateTime.UnixEpoch, AcSchema.Instance);
        Assert.Contains("UPDATE `gameobject_template`", scripts.Apply);
        Assert.Contains("`Data1`", scripts.Apply);
        Assert.DoesNotContain("VerifiedBuild", scripts.Apply);
        Assert.Contains("`type`", scripts.Apply);
    }
}
