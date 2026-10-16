using ACWorldEdit;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

namespace ACWorldEdit.GdTests;

/// <summary>
/// The real PickerPopup control, headless, with in-memory search functions (no database).
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class PickerPopupTests
{
    static readonly (uint Id, string Name)[] Items = [(10, "Alpha"), (20, "Beta"), (30, "Gamma")];

    static Task<IReadOnlyList<(uint Id, string Name)>> Search(string term) =>
        Task.FromResult<IReadOnlyList<(uint Id, string Name)>>(Items.Where(i => i.Name.Contains(term, StringComparison.OrdinalIgnoreCase) || i.Id.ToString() == term).ToArray());

    static SceneTree Tree => (SceneTree)Engine.GetMainLoop();

    static async Task Frames(int count)
    {
        for (int i = 0; i < count; i++)
        {
            await Tree.ToSignal(Tree, SceneTree.SignalName.ProcessFrame);
        }
    }

    static ItemList Results(PickerPopup popup) => (ItemList)popup.FindChild("PickerResults", true, false)!;

    static LineEdit SearchBox(PickerPopup popup) => (LineEdit)popup.FindChild("PickerSearch", true, false)!;

    static async Task<(Node Owner, PickerPopup Popup, List<(uint, string)> Picked)> Open(string initial = "", uint? current = null, bool singleClick = false)
    {
        var owner = new Node();
        Tree.Root.AddChild(owner);
        AutoFree(owner);
        List<(uint, string)> picked = [];
        var popup = PickerPopup.Show(owner, "Pick", Search, (id, name) => picked.Add((id, name)), initial, current, singleClick);
        await Frames(3);
        return (owner, popup, picked);
    }

    [TestCase]
    public async Task ListsEveryResultForAnEmptyTerm()
    {
        var (_, popup, _) = await Open();
        AssertThat(Results(popup).ItemCount).IsEqual(3);
    }

    [TestCase]
    public async Task InitialTermFiltersTheResults()
    {
        var (_, popup, _) = await Open("gam");
        AssertThat(Results(popup).ItemCount).IsEqual(1);
        AssertString(SearchBox(popup).Text).IsEqual("gam");
    }

    [TestCase]
    public async Task TypingSearchesAgain()
    {
        var (_, popup, _) = await Open();
        SearchBox(popup).Text = "beta";
        SearchBox(popup).EmitSignal(LineEdit.SignalName.TextChanged, "beta");
        await Frames(3);
        AssertThat(Results(popup).ItemCount).IsEqual(1);
    }

    [TestCase]
    public async Task ActivatingAResultPicksItAndReportsIdAndName()
    {
        var (_, popup, picked) = await Open();
        Results(popup).EmitSignal(ItemList.SignalName.ItemActivated, 1);
        await Frames(2);
        AssertThat(picked.Count).IsEqual(1);
        AssertThat(picked[0]).IsEqual((20u, "Beta"));
    }

    [TestCase]
    public async Task EnterPicksTheFirstResult()
    {
        var (_, popup, picked) = await Open("a");
        SearchBox(popup).EmitSignal(LineEdit.SignalName.TextSubmitted, "a");
        await Frames(2);
        AssertThat(picked.Count).IsEqual(1);
        AssertThat(picked[0].Item1).IsEqual(10u);
    }

    [TestCase]
    public async Task NoMatchesLeavesTheListEmptyAndPicksNothing()
    {
        var (_, popup, picked) = await Open("zzz");
        AssertThat(Results(popup).ItemCount).IsEqual(0);
        SearchBox(popup).EmitSignal(LineEdit.SignalName.TextSubmitted, "zzz");
        await Frames(2);
        AssertThat(picked.Count).IsEqual(0);
    }
}
