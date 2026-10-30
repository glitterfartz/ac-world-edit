using Godot;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// Opt-in helpers for --content-browser-smoke. They act through the browser's real controls (the tab,
/// the type filter, the search box and the results list) rather than calling the search directly.
/// </summary>
public partial class ContentBrowser
{
    Tab PageOf(SpawnKind kind) => pages.First(p => p.Kind == kind);

    public IReadOnlyList<TemplateHit> SmokeResults(SpawnKind kind) => PageOf(kind).Found;

    public int SmokeRows(SpawnKind kind) => PageOf(kind).Results.ItemCount;

    /// <summary>Show the kind's tab, pick the type (null for any) and submit the term.</summary>
    public void SmokeSearch(SpawnKind kind, string term, int? type)
    {
        var page = PageOf(kind);
        tabs.CurrentTab = pages.IndexOf(page);
        int index = page.Type.GetItemIndex((type ?? AnyType) + 1);
        page.Type.Select(index);
        page.Type.EmitSignal(OptionButton.SignalName.ItemSelected, index);
        page.Term.Text = term;
        page.Term.EmitSignal(LineEdit.SignalName.TextSubmitted, term);
    }

    /// <summary>Double-click (activate) the result row with this entry; false when it is not listed.</summary>
    public bool SmokeActivate(SpawnKind kind, uint entry)
    {
        var page = PageOf(kind);
        int index = page.Found.ToList().FindIndex(h => h.Entry == entry);
        if (index < 0)
        {
            return false;
        }

        page.Results.Select(index);
        page.Results.EmitSignal(ItemList.SignalName.ItemActivated, index);
        return true;
    }
}
