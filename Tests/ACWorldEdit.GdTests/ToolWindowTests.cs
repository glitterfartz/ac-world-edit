using ACWorldEdit;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

namespace ACWorldEdit.GdTests;

/// <summary>
/// The tool-window contract every editor window inherits: a centered first open, the close paths,
/// a body that grows with the window, a floor that stops it shrinking into its content, an action
/// row that wraps instead of widening the window, and a status row fed by one SetStatus call.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public partial class ToolWindowTests
{
    sealed partial class Probe : ContentEditorWindow
    {
        readonly int actionCount;

        public Probe(int actions = 0) : base("Probe", "Probe") => actionCount = actions;

        protected override void BuildEditor()
        {
            // Actions and Body exist by the time BuildEditor runs.
            for (int i = 0; i < actionCount; i++)
            {
                Actions.AddChild(new Button { Text = "Action " + i });
            }
        }
    }

    static Probe Spawn(int actions = 0)
    {
        var window = AutoFree(new Probe(actions))!;
        ((SceneTree)Engine.GetMainLoop()).Root.AddChild(window);
        window.Open();
        return window;
    }

    static async Task Frames(int count = 2)
    {
        var tree = (SceneTree)Engine.GetMainLoop();
        for (int i = 0; i < count; i++)
        {
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        }
    }

    [TestCase]
    public void OpeningCentersShowsAndFocuses()
    {
        var window = Spawn();
        var main = ((SceneTree)Engine.GetMainLoop()).Root;

        AssertThat(window.Visible).IsTrue();
        AssertThat(window.HasFocus()).IsTrue();
        Vector2I expected = main.Position + (main.Size - window.Size) / 2;
        AssertThat(Mathf.Abs(window.Position.X - expected.X) <= 2).IsTrue();
        AssertThat(Mathf.Abs(window.Position.Y - expected.Y) <= 2).IsTrue();
    }

    [TestCase]
    public async Task CloseRequestedAndEscapeHideTheWindow()
    {
        var window = Spawn();

        window.EmitSignal(Window.SignalName.CloseRequested);
        AssertThat(window.Visible).IsFalse();

        window.Open();
        window.PushInput(new InputEventKey { Keycode = Key.Escape, Pressed = true });
        await Frames();
        AssertThat(window.Visible).IsFalse();
    }

    [TestCase]
    public async Task EscapeHidesTheFocusedWindowOnly()
    {
        var first = Spawn();
        var second = Spawn();

        second.PushInput(new InputEventKey { Keycode = Key.Escape, Pressed = true });
        await Frames();

        AssertThat(second.Visible).IsFalse();
        AssertThat(first.Visible).IsTrue();
    }

    [TestCase]
    public async Task GrowingTheWindowExpandsTheBody()
    {
        var window = Spawn();
        await Frames();
        float before = window.Body.Size.Y;

        window.Size = new Vector2I(1400, 1000);
        await Frames();

        AssertThat(window.Body.Size.Y > before + 100).IsTrue();
    }

    [TestCase]
    public async Task ResizingBelowTheContentKeepsTheMinimumAndScrolls()
    {
        var window = Spawn();

        window.Size = new Vector2I(320, 240);
        await Frames();

        AssertThat(window.Size.X >= window.MinSize.X).IsTrue();
        AssertThat(window.Size.Y >= window.MinSize.Y).IsTrue();
    }

    [TestCase]
    public async Task ManyActionsWrapWithoutWideningTheWindow()
    {
        var window = Spawn(10);
        await Frames();

        AssertThat(window.Actions.GetCombinedMinimumSize().X < 1200).IsTrue();
        AssertThat(window.Size.X <= window.MinSize.X + 600).IsTrue();
    }

    [TestCase]
    public void SetStatusFillsTheRowAndReportsOnce()
    {
        var window = Spawn();
        int reported = 0;
        window.Report = _ => reported++;

        window.SetStatus("Saved.");
        AssertThat(window.StatusLabel.Text).IsEqual("Saved.");
        AssertThat(window.StatusLabel.Visible).IsTrue();
        AssertThat(reported).IsEqual(1);

        window.SetStatus("");
        AssertThat(window.StatusLabel.Visible).IsFalse();
    }
}
