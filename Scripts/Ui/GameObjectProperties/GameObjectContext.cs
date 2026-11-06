using System.Globalization;
using Godot;
using ACWorldEdit.Assets;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// State shared by the GameObject properties pages: the template being edited, the database
/// snapshot merged with the local draft, the client catalog and the commit path. Every page
/// draws from this context; a commit raises <see cref="Changed"/> and the window redraws.
/// The window edits the template, shared by every spawn of it; a spawn's own settings stay in
/// the Phase 11 inspector.
/// </summary>
public sealed class GameObjectContext : IEntityContext
{
    public Func<WorldConnection> Connection { get; set; } = () => WorldConnection.Empty;
    public Func<IClientFiles?> Store { get; set; } = () => null;
    public Func<IClientLookup?> Lookup { get; set; } = () => null;
    public Func<IThumbnailSource?> Thumbnails { get; set; } = () => null;
    public ContentDrafts Drafts { get; }
    public ClientIcons Icons { get; }
    /// <summary>Teleport to one spawn of this template (its GUID, map and server position); returns a status line.</summary>
    public Func<ulong, uint, double, double, double, Task<string>> FlyToSpawn { get; set; } = (_, _, _, _, _) => Task.FromResult("");

    /// <summary>The template entry shown, or null when none is selected.</summary>
    public uint? Entry { get; private set; }
    /// <summary>What the opener called it, until the row's own name loads.</summary>
    public string RequestedName { get; private set; } = "";
    /// <summary>The spawn the window followed, when it was opened from one.</summary>
    public GameObjectSpawn? TargetSpawn { get; private set; }
    /// <summary>True while the window follows the world selection; entry-opened windows do not.</summary>
    public bool FollowsSelection { get; private set; }

    public GameObjectSnapshot? Snapshot { get; private set; }
    public InteractionCatalog? Catalog { get; private set; }
    public RowSet Effective { get; private set; } = new();
    public string SourceKey { get; private set; } = "";
    public string Status { get; set; } = "";

    public WorldDialect Dialect => Connection().Dialect;
    public IWorldSchema Schema => WorldSchemas.For(Dialect);
    public GameObjectTypeCatalog TypeCatalog => GameObjectTypeCatalog.For(Dialect);
    public GameObjectQuestPageSpec QuestPage => GameObjectQuestPage.For(Dialect);
    public InteractionDraft? Draft => Drafts.Draft;
    public bool Editable => Draft != null;

    public event Action? Changed;

    int generation;
    CancellationTokenSource cancellation = new();

    public GameObjectContext(ContentDrafts drafts)
    {
        Drafts = drafts;
        Icons = new ClientIcons(() => Store());
        Status = drafts.Error ?? "";
        drafts.Changed += sender =>
        {
            if (sender != this && Entry != null)
            {
                _ = Reload();
            }
        };
    }

    public void Redraw() => Changed?.Invoke();

    /// <summary>The template row in the effective view, or null while loading.</summary>
    public Row? Template => Entry is uint entry && Snapshot != null
        ? Effective.Get(Schema.Tables.Find("gameobject_template"), entry.ToString(CultureInfo.InvariantCulture))
        : null;

    public Row? Addon => Entry is uint entry && Snapshot != null
        ? Effective.Get(Schema.Tables.Find("gameobject_template_addon"), entry.ToString(CultureInfo.InvariantCulture))
        : null;

    public int Type => Template is { } template ? (int)template.U("type") : 0;

    public string Name => Template?.T("name") is { Length: > 0 } name ? name : Snapshot?.Name is { Length: > 0 } loaded ? loaded : RequestedName;

    public int SpawnCount => Snapshot?.SpawnCount ?? 0;

    /// <summary>A linked quest's title, or its ID when the title is not loaded.</summary>
    public string QuestName(uint id) => Snapshot?.QuestTitles.GetValueOrDefault(id) is { Length: > 0 } title ? title : $"Quest {id}";

    /// <summary>An item name from the retail client lookup, or the ID when it is not available.</summary>
    public string ItemName(uint id) => Lookup()?.Item(id)?.Name is { Length: > 0 } name ? name : $"Item {id}";

    /// <summary>Follow a selected GameObject spawn; null clears a spawn-opened window.</summary>
    public void Track(GameObjectSpawn? spawn)
    {
        if (!FollowsSelection && spawn == null)
        {
            return;
        }

        if (spawn == null)
        {
            FollowsSelection = false;
            Entry = null;
            TargetSpawn = null;
            Snapshot = null;
            Effective = new RowSet();
            generation++;
            cancellation.Cancel();
            Redraw();
            return;
        }

        FollowsSelection = true;
        if (Entry == spawn.TemplateId && TargetSpawn?.Guid == spawn.Guid)
        {
            TargetSpawn = spawn;
            return;
        }

        Entry = spawn.TemplateId;
        TargetSpawn = spawn;
        RequestedName = spawn.Name;
        _ = Reload();
    }

