using ACWorldEdit;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

namespace ACWorldEdit.GdTests;

/// <summary>
/// The delete-impact dialog's third answer: a spawn delete can offer "Remove these too", which must run
/// exactly the cleanup it names and then close, and must not appear when nothing can be removed.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public partial class DeleteImpactDialogTests
{
    static SceneTree Tree => (SceneTree)Engine.GetMainLoop();

    static Button? Find(Node root, string text) => root.FindChildren("*", "Button", true, false).OfType<Button>().FirstOrDefault(button => button.Text == text);

    [TestCase]
    public void TheThirdAnswerRunsItsActionOnceAndClosesTheDialog()
    {
        var owner = new Node();
        Tree.Root.AddChild(owner);
        AutoFree(owner);

        int ran = 0;
        var dialog = Dialogs.Confirm(owner, "Delete Hogger", "Question?", () => { }, ok: "Delete only this", cancel: "Keep",
            extra: ("Remove these too", () => ran++));

        var extra = Find(dialog, "Remove these too");
        AssertThat(extra).IsNotNull();
        AssertThat(Find(dialog, "Delete only this")).IsNotNull();
        AssertThat(Find(dialog, "Keep")).IsNotNull();

        extra!.EmitSignal(BaseButton.SignalName.Pressed);
        AssertThat(ran).IsEqual(1);
        AssertThat(dialog.IsQueuedForDeletion()).IsTrue();
    }

    [TestCase]
    public void WithoutAnExtraActionTheThirdAnswerIsNotOffered()
    {
        var owner = new Node();
        Tree.Root.AddChild(owner);
        AutoFree(owner);

        var dialog = Dialogs.Confirm(owner, "Delete Hogger", "Question?", () => { }, ok: "Delete anyway", cancel: "Keep");
        AssertThat(Find(dialog, "Remove these too")).IsNull();
        AssertThat(dialog.GetOkButton().Text).IsEqual("Delete anyway");
        AssertThat(dialog.GetCancelButton().Text).IsEqual("Keep");
    }
}
