using ACWorldEdit;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

namespace ACWorldEdit.GdTests;

/// <summary>
/// The form builder is construction only: it appends to the container it was given, keeps creation
/// order, fires the callbacks it was handed, and re-evaluates enabled predicates on Refresh.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class UiFormTests
{
    [TestCase]
    public void AppendsToTheExistingContainerInCreationOrder()
    {
        var root = AutoFree(new VBoxContainer())!;
        var form = new UiForm(root);

        form.Heading("AC WORLD EDIT");
        form.Caption("WORLD VIEWER", Colors.Red);
        form.Add(new HSeparator());
        var spin = form.Spin(0, 63, 34);
        form.LabeledRow("Tile X", spin);

        // No wrapper node: the four calls are four children of the caller's box.
        AssertThat(root.GetChildCount()).IsEqual(4);
        AssertThat(root.GetChild<Label>(0).Text).IsEqual("AC WORLD EDIT");
        AssertThat(root.GetChild<Label>(0).GetThemeFontSize("font_size")).IsEqual(24);
        AssertThat(root.GetChild<Label>(1).Text).IsEqual("WORLD VIEWER");
        AssertThat(root.GetChild(2)).IsInstanceOf<HSeparator>();
        var row = (HBoxContainer)root.GetChild(3);
        AssertThat((string)((Label)row.GetChild(0)).Text).IsEqual("Tile X");
        AssertThat(row.GetChild(1)).IsInstanceOf<SpinBox>();
        AssertThat(((SpinBox)row.GetChild(1)).Value).IsEqual(34);
        AssertThat(form.Manifest.Count).IsEqual(5);
    }

    [TestCase]
    public void CallbacksFireAndRefreshMirrorsState()
    {
        var root = AutoFree(new VBoxContainer())!;
        var form = new UiForm(root);
        int presses = 0, chosen = -1;
        double changed = -1;
        bool canPress = false;

        var button = form.Action("Go", () => presses++, "tooltip", enabled: () => canPress);
        var options = form.Options(["0.5x", "1x", "2x"], 1, index => chosen = index, name: "Speed");
        var spin = form.Spin(0, 10, 2, value => changed = value);

        AssertThat(button.Disabled).IsTrue();
        canPress = true;
        form.Refresh();
        AssertThat(button.Disabled).IsFalse();

        button.EmitSignal(BaseButton.SignalName.Pressed);
        options.EmitSignal(OptionButton.SignalName.ItemSelected, 2L);
        spin.EmitSignal(Godot.Range.SignalName.ValueChanged, 7.0);

        AssertThat(presses).IsEqual(1);
        AssertThat(chosen).IsEqual(2);
        AssertThat(changed).IsEqual(7.0);
        AssertThat(options.Name).IsEqual("Speed");
        AssertThat(options.Selected).IsEqual(1);
    }

    [TestCase]
    public void ClippedOptionsTrimInsteadOfGrowing()
    {
        var root = AutoFree(new VBoxContainer())!;
        var form = new UiForm(root);

        var clipped = form.Options(["A long map name"], 0, _ => { }, clip: true);
        var plain = form.Options(["Short"], 0, _ => { });

        AssertThat(clipped.ClipText).IsTrue();
        AssertThat(clipped.FitToLongestItem).IsFalse();
        AssertThat(plain.ClipText).IsFalse();
        AssertThat(plain.FitToLongestItem).IsTrue();
    }
}
