using System.Globalization;
using Godot;
using ACWorldEdit.Assets;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;
using static ACWorldEdit.Foundation.InteractionEdits;

namespace ACWorldEdit;

/// <summary>
/// State shared by every section of the creature properties window: the creature being edited, the
/// connection, asset and draft hooks, and the interaction rows (gossip, quests, vendor, trainer and
/// template) read once per creature and merged with the local draft. Sections draw from it and call
/// <see cref="Commit"/>; anything that changes raises <see cref="Changed"/> and sections redraw.
/// A section with its own data (behavior, quests) keeps that in its own panel and only uses the hooks.
/// </summary>
public sealed class CreatureContext : IEntityContext
{
    public Func<WorldConnection> Connection { get; set; } = () => WorldConnection.Empty;
    public Func<IClientFiles?> Store { get; set; } = () => null;
    /// <summary>The retail client's DB2 lookup (item, spell and text names TrinityCore keeps out of its database).</summary>
    public Func<IClientLookup?> Lookup { get; set; } = () => null;
    public ContentDrafts Drafts { get; }
    public ClientIcons Icons { get; }
    public Func<AreaLookup?> Areas { get; set; } = () => null;
    /// <summary>The viewer's model thumbnails for the template preview (null when no client is open).</summary>
    public Func<IThumbnailSource?> Thumbnails { get; set; } = () => null;
    /// <summary>Locate a creature entry on the map (quest giver cards); returns a status line.</summary>
    public Func<uint, string, Task<string>> FlyToEntry { get; set; } = (_, _) => Task.FromResult("");
    /// <summary>Teleport to one spawn of this creature (its GUID, map and server position); returns a status line.</summary>
    public Func<ulong, uint, double, double, double, Task<string>> FlyToSpawn { get; set; } = (_, _, _, _, _) => Task.FromResult("");
    /// <summary>A section wants the behavior of another creature entry (a quest giver card).</summary>
    public Action<uint> BehaviorForEntryRequested { get; set; } = _ => { };

    BehaviorPanel? behavior;
    QuestPanel? quest;
    (uint Entry, ulong Guid)? behaviorOpenedFor;

    /// <summary>
    /// The one behavior editor body shared by the Behavior and Speech sections (they are two views of
    /// the same rows and snapshot).
    /// </summary>
    public BehaviorPanel Behavior => behavior ??= new BehaviorPanel
    {
        Connection = () => Connection(),
        Store = () => Store(),
        Lookup = () => Lookup(),
        Drafts = Drafts,
        SpeechInScopes = false
    };

    /// <summary>
    /// The quest editor body the Quests section embeds for a quest of this creature, of the open
    /// database's dialect. A dialect switch replaces it (the section reparents the panel it hosts).
    /// </summary>
    public QuestPanel Quest
    {
        get
        {
            if (quest != null && quest.Dialect == Dialect)
            {
                return quest;
            }

            quest?.GetParent()?.RemoveChild(quest);
            quest?.QueueFree();
            QuestPanel panel = QuestDialects.For(Dialect);
            panel.Connection = () => Connection();
            panel.Store = () => Store();
            panel.Drafts = Drafts;
            panel.Areas = () => Areas();
            panel.Lookup = () => Lookup();
            panel.FlyToEntry = (entry, name) => FlyToEntry(entry, name);
            panel.BehaviorRequested = entry => BehaviorForEntryRequested(entry);
            return quest = panel;
        }
    }

    /// <summary>Open the behavior panel on the current creature unless it already shows it.</summary>
    public void EnsureBehaviorOpen()
    {
        if (Target is not { } spawn)
        {
            behaviorOpenedFor = null;
            Behavior.Open(0, 0);
            return;
        }

        if (behaviorOpenedFor != (spawn.TemplateId, spawn.Guid))
        {
            behaviorOpenedFor = (spawn.TemplateId, spawn.Guid);
            Behavior.Open(spawn.TemplateId, spawn.Guid);
        }
    }

