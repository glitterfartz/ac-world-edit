using Godot;
using ACWorldEdit.Assets;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// The content browser: a panel along the bottom with one tab per kind of template (creatures, game
/// objects), a search box and the matching templates. Searches are bounded and read-only, and a newer
/// search replaces an older one still running. A template is picked with a click and opened with a
/// double-click or Enter; what opening does is up to the viewer (<see cref="Activated"/>). Each result shows
/// its model's thumbnail once <see cref="Thumbnails"/> has one, and a blank tile until then.
/// </summary>
public sealed partial class ContentBrowser : PanelContainer
{
    const int AnyType = -1;

    public Func<WorldConnection> Connection { get; set; } = () => WorldConnection.Empty;
    public Func<IClientLookup?> Lookup { get; set; } = () => null;
    public IThumbnailSource? Thumbnails { get; set; }

    /// <summary>Feedback for the status bar.</summary>
    public Action<string> Report { get; set; } = _ => { };

    /// <summary>A template was opened (double-click or Enter): its kind, entry and name.</summary>
    public event Action<SpawnKind, uint, string>? Activated;

    sealed class Tab(SpawnKind kind)
    {
        public SpawnKind Kind { get; } = kind;
        public LineEdit Term = null!;
        public ItemList Results = null!;
        public OptionButton Type = null!;
        public Label Count = null!;
        public IReadOnlyList<TemplateHit> Found = [];
        /// <summary>Display ID by entry for the current results.</summary>
        public IReadOnlyDictionary<uint, uint> Displays = new Dictionary<uint, uint>();
        public int Generation;
        /// <summary>The page's running search; a newer search on the same page cancels it, other pages' searches do not.</summary>
        public CancellationTokenSource? Cancellation;
        public bool Loaded;
    }

    readonly List<Tab> pages = [];
    TabContainer tabs = null!;

