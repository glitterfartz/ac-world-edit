using Godot;
using ACWorldEdit.Assets;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// One result of a <see cref="RecordPicker"/>: an ID, the text shown, optional detail, a client icon
/// path or FileDataID, the WoW-style tooltip, an optional payload and an optional model thumbnail
/// loader (creature and GameObject previews).
/// </summary>
public sealed record PickerRecord(
    uint Id,
    string Label,
    string Detail = "",
    string Icon = "",
    Tooltip? Tip = null,
    object? Value = null,
    Func<Action<Texture2D>, Texture2D?>? Thumbnail = null);

/// <summary>One tab of a <see cref="RecordPicker"/> and the search it runs (creatures vs objects).</summary>
public sealed record PickerTab(string Label, Func<string, Task<IReadOnlyList<PickerRecord>>> Search);

/// <summary>
/// The shared modal search window for references: a client icon or model thumbnail on each row and
/// the game tooltip on hover. It can show tabs of different searches, pages long result sets, and
/// takes a search function, so every editor and the vendor item search use one implementation.
/// </summary>
public partial class RecordPicker : PopupPanel
{
    const int PageSize = 40;

    readonly List<(PickerRecord Record, TooltipButton Button)> rows = [];
    readonly ClientIcons icons;
    readonly List<PickerTab> tabs = [];
    IReadOnlyList<PickerRecord> found = [];
    LineEdit search = null!;
    VBoxContainer list = null!;
    HBoxContainer tabRow = null!;
    Label status = null!, pageLabel = null!;
    Button previous = null!, next = null!;
    Func<string, Task<IReadOnlyList<PickerRecord>>> run = null!;
    Action<PickerRecord> pick = null!;
    int generation, activeTab, page;
    uint? current;

    public RecordPicker() => icons = new(() => Store());

    /// <summary>The asset store icons are read from (set by <see cref="Show"/>).</summary>
    public Func<IClientFiles?> Store { get; private set; } = () => null;

    /// <summary>A rich picker with one search.</summary>
    public static RecordPicker Show(Node owner, Func<IClientFiles?> store, string title,
        Func<string, Task<IReadOnlyList<PickerRecord>>> search, Action<PickerRecord> picked,
        string initial = "", uint? current = null) =>
        Open(owner, store, title, [new PickerTab(title, search)], picked, initial, current);

    /// <summary>A rich picker with tabs, such as creatures and GameObject quest givers.</summary>
    public static RecordPicker Show(Node owner, Func<IClientFiles?> store, string title,
        IReadOnlyList<PickerTab> tabs, Action<PickerRecord> picked,
        string initial = "", uint? current = null) =>
        Open(owner, store, title, tabs, picked, initial, current);

    /// <summary>
    /// A plain ID/name picker. <paramref name="iconFor"/> and <paramref name="tipFor"/> add the client
    /// icon and game tooltip where the caller has them; leave them null for a plain list.
    /// </summary>
    public static RecordPicker Show(Node owner, string title,
        Func<string, Task<IReadOnlyList<(uint Id, string Name)>>> search, Action<uint, string> picked,
        string initial = "", uint? current = null, Func<IClientFiles?>? store = null,
        Func<(uint Id, string Name), string?>? iconFor = null, Func<(uint Id, string Name), Tooltip?>? tipFor = null) =>
        Open(owner, store ?? (() => null), title,
        [
            new PickerTab(title, async term =>
            {
                var hits = await search(term);
                return hits.Select(hit => new PickerRecord(hit.Id, hit.Name, Icon: iconFor?.Invoke(hit) ?? "", Tip: tipFor?.Invoke(hit))).ToList();
            })
        ], record => picked(record.Id, record.Label), initial, current);

    static RecordPicker Open(Node owner, Func<IClientFiles?> store, string title,
        IReadOnlyList<PickerTab> tabs, Action<PickerRecord> picked, string initial, uint? current)
    {
        var popup = new RecordPicker { Store = store };
        popup.tabs.AddRange(tabs.Count > 0 ? tabs : [new PickerTab(title, _ => Task.FromResult<IReadOnlyList<PickerRecord>>([]))]);
        popup.Build(title);
        popup.run = popup.tabs[0].Search;
        popup.pick = picked;
        popup.current = current;
        owner.AddChild(popup);
        popup.PopupCentered(new Vector2I(560, 560));
        popup.search.Text = initial;
        popup.search.GrabFocus();
        _ = popup.Search();
        popup.PopupHide += popup.QueueFree;
        return popup;
    }

    void Build(string title)
    {
        Theme = ClientStyle.Theme();
        AddThemeStyleboxOverride("panel", ClientStyle.Panel());
        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 6);
        AddChild(root);
        root.AddChild(ClientStyle.Text(title, 17, ClientStyle.Header));
        tabRow = new HBoxContainer { Name = "RecordTabs", Visible = tabs.Count > 1 };
        root.AddChild(tabRow);
        for (int i = 0; i < tabs.Count; i++)
        {
            int index = i;
            var button = new Button
            {
                Text = tabs[i].Label,
                ToggleMode = true,
                ButtonPressed = i == 0,
                Name = "RecordTab" + i
            };
            button.Pressed += () => SelectTab(index);
            tabRow.AddChild(button);
        }

