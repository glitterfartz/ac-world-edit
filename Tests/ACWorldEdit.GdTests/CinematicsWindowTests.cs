using ACWorldEdit;
using ACWorldEdit.Assets;
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

    static ConversationSnapshot Conversation()
    {
        var rows = new RowSet();
        rows.Set(TcTables.ConversationTemplate, TcConversationRules.NewConversation(754, 1790));
        rows.Set(TcTables.ConversationActors, TcConversationRules.NewActor(754, 0).With("CreatureId", 99426u));
        rows.Set(TcTables.ConversationActors, TcConversationRules.NewActor(754, 1).With("ConversationActorGuid", 280710UL));
        rows.Set(TcTables.ConversationActors, TcConversationRules.NewActor(754, 2).With("CreatureId", 99857u));
        rows.Set(TcTables.ConversationLineTemplate, TcConversationRules.NewLineOverride(1790).With("ActorIdx", 0u));
        rows.Set(TcTables.ConversationLineTemplate, TcConversationRules.NewLineOverride(1791).With("ActorIdx", 1u));
        rows.Set(TcTables.ConversationLineTemplate, TcConversationRules.NewLineOverride(1792).With("ActorIdx", 2u).With("ChatType", 1u));
        var chain = new List<ConversationLineView>
        {
            new(1790, 1791, 100, 0, 0, "First line.", rows.Get(TcTables.ConversationLineTemplate, "1790")),
            new(1791, 1792, 101, 0, 500, "Second line.", rows.Get(TcTables.ConversationLineTemplate, "1791")),
            new(1792, 1793, 102, 0, 0, "Third line.", rows.Get(TcTables.ConversationLineTemplate, "1792")),
            new(1793, 0, 103, 0, 0, "Fourth line.", null)
        };
        return new(rows, new ContentNames(null), "test", 1100, chain, new Dictionary<uint, IReadOnlyList<Row>>(), true);
    }

    static SceneSnapshot Scene()
    {
        var rows = new RowSet();
        rows.Set(TcTables.SceneTemplate, TcSceneRules.NewScene(35).With("Flags", 25u).With("ScriptPackageID", 186u));
        var package = new ClientScenePackage(186, "Test scene",
            [new ClientSceneStep(1, "Open", "play open", 0), new ClientSceneStep(2, "Close", "play close", 1)]);
        return new(rows, "test", 575, package);
    }

    static (CinematicsEditorWindow Window, TcCinematicsPanel Panel) SceneLoaded()
    {
        var window = Window();
        window.Open(0);
        var panel = (TcCinematicsPanel)window.Panel;
        panel.SceneReader = (_, _) => Task.FromResult(Scene());
        window.ShowTab(CinematicsPanel.Tab.Scenes);
        return (window, panel);
    }

    static (CinematicsEditorWindow Window, TcCinematicsPanel Panel) ConversationLoaded()
    {
        var window = Window();
        window.Open(0);
        var panel = (TcCinematicsPanel)window.Panel;
        panel.ConversationReader = (_, _) => Task.FromResult(Conversation());
        window.ShowTab(CinematicsPanel.Tab.Conversations);
        return (window, panel);
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

    /// <summary>Run process frames until the condition holds or a bound is reached (an async command finishing).</summary>
    static async Task WaitFor(Func<bool> condition)
    {
        for (int i = 0; i < 120 && !condition(); i++)
        {
            await Tree.ToSignal(Tree, SceneTree.SignalName.ProcessFrame);
        }
    }

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
    public async Task ConversationTabRendersTheTemplateActorsAndLineStrip()
    {
        var (_, panel) = ConversationLoaded();
        await panel.Load(754);

        AssertThat(panel.CurrentTab).IsEqual(CinematicsPanel.Tab.Conversations);
        AssertThat(panel.CurrentId!.Value).IsEqual(754u);
        AssertThat(panel.Conversation).IsNotNull();
        AssertThat(panel.Conversation!.ActorCount).IsEqual(3);
        AssertThat(panel.Conversation.LineCount).IsEqual(4);
        AssertString(panel.Conversation.LineText(0)).Contains("First line.");
        AssertString(panel.Conversation.LineText(2)).Contains("Third line.");
    }

    [TestCase]
    public async Task AddingAnActorStagesOneInsertAtTheNextIndex()
    {
        var (window, panel) = ConversationLoaded();
        await panel.Load(754);

        Named(panel, "AddActor").EmitSignal(BaseButton.SignalName.Pressed);

        var edits = window.Drafts.Draft!.NetFor("test");
        AssertThat(edits.Count).IsEqual(1);
        AssertThat(edits[0].Edit.Table).IsEqual("conversation_actors");
        AssertThat(edits[0].Edit.After!.U("Idx")).IsEqual(3u);
    }

    [TestCase]
    public async Task TogglingAConversationFlagStagesOneUpdate()
    {
        var (window, panel) = ConversationLoaded();
        await panel.Load(754);

        var flags = (CheckBox)panel.FindChild("Flag_MultipleConversationType", true, false)!;
        flags.ButtonPressed = true;
        flags.EmitSignal(BaseButton.SignalName.Toggled, true);

        var edits = window.Drafts.Draft!.NetFor("test");
        AssertThat(edits.Count).IsEqual(1);
        AssertThat(edits[0].Edit.Table).IsEqual("conversation_template");
        AssertThat(edits[0].Edit.After!.U("Flags")).IsEqual(1u);
    }

    [TestCase]
    public async Task AddingALineOverrideStagesOneInsertAndRemovingItOneDelete()
    {
        var (window, panel) = ConversationLoaded();
        await panel.Load(754);

        panel.Conversation!.SelectLine(1793);
        Named(panel, "AddOverride").EmitSignal(BaseButton.SignalName.Pressed);

        var added = window.Drafts.Draft!.NetFor("test");
        AssertThat(added.Count).IsEqual(1);
        AssertThat(added[0].Edit.Table).IsEqual("conversation_line_template");
        AssertThat(added[0].Edit.After!.U("Id")).IsEqual(1793u);

        panel.History(false);
        panel.Conversation!.SelectLine(1790);
        Named(panel, "RemoveOverride").EmitSignal(BaseButton.SignalName.Pressed);

        var removed = window.Drafts.Draft!.NetFor("test");
        AssertThat(removed.Count).IsEqual(1);
        AssertThat(removed[0].Edit.Table).IsEqual("conversation_line_template");
        AssertThat(removed[0].Edit.After).IsNull();
    }

    [TestCase]
    public async Task PlayStepsThroughTheChainInOrder()
    {
        var (_, panel) = ConversationLoaded();
        await panel.Load(754);

        Named(panel, "Play").EmitSignal(BaseButton.SignalName.Pressed);
        AssertThat(panel.Conversation!.SelectedLineId!.Value).IsEqual(1790u);
        AssertString(panel.Conversation.PlayingText).Contains("1 of 4");

        Named(panel, "Play").EmitSignal(BaseButton.SignalName.Pressed);
        AssertThat(panel.Conversation.SelectedLineId!.Value).IsEqual(1791u);
        AssertString(panel.Conversation.PlayingText).Contains("2 of 4");
    }

    [TestCase]
    public async Task SceneTabRendersTheTemplateAndItsScriptSteps()
    {
        var (_, panel) = SceneLoaded();
        await panel.Load(35);

        AssertThat(panel.CurrentTab).IsEqual(CinematicsPanel.Tab.Scenes);
        AssertThat(panel.CurrentId!.Value).IsEqual(35u);
        AssertThat(panel.Scene).IsNotNull();
        AssertThat(panel.Scene!.StepCount).IsEqual(2);
        AssertString(panel.Scene.PackageName).Contains("Test scene");
        AssertString(panel.Scene.StepText(0)).Contains("open");
    }

    [TestCase]
    public async Task EditingASceneFlagStagesOneUpdate()
    {
        var (window, panel) = SceneLoaded();
        await panel.Load(35);

        AssertThat(panel.CurrentTab).IsEqual(CinematicsPanel.Tab.Scenes);
        AssertThat(panel.Scene).IsNotNull();
        AssertThat(panel.Scene!.StepCount).IsEqual(2);
        AssertThat(panel.FindChild("SceneFlag_8", true, false)).IsNotNull();
        panel.Scene!.SetFlag(8, false);

        var edits = window.Drafts.Draft!.NetFor("test");
        AssertThat(edits.Count).IsEqual(1);
        AssertThat(edits[0].Edit.Table).IsEqual("scene_template");
        AssertThat(edits[0].Edit.After!.U("Flags")).IsEqual(17u);
    }

    [TestCase]
    public async Task NewSceneStagesAnInsertAboveTheMaximum()
    {
        var (window, panel) = SceneLoaded();
        await panel.Load(35);

        Named(panel, "NewScene").EmitSignal(BaseButton.SignalName.Pressed);
        await WaitFor(() => window.Drafts.Draft!.NetFor("test").Any(edit => edit.Edit.Table == "scene_template"));

        var edits = window.Drafts.Draft!.NetFor("test");
        AssertThat(edits.Any(edit => edit.Edit.Table == "scene_template" && edit.Edit.After!.U("SceneId") == 576u)).IsTrue();
    }

    [TestCase]
    public async Task NewChoiceWorksFromTheEmptyTab()
    {
        var (window, panel) = Loaded();
        window.ShowTab(CinematicsPanel.Tab.PlayerChoices);

        var create = Named(panel, "NewChoice");
        AssertThat(create.Disabled).IsFalse();
        create.EmitSignal(BaseButton.SignalName.Pressed);
        await WaitFor(() => window.Drafts.Draft!.NetFor("test").Any(edit => edit.Edit.Table == "playerchoice"));

        AssertThat(window.Drafts.Draft!.NetFor("test").Any(edit => edit.Edit.Table == "playerchoice" && edit.Edit.After!.I("ChoiceId") == 975)).IsTrue();
    }

    [TestCase]
    public async Task NewConversationWorksFromTheEmptyTab()
    {
        var (window, panel) = ConversationLoaded();

        Named(panel, "NewConversation").EmitSignal(BaseButton.SignalName.Pressed);
        await WaitFor(() => window.Drafts.Draft!.NetFor("test").Any(edit => edit.Edit.Table == "conversation_template"));

        var created = window.Drafts.Draft!.NetFor("test").Single(edit => edit.Edit.Table == "conversation_template");
        AssertThat(created.Edit.After!.U("Id")).IsEqual(1101u);
    }

    [TestCase]
    public async Task NewSceneWorksFromTheEmptyTab()
    {
        var (window, panel) = SceneLoaded();

        Named(panel, "NewScene").EmitSignal(BaseButton.SignalName.Pressed);
        await WaitFor(() => window.Drafts.Draft!.NetFor("test").Any(edit => edit.Edit.Table == "scene_template"));

        AssertThat(window.Drafts.Draft!.NetFor("test").Single(edit => edit.Edit.Table == "scene_template").Edit.After!.U("SceneId")).IsEqual(576u);
    }

    [TestCase]
    public async Task ASceneWarningIsListedUnderItsFields()
    {
        var (_, panel) = SceneLoaded();
        panel.SceneReader = (_, _) => Task.FromResult(Scene() with
        {
            Rows = Rows((TcTables.SceneTemplate, TcSceneRules.NewScene(35).With("ScriptPackageID", 0u)))
        });
        await panel.Load(35);

        AssertThat(panel.FindChild("Issue_0", true, false)).IsNotNull();
    }

    static RowSet Rows(params (TableSchema Table, Row Row)[] rows)
    {
        var set = new RowSet();
        foreach (var (table, row) in rows)
        {
            set.Set(table, row);
        }

        return set;
    }

    [TestCase]
    public async Task EditingTheQuestionStagesOneUpdate()
    {
        var (window, panel) = Loaded();
        await panel.Load(240);

        var question = (LineEdit)panel.FindChild("Question", true, false)!;
        question.Text = "Which weapon shall we forge?";
        question.EmitSignal(LineEdit.SignalName.TextSubmitted, "Which weapon shall we forge?");

        var edits = window.Drafts.Draft!.NetFor("test");
        AssertThat(edits.Count).IsEqual(1);
        AssertThat(edits[0].Edit.Table).IsEqual("playerchoice");
        AssertThat(edits[0].Edit.After!.T("Question")).IsEqual("Which weapon shall we forge?");
    }

    [TestCase]
    public async Task ARewardConditionIsShownOnItsAnswer()
    {
        var (_, panel) = Loaded();
        ((TcCinematicsPanel)panel).Reader = (_, _) => Task.FromResult(SnapshotWithACondition());
        await panel.Load(240);
        Named(panel, "Response_504").EmitSignal(BaseButton.SignalName.Pressed);

        AssertThat(panel.FindChild("Condition_504_0", true, false)).IsNotNull();
    }

    static PlayerChoiceSnapshot SnapshotWithACondition()
    {
        var rows = Snapshot().Rows.Clone();
        var condition = new Row(TcTables.Conditions.Columns.Select(column => new KeyValuePair<string, string>(column.Name, "0")))
            .With("SourceTypeOrReferenceId", 36)
            .With("SourceGroup", 240u)
            .With("SourceEntry", 504)
            .With("ConditionTypeOrReference", 23)
            .With("ConditionValue1", 1519L);
        rows.Set(TcTables.Conditions, condition);
        return new PlayerChoiceSnapshot(rows, new ContentNames(null), "test", 974);
    }

    [TestCase]
    public async Task AConversationWarningIsListedUnderItsCards()
    {
        var (window, panel) = ConversationLoaded();
        var rows = Conversation().Rows.Clone();
        rows.Set(TcTables.ConversationActors, TcConversationRules.NewActor(754, 3).With("CreatureId", 55555u));
        var snapshot = Conversation() with { Rows = rows };
        panel.ConversationReader = (_, _) => Task.FromResult(snapshot);
        await panel.Load(754);

        AssertThat(panel.FindChild("Issue_0", true, false)).IsNotNull();
        AssertThat(window.Drafts.Draft!.NetFor("test")).IsEmpty();
    }

    [TestCase]
    public async Task TheScenePackageNameIsShownReadOnly()
    {
        var (_, panel) = SceneLoaded();
        await panel.Load(35);

        AssertThat(panel.FindChild("PackageName", true, false)).IsInstanceOf<Label>();
        AssertThat(panel.FindChild("package name", true, false)).IsNull();
    }

    [TestCase]
    public async Task AnActorCreatureTypedAfterTheReadStillSaves()
    {
        var (window, panel) = ConversationLoaded();
        panel.ConversationReader = (_, _) => Task.FromResult(Conversation());
        await panel.Load(754);

        var creature = (LineEdit)panel.FindChild("ActorCreature_0", true, false)!;
        creature.Text = "55555";
        creature.EmitSignal(LineEdit.SignalName.TextSubmitted, "55555");

        var edits = window.Drafts.Draft!.NetFor("test");
        AssertThat(edits.Count).IsEqual(1);
        AssertThat(edits[0].Edit.After!.U("CreatureId")).IsEqual(55555u);
    }

    [TestCase]
    public async Task ARewardQuestTypedAfterTheReadStillSaves()
    {
        var (window, panel) = Loaded();
        await panel.Load(240);
        Named(panel, "Response_504").EmitSignal(BaseButton.SignalName.Pressed);

        var quest = (LineEdit)panel.FindChild("RewardQuest_504", true, false)!;
        quest.Text = "12345";
        quest.EmitSignal(LineEdit.SignalName.TextSubmitted, "12345");

        var edits = window.Drafts.Draft!.NetFor("test");
        AssertThat(edits.Count).IsEqual(1);
        AssertThat(edits[0].Edit.After!.U("RewardQuestID")).IsEqual(12345u);
    }

    [TestCase]
    public async Task ASmartAiLinkOpensTheMatchingTabAndRow()
    {
        var window = Window();
        window.Open(0);
        var panel = (TcCinematicsPanel)window.Panel;
        panel.ConversationReader = (_, _) => Task.FromResult(Conversation());
        panel.SceneReader = (_, _) => Task.FromResult(Scene());

        window.Open(SmartParamKind.Scene, 35);
        await WaitFor(() => panel.CurrentId == 35);
        AssertThat(panel.CurrentTab).IsEqual(CinematicsPanel.Tab.Scenes);

        window.Open(SmartParamKind.Conversation, 754);
        await WaitFor(() => panel.CurrentId == 754);
        AssertThat(panel.CurrentTab).IsEqual(CinematicsPanel.Tab.Conversations);
    }

    [TestCase]
    public async Task DuplicatingAConversationStagesItsTemplateAndActors()
    {
        var (window, panel) = ConversationLoaded();
        await panel.Load(754);

        await panel.NewItem(754);

        var edits = window.Drafts.Draft!.NetFor("test");
        AssertThat(edits.Count).IsEqual(4);
        AssertThat(edits.Count(edit => edit.Edit.Table == "conversation_template")).IsEqual(1);
        AssertThat(edits.Count(edit => edit.Edit.Table == "conversation_actors")).IsEqual(3);
        AssertThat(edits.Single(edit => edit.Edit.Table == "conversation_template").Edit.After!.U("Id")).IsEqual(1101u);
        AssertThat(edits.Where(edit => edit.Edit.Table == "conversation_actors").Select(edit => edit.Edit.After!.U("ConversationId")).Distinct().Single()).IsEqual(1101u);
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
