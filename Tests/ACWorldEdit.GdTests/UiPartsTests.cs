using ACWorldEdit;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

namespace ACWorldEdit.GdTests;

/// <summary>
/// Shared window and card parts: the draggable header, the history row, the pose block, the plain
/// option list and the two dialog shapes. Construction only; callbacks run on the emitted signal.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public partial class UiPartsTests
{
    static SceneTree Tree => (SceneTree)Engine.GetMainLoop();

    sealed partial class TestWindow : ContentEditorWindow
    {
        public TestWindow() : base("TestWindow", "Test window")
        {
        }
    }

    [TestCase]
    public void HeaderKeepsTheTitleAndOptionalSubtitle()
    {
        var window = AutoFree(new TestWindow())!;
        Tree.Root.AddChild(window);
        window.Open();

        window.SetHeading("Quest Editor");
        AssertThat(window.TitleLabel.Text).IsEqual("Quest Editor");
        AssertThat(window.SubtitleLabel.Visible).IsFalse();
        AssertThat(window.Title).IsEqual("Quest Editor");

        window.SetHeading("Quest Editor", "Quest 1");
        AssertThat(window.SubtitleLabel.Text).IsEqual("Quest 1");
        AssertThat(window.SubtitleLabel.Visible).IsTrue();
    }

    [TestCase]
    public void HistoryRowAndPoseCardAppendInOrder()
    {
        var card = AutoFree(new VBoxContainer())!;
        int undos = 0, redos = 0, submits = 0;
        var fields = new LineEdit[4];

        var (undo, redo) = InspectorUi.HistoryRow(card, () => undos++, () => redos++);
        InspectorUi.PoseCard(card, fields, () => submits++);

        AssertThat(card.GetChild(0)).IsInstanceOf<HBoxContainer>();
        AssertThat(((HBoxContainer)card.GetChild(0)).GetChildCount()).IsEqual(2);
        AssertThat(undo.Text).IsEqual("Undo move");
        AssertThat(redo.Text).IsEqual("Redo move");
        AssertThat(card.GetChild(1)).IsInstanceOf<HSeparator>();
        AssertThat(fields.All(field => field != null)).IsTrue();
        AssertThat(fields.All(field => !field.Editable)).IsTrue();

        undo.EmitSignal(BaseButton.SignalName.Pressed);
        redo.EmitSignal(BaseButton.SignalName.Pressed);
        AssertThat(undos).IsEqual(1);
        AssertThat(redos).IsEqual(1);
        AssertThat(submits).IsEqual(0);
    }

    [TestCase]
    public void ChoiceSelectsWithoutEmittingAndReportsPicks()
    {
        var card = AutoFree(new VBoxContainer())!;
        int picked = -1;

        var choice = InspectorUi.Choice(["Neutral (0)", "Alliance (469)", "Horde (67)"], 2,
            index => picked = index, card, "side", expand: true, fitToLongest: false);

        AssertThat(card.GetChildCount()).IsEqual(1);
        AssertThat(choice.Selected).IsEqual(2);
        AssertThat(choice.FitToLongestItem).IsFalse();
        AssertThat(choice.SizeFlagsHorizontal).IsEqual(Control.SizeFlags.ExpandFill);
        AssertThat(picked).IsEqual(-1);
        choice.EmitSignal(OptionButton.SignalName.ItemSelected, 1L);
        AssertThat(picked).IsEqual(1);
    }

    [TestCase]
    public void InfoAndConfirmConfigureThenAttach()
    {
        var owner = new Node();
        Tree.Root.AddChild(owner);
        AutoFree(owner);

        var info = Dialogs.Info(owner, "About", "Text.");
        var confirm = Dialogs.Confirm(owner, "Discard", "Question?", () => { }, ok: "Discard all");

        AssertThat(owner.GetChildCount()).IsEqual(2);
        AssertThat(info.Title).IsEqual("About");
        AssertThat(info.DialogText).IsEqual("Text.");
        AssertThat(confirm.Title).IsEqual("Discard");
        AssertThat(confirm.DialogText).IsEqual("Question?");
        AssertThat(confirm.OkButtonText).IsEqual("Discard all");
    }
}