    public ContentBrowser()
    {
        Name = "ContentBrowser";
        Visible = false;
        AnchorTop = AnchorBottom = AnchorRight = 1;
        OffsetLeft = 360;
        OffsetRight = -436;
        OffsetTop = -380;
        OffsetBottom = -70;
        Theme = InspectorUi.ExplorerTheme();
        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 6);
        AddChild(root);
        tabs = new TabContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        root.AddChild(tabs);
        AddPage(SpawnKind.Creature, "Creatures", "Search creature templates by name or entry");
        AddPage(SpawnKind.GameObject, "Game objects", "Search game object templates by name or entry");
        tabs.TabChanged += _ => Search(Current);
    }

    Tab Current => pages[tabs.CurrentTab];

    public void Open()
    {
        Visible = true;
        if (!Current.Loaded)
        {
            Search(Current);
        }
    }

    public void Close()
    {
        Visible = false;
        Thumbnails?.CancelWaiting();
    }

    const int IconSize = 96;
    static readonly Texture2D Blank = ImageTexture.CreateFromImage(Image.CreateEmpty(IconSize, IconSize, false, Image.Format.Rgba8));

    void AddPage(SpawnKind kind, string title, string placeholder)
    {
        var page = new Tab(kind);
        pages.Add(page);
        var box = new VBoxContainer { Name = title };
        box.AddThemeConstantOverride("separation", 6);
        tabs.AddChild(box);
        var bar = new HBoxContainer();
        box.AddChild(bar);
        page.Term = new LineEdit { Name = "Search", PlaceholderText = placeholder, SizeFlagsHorizontal = SizeFlags.ExpandFill, ClearButtonEnabled = true };
        page.Term.TextSubmitted += _ => Search(page);
        bar.AddChild(page.Term);
        page.Type = new OptionButton { Name = "TypeFilter", TooltipText = $"Only {title.ToLowerInvariant()} of this type." };
        page.Type.AddItem("Any type", AnyType + 1);
        int named = kind == SpawnKind.Creature ? CreatureTypes.MaxNamed : GameObjectTypes.MaxNamed;
        for (int type = 0; type <= named; type++)
        {
            page.Type.AddItem(kind == SpawnKind.Creature ? CreatureTypes.Name(type) : GameObjectTypes.Name(type), type + 1);
        }

        page.Type.ItemSelected += _ => Search(page);
        bar.AddChild(page.Type);

        var search = new Button { Text = "Search" };
        search.Pressed += () => Search(page);
        bar.AddChild(search);
        page.Count = ClientStyle.Text("", 12, ClientStyle.Faded);
        bar.AddChild(page.Count);
        page.Results = new ItemList
        {
            Name = "Results",
            SizeFlagsVertical = SizeFlags.ExpandFill,
            MaxColumns = 0,
            SameColumnWidth = true,
            FixedColumnWidth = 132,
            IconMode = ItemList.IconModeEnum.Top,
            FixedIconSize = new Vector2I(IconSize, IconSize),
            MaxTextLines = 2,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            AllowReselect = true
        };
        page.Results.ItemActivated += index => Activate(page, (int)index);
        box.AddChild(page.Results);
    }

    void Activate(Tab page, int index)
    {
        if (index >= 0 && index < page.Found.Count)
        {
            Activated?.Invoke(page.Kind, page.Found[index].Entry, page.Found[index].Name);
        }
    }

    /// <summary>Run the page's search on a worker; the newest search for a page wins.</summary>
    void Search(Tab page)
    {
        _ = SearchAsync(page);
    }

    async Task SearchAsync(Tab page)
    {
        int generation = ++page.Generation;
        page.Cancellation?.Cancel();
        page.Cancellation = new();
        var token = page.Cancellation.Token;
        page.Count.Text = "Searching…";
        try
        {
            var db = DBSource.Open(Connection(), Lookup());
            string term = page.Term.Text.Trim();
            term = term[..Math.Min(80, term.Length)];
            int type = page.Type.GetSelectedId() - 1;
            var found = await db.SearchTemplatesAsync(page.Kind, term, type == AnyType ? null : type, token);
            var entries = found.Select(h => h.Entry).ToList();
            var displays = page.Kind == SpawnKind.Creature
                ? (await db.ReadCreatureLooksAsync(entries, token)).ToDictionary(p => p.Key, p => p.Value.DisplayId)
                : (await db.ReadGameObjectLooksAsync(entries, token)).ToDictionary(p => p.Key, p => p.Value.DisplayId);

            if (generation != page.Generation)
            {
                return;
            }

            page.Found = found;
            page.Displays = displays;
            page.Loaded = true;
            ShowResults(page);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception error)
        {
            if (generation == page.Generation)
            {
                page.Count.Text = "";
                Report(DbErrors.Safe(error));
            }
        }
    }

    void ShowResults(Tab page)
    {
        page.Results.Clear();
        Thumbnails?.CancelWaiting();
        int generation = page.Generation;
        foreach (var hit in page.Found)
        {
            string label = string.IsNullOrEmpty(hit.Name) ? $"({hit.Entry})" : hit.Name;
            string kind = page.Kind == SpawnKind.Creature ? CreatureTypes.Name(hit.Type) : GameObjectTypes.Name(hit.Type);
            int row = page.Results.AddItem($"{label}\n{hit.Entry} · {kind}", Thumbnail(page, generation, hit.Entry) ?? Blank);
            page.Results.SetItemTooltip(row, $"{label} ({hit.Entry}) · {kind}. Double-click to open.");
        }

        int count = page.Found.Count;
        page.Count.Text = count == 0 ? "No templates found." : count >= DbLimits.MaxSearch ? $"First {count} shown. Narrow the search." : $"{count} found.";
    }

    /// <summary>
    /// The entry's thumbnail if known; otherwise null, and the row's icon is set when it arrives, provided
    /// the page still shows the same search (rows are found again by entry, not by a saved index).
    /// </summary>
    Texture2D? Thumbnail(Tab page, int generation, uint entry)
    {
        if (Thumbnails == null || !page.Displays.TryGetValue(entry, out uint display))
        {
            return null;
        }

        void Ready(Texture2D texture)
        {
            int row = page.Found.ToList().FindIndex(h => h.Entry == entry);
            if (page.Generation == generation && row >= 0 && row < page.Results.ItemCount)
            {
                page.Results.SetItemIcon(row, texture);
            }
        }

        return page.Kind == SpawnKind.Creature ? Thumbnails.Creature(display, Ready) : Thumbnails.GameObject(display, Ready);
    }

    public override void _ExitTree()
    {
        foreach (var page in pages)
        {
            page.Cancellation?.Cancel();
        }
    }
}
