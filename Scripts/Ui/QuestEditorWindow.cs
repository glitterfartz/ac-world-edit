using Godot;
using ACWorldEdit.Assets;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// Standalone quest editor: a quest search list on the left and the open database's
/// <see cref="QuestPanel"/> (<see cref="AcQuestPanel"/> or <see cref="TcQuestPanel"/>) on the right.
/// </summary>
public partial class QuestEditorWindow : ContentEditorWindow
{
    protected override Vector2 TargetSize => new(1000, 720);
    /// <summary>The panel of the open database's dialect; replaced when the dialect changes.</summary>
    public QuestPanel Panel { get; private set; } = new AcQuestPanel();
    public Func<AreaLookup?> Areas { get; set; } = () => null;
    public Func<IClientLookup?> Lookup { get; set; } = () => null;
    public Func<uint, string, Task<string>> FlyToEntry { get; set; } = (_, _) => Task.FromResult("");
    public Action<uint> BehaviorRequested { get; set; } = _ => { };
    HBoxContainer split = null!;

    int searchGeneration;
    LineEdit search = null!;
    ItemList results = null!;
    Button undo = null!, redo = null!, duplicate = null!;
    public int ResultCount { get; private set; }
    public uint? QuestId => Panel.QuestId;
    public bool IsLoading => Panel.IsLoading;

    public QuestEditorWindow() : base("QuestEditor")
    {
        Size = TargetSize;
    }

    public override void _Ready()
    {
        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 6);
        AddChild(root);
        var header = new HBoxContainer();
        header.GuiInput += DragHeader;
        root.AddChild(header);
        var heading = ClientStyle.Text("Quest Editor", 19, ClientStyle.Header);
        heading.MouseFilter = MouseFilterEnum.Ignore;
        heading.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        header.AddChild(heading);
        undo = ButtonIn(header, "Undo", () => Panel.History(false));
        redo = ButtonIn(header, "Redo", () => Panel.History(true));
        ButtonIn(header, "+ New quest", () => _ = Panel.NewQuest(null));
        duplicate = ButtonIn(header, "Duplicate", () => _ = Panel.NewQuest(Panel.QuestId));
        duplicate.TooltipText = "Copy this quest (texts, objectives, rewards, chain settings) under a new ID.";
        ButtonIn(header, "✕", () => Visible = false);
        split = new HBoxContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        root.AddChild(split);
        var left = new VBoxContainer
        {
            CustomMinimumSize = new(220, 0)
        };
        split.AddChild(left);
        search = new LineEdit
        {
            PlaceholderText = "Find quest by title or ID…",
            ClearButtonEnabled = true
        };
        search.TextSubmitted += text =>
        {
            _ = Search();
        };
        left.AddChild(search);
        results = new ItemList
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis
        };
        results.ItemSelected += index => _ = Panel.Load((uint)results.GetItemMetadata((int)index).AsInt64());
        left.AddChild(results);
        split.AddChild(new VSeparator());
        Attach(Panel);
        Sync();
    }

    /// <summary>Wire a panel to this window's hooks and put it beside the search list.</summary>
    void Attach(QuestPanel panel)
    {
        panel.Connection = () => Connection();
        panel.Store = () => Store();
        panel.Drafts = Drafts;
        panel.Areas = () => Areas();
        panel.Lookup = () => Lookup();
        panel.FlyToEntry = (entry, name) => FlyToEntry(entry, name);
        panel.BehaviorRequested = entry => BehaviorRequested(entry);
        panel.Rendered += Sync;
        Panel = panel;
        split.AddChild(panel);
    }

    /// <summary>Swap in the panel for the open database's dialect.</summary>
    void MatchDialect()
    {
        var dialect = Connection().Dialect;
        if (dialect == WorldDialect.TrinityCore == Panel is TcQuestPanel)
        {
            return;
        }

        split.RemoveChild(Panel);
        Panel.QueueFree();
        results.Clear();
        ResultCount = 0;
        Attach(dialect == WorldDialect.TrinityCore ? new TcQuestPanel() : new AcQuestPanel());
    }

    void Sync()
    {
        duplicate.Disabled = !Panel.HasQuest;
        Panel.SyncHistoryButtons(undo, redo);
    }

    /// <summary>
    /// Show the editor; 0 opens it without a quest (toolbar), otherwise loads that quest.
    /// </summary>
    public void Open(uint quest)
    {
        Visible = true;
        PlaceOnFirstOpen(0, 80);
        MatchDialect();
        Panel.Open(quest);
    }

    public Task Load(uint quest) => Panel.Load(quest);

    public Task NewQuest(uint? copyFrom) => Panel.NewQuest(copyFrom);

    public void ShowTab(QuestPanel.Tab value) => Panel.ShowTab(value);

    public async Task Search()
    {
        int current = ++searchGeneration;
        Panel.ShowStatus("Searching…");
        try
        {
            MatchDialect();
            var found = await DBSource.Open(Connection(), Lookup()).SearchAsync(SmartParamKind.Quest, search.Text, CancellationToken.None);
            if (current != searchGeneration)
            {
                return;
            }

            results.Clear();
            foreach (var (id, name) in found)
            {
                results.AddItem($"{(name.Length > 0 ? name : "(untitled)")}  ·  {id}");
                results.SetItemMetadata(results.ItemCount - 1, (long)id);
            }

            ResultCount = found.Count;
            Panel.ShowStatus(found.Count >= DbLimits.MaxSearch ? "Showing the first matches; refine the search for more." : "");
        }
        catch (Exception error)
        {
            Panel.ShowStatus(DbErrors.Safe(error));
        }
    }
}
