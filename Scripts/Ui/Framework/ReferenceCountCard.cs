using Godot;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// The "Used by" list of an entity page: how many rows of each other table name this template. Read-only,
/// read once per entry, and refreshed by the page's redraw when the counts arrive.
/// </summary>
public sealed partial class ReferenceCountCard(SpawnKind kind) : VBoxContainer
{
    readonly Label summary = ClientStyle.Text("", 14, ClientStyle.Body);
    readonly VBoxContainer items = new();
    readonly HashSet<uint> requested = [];
    Func<IDBSource>? source;
    Action? changed;
    IReadOnlyList<SpawnImpactItem>? counts;
    string? error;
    uint forEntry;
    bool built;

    /// <summary>Where the counts come from, and who redraws the page when they arrive.</summary>
    public void Bind(Func<IDBSource> open, Action redraw)
    {
        source = open;
        changed = redraw;
    }

    /// <summary>Forget the entry's counts: the entity changed, so the next <see cref="Show"/> reads again.</summary>
    public void Reset()
    {
        counts = null;
        error = null;
        forEntry = 0;
        requested.Clear();
    }

    public void Show(uint entry)
    {
        if (!built)
        {
            built = true;
            summary.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            AddChild(summary);
            AddChild(items);
        }

        if (forEntry != entry)
        {
            forEntry = entry;
            counts = null;
            error = null;
        }

        Load(entry);
        Render();
    }

    /// <summary>The note and the counts, drawn from whatever the last read left (including nothing yet).</summary>
    void Render()
    {
        summary.Text = error != null ? "Could not read what uses this template: " + error
            : counts == null ? "Counting what else names this template…"
            : counts.Count == 0 ? "Nothing else in the database names this template."
            : "These rows name this template. An edit here reaches them:";
        foreach (var child in items.GetChildren())
        {
            child.QueueFree();
        }

        foreach (var item in counts ?? [])
        {
            items.AddChild(ClientStyle.Text($"• {item.Count} {item.Reference.Meaning} ({item.Reference.Table})", 13, ClientStyle.Body));
        }
    }

    async void Load(uint entry)
    {
        if (source == null || counts != null || error != null || !requested.Add(entry))
        {
            return;
        }

        try
        {
            var found = await source().CountTemplateReferencesAsync(kind, entry, default);
            if (forEntry == entry)
            {
                counts = found;
            }
        }
        catch (Exception failure)
        {
            if (forEntry == entry)
            {
                error = DbErrors.Safe(failure);
            }
        }

        requested.Remove(entry);
        if (forEntry == entry)
        {
            Render();
            changed?.Invoke();
        }
    }

    /// <summary>The note and the counts the user sees, for tests and the smoke.</summary>
    public string SmokeText => summary.Text;
    public IReadOnlyList<SpawnImpactItem>? SmokeCounts => counts;
}
