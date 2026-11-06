using ACWorldEdit;
using ACWorldEdit.Foundation;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

namespace ACWorldEdit.GdTests;

/// <summary>The Spawns table both template windows share: one page of Fly to buttons plus a pager.</summary>
[TestSuite]
[RequireGodotRuntime]
public class SpawnsTableTests
{
    static VBoxContainer Render(IReadOnlyList<SpawnListRow> spawns, int total, int page, Action<int> setPage, Func<SpawnListRow, Task<string>> fly, Action<string> report)
    {
        var parent = new VBoxContainer();
        AutoFree(parent);
        SpawnsTable.Render(parent, spawns, total, page, setPage, fly, report);
        return parent;
    }

    static List<SpawnListRow> Rows(int count) =>
        Enumerable.Range(1, count).Select(i => new SpawnListRow((ulong)i, 0, i, i, i)).ToList();

    [TestCase]
    public void FirstPageShowsFlyButtonsAndPager()
    {
        var rows = Rows(SpawnPages.PageSize + 3);
        int page = 0;
        var reported = new List<string>();
        var flown = new List<ulong>();
        var parent = Render(rows, rows.Count, page, next => page = next,
            row => { flown.Add(row.Guid); return Task.FromResult("arrived"); }, message => reported.Add(message));

        var grid = (GridContainer)parent.FindChild("SpawnTable", true, false)!;
        // Header row plus one row of four cells per spawn on the page.
        AssertThat(grid.GetChildCount()).IsEqual(4 * (SpawnPages.PageSize + 1));
        AssertThat(((Label)parent.FindChild("SpawnPage", true, false)!).Text).IsEqual($"Page 1 of 2 · {rows.Count} spawns");
        AssertThat(((Button)parent.FindChild("SpawnPrev", true, false)!).Disabled).IsTrue();
        AssertThat(((Button)parent.FindChild("SpawnNext", true, false)!).Disabled).IsFalse();

        var fly = (Button)parent.FindChild("FlyTo_1", true, false)!;
        fly.EmitSignal(BaseButton.SignalName.Pressed);
        AssertThat(flown.Count).IsEqual(1);
        AssertThat(flown[0]).IsEqual(1ul);
        AssertThat(reported.Count).IsEqual(2);
        AssertThat(reported[0]).IsEqual("Locating spawn #1…");
        AssertThat(reported[1]).IsEqual("arrived");
    }

    [TestCase]
    public void PagerMovesBetweenPages()
    {
        var rows = Rows(SpawnPages.PageSize + 3);
        int page = 0;
        var parent = Render(rows, rows.Count, page, next => page = next,
            _ => Task.FromResult(""), _ => { });
        ((Button)parent.FindChild("SpawnNext", true, false)!).EmitSignal(BaseButton.SignalName.Pressed);
        AssertThat(page).IsEqual(1);

        parent.QueueFree();
        parent = Render(rows, rows.Count, page, next => page = next,
            _ => Task.FromResult(""), _ => { });
        var grid = (GridContainer)parent.FindChild("SpawnTable", true, false)!;
        AssertThat(grid.GetChildCount()).IsEqual(4 * (3 + 1));
        AssertThat(((Label)parent.FindChild("SpawnPage", true, false)!).Text).IsEqual($"Page 2 of 2 · {rows.Count} spawns");
        AssertThat(parent.FindChild("FlyTo_1", true, false)).IsNull();
        AssertThat(parent.FindChild("FlyTo_28", true, false)).IsNotNull();
    }

    [TestCase]
    public void EmptyTableNamesNoSpawns()
    {
        var parent = Render([], 0, 0, _ => { }, _ => Task.FromResult(""), _ => { });
        AssertThat(parent.FindChild("SpawnTable", true, false)).IsNull();
        AssertThat(parent.FindChild("SpawnPager", true, false)).IsNull();
    }
}
