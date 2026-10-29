using Godot;

namespace ACWorldEdit;

/// <summary>
/// A search box and result list for the templates a new spawn can be made from. Choosing an entry arms
/// the placement tool: the next click on the ground places it. The card only searches and reports the
/// choice; the viewer owns the tool.
/// </summary>
public sealed partial class PlaceCard : VBoxContainer
{
    readonly string noun;
    readonly LineEdit search = new() { PlaceholderText = "Name or ID…", ClearButtonEnabled = true };
    readonly ItemList results = new() { CustomMinimumSize = new(0, 110), AllowReselect = true };
    readonly Label hint = InspectorUi.Text("", 11, true);
    readonly Godot.Timer debounce = new() { OneShot = true, WaitTime = 0.25 };
    readonly List<(uint Id, string Name)> shown = [];
    ulong generation;

    /// <summary>Find templates by name or ID.</summary>
    public Func<string, Task<IReadOnlyList<(uint Id, string Name)>>>? Search { get; set; }

    /// <summary>The user chose a template to place.</summary>
    public event Action<uint, string>? Chosen;

    public PlaceCard(string noun)
    {
        this.noun = noun;
        Name = "PlaceCard";
        search.Name = "PlaceSearch";
        results.Name = "PlaceResults";
        AddChild(InspectorUi.Text($"Place a {noun}", 12, true));
        AddChild(search);
        AddChild(results);
        AddChild(hint);
        AddChild(debounce);
        Idle();
        search.TextChanged += _ => debounce.Start();
        search.TextSubmitted += _ => RunSearch();
        debounce.Timeout += RunSearch;
        results.ItemSelected += index => Choose((int)index);
        results.ItemActivated += index => Choose((int)index);
    }

    void Idle() => hint.Text = $"Search for a {noun}, pick one, then click the ground.";

    /// <summary>Say what is being placed, or go back to the prompt when nothing is.</summary>
    public void SetPlacing(string? name)
    {
        if (name == null)
        {
            Idle();
            results.DeselectAll();
        }
        else
        {
            hint.Text = $"Placing {name}: click the ground. Shift-click keeps placing, Esc stops.";
        }
    }

    async void RunSearch()
    {
        if (Search == null)
        {
            return;
        }

        ulong mine = ++generation;
        string term = search.Text.Trim();
        try
        {
            var found = term.Length == 0 ? [] : await Search(term);
            if (mine != generation)
            {
                return;
            }

            Fill(found);
        }
        catch (Exception)
        {
            if (mine == generation)
            {
                Fill([]);
                hint.Text = "Could not search just now.";
            }
        }
    }

    void Fill(IReadOnlyList<(uint Id, string Name)> found)
    {
        shown.Clear();
        shown.AddRange(found);
        results.Clear();
        foreach (var (id, name) in found)
        {
            results.AddItem($"{name}   ·   {id}");
        }

        if (found.Count == 0 && search.Text.Trim().Length > 0)
        {
            hint.Text = $"No {noun} matches.";
        }
    }

    void Choose(int index)
    {
        if (index >= 0 && index < shown.Count)
        {
            Chosen?.Invoke(shown[index].Id, shown[index].Name);
        }
    }

    /// <summary>Search and wait for the list, as typing and pressing Enter does.</summary>
    public async Task SmokeSearchAsync(string term)
    {
        search.Text = term;
        generation++;
        Fill(Search == null || term.Length == 0 ? [] : await Search(term));
    }

    public int SmokeCount => results.ItemCount;

    public string SmokeHint => hint.Text;

    public string SmokeItem(int index) => results.GetItemText(index);

    /// <summary>Click an entry in the list.</summary>
    public void SmokeChoose(int index)
    {
        results.Select(index);
        results.EmitSignal(ItemList.SignalName.ItemSelected, index);
    }
}
