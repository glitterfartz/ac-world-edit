using Godot;
using ACWorldEdit.Assets;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// Standalone cinematics editor: a search list on the left and the open database's
/// <see cref="CinematicsPanel"/> (<see cref="AcCinematicsPanel"/> or <see cref="TcCinematicsPanel"/>)
/// on the right. The panel owns the tabs, the cards and every draft command.
/// </summary>
public partial class CinematicsEditorWindow : ContentEditorWindow
{
    protected override Vector2 TargetSize => new(1080, 760);

    /// <summary>The panel of the open database's dialect; replaced when the dialect changes.</summary>
    public CinematicsPanel Panel { get; private set; } = null!;
    public Func<IClientLookup?> Lookup { get; set; } = () => null;
    public int ResultCount { get; private set; }
    public uint? CurrentId => Panel?.CurrentId;

    HBoxContainer split = null!;
    LineEdit search = null!;
    ItemList results = null!;
    Button undo = null!, redo = null!;
    int searchGeneration;

    public CinematicsEditorWindow() : base("CinematicsEditor")
    {
        Size = TargetSize;
    }

    public override void _Ready()
    {
        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 6);
        AddChild(root);
        var header = AddHeader(root, "Cinematics");
        undo = ButtonIn(header.Row, "Undo", () => Panel.History(false));
        undo.TooltipText = "Undo the last cinematics change.";
        redo = ButtonIn(header.Row, "Redo", () => Panel.History(true));
        redo.TooltipText = "Redo the last undone cinematics change.";
        var close = ButtonIn(header.Row, "✕", () => Visible = false);
        close.TooltipText = "Close the cinematics window.";

        split = new HBoxContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        root.AddChild(split);
        var left = new VBoxContainer
        {
            CustomMinimumSize = new(230, 0)
        };
        split.AddChild(left);
        search = new LineEdit
        {
            PlaceholderText = "Find a choice, conversation or scene…",
            ClearButtonEnabled = true,
            TooltipText = "Find a player choice, conversation or scene by name, text or ID."
        };
        search.TextSubmitted += text => _ = Search(text);
        left.AddChild(search);
        results = new ItemList
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis
        };
        results.ItemSelected += index => Panel.Open((uint)results.GetItemMetadata((int)index).AsInt64());
        left.AddChild(results);
        split.AddChild(new VSeparator());

        Attach(CinematicsDialects.For(Connection().Dialect));
        Sync();
    }

    /// <summary>Wire a panel to this window's hooks and put it beside the search list.</summary>
    void Attach(CinematicsPanel panel)
    {
        panel.Connection = () => Connection();
        panel.Store = () => Store();
        panel.Drafts = Drafts;
        if (panel is TcCinematicsPanel tc)
        {
            tc.Lookup = () => Lookup();
        }

        panel.Rendered += Sync;
        panel.TabChanged += () =>
        {
            results.Clear();
            ResultCount = 0;
        };
        Panel = panel;
        split.AddChild(panel);
    }

    /// <summary>Swap in the panel for the open database's dialect.</summary>
    void MatchDialect()
    {
        var dialect = Connection().Dialect;
        if (Panel.Dialect == dialect)
        {
            return;
        }

        split.RemoveChild(Panel);
        Panel.QueueFree();
        results.Clear();
        ResultCount = 0;
        Attach(CinematicsDialects.For(dialect));
    }

    void Sync()
    {
        if (undo != null)
        {
            Panel.SyncHistoryButtons(undo, redo);
        }
    }

    /// <summary>Show the editor; 0 opens it without a selection, otherwise opens that player choice.</summary>
    public void Open(uint id)
    {
        Visible = true;
        PlaceOnFirstOpen(0, 70);
        MatchDialect();
        Panel.Open(id);
    }

    public void ShowTab(CinematicsPanel.Tab tab) => Panel.ShowTab(tab);

    /// <summary>Open a SmartAI parameter's conversation or scene on its tab.</summary>
    public void Open(SmartParamKind kind, uint id)
    {
        Visible = true;
        PlaceOnFirstOpen(0, 70);
        MatchDialect();
        Panel.ShowTab(kind == SmartParamKind.Scene ? CinematicsPanel.Tab.Scenes : CinematicsPanel.Tab.Conversations);
        Panel.Open(id);
    }

    public Task Load(uint id) => Panel.Load(id);

    public Task NewItem(uint? copyFrom) => Panel.NewItem(copyFrom);

    /// <summary>Search the open tab and list the matches; stale searches drop.</summary>
    public async Task Search(string term)
    {
        int current = ++searchGeneration;
        Panel.ShowStatus("Searching…");
        try
        {
            MatchDialect();
            var found = await Panel.Search(term);
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
