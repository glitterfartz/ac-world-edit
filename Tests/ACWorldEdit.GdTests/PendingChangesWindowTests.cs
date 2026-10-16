using ACWorldEdit;
using ACWorldEdit.Foundation;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

namespace ACWorldEdit.GdTests;

/// <summary>
/// The real PendingChangesWindow control, headless. It only presents a list and raises requests, so
/// these tests never touch a draft file, the database or the viewer.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class PendingChangesWindowTests
{
    static readonly WorldPose Base = new(0, 1, 2, 3, 0);

    static PendingChange Creature(string label, uint guid = 1) =>
        new("id-" + guid, PendingKind.Creature, "s", label, new("s", guid, Base, Base with { X = 9 }), null, null);

    static PendingChange Graveyard(string label) =>
        new("gy", PendingKind.Graveyard, "s", label, null, new GraveyardEdit("s", 4, new(4, 0, 1, 2, 3, "GY", []), new(4, 0, 9, 2, 3, "GY", [])), null);

    static PendingChange Row(string label) =>
        new("row", PendingKind.Row, "s", label, null, null, new RowEdit("creature_template", "42", null, null));

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
    public void EmptyListSaysSoAndDisablesBulkActions()
    {
        var window = Spawn();
        window.Refresh([], 0);
        AssertThat(All<Label>(window).Any(l => l.Text == "No unsaved changes.")).IsTrue();
        AssertThat(ButtonText(window, "Export All…").Disabled).IsTrue();
        AssertThat(ButtonText(window, "Discard All…").Disabled).IsTrue();
        AssertThat(All<Button>(window).Count(b => b.Text == "Discard")).IsEqual(0);
    }

    [TestCase]
    public void ExportedChangesAreMentionedWhenHidden()
    {
        var window = Spawn();
        window.Refresh([], 3);
        AssertThat(All<Label>(window).Any(l => l.Text.Contains("3 exported change(s) stay applied"))).IsTrue();
    }

    [TestCase]
    public void EachChangeGetsATagLabelAndDiscardButton()
    {
        var window = Spawn();
        window.Refresh([Creature("Move Kobold"), Graveyard("Edit GY 4"), Row("Row edit")], 0);
        AssertThat(All<Button>(window).Count(b => b.Text == "Discard")).IsEqual(3);
        AssertThat(All<Label>(window).Any(l => l.Text == "3 unsaved change(s). Drafts are kept between sessions. Discard reverts only that one change.")).IsTrue();
        foreach (string label in new[] { "Move Kobold", "Edit GY 4", "Row edit" })
        {
            AssertThat(All<Label>(window).Any(l => l.Text == label && l.TooltipText == label)).IsTrue();
        }

        foreach (string tag in new[] { "CREATURE", "GRAVEYARD", "ROW" })
        {
            AssertThat(All<Label>(window).Any(l => l.Text == tag)).IsTrue();
        }

        AssertThat(ButtonText(window, "Export All…").Disabled).IsFalse();
        AssertThat(ButtonText(window, "Discard All…").Disabled).IsFalse();
    }

    [TestCase]
    public void RefreshReplacesTheOldRows()
    {
        var window = Spawn();
        window.Refresh([Creature("a", 1), Creature("b", 2)], 0);
        window.Refresh([Creature("c", 3)], 0);
        // RemoveChild is immediate, so only the new row's button remains.
        AssertThat(All<Button>(window).Count(b => b.Text == "Discard")).IsEqual(1);
        AssertThat(All<Label>(window).Any(l => l.Text == "a")).IsFalse();
        AssertThat(All<Label>(window).Any(l => l.Text == "c")).IsTrue();
    }

    [TestCase]
    public void ButtonsRaiseRequestsWithoutActingThemselves()
    {
        var window = Spawn();
        var change = Creature("Move Kobold");
        window.Refresh([change], 0);
        PendingChange? discarded = null;
        int exports = 0, discardAlls = 0;
        window.DiscardRequested += c => discarded = c;
        window.ExportAllRequested += () => exports++;
        window.DiscardAllRequested += () => discardAlls++;
        ButtonText(window, "Discard").EmitSignal(BaseButton.SignalName.Pressed);
        ButtonText(window, "Export All…").EmitSignal(BaseButton.SignalName.Pressed);
        ButtonText(window, "Discard All…").EmitSignal(BaseButton.SignalName.Pressed);
        AssertThat(discarded).IsEqual(change);
        AssertThat(exports).IsEqual(1);
        AssertThat(discardAlls).IsEqual(1);
    }

    [TestCase]
    public void StatusTextAndCloseButton()
    {
        var window = Spawn();
        window.Open();
        AssertThat(window.Visible).IsTrue();
        window.SetStatus("Exported 2 change(s).");
        AssertThat(All<Label>(window).Any(l => l.Text == "Exported 2 change(s).")).IsTrue();
        ButtonText(window, "✕").EmitSignal(BaseButton.SignalName.Pressed);
        AssertThat(window.Visible).IsFalse();
    }
}