        search = new LineEdit
        {
            PlaceholderText = "Type a name or ID…",
            ClearButtonEnabled = true,
            Name = "RecordSearch"
        };
        search.TextChanged += text =>
        {
            _ = Search();
        };
        search.TextSubmitted += _ => ChooseFirst();
        root.AddChild(search);
        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new(0, 380),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
        };
        root.AddChild(scroll);
        list = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            Name = "RecordList"
        };
        list.AddThemeConstantOverride("separation", 2);
        scroll.AddChild(list);
        var nav = new HBoxContainer { Name = "RecordPages" };
        root.AddChild(nav);
        previous = new Button { Text = "◂ Prev", Name = "RecordPrev" };
        previous.Pressed += () => ShowPage(page - 1);
        nav.AddChild(previous);
        pageLabel = ClientStyle.Tag("");
        pageLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        pageLabel.HorizontalAlignment = HorizontalAlignment.Center;
        nav.AddChild(pageLabel);
        next = new Button { Text = "Next ▸", Name = "RecordNext" };
        next.Pressed += () => ShowPage(page + 1);
        nav.AddChild(next);
        status = ClientStyle.Text("", 11, ClientStyle.Faded);
        root.AddChild(status);
    }

    void SelectTab(int index)
    {
        if (index < 0 || index >= tabs.Count)
        {
            return;
        }

        activeTab = index;
        run = tabs[index].Search;
        page = 0;
        for (int i = 0; i < tabRow.GetChildCount(); i++)
        {
            ((Button)tabRow.GetChild(i)).ButtonPressed = i == index;
        }

        _ = Search();
    }

    async Task Search()
    {
        int current = ++generation;
        string term = search.Text;
        try
        {
            var results = await run(term);
            if (current != generation || !IsInstanceValid(this))
            {
                return;
            }

            found = results;
            page = 0;
            if (this.current is uint selected)
            {
                int index = results.ToList().FindIndex(record => record.Id == selected);
                if (index >= 0)
                {
                    page = index / PageSize;
                }
            }

            RenderPage();
            ShownTerm = term;
        }
        catch (Exception error)
        {
            found = [];
            page = 0;
            ClearRows();
            previous.Disabled = true;
            next.Disabled = true;
            pageLabel.Text = "";
            status.Text = Data.DbErrors.Safe(error);
        }
    }

    void RenderPage()
    {
        ClearRows();
        int pages = PageCount;
        page = Math.Clamp(page, 0, pages - 1);
        foreach (var record in found.Skip(page * PageSize).Take(PageSize))
        {
            AddRow(record);
        }

        previous.Disabled = page <= 0;
        next.Disabled = page >= pages - 1;
        pageLabel.Text = found.Count == 0 ? "" : $"Page {page + 1} of {pages} · {found.Count} result(s)";
        status.Text = found.Count == 0 ? "No matches." : "Click a result to choose.";
    }

    void ClearRows()
    {
        foreach (var (_, button) in rows)
        {
            list.RemoveChild(button);
            button.QueueFree();
        }

        rows.Clear();
    }

    void AddRow(PickerRecord record)
    {
        var button = new TooltipButton
        {
            Text = record.Detail.Length > 0 ? $"{record.Label}   ·   {record.Detail}" : record.Label,
            Alignment = HorizontalAlignment.Left,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            TooltipText = record.Label,
            Source = record.Tip == null ? null : () => record.Tip
        };
        if (record.Thumbnail != null)
        {
            void Ready(Texture2D texture)
            {
                if (GodotObject.IsInstanceValid(button) && button.IsInsideTree())
                {
                    button.Icon = texture;
                }
            }

            if (record.Thumbnail(Ready) is { } known)
            {
                button.Icon = known;
            }

            button.ExpandIcon = true;
            button.AddThemeConstantOverride("icon_max_width", 96);
        }
        else if (record.Icon.Length > 0)
        {
            button.Icon = Icon(record.Icon);
            button.ExpandIcon = true;
            button.AddThemeConstantOverride("icon_max_width", 36);
        }

        button.Pressed += () => Choose(record);
        list.AddChild(button);
        rows.Add((record, button));
        if (record.Id == current)
        {
            button.AddThemeColorOverride("font_color", ClientStyle.Header);
        }
    }

    Texture2D? Icon(string path) => icons.Get(path) ?? icons.Get(ClientStyle.MissingIcon);

    /// <summary>When client icons finish decoding, put them on the rows already shown.</summary>
    public override void _Process(double delta)
    {
        if (icons.Poll())
        {
            foreach (var (record, button) in rows)
            {
                if (record.Thumbnail == null && record.Icon.Length > 0)
                {
                    button.Icon = Icon(record.Icon);
                }
            }
        }
    }

    /// <summary>Total results in the current tab, across every page.</summary>
    public int ResultCount => found.Count;

    /// <summary>Pages in the current result set (at least one).</summary>
    public int PageCount => Math.Max(1, (found.Count + PageSize - 1) / PageSize);

    /// <summary>Zero-based page now shown.</summary>
    public int CurrentPage => page;

    /// <summary>Search term whose results are currently listed.</summary>
    public string? ShownTerm { get; private set; }

    /// <summary>Show a page of the current results (pagination buttons and tests).</summary>
    public void ShowPage(int index)
    {
        page = index;
        RenderPage();
    }

    /// <summary>Choose the nth visible result, for smoke checks and tests.</summary>
    public void ChooseResult(int index)
    {
        if (index >= 0 && index < rows.Count)
        {
            Choose(rows[index].Record);
        }
    }

    /// <summary>Run a search as if the term were typed, for smoke checks and tests.</summary>
    public void SearchFor(string term)
    {
        search.Text = term;
        _ = Search();
    }

    void ChooseFirst()
    {
        if (rows.Count > 0)
        {
            Choose(rows[0].Record);
        }
    }

    void Choose(PickerRecord record)
    {
        Hide();
        pick(record);
    }
}
