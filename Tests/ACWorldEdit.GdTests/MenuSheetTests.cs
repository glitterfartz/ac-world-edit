using ACWorldEdit;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

namespace ACWorldEdit.GdTests;

/// <summary>
/// The menu sheet is plain data loaded once: menus keep sheet order, separators and submenus survive,
/// item callbacks run, and a chord cannot be claimed twice.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class MenuSheetTests
{
    [TestCase]
    public void LoadBuildsMenusAndItemsInSheetOrder()
    {
        var menu = AutoFree(new EditorMenu())!;
        int runs = 0;
        bool grid = false;

        menu.Load(MenuSheet.Of(
            ("File", [
                new MenuCommand("Open", () => runs++),
                new MenuSeparator(),
                new MenuCommand("Quit", () => runs += 10, Accelerator: Key.Q)
            ]),
            ("View", [
                new MenuCheck("Terrain cell borders", () => grid = true, () => grid),
                new MenuSubmenu("UI scale", [
                    new MenuCheck("100%", () => { }, () => true),
                    new MenuCheck("125%", () => { }, () => false)
                ])
            ])));

        AssertThat(menu.Manifest.Count).IsEqual(7);
        AssertThat(menu.GetChildCount()).IsEqual(2);
        var file = (PopupMenu)menu.GetChild(0);
        AssertThat(file.Name.ToString()).IsEqual("File");
        AssertThat(file.ItemCount).IsEqual(3);
        AssertThat(file.GetItemText(0)).IsEqual("Open");
        AssertThat(file.IsItemSeparator(1)).IsTrue();
        AssertThat(file.GetItemAccelerator(2)).IsEqual((Key)Key.Q);

        file.EmitSignal(PopupMenu.SignalName.IdPressed, file.GetItemId(0));
        AssertThat(runs).IsEqual(1);

        var view = (PopupMenu)menu.GetChild(1);
        AssertThat(view.ItemCount).IsEqual(2);
        AssertThat(view.GetItemText(0)).IsEqual("Terrain cell borders");
        view.EmitSignal(PopupMenu.SignalName.IdPressed, view.GetItemId(0));
        AssertThat(grid).IsTrue();
    }

    [TestCase]
    public void ADuplicateAcceleratorIsRejected()
    {
        var menu = AutoFree(new EditorMenu())!;
        string? error = null;
        try
        {
            menu.Load(MenuSheet.Of(
                ("File", [new MenuCommand("One", () => { }, Accelerator: Key.F)]),
                ("View", [new MenuCommand("Two", () => { }, Accelerator: Key.F)])));
        }
        catch (InvalidOperationException caught)
        {
            error = caught.Message;
        }

        AssertThat(error != null).IsTrue();
        AssertString(error).Contains("Accelerator F is used");
    }

    [TestCase]
    public void ToolbarLabelAndChoiceKeepTheirOrderAndCallback()
    {
        var bar = AutoFree(new EditorToolbar())!;
        int chosen = -1;

        var label = bar.Label("Rotation step");
        var choice = bar.Choice("Fixed heading increments while rotating", ["Free", "5°"], 0, index => chosen = index);

        AssertThat(label.Text).IsEqual("Rotation step");
        AssertThat(bar.GetChildCount()).IsEqual(2);
        AssertThat(choice.ItemCount).IsEqual(2);
        AssertThat(choice.Selected).IsEqual(0);
        choice.EmitSignal(OptionButton.SignalName.ItemSelected, 1L);
        AssertThat(chosen).IsEqual(1);
    }
}