    /// <summary>Open a template by entry (the content browser, a quest link or a test).</summary>
    public void TrackEntry(uint entry, string name)
    {
        FollowsSelection = false;
        TargetSpawn = null;
        RequestedName = name;
        if (Entry == entry && Snapshot != null)
        {
            Redraw();
            return;
        }

        Entry = entry;
        Snapshot = null;
        Effective = new RowSet();
        _ = Reload();
    }

    /// <summary>Map/archive change: drop everything tied to the old world.</summary>
    public void Reset()
    {
        generation++;
        cancellation.Cancel();
        Entry = null;
        TargetSpawn = null;
        FollowsSelection = false;
        Snapshot = null;
        Effective = new RowSet();
        RequestedName = "";
        Redraw();
    }

    /// <summary>Re-read the template and its addon/quest rows on a worker, then rebuild.</summary>
    public async Task Reload()
    {
        if (Entry is not uint entry)
        {
            return;
        }

        int current = ++generation;
        cancellation.Cancel();
        cancellation = new();
        var token = cancellation.Token;
        if (Snapshot == null)
        {
            Status = "Loading the GameObject template…";
        }

        try
        {
            var source = DBSource.Open(Connection(), Lookup());
            SourceKey = source.SourceKey;
            var keys = Draft?.Keys(SourceKey) ?? [];
            var apply = Draft?.Frozen(SourceKey) ?? (rows => rows);
            var result = await Task.Run(() => source.ReadGameObjectTemplateAsync(entry, keys, apply, token), token);
            if (current != generation)
            {
                return;
            }

            Snapshot = result;
            Catalog = await ContentEditorWindow.LoadCatalog(Store());
            if (current != generation)
            {
                return;
            }

            var stale = Draft?.Conflicts(SourceKey, result.Rows).ToList() ?? [];
            if (stale.Count > 0)
            {
                GD.Print($"[draft conflict] {string.Join("; ", stale.Select(c => $"{c.Table} {c.Key}"))}");
            }

            Status = stale.Count > 0
                ? $"Some draft rows changed in the database since they were edited ({string.Join(", ", stale.Take(3).Select(c => $"{c.Table} {c.Key}"))}{(stale.Count > 3 ? ", …" : "")}); those rows show database values."
                : "";
            Rebuild();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception error)
        {
            if (current == generation)
            {
                Status = DbErrors.Safe(error);
                Redraw();
            }
        }
    }

    /// <summary>Effective rows = snapshot + draft; rebuild the pages.</summary>
    public void Rebuild()
    {
        if (Snapshot == null)
        {
            Redraw();
            return;
        }

        Effective = Draft?.Effective(SourceKey, Snapshot.Rows) ?? Snapshot.Rows;
        Redraw();
    }

    /// <summary>Validate and record one command.</summary>
    public bool Commit(string label, IEnumerable<RowEdit> edits)
    {
        if (Draft == null || Snapshot == null || Entry is not uint entry)
        {
            return false;
        }

        var (ok, message) = Drafts.Commit(this, SourceKey, Snapshot.Rows, Effective, label, edits.ToArray(),
            rows => GameObjectRules.Check(Dialect, rows, entry));
        Status = message;
        if (ok)
        {
            Rebuild();
            _ = Reload();
        }
        else
        {
            Redraw();
        }

        return ok;
    }

    /// <summary>
    /// A display view of the effective rows with a synthetic default addon row when the
    /// template has none. The template page can show defaults; the first edit inserts the row
    /// because <see cref="Effective"/> still lacks it.
    /// </summary>
    public RowSet DisplayRows()
    {
        var rows = Effective.Clone();
        if (Entry is not uint entry)
        {
            return rows;
        }

        var addon = Schema.Tables.Find("gameobject_template_addon");
        string key = entry.ToString(CultureInfo.InvariantCulture);
        if (rows.Get(addon, key) == null)
        {
            rows.Set(addon, GameObjectEdits.NewAddon(Dialect, entry));
        }

        return rows;
    }

    /// <summary>The first free quest item index, 0 when the list is empty and 6 when it is full.</summary>
    public int NextQuestItemIndex()
    {
        if (Entry is not uint entry)
        {
            return 0;
        }

        var table = Schema.Tables.Find("gameobject_questitem");
        return Effective.All(table).Where(r => r.U("GameObjectEntry") == entry).Select(r => (int)r.U("Idx")).DefaultIfEmpty(-1).Max() + 1;
    }

    /// <summary>Run a parse/build step from a control; bad numbers show a message instead of throwing.</summary>
    public void Try(Action action)
    {
        try
        {
            action();
        }
        catch (Exception error) when (error is FormatException or OverflowException)
        {
            Status = "Not saved: enter a whole number (decimal or 0x hex).";
            Redraw();
        }
    }

    public void Cancel() => cancellation.Cancel();

    /// <summary>Reload from the draft file after it was reopened (smoke tests).</summary>
    public void ReopenDraft()
    {
        Drafts.Reopen();
        Rebuild();
    }
}
