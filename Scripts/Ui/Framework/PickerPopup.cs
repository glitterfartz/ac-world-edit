using Godot;

namespace ACWorldEdit;

/// <summary>
/// Modal search picker: type to search (bounded results from the database or a client list), then
/// double-click or Enter to choose. Lives outside editor pages, so re-renders never lose results.
/// </summary>
public partial class PickerPopup : PopupPanel
{
    LineEdit search = null!;
    ItemList results = null!;
    Label status = null!;
    Func<string, Task<IReadOnlyList<(uint Id, string Name)>>> run = null!;
    Action<uint, string> pick = null!;
    int generation;
    uint? current;
    bool singleClick;

    /// <summary>
    /// Open a picker. <paramref name="current"/> is selected and scrolled to when listed;
    /// <paramref name="singleClick"/> chooses on one click, for short fixed lists such as types.
    /// </summary>
    public static PickerPopup Show(Node owner, string title, Func<string, Task<IReadOnlyList<(uint Id, string Name)>>> search, Action<uint, string> picked, string initial = "", uint? current = null, bool singleClick = false)
    {
        var popup = new PickerPopup();
        popup.Build(title);
        popup.run = search;
        popup.pick = picked;
        popup.current = current;
        popup.singleClick = singleClick;
        if (singleClick)
        {
            popup.results.ItemClicked += (index, _, button) =>
            {
                if (button == (long)MouseButton.Left)
                {
                    popup.Choose((int)index);
                }
            };
        }

        owner.AddChild(popup);
        popup.PopupCentered(new Vector2I(460, 480));
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
        AddChild(root);
        root.AddChild(ClientStyle.Text(title, 17, ClientStyle.Header));
        search = new LineEdit
        {
            PlaceholderText = "Type a name or ID…",
            ClearButtonEnabled = true,
            Name = "PickerSearch"
        };
        search.TextChanged += text =>
        {
            _ = Search();
        };
        search.TextSubmitted += _ => Choose(0);
        root.AddChild(search);
        results = new ItemList
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new(0, 340),
            Name = "PickerResults"
        };
        results.ItemActivated += index => Choose((int)index);
        root.AddChild(results);
        status = ClientStyle.Text("", 11, ClientStyle.Faded);
        root.AddChild(status);
    }

    async Task Search()
    {
        int current = ++generation;
        string term = search.Text;
        try
        {
            var found = await run(term);
            if (current != generation || !IsInstanceValid(this))
            {
                return;
            }

            results.Clear();
            foreach (var (id, name) in found)
            {
                results.AddItem($"{name}   ·   {id}");
                results.SetItemMetadata(results.ItemCount - 1, (long)id);
                if (id == current)
                {
                    results.Select(results.ItemCount - 1);
                    results.EnsureCurrentIsVisible();
                }
            }

            status.Text = found.Count == 0 ? "No matches." : $"{found.Count} result(s) · {(singleClick ? "click" : "double-click")} to choose";
            ShownTerm = term;
        }
        catch (Exception error)
        {
            status.Text = Data.DbErrors.Safe(error);
        }
    }

    public int ResultCount => results.ItemCount;
    /// <summary>
    /// Search term whose results are currently listed.
    /// </summary>
    public string? ShownTerm { get; private set; }

    /// <summary>
    /// Choose a result by index (Enter picks the first).
    /// </summary>
    public void Choose(int index)
    {
        if (index < 0 || index >= results.ItemCount)
        {
            return;
        }

        uint id = (uint)results.GetItemMetadata(index).AsInt64();
        string name = results.GetItemText(index).Split("   ·   ")[0];
        Hide();
        pick(id, name);
    }
}
