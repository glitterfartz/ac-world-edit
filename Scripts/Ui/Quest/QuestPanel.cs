using System.Globalization;
using Godot;
using ACWorldEdit.Assets;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// Quest editor: friendly pages over the quest tables and the creature starter/ender links. Every
/// change is one validated command in the shared local content draft (undo/redo, stale-row
/// conflicts); nothing is written to the database. This base holds the loading, drafting and the
/// pages both databases share (QuestPanel.Pages.cs); <see cref="AcQuestPanel"/> and
/// <see cref="TcQuestPanel"/> name their tables and draw the pages that differ.
/// </summary>
public abstract partial class QuestPanel : ContentPanel
{
    public enum Tab
    {
        Overview,
        Story,
        Objectives,
        Rewards,
        Chain,
        Givers,
        Preview
    }

    public Func<AreaLookup?> Areas { get; set; } = () => null;
    /// <summary>The client's tables for item, spell and currency names (TrinityCore).</summary>
    public Func<IClientLookup?> Lookup { get; set; } = () => null;
    /// <summary>Creature and GameObject model thumbnails for the giver picker (null without a client).</summary>
    public IThumbnailSource? Thumbnails { get; set; }
    /// <summary>
    /// Locate a creature entry (loading its map when needed) by entry and display name; returns a
    /// status line, empty when it was selected immediately.
    /// </summary>
    public Func<uint, string, Task<string>> FlyToEntry { get; set; } = (_, _) => Task.FromResult("");
    /// <summary>
    /// Open the behavior editor for a creature entry (giver cards link to its SmartAI).
    /// </summary>
    public Action<uint> BehaviorRequested { get; set; } = _ => { };

    protected ContentSnapshot? snapshot;
    protected RowSet effective = new();
    protected IReadOnlyList<InteractionIssue> issues = [];
    protected string sourceKey = "";
    int generation;
    protected Tab tab;
    protected VBoxContainer page = null!, issueBox = null!;
    HBoxContainer tabs = null!;
    Label title = null!, subtitle = null!, statusLabel = null!;
    /// <summary>Client icon textures for the item, spell, currency and skill references on the pages.</summary>
    readonly ClientIcons icons;
    public uint? QuestId { get; private set; }
    /// <summary>Raised after every redraw so a host can refresh its Duplicate/Undo/Redo state.</summary>
    public event Action? Rendered;
    public bool HasQuest => Quest != null;
    /// <summary>Show a message in the status line (the standalone window reports its search here).</summary>
    public void ShowStatus(string text)
    {
        status = text;
        UpdateStatus();
    }

    public bool IsLoading { get; private set; }

    /// <summary>The database dialect this panel edits.</summary>
    public abstract WorldDialect Dialect { get; }

    protected abstract TableSchema Template { get; }
    protected abstract TableSchema Addon { get; }
    protected abstract TableSchema Request { get; }
    protected abstract TableSchema Offer { get; }
    protected abstract TableSchema Starter { get; }
    protected abstract TableSchema Ender { get; }
    /// <summary>GameObject quest links; a quest may be offered or completed by either kind.</summary>
    protected abstract TableSchema ObjectStarter { get; }
    protected abstract TableSchema ObjectEnder { get; }

    /// <summary>The per-quest tables (all keyed by ID), in the order <see cref="NewQuestRows"/> returns their rows.</summary>
    protected abstract TableSchema[] Tables { get; }

    protected abstract Row[] NewQuestRows(uint id, string title);

    /// <summary>The dialect's loader rules over the effective rows of one quest.</summary>
    protected abstract IEnumerable<InteractionIssue> Check(uint id, RowSet rows, IContentNames names);

    /// <summary>The line under the quest title (the dialect's level fields).</summary>
    protected abstract string Subtitle(Row q);

    protected abstract void RenderOverviewPage(Row q);
    protected abstract void RenderObjectivesPage(Row q);
    protected abstract void RenderRewardsPage(Row q);
    protected abstract void RenderPreviewPage(Row q);

