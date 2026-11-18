using ACWorldEdit;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

namespace ACWorldEdit.GdTests;

/// <summary>
/// The cinematics window, headless: the tabs, the AzerothCore not-applicable panel, choice cards fed
/// by an injected snapshot, and every staged command (add, edit, remove, new, duplicate, undo).
/// No database is read.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class CinematicsWindowTests
{
    static SceneTree Tree => (SceneTree)Engine.GetMainLoop();

    static CinematicsEditorWindow Window(WorldDialect dialect = WorldDialect.TrinityCore)
    {
        string path = Path.Combine(Path.GetTempPath(), "acwe-gdtest-" + Guid.NewGuid().ToString("N") + ".json");
        var window = new CinematicsEditorWindow
        {
            Drafts = new ContentDrafts(path, _ => TcTables.Instance),
            Connection = () => WorldConnection.DefaultFor(dialect)
        };
        Tree.Root.AddChild(window);
        AutoFree(window);
        return window;
    }

    static PlayerChoiceSnapshot Snapshot()
    {
        var rows = new RowSet();
        rows.Set(TcTables.PlayerChoice, TcCinematicsRules.NewChoice(240).With("Question", "Which weapon should we pursue first?"));
        rows.Set(TcTables.PlayerChoiceResponse, TcCinematicsRules.NewResponse(240, 504, 0, "Select").With("Header", "Beast Mastery"));
        rows.Set(TcTables.PlayerChoiceResponse, TcCinematicsRules.NewResponse(240, 505, 1, "Select").With("Header", "Survival"));
        rows.Set(TcTables.PlayerChoiceResponse, TcCinematicsRules.NewResponse(240, 506, 2, "Select").With("Header", "Marksmanship"));
        rows.Set(TcTables.PlayerChoiceResponseReward, TcCinematicsRules.NewReward(240, 504).With("Money", 30000UL));
        rows.Set(TcTables.PlayerChoiceResponseRewardItem, TcCinematicsRules.NewRewardItem(240, 504, 0, 128861, 1));
        return new PlayerChoiceSnapshot(rows, new ContentNames(null), "test", 974);
    }

    /// <summary>A TrinityCore window with the player-choice reader replaced by the fixed snapshot.</summary>
    static (CinematicsEditorWindow Window, TcCinematicsPanel Panel) Loaded()
    {
        var window = Window();
        window.Open(0);
        var panel = (TcCinematicsPanel)window.Panel;
        panel.Reader = (_, _) => Task.FromResult(Snapshot());
        return (window, panel);
    }

    static Button Named(Node root, string name) => (Button)root.FindChild(name, true, false)!;

    [TestCase]
    public void TabsSwitchAndTheAzerothCorePanelSaysNotApplicable()
    {
        var window = Window();
        window.Open(0);
        AssertThat(window.Panel.Dialect).IsEqual(WorldDialect.TrinityCore);
        AssertThat(window.Panel.CurrentTab).IsEqual(CinematicsPanel.Tab.PlayerChoices);

        window.ShowTab(CinematicsPanel.Tab.Conversations);
        AssertThat(window.Panel.CurrentTab).IsEqual(CinematicsPanel.Tab.Conversations);
        window.ShowTab(CinematicsPanel.Tab.PlayerChoices);
        AssertThat(window.Panel.CurrentTab).IsEqual(CinematicsPanel.Tab.PlayerChoices);

        var ac = Window(WorldDialect.AzerothCore);
        ac.Open(0);
        AssertThat(ac.Panel).IsInstanceOf<AcCinematicsPanel>();
        AssertString(((AcCinematicsPanel)ac.Panel).Notice).Contains("TrinityCore");
    }

    [TestCase]
    public async Task CardsRenderFromAnInjectedSnapshot()
    {
        var (_, panel) = Loaded();
        await panel.Load(240);

        AssertThat(panel.CurrentId!.Value).IsEqual(240u);
        AssertThat(panel.PlayerChoice).IsNotNull();
        AssertThat(panel.PlayerChoice!.CardCount).IsEqual(3);
        AssertString(panel.PlayerChoice.QuestionText).Contains("Which weapon");
        AssertString(panel.PlayerChoice.RewardSummary(504)).Contains("gold");
    }

    [TestCase]
    public async Task AddingAResponseStagesOneInsert()
    {
        var (window, panel) = Loaded();
        await panel.Load(240);

        Named(panel, "AddResponse").EmitSignal(BaseButton.SignalName.Pressed);

        var edits = window.Drafts.Draft!.NetFor("test");
        AssertThat(edits.Count).IsEqual(1);
        AssertThat(edits[0].Edit.Table).IsEqual("playerchoice_response");
        AssertThat(edits[0].Edit.After).IsNotNull();
        AssertThat(edits[0].Edit.After!.I("ChoiceId")).IsEqual(240);
        AssertThat(panel.PlayerChoice!.CardCount).IsEqual(4);
    }

    [TestCase]
    public async Task EditingTheHeaderStagesOneUpdateAndUndoRevertsIt()
    {
        var (window, panel) = Loaded();
        await panel.Load(240);
        Named(panel, "Response_504").EmitSignal(BaseButton.SignalName.Pressed);

        var header = (LineEdit)panel.FindChild("Header", true, false)!;
        header.Text = "Beast Mastery II";
        header.EmitSignal(LineEdit.SignalName.TextSubmitted, "Beast Mastery II");

        var edits = window.Drafts.Draft!.NetFor("test");
        AssertThat(edits.Count).IsEqual(1);
        AssertThat(edits[0].Edit.Table).IsEqual("playerchoice_response");
        AssertThat(edits[0].Edit.After!.T("Header")).IsEqual("Beast Mastery II");

        panel.History(false);
        AssertThat(window.Drafts.Draft!.NetFor("test").Count).IsEqual(0);
    }

    [TestCase]
    public async Task RemovingAResponseStagesItsRewardAndItemDeletes()
    {
        var (window, panel) = Loaded();
        await panel.Load(240);
        Named(panel, "Response_504").EmitSignal(BaseButton.SignalName.Pressed);

        Named(panel, "RemoveResponse").EmitSignal(BaseButton.SignalName.Pressed);

        var edits = window.Drafts.Draft!.NetFor("test");
        AssertThat(edits.Count).IsEqual(3);
        AssertThat(edits.All(e => e.Edit.After == null)).IsTrue();
        AssertThat(edits.Count(e => e.Edit.Table == "playerchoice_response")).IsEqual(1);
        AssertThat(edits.Count(e => e.Edit.Table == "playerchoice_response_reward")).IsEqual(1);
        AssertThat(edits.Count(e => e.Edit.Table == "playerchoice_response_reward_item")).IsEqual(1);
    }

    [TestCase]
    public async Task NewChoiceStagesAnInsertAboveTheMaximum()
    {
        var (window, panel) = Loaded();
        await panel.Load(240);

        await panel.NewItem(null);

        var edits = window.Drafts.Draft!.NetFor("test");
        AssertThat(edits.Count).IsEqual(1);
        var created = edits[0].Edit.After!;
        AssertThat(edits[0].Edit.Table).IsEqual("playerchoice");
        AssertThat(created.I("ChoiceId")).IsEqual(975);
    }

    [TestCase]
    public async Task DuplicateStagesTheChoiceWithItsResponsesAndRewards()
    {
        var (window, panel) = Loaded();
        await panel.Load(240);

        await panel.NewItem(240);

        var edits = window.Drafts.Draft!.NetFor("test");
        AssertThat(edits.Count).IsEqual(6);
        AssertThat(edits.All(e => e.Edit.After != null)).IsTrue();
        AssertThat(edits.Count(e => e.Edit.Table == "playerchoice")).IsEqual(1);
        AssertThat(edits.Count(e => e.Edit.Table == "playerchoice_response")).IsEqual(3);
        AssertThat(edits.Count(e => e.Edit.Table == "playerchoice_response_reward")).IsEqual(1);
        AssertThat(edits.Count(e => e.Edit.Table == "playerchoice_response_reward_item")).IsEqual(1);
        AssertThat(edits.Select(e => e.Edit.After!.I("ChoiceId")).Distinct().Single()).IsEqual(975);
    }
}
