using ACWorldEdit;
using ACWorldEdit.Foundation;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

namespace ACWorldEdit.GdTests;

/// <summary>
/// Full interaction coverage for the change-history window: checking rows, bulk and per-row
/// apply/revert/discard, preview/export with the apply/revert toggle, notes editing, show-all
/// and refresh. The window only raises requests with fingerprints, so no draft, database or
/// viewer is ever involved.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class ChangeHistoryWindowInteractionTests
{
    static string Fingerprint(char c) => new(c, 64);

    static ChangeRecord Record(string fingerprint, ChangeStatus status, string label) => new(
        fingerprint,
        "source",
        WorldDialect.AzerothCore,
        label,
        "-- apply " + label,
        "-- revert " + label,
        status,
        new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc),
        status is ChangeStatus.Applied or ChangeStatus.Reverted ? new DateTime(2026, 1, 2, 12, 0, 0, DateTimeKind.Utc) : null,
        status == ChangeStatus.Reverted ? new DateTime(2026, 1, 3, 12, 0, 0, DateTimeKind.Utc) : null,
        "");

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

    static List<Button> Buttons(PendingChangesWindow window, string text) => All<Button>(window).Where(b => b.Text == text).ToList();

    static Button ButtonText(PendingChangesWindow window, string text) => All<Button>(window).First(b => b.Text == text);

    static void CheckRow(PendingChangesWindow window, int index)
    {
        All<CheckBox>(window)[index].ButtonPressed = true;
    }

    [TestCase]
    public void CheckedRowsApplyInRowOrder()
    {
        var window = Spawn();
        window.RefreshHistory([Record(Fingerprint('a'), ChangeStatus.Staged, "first"), Record(Fingerprint('b'), ChangeStatus.Staged, "second")], null);
        List<string>? applied = null;
        window.ApplyRequested += fps => applied = fps.ToList();
        CheckRow(window, 0);
        CheckRow(window, 1);
        ButtonText(window, "Apply selected").EmitSignal(BaseButton.SignalName.Pressed);
        AssertThat(applied).IsNotNull();
        AssertThat(applied!.Count).IsEqual(2);
        AssertThat(applied[0]).IsEqual(Fingerprint('a'));
        AssertThat(applied[1]).IsEqual(Fingerprint('b'));
    }

    [TestCase]
    public void ExportKindToggleSwitchesPreviewAndExportToRevert()
    {
        var window = Spawn();
        window.RefreshHistory([Record(Fingerprint('a'), ChangeStatus.Applied, "done")], null);
        CheckRow(window, 0);
        (IReadOnlyList<string> Fingerprints, bool Revert)? preview = null;
        (IReadOnlyList<string> Fingerprints, bool Revert)? export = null;
        window.PreviewRequested += (fps, revert) => preview = (fps, revert);
        window.ExportRequested += (fps, revert) => export = (fps, revert);

        ButtonText(window, "Preview…").EmitSignal(BaseButton.SignalName.Pressed);
        AssertThat(preview.HasValue && !preview.Value.Revert).IsTrue();

        var kind = All<OptionButton>(window).First();
        kind.Selected = 1;
        ButtonText(window, "Preview…").EmitSignal(BaseButton.SignalName.Pressed);
        AssertThat(preview.HasValue && preview.Value.Revert).IsTrue();
        ButtonText(window, "Export selected…").EmitSignal(BaseButton.SignalName.Pressed);
        AssertThat(export.HasValue && export.Value.Revert).IsTrue();
        AssertThat(export!.Value.Fingerprints.Count).IsEqual(1);
    }

    [TestCase]
    public void PerRowButtonsFireSingleFingerprintActions()
    {
        var window = Spawn();
        window.RefreshHistory([Record(Fingerprint('a'), ChangeStatus.Staged, "stage me"), Record(Fingerprint('b'), ChangeStatus.Applied, "undo me")], null);
        List<string>? applied = null, reverted = null, discarded = null;
        window.ApplyRequested += fps => applied = fps.ToList();
        window.RevertRequested += fps => reverted = fps.ToList();
        window.DiscardStagedRequested += fps => discarded = fps.ToList();

        Buttons(window, "Apply").Single().EmitSignal(BaseButton.SignalName.Pressed);
        AssertThat(applied!.Single()).IsEqual(Fingerprint('a'));
        Buttons(window, "Revert").Single().EmitSignal(BaseButton.SignalName.Pressed);
        AssertThat(reverted!.Single()).IsEqual(Fingerprint('b'));
        Buttons(window, "Discard").Single().EmitSignal(BaseButton.SignalName.Pressed);
        AssertThat(discarded!.Single()).IsEqual(Fingerprint('a'));
    }

    [TestCase]
    public void RevertedAndFailedRowsShowStatusWithoutActions()
    {
        var window = Spawn();
        window.RefreshHistory(
            [Record(Fingerprint('a'), ChangeStatus.Reverted, "undone"), Record(Fingerprint('b'), ChangeStatus.Failed, "broken")], null);
        AssertThat(All<Label>(window).Any(l => l.Text == "REVERTED")).IsTrue();
        AssertThat(All<Label>(window).Any(l => l.Text == "FAILED")).IsTrue();
        AssertThat(Buttons(window, "Apply").Count).IsEqual(0);
        AssertThat(Buttons(window, "Revert").Count).IsEqual(0);
        AssertThat(Buttons(window, "Discard").Count).IsEqual(0);
        // Bulk actions stay available for the checked set; discard-all needs a staged row.
        AssertThat(ButtonText(window, "Apply selected").Disabled).IsFalse();
        AssertThat(ButtonText(window, "Discard staged…").Disabled).IsTrue();
    }

    [TestCase]
    public void SummaryCountsStatusesAndTimestamps()
    {
        var window = Spawn();
        window.RefreshHistory(
            [Record(Fingerprint('a'), ChangeStatus.Staged, "s"), Record(Fingerprint('b'), ChangeStatus.Applied, "a"),
             Record(Fingerprint('c'), ChangeStatus.Reverted, "r"), Record(Fingerprint('d'), ChangeStatus.Failed, "f")], null);
        AssertThat(All<Label>(window).Any(l => l.Text.Contains("4 change(s): 1 staged, 1 applied."))).IsTrue();
        AssertThat(All<Label>(window).Any(l => l.Text.Contains("staged 2026-01-01"))).IsTrue();
        AssertThat(All<Label>(window).Any(l => l.Text.Contains("applied 2026-01-02"))).IsTrue();
        AssertThat(All<Label>(window).Any(l => l.Text.Contains("reverted 2026-01-03"))).IsTrue();
        AssertThat(All<LineEdit>(window).Count).IsEqual(4);
    }

    [TestCase]
    public void ShowAllRefreshAndDiscardAllForwardEvents()
    {
        var window = Spawn();
        window.RefreshHistory([Record(Fingerprint('a'), ChangeStatus.Staged, "s")], null);
        bool? shown = null;
        int refreshes = 0, discardAlls = 0;
        window.ShowAllChanged += show => shown = show;
        window.HistoryRefreshRequested += () => refreshes++;
        window.DiscardAllStagedRequested += () => discardAlls++;

        var showAll = All<CheckButton>(window).First(b => b.Text == "Show all worlds");
        showAll.ButtonPressed = true;
        AssertThat(shown.HasValue && shown.Value).IsTrue();
        showAll.ButtonPressed = false;
        AssertThat(shown.HasValue && !shown.Value).IsTrue();
        ButtonText(window, "Refresh").EmitSignal(BaseButton.SignalName.Pressed);
        AssertThat(refreshes).IsEqual(1);
        ButtonText(window, "Discard staged…").EmitSignal(BaseButton.SignalName.Pressed);
        AssertThat(discardAlls).IsEqual(1);
    }

    [TestCase]
    public void NotesSaveOnEnterAndOnFocusLoss()
    {
        var window = Spawn();
        window.RefreshHistory([Record(Fingerprint('a'), ChangeStatus.Staged, "s")], null);
        var saved = new List<(string Fingerprint, string Notes)>();
        window.NotesChanged += (fp, notes) => saved.Add((fp, notes));

        var field = All<LineEdit>(window).First();
        field.Text = "by the tree";
        field.EmitSignal(LineEdit.SignalName.TextSubmitted, "by the tree");
        field.EmitSignal(Control.SignalName.FocusExited);
        AssertThat(saved.Count).IsEqual(2);
        AssertThat(saved[0].Notes).IsEqual("by the tree");
        AssertThat(saved[0].Fingerprint).IsEqual(Fingerprint('a'));
    }

    [TestCase]
    public void CheckedBoxesSurviveRefreshWithChangedRows()
    {
        var window = Spawn();
        window.RefreshHistory([Record(Fingerprint('a'), ChangeStatus.Staged, "first")], null);
        CheckRow(window, 0);
        // A background sync rebuilds the rows (new label here); the checked box must come back.
        window.RefreshHistory([Record(Fingerprint('a'), ChangeStatus.Staged, "first, edited")], null);
        List<string>? applied = null;
        window.ApplyRequested += fps => applied = fps.ToList();
        ButtonText(window, "Apply selected").EmitSignal(BaseButton.SignalName.Pressed);
        AssertThat(applied).IsNotNull();
        AssertThat(applied!.Single()).IsEqual(Fingerprint('a'));
    }

    [TestCase]
    public void UncheckingRemovesTheRowFromTheSelection()
    {
        var window = Spawn();
        window.RefreshHistory([Record(Fingerprint('a'), ChangeStatus.Staged, "s")], null);
        CheckRow(window, 0);
        All<CheckBox>(window).First().ButtonPressed = false;
        List<string>? applied = null;
        window.ApplyRequested += fps => applied = fps.ToList();
        ButtonText(window, "Apply selected").EmitSignal(BaseButton.SignalName.Pressed);
        AssertThat(applied).IsNotNull();
        AssertThat(applied!.Count).IsEqual(0);
    }

    [TestCase]
    public void InProgressNotesSurviveRefresh()
    {
        var window = Spawn();
        window.RefreshHistory([Record(Fingerprint('a'), ChangeStatus.Staged, "s")], null);
        var field = All<LineEdit>(window).First();
        field.Text = "half-typed";
        // The stored note is still blank, but the user's keystrokes must not be lost.
        window.RefreshHistory([Record(Fingerprint('a'), ChangeStatus.Staged, "s")], null);
        AssertThat(All<LineEdit>(window).First().Text).IsEqual("half-typed");
    }

    [TestCase]
    public void EmptyChecklistDisablesBulkActions()
    {
        var window = Spawn();
        window.RefreshHistory([], null);
        AssertThat(ButtonText(window, "Apply selected").Disabled).IsTrue();
        AssertThat(ButtonText(window, "Revert selected").Disabled).IsTrue();
        AssertThat(ButtonText(window, "Preview…").Disabled).IsTrue();
        AssertThat(ButtonText(window, "Export selected…").Disabled).IsTrue();
    }
}