    /// <summary>The selected creature spawn (null when nothing is selected).</summary>
    public CreatureSpawn? Target { get; private set; }
    public InteractionSnapshot? Snapshot { get; private set; }
    /// <summary>The template's first model, read for the template page's preview; null while loading or without a client.</summary>
    public CreatureLook? Look { get; private set; }
    /// <summary>The loaded creature in the open database's read model.</summary>
    public ICreatureView? View { get; private set; }
    public AcCreatureView? AcView => View as AcCreatureView;
    public TcCreatureView? TcView => View as TcCreatureView;
    public WorldDialect Dialect => Connection().Dialect;
    public RowSet Effective { get; private set; } = new();
    public InteractionCatalog? Catalog { get; private set; }
    public InteractionDraft? Draft => Drafts.Draft;
    public string SourceKey { get; private set; } = "";

    /// <summary>The window's status line; sections set it and call <see cref="Redraw"/>.</summary>
    public string Status { get; set; } = "";

    /// <summary>
    /// The sections show editors whenever a content draft is open; otherwise they are read-only.
    /// </summary>
    public bool Editable => Draft != null;

    public IWorldSchema Schema => WorldSchemas.For(Dialect);

    /// <summary>Data, status or mode changed: sections and the window frame redraw.</summary>
    public event Action? Changed;
    /// <summary>A different creature (or none) is now the target.</summary>
    public event Action<CreatureSpawn?>? CreatureChanged;
    /// <summary>One-line interaction summary for the creature inspector card.</summary>
    public event Action<string>? SummaryChanged;
    /// <summary>A section asks the window to switch section (id, optional argument).</summary>
    public event Action<string, object?>? SectionRequested;
    /// <summary>Open the standalone quest editor on a quest.</summary>
    public event Action<uint>? QuestEditorRequested;

    int generation;
    CancellationTokenSource cancellation = new();

    public CreatureContext(ContentDrafts drafts, Func<IClientFiles?> store)
    {
        Drafts = drafts;
        Store = store;
        Icons = new ClientIcons(() => Store());
        Status = drafts.Error ?? "";
        drafts.Changed += sender =>
        {
            if (sender != this && Target != null)
            {
                _ = Reload();
            }
        };
    }

    public void Redraw() => Changed?.Invoke();

    public void ShowSection(string id, object? argument = null) => SectionRequested?.Invoke(id, argument);

    public void RequestQuestEditor(uint quest) => QuestEditorRequested?.Invoke(quest);

    /// <summary>
    /// Follow the inspector selection: load its interactions in the background for the summary.
    /// A different creature cancels the previous load; stale results are dropped.
    /// </summary>
    public void Track(CreatureSpawn? spawn)
    {
        // Same creature: keep the loaded (or loading) snapshot.
        if (spawn != null && Target != null && spawn.TemplateId == Target.TemplateId && spawn.Guid == Target.Guid)
        {
            Target = spawn;
            return;
        }

        Target = spawn;
        Snapshot = null;
        Look = null;
        View = null;
        behaviorOpenedFor = null;
        CreatureChanged?.Invoke(spawn);
        if (spawn == null)
        {
            generation++;
            cancellation.Cancel();
            SummaryChanged?.Invoke("Select a creature to see its interactions.");
            Redraw();
            return;
        }

        _ = Reload();
    }

    /// <summary>Map/archive change: drop everything tied to the old world.</summary>
    public void Reset()
    {
        generation++;
        cancellation.Cancel();
        Target = null;
        Snapshot = null;
        Look = null;
        View = null;
        behaviorOpenedFor = null;
        CreatureChanged?.Invoke(null);
        Redraw();
    }

