using ACWorldEdit;
using ACWorldEdit.Foundation;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

namespace ACWorldEdit.GdTests;

/// <summary>
/// The real change-history window control, headless. It only presents a list and raises requests,
/// so these tests never touch a draft file, the database or the viewer.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class PendingChangesWindowTests
{
    static ChangeRecord Record(string fingerprint, ChangeStatus status, string label = "Move Kobold") => new(
        fingerprint.PadRight(64, '0'),
        "source",
        WorldDialect.AzerothCore,
        label,
        "-- apply",
        "-- revert",
        status,
        new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc),
        status == ChangeStatus.Applied ? new DateTime(2026, 1, 2, 12, 0, 0, DateTimeKind.Utc) : null,
        null,
        "");

    /// <summary>
    /// A window added to the scene tree so _Ready builds its controls; freed with the test.
    /// </summary>
    static PendingChangesWindow Spawn()
    {
        var window = new PendingChangesWindow();
        ((SceneTree)Engine.GetMainLoop()).Root.AddChild(window);
        AutoFree(window);
        return window;
    }

    static List<T> All<T>(Node node) where T : Node
    {
        var found = new List<T>();
        void Walk(Node n)
        {
            foreach (var child in n.GetChildren())
            {
                if (child is T match)
                {
                    found.Add(match);
                }

                Walk(child);
            }
        }

        Walk(node);
        return found;
    }

    static Button ButtonText(PendingChangesWindow window, string text) => All<Button>(window).First(b => b.Text == text);

    [TestCase]
    public void EmptyHistorySaysSoAndDisablesBulkActions()
    {
        var window = Spawn();
        window.RefreshHistory([], null);
        AssertThat(All<Label>(window).Any(l => l.Text.Contains("No tracked changes"))).IsTrue();
        AssertThat(ButtonText(window, "Apply selected").Disabled).IsTrue();
        AssertThat(ButtonText(window, "Revert selected").Disabled).IsTrue();
        AssertThat(ButtonText(window, "Export selected…").Disabled).IsTrue();
        AssertThat(ButtonText(window, "Discard staged…").Disabled).IsTrue();
    }

    [TestCase]
    public void StagedRowsShowStatusAndPerRowActions()
    {
        var window = Spawn();
        window.RefreshHistory([Record("a", ChangeStatus.Staged), Record("b", ChangeStatus.Applied, "Revert me")], null);
        AssertThat(All<Label>(window).Any(l => l.Text.Contains("2 change(s): 1 staged, 1 applied."))).IsTrue();
        AssertThat(All<Label>(window).Any(l => l.Text == "STAGED")).IsTrue();
        AssertThat(All<Label>(window).Any(l => l.Text == "APPLIED")).IsTrue();
        AssertThat(All<Button>(window).Count(b => b.Text == "Apply")).IsEqual(1);
        AssertThat(All<Button>(window).Count(b => b.Text == "Revert")).IsEqual(1);
        AssertThat(All<Button>(window).Count(b => b.Text == "Discard")).IsEqual(1);
        AssertThat(All<LineEdit>(window).Count).IsEqual(2);
    }

    [TestCase]
    public void RefreshReplacesTheOldRows()
    {
        var window = Spawn();
        window.RefreshHistory([Record("a", ChangeStatus.Staged, "first"), Record("b", ChangeStatus.Staged, "second")], null);
        window.RefreshHistory([Record("c", ChangeStatus.Staged, "third")], null);
        AssertThat(All<Label>(window).Any(l => l.Text == "first")).IsFalse();
        AssertThat(All<Label>(window).Any(l => l.Text == "third")).IsTrue();
    }

    [TestCase]
    public void ButtonsRaiseRequestsWithoutActingThemselves()
    {
        var window = Spawn();
        window.RefreshHistory([Record("a", ChangeStatus.Staged, "Move Kobold")], null);
        int applies = 0, reverts = 0, discardAlls = 0, previews = 0, exports = 0, refreshes = 0;
        window.ApplyRequested += _ => applies++;
        window.RevertRequested += _ => reverts++;
        window.DiscardAllStagedRequested += () => discardAlls++;
        window.PreviewRequested += (_, _) => previews++;
        window.ExportRequested += (_, _) => exports++;
        window.HistoryRefreshRequested += () => refreshes++;
        // Check the row so bulk actions have a selection.
        All<CheckBox>(window).First().ButtonPressed = true;
        ButtonText(window, "Apply").EmitSignal(BaseButton.SignalName.Pressed);
        ButtonText(window, "Apply selected").EmitSignal(BaseButton.SignalName.Pressed);
        ButtonText(window, "Revert selected").EmitSignal(BaseButton.SignalName.Pressed);
        ButtonText(window, "Preview…").EmitSignal(BaseButton.SignalName.Pressed);
        ButtonText(window, "Export selected…").EmitSignal(BaseButton.SignalName.Pressed);
        ButtonText(window, "Discard staged…").EmitSignal(BaseButton.SignalName.Pressed);
        ButtonText(window, "Refresh").EmitSignal(BaseButton.SignalName.Pressed);
        AssertThat(applies).IsEqual(2);
        AssertThat(reverts).IsEqual(1);
        AssertThat(previews).IsEqual(1);
        AssertThat(exports).IsEqual(1);
        AssertThat(discardAlls).IsEqual(1);
        AssertThat(refreshes).IsEqual(1);
    }

    [TestCase]
    public void NotesSaveOnEnter()
    {
        var window = Spawn();
        window.RefreshHistory([Record("a", ChangeStatus.Staged, "Move Kobold")], null);
        (string Fingerprint, string Notes) saved = ("", "");
        window.NotesChanged += (fp, notes) => saved = (fp, notes);
        var field = All<LineEdit>(window).First();
        field.Text = "near the tree";
        field.EmitSignal(LineEdit.SignalName.TextSubmitted, "near the tree");
        AssertThat(saved.Notes).IsEqual("near the tree");
        AssertThat(saved.Fingerprint.Length).IsEqual(64);
    }

    [TestCase]
    public void StatusTextAndCloseButton()
    {
        var window = Spawn();
        window.Open();
        AssertThat(window.Visible).IsTrue();
        window.SetStatus("Applied 2 change(s).");
        AssertThat(All<Label>(window).Any(l => l.Text == "Applied 2 change(s).")).IsTrue();
        ButtonText(window, "✕").EmitSignal(BaseButton.SignalName.Pressed);
        AssertThat(window.Visible).IsFalse();
    }
}
