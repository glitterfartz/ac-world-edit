using ACWorldEdit;
using ACWorldEdit.Foundation;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

namespace ACWorldEdit.GdTests;

/// <summary>The rich reference window lists icons/tooltips and returns the chosen record, headless.</summary>
[TestSuite]
[RequireGodotRuntime]
public class RecordPickerTests
{
    static readonly PickerRecord[] Records =
    [
        new(10, "Alpha", "item · 10", "", new Tooltip([new("Alpha", "", TooltipTone.Title)]), "alpha"),
        new(20, "Beta", "spell · 20")
    ];

    static Task<IReadOnlyList<PickerRecord>> Search(string term) =>
        Task.FromResult<IReadOnlyList<PickerRecord>>(Records.Where(r => r.Label.Contains(term, StringComparison.OrdinalIgnoreCase) || r.Id.ToString() == term).ToArray());

    static SceneTree Tree => (SceneTree)Engine.GetMainLoop();

    static async Task Frames(int count)
    {
        for (int i = 0; i < count; i++)
        {
            await Tree.ToSignal(Tree, SceneTree.SignalName.ProcessFrame);
        }
    }

    static VBoxContainer List(RecordPicker popup) => (VBoxContainer)popup.FindChild("RecordList", true, false)!;

    [TestCase]
    public async Task ListsEveryRecordAndReturnsTheChoice()
    {
        var owner = new Node();
        Tree.Root.AddChild(owner);
        AutoFree(owner);
        List<PickerRecord> picked = [];
        var popup = RecordPicker.Show(owner, () => null, "Pick", Search, record => picked.Add(record));
        await Frames(3);

        AssertThat(popup.ResultCount).IsEqual(2);
        var first = (Button)List(popup).GetChild(0);
        first.EmitSignal(BaseButton.SignalName.Pressed);
        await Frames(2);

        AssertThat(picked.Count).IsEqual(1);
        AssertThat(picked[0].Id).IsEqual(10u);
        AssertThat(picked[0].Value).IsEqual("alpha");
    }

    [TestCase]
    public async Task TypingFiltersTheRows()
    {
        var owner = new Node();
        Tree.Root.AddChild(owner);
        AutoFree(owner);
        var popup = RecordPicker.Show(owner, () => null, "Pick", Search, _ => { });
        await Frames(3);

        var search = (LineEdit)popup.FindChild("RecordSearch", true, false)!;
        search.Text = "beta";
        search.EmitSignal(LineEdit.SignalName.TextChanged, "beta");
        await Frames(3);

        AssertThat(popup.ResultCount).IsEqual(1);
    }

    [TestCase]
    public async Task LongResultSetsArePaged()
    {
        var owner = new Node();
        Tree.Root.AddChild(owner);
        AutoFree(owner);
        var many = Enumerable.Range(1, 45).Select(i => new PickerRecord((uint)i, $"Record {i}")).ToArray();
        var popup = RecordPicker.Show(owner, () => null, "Pick",
            _ => Task.FromResult<IReadOnlyList<PickerRecord>>(many), _ => { });
        await Frames(3);

        AssertThat(popup.PageCount).IsEqual(2);
        AssertThat(List(popup).GetChildCount()).IsEqual(40);
        popup.ShowPage(1);
        AssertThat(List(popup).GetChildCount()).IsEqual(5);
    }

    [TestCase]
    public async Task ThumbnailsAppearOnRows()
    {
        var owner = new Node();
        Tree.Root.AddChild(owner);
        AutoFree(owner);
        using var image = Image.CreateEmpty(8, 8, false, Image.Format.Rgba8);
        var texture = ImageTexture.CreateFromImage(image);
        var popup = RecordPicker.Show(owner, () => null, "Pick",
            _ => Task.FromResult<IReadOnlyList<PickerRecord>>([new PickerRecord(1, "Creature", Thumbnail: _ => texture)]), _ => { });
        await Frames(3);

        AssertThat(((Button)List(popup).GetChild(0)).Icon).IsNotNull();
    }

    [TestCase]
    public async Task TabsSwitchTheSearch()
    {
        var owner = new Node();
        Tree.Root.AddChild(owner);
        AutoFree(owner);
        var popup = RecordPicker.Show(owner, () => null, "Pick",
            [
                new PickerTab("Creatures", _ => Task.FromResult<IReadOnlyList<PickerRecord>>([new(1, "Creature")])),
                new PickerTab("Objects", _ => Task.FromResult<IReadOnlyList<PickerRecord>>([new(2, "Object")]))
            ], _ => { });
        await Frames(3);

        AssertThat(popup.ResultCount).IsEqual(1);
        ((Button)popup.FindChild("RecordTab1", true, false)!).EmitSignal(BaseButton.SignalName.Pressed);
        await Frames(3);
        AssertThat(popup.ResultCount).IsEqual(1);
        AssertThat(((Button)List(popup).GetChild(0)).Text).IsEqual("Object");
    }
}