    /// <summary>
    /// Re-read the database snapshot (including draft-touched rows) on a worker, then rebuild.
    /// </summary>
    public async Task Reload()
    {
        if (Target is not { } spawn)
        {
            return;
        }

        int current = ++generation;
        cancellation.Cancel();
        cancellation = new();
        var token = cancellation.Token;
        if (Snapshot == null)
        {
            Status = "Loading interactions…";
            SummaryChanged?.Invoke(Status);
        }

        try
        {
            var source = DBSource.Open(Connection(), Lookup());
            var (catalog, note) = await CreatureDialects.For(Dialect).LoadCatalog(Store());
            Catalog = catalog;
            if (note != null)
            {
                Status = note;
            }

            SourceKey = source.SourceKey;
            var keys = Draft?.Keys(SourceKey) ?? [];
            var apply = Draft?.Frozen(SourceKey) ?? (rows => rows);
            var cat = Catalog;
            var result = await Task.Run(() => source.ReadInteractionAsync(spawn.TemplateId, spawn.Guid, keys, apply, cat, token), token);
            if (current != generation)
            {
                return;
            }

            try
            {
                var looks = await source.ReadCreatureLooksAsync([spawn.TemplateId], token);
                if (current != generation)
                {
                    return;
                }

                Look = looks.GetValueOrDefault(spawn.TemplateId);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                Look = null;
            }

            Snapshot = result;
            var stale = Draft?.Conflicts(SourceKey, result.Rows).Where(c => c.Table is not ("smart_scripts" or "creature_ai" or "creature_text" or "areatrigger_scripts")).ToList() ?? [];
            if (stale.Count > 0)
            {
                GD.Print($"[draft conflict] {string.Join("; ", stale.Select(c => $"{c.Table} {c.Key}"))}");
            }

            Status = stale.Count > 0 ? $"Some draft rows changed in the database since they were edited ({string.Join(", ", stale.Take(3).Select(c => $"{c.Table} {c.Key}"))}{(stale.Count > 3 ? ", …" : "")}); those rows show database values." : "";
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
                SummaryChanged?.Invoke(Status);
                Redraw();
            }
        }
    }

    /// <summary>Effective rows = snapshot + draft; rebuild the view, summary and sections.</summary>
    public void Rebuild()
    {
        if (Snapshot == null)
        {
            return;
        }

        Effective = Draft?.Effective(SourceKey, Snapshot.Rows) ?? Snapshot.Rows;
        var built = CreatureDialects.For(Dialect).Build(Snapshot, Effective);
        View = built.View;
        SummaryChanged?.Invoke(built.Summary);
        Redraw();
    }

    /// <summary>
    /// Validate and record one command. Rejected when the row edits break the column rules or
    /// introduce a server-rule error that was not already present.
    /// </summary>
    public bool Commit(string label, IEnumerable<RowEdit> edits)
    {
        if (Draft == null || Snapshot == null || View == null)
        {
            return false;
        }

        var client = Lookup();
        var dialect = CreatureDialects.For(Dialect);
        var (ok, message) = Drafts.Commit(this, SourceKey, Snapshot.Rows, Effective, label, edits.ToArray(), rows => dialect.Check(Snapshot, rows, client));
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

    public Row TemplateRow => Effective.Get(Schema.Tables.Find("creature_template"),Snapshot!.Entry.ToString(CultureInfo.InvariantCulture))!;

    /// <summary>
    /// Next free ID above the database maximum, earlier reservations in the draft and rows in view.
    /// </summary>
    public uint NextId(TableSchema table, string column, uint databaseMax) =>
        Math.Max(Math.Max(databaseMax, Draft?.MaxReserved(SourceKey, table, column) ?? 0), Effective.All(table).Select(r => r.U(column)).DefaultIfEmpty(0u).Max()) + 1;

    /// <summary>
    /// Run a parse/build step from a control; bad numbers show a message instead of throwing.
    /// </summary>
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

    /// <summary>Reload from the draft file after it was reopened (smoke tests).</summary>
    public void ReopenDraft()
    {
        Drafts.Reopen();
        Rebuild();
    }

    public void Cancel() => cancellation.Cancel();
}