    /// <summary>
    /// Rows a dialect copies along with the per-quest tables when a quest is duplicated (TrinityCore's
    /// objectives); the new quest's ID is <paramref name="id"/>.
    /// </summary>
    protected virtual IEnumerable<RowEdit> CopyRelated(uint from, uint id, ContentSnapshot probe, RowSet view) => [];

    protected QuestPanel()
    {
        Name = GetType().Name;
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        SizeFlagsVertical = SizeFlags.ExpandFill;
        icons = new(() => Store());
    }

    /// <summary>Icons decode on a worker; redraw once when they arrive so the references show them.</summary>
    public override void _Process(double delta)
    {
        if (icons.Poll() && Quest != null && IsVisibleInTree())
        {
            Render();
        }
    }

    public override void _Ready()
    {
        ReloadOnForeignChange(() => QuestId.HasValue, () => _ = Load(QuestId!.Value));
        AddThemeConstantOverride("separation", 4);
        title = ClientStyle.Text("", 19, ClientStyle.Header);
        AddChild(title);
        subtitle = ClientStyle.Text("", 12, ClientStyle.Faded);
        AddChild(subtitle);
        tabs = new HBoxContainer();
        AddChild(tabs);
        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
        };
        AddChild(scroll);
        page = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        page.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(page);
        AddChild(new HSeparator());
        issueBox = new VBoxContainer();
        AddChild(issueBox);
        statusLabel = ClientStyle.Text("", 12, ClientStyle.Warning);
        AddChild(statusLabel);
        Render();
    }

    /// <summary>
    /// Show a quest; 0 shows nothing (the standalone window without a selection).
    /// </summary>
    public void Open(uint quest)
    {
        if (quest == 0)
        {
            QuestId = null;
            snapshot = null;
            Render();
        }
        else
        {
            _ = Load(quest);
        }
    }

    /// <summary>
    /// Read the quest rows (plus every draft-touched quest row) on a worker; stale results drop.
    /// </summary>
    public async Task Load(uint quest)
    {
        int current = ++generation;
        IsLoading = true;
        try
        {
            var source = DBSource.Open(Connection(), Lookup());
            var cat = await EnsureCatalog();
            var draft = Drafts.Draft;
            var keys = draft?.Keys(source.SourceKey) ?? [];
            var apply = draft?.Frozen(source.SourceKey) ?? (rows => rows);
            var result = await Task.Run(() => source.ReadQuestAsync(quest, keys, apply, cat, CancellationToken.None));
            if (current != generation)
            {
                return;
            }

            sourceKey = source.SourceKey;
            if (QuestId != quest)
            {
                status = "";
            }

            QuestId = quest;
            snapshot = result;
            if (draft != null && draft.Conflicts(sourceKey, result.Rows).Count > 0)
            {
                status = "Some draft rows changed in the database since you edited them; undo or re-edit them.";
            }

            Rebuild();
        }
        catch (Exception error)
        {
            if (current == generation)
            {
                status = DbErrors.Safe(error);
                UpdateStatus();
            }
        }
        finally
        {
            if (current == generation)
            {
                IsLoading = false;
            }
        }
    }

    void Rebuild()
    {
        if (snapshot == null)
        {
            Render();
            return;
        }

        effective = Drafts.Draft?.Effective(sourceKey, snapshot.Rows) ?? snapshot.Rows;
        issues = QuestId is uint id ? Check(id, effective, snapshot.Names).ToArray() : [];
        Render();
    }

    protected Func<uint, bool>? ZoneExists => Areas() is { } areas ? areas.Contains : null;

    protected string Key => QuestId!.Value.ToString(CultureInfo.InvariantCulture);

    protected Row? Quest => QuestId.HasValue ? effective.Get(Template, Key) : null;

    /// <summary>
    /// The quest's row in a per-quest table, or its default when the quest has none yet (the first
    /// edit then inserts it).
    /// </summary>
    protected Row RowOf(TableSchema table) => effective.Get(table, Key) ?? NewQuestRows(QuestId!.Value, "")[Array.IndexOf(Tables, table)];

    void Render()
    {
        if (page == null)
        {
            return;
        }

        foreach (var child in page.GetChildren().Concat(tabs.GetChildren()).Concat(issueBox.GetChildren()))
        {
            child.QueueFree();
        }

        EditorFields.Error = message =>
        {
            status = message;
            UpdateStatus();
        };
        UpdateStatus();
        if (Quest is not { } q)
        {
            title.Text = QuestId.HasValue ? $"Quest {QuestId} does not exist" : "No quest open";
            subtitle.Text = QuestId.HasValue ? "Create one with + New quest, or search for another." : "Find a quest on the left, or create one with + New quest.";
            Rendered?.Invoke();
            return;
        }

        var original = snapshot!.Rows.Get(Template, Key);
        title.Text = q.T("LogTitle").Length > 0 ? q.T("LogTitle") : "(untitled quest)";
        subtitle.Text = $"{Subtitle(q)}{(original == null ? " · new in draft" : Tables.Any(t => snapshot.Rows.Get(t, Key) != effective.Get(t, Key)) ? " · edited in draft" : "")}";
        RenderTabs();
        RenderPage(q);
        RenderIssues();
        Rendered?.Invoke();
    }

    /// <summary>The tab buttons; pressing one switches the page and redraws.</summary>
    void RenderTabs()
    {
        foreach (var value in Enum.GetValues<Tab>())
        {
            var button = new Button
            {
                Text = value == Tab.Chain ? "Chain & requirements" : value.ToString(),
                ToggleMode = true,
                ButtonPressed = tab == value,
                Name = "Tab" + value,
                TooltipText = value switch
                {
                    Tab.Overview => "Title, category, level range, who can take it and its behaviour flags.",
                    Tab.Story => "The text shown when the quest is offered, while it is in progress and when it is turned in.",
                    Tab.Objectives => "What the player must kill, use, collect or reach, and the items given on accept.",
                    Tab.Rewards => "Money, experience, items, reputation and other rewards.",
                    Tab.Chain => "The quests before and after it, exclusive groups and requirements.",
                    Tab.Givers => "The creatures that offer and accept the quest.",
                    _ => "A read-only preview of the quest as the player sees it."
                }
            };
            var chosen = value;
            button.Pressed += () =>
            {
                tab = chosen;
                Render();
            };
            tabs.AddChild(button);
        }
    }

    /// <summary>The body of the shown tab.</summary>
    void RenderPage(Row q)
    {
        switch (tab)
        {
            case Tab.Overview:
                RenderOverviewPage(q);
                break;
            case Tab.Story:
                RenderStory(q);
                break;
            case Tab.Objectives:
                RenderObjectivesPage(q);
                break;
            case Tab.Rewards:
                RenderRewardsPage(q);
                break;
            case Tab.Chain:
                RenderChain(q);
                break;
            case Tab.Givers:
                RenderGivers();
                break;
            default:
                RenderPreviewPage(q);
                break;
        }
    }

    void RenderIssues()
    {
        foreach (var issue in issues.OrderByDescending(i => i.Error).Take(5))
        {
            var row = new HBoxContainer();
            var label = ClientStyle.Text((issue.Error ? "⛔ " : "⚠ ") + issue.Message, 12, issue.Error ? ClientStyle.Error : ClientStyle.Warning);
            label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            row.AddChild(label);
            issueBox.AddChild(row);
        }

        if (issues.Count > 5)
        {
            issueBox.AddChild(ClientStyle.Text($"…and {issues.Count - 5} more.", 11, ClientStyle.Faded));
        }
    }

    /// <summary>
    /// Switch page (smoke hook and cross-links).
    /// </summary>
    public void ShowTab(Tab value)
    {
        tab = value;
        Render();
    }
}
