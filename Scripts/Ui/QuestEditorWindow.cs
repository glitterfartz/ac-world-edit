using Godot;
using ACWorldEdit.Assets;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// Standalone quest editor: a quest search list on the left and a <see cref="QuestPanel"/> (the same
/// editor the creature properties window embeds for its quest givers) on the right.
/// </summary>
public partial class QuestEditorWindow : ContentEditorWindow
{
    protected override Vector2 TargetSize => new(1000, 720);
    public QuestPanel Panel { get; } = new();
    public Func<AreaLookup?> Areas { get => Panel.Areas; set => Panel.Areas = value; }
    public Func<uint, string, Task<string>> FlyToEntry { get => Panel.FlyToEntry; set => Panel.FlyToEntry = value; }
    public Action<uint> BehaviorRequested { get => Panel.BehaviorRequested; set => Panel.BehaviorRequested = value; }

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
        Panel.Connection = () => Connection();
        Panel.Store = () => Store();
        Panel.Drafts = Drafts;
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
        var split = new HBoxContainer
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
        split.AddChild(Panel);
        Panel.Rendered += Sync;
        Sync();
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
            var found = await DBSource.Open(Connection()).SearchAsync(SmartParamKind.Quest, search.Text, CancellationToken.None);
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
