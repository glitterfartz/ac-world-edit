using ACWorldEdit.Foundation;
using Xunit;

namespace ACWorldEdit.Tests.GameObject;

/// <summary>The GameObject quest link and quest item row arithmetic.</summary>
public class GameObjectQuestPageTests
{
    static RowSet Applied(params RowEdit[] edits)
    {
        var rows = new RowSet();
        foreach (var edit in edits)
        {
            rows.Apply(edit, AcTables.Instance);
        }

        return rows;
    }

    [Fact]
    public void LinkingAQuestAddsBothRolesAndUnlinkingRemovesThem()
    {
        var page = GameObjectQuestPage.AzerothCore;
        var rows = Applied(page.SetRoles(new RowSet(), 1, 100, gives: true, takes: true));
        Assert.Single(page.Links.Starts.Select(rows, 1));
        Assert.Single(page.Links.Ends.Select(rows, 1));

        var unlink = page.SetRoles(rows, 1, 100, gives: false, takes: false);
        Assert.Equal(2, unlink.Length);
        foreach (var edit in unlink)
        {
            rows.Apply(edit, AcTables.Instance);
        }

        Assert.Empty(page.Links.Starts.Select(rows, 1));
        Assert.Empty(page.Links.Ends.Select(rows, 1));
    }

    [Fact]
    public void AddingAQuestItemKeepsTheIndexAndItem()
    {
        var page = GameObjectQuestPage.AzerothCore;
        var rows = new RowSet();
        rows.Apply(page.Items.Add(rows, 1, ("Idx", 3u), ("ItemId", 100u)), AcTables.Instance);
        var item = Assert.Single(page.Items.Select(rows, 1));
        Assert.Equal(3u, item.U("Idx"));
        Assert.Equal(100u, item.U("ItemId"));

        rows.Apply(page.Items.Remove(item), AcTables.Instance);
        Assert.Empty(page.Items.Select(rows, 1));
    }

    [Fact]
    public void TrinityCoreUsesItsOwnQuestTables()
    {
        var page = GameObjectQuestPage.TrinityCore;
        Assert.Same(TcTables.GameObjectQuestStarter, page.Links.Starts.Table);
        Assert.Same(TcTables.GameObjectQuestItem, page.Items.Table);
    }
}
