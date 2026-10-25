using System.Globalization;
using Godot;
using ACWorldEdit.Assets;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// Quest editor: friendly pages over quest_template, quest_template_addon, quest_request_items,
/// quest_offer_reward and the creature starter/ender links. Every change is one validated command
/// in the shared local content draft (undo/redo, stale-row conflicts); nothing is written to the
/// database. Pages are in QuestPanel.Pages.cs.
/// </summary>
public partial class QuestPanel : ContentPanel
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
    /// <summary>
    /// Locate a creature entry (loading its map when needed) by entry and display name; returns a
    /// status line, empty when it was selected immediately.
    /// </summary>
    public Func<uint, string, Task<string>> FlyToEntry { get; set; } = (_, _) => Task.FromResult("");
    /// <summary>
    /// Open the behavior editor for a creature entry (giver cards link to its SmartAI).
    /// </summary>
    public Action<uint> BehaviorRequested { get; set; } = _ => { };

    ContentSnapshot? snapshot;
    RowSet effective = new();
    IReadOnlyList<InteractionIssue> issues = [];
    string sourceKey = "";
    int generation;
    Tab tab;
    VBoxContainer page = null!, issueBox = null!;
    HBoxContainer tabs = null!;
    Label title = null!, subtitle = null!, statusLabel = null!;
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

    public QuestPanel()
    {
        Name = "QuestPanel";
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        SizeFlagsVertical = SizeFlags.ExpandFill;
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
            var source = DBSource.Open(Connection());
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
        issues = QuestId is uint id ? QuestModel.Check(id, effective, snapshot.Names, ZoneExists).ToArray() : [];
        Render();
    }

    Func<uint, bool>? ZoneExists => Areas() is { } areas ? areas.Contains : null;

    string Key => QuestId!.Value.ToString(CultureInfo.InvariantCulture);

    Row? Quest => QuestId.HasValue ? effective.Get(InteractionTables.QuestTemplate, Key) : null;

    /// <summary>
    /// The quest's row in a per-quest table, or its default when the quest has none yet (the first
    /// edit then inserts it).
    /// </summary>
    Row RowOf(TableSchema table) => effective.Get(table, Key) ?? QuestModel.NewQuest(QuestId!.Value, "")[Array.IndexOf(QuestModel.Tables, table)];

    /// <summary>
    /// Record one validated command against this quest's rows.
    /// </summary>
    bool Commit(string label, params RowEdit[] edits)
    {
        if (snapshot == null || QuestId is not uint id)
        {
            return false;
        }

        var names = snapshot.Names;
        return CommitEdits(sourceKey, snapshot.Rows, effective, label, edits, rows => QuestModel.Check(id, rows, names, ZoneExists), Rebuild, UpdateStatus);
    }

    /// <summary>
    /// Change columns on one per-quest row (inserting the row with defaults when missing).
    /// </summary>
    bool Set(TableSchema table, string label, params (string Column, object Value)[] values)
    {
        var existing = effective.Get(table, Key);
        var row = RowOf(table);
        foreach (var (column, value) in values)
        {
            row = row.With(column, value);
        }

        if (row == existing)
        {
            return true;
        }

        return Commit(label, new RowEdit(table.Name, Key, existing, row));
    }

    public void History(bool forward)
    {
        ApplyHistory(forward, Rebuild, () =>
        {
            if (QuestId is uint id)
            {
                _ = Load(id);
            }
        });
    }

    /// <summary>
    /// Create a quest (or copy the current one) under the next ID above both the database maximum
    /// and every quest ID already reserved in the draft.
    /// </summary>
    public async Task NewQuest(uint? copyFrom)
    {
        int current = ++generation;
        try
        {
            var source = DBSource.Open(Connection());
            var cat = await EnsureCatalog();
            var draft = Drafts.Draft;
            if (draft == null)
            {
                status = Drafts.Error ?? "Editing is disabled.";
                UpdateStatus();
                return;
            }

            var keys = draft.Keys(source.SourceKey);
            var apply = draft.Frozen(source.SourceKey);
            var probe = await Task.Run(() => source.ReadQuestAsync(copyFrom ?? 0, keys, apply, cat, CancellationToken.None));
            if (current != generation)
            {
                return;
            }

            sourceKey = source.SourceKey;
            uint id = Math.Max(probe.MaxQuestId, draft.MaxReserved(sourceKey, InteractionTables.QuestTemplate, "ID")) + 1;
            string key = id.ToString(CultureInfo.InvariantCulture);
            var probeView = draft.Effective(sourceKey, probe.Rows);
            var defaults = QuestModel.NewQuest(id, "New quest");
            List<RowEdit> edits = [];
            for (int i = 0; i < QuestModel.Tables.Length; i++)
            {
                var table = QuestModel.Tables[i];
                var row = copyFrom is uint from && probeView.Get(table, from.ToString(CultureInfo.InvariantCulture)) is { } original ? original.With("ID", id) : defaults[i];
                if (copyFrom != null && i == 0)
                {
                    row = row.With("LogTitle", row.T("LogTitle") + " (copy)");
                }

                edits.Add(new RowEdit(table.Name, key, null, row));
            }

            var (ok, message) = Drafts.Commit(this, sourceKey, probe.Rows, probeView, copyFrom == null ? $"Create quest {id}" : $"Copy quest {copyFrom} as {id}", edits, _ => []);
            status = ok ? (copyFrom == null ? $"Created quest {id}. Give it a title and an objective, then pick who offers it." : $"Copied as quest {id}. Givers are not copied.") : message;
            if (ok)
            {
                tab = Tab.Overview;
                await Load(id);
            }
            else
            {
                UpdateStatus();
            }
        }
        catch (Exception error)
        {
            status = DbErrors.Safe(error);
            UpdateStatus();
        }
    }

    /// <summary>
    /// Search picker over creatures, objects, items or quests. The chosen name is remembered so the
    /// edit validates before the next reload.
    /// </summary>
    void Pick(SmartParamKind kind, string heading, Action<uint> apply)
    {
        var names = snapshot?.Names;
        PickerPopup.Show(this, heading, term => DBSource.Open(Connection()).SearchAsync(kind, term, CancellationToken.None), (id, name) =>
        {
            var target = kind switch
            {
                SmartParamKind.Creature => names?.Creatures,
                SmartParamKind.GameObject => names?.GameObjects,
                SmartParamKind.Item => names?.Items,
                _ => names?.Quests
            };
            if (target != null)
            {
                target[id] = name;
            }

            apply(id);
        });
    }

    void PickSpell(string heading, Action<uint> apply) =>
        PickerPopup.Show(this, heading, term => Task.FromResult(catalog?.SearchSpells(term) ?? []), (id, _) => apply(id));

    void PickFromList(string heading, IEnumerable<(uint Id, string Name)> all, Action<uint> apply)
    {
        var list = all.Where(e => e.Name.Length > 0).ToList();
        PickerPopup.Show(this, heading, term => Task.FromResult<IReadOnlyList<(uint Id, string Name)>>(list
            .Where(e => term.Length == 0 || e.Name.Contains(term, StringComparison.OrdinalIgnoreCase) || e.Id.ToString(CultureInfo.InvariantCulture) == term.Trim())
            .OrderBy(e => e.Name.Length).Take(200).ToList()), (id, _) => apply(id));
    }

    void UpdateStatus()
    {
        if (statusLabel != null)
        {
            statusLabel.Text = status;
            statusLabel.Visible = status.Length > 0;
        }
    }

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

        var original = snapshot!.Rows.Get(InteractionTables.QuestTemplate, Key);
        title.Text = q.T("LogTitle").Length > 0 ? q.T("LogTitle") : "(untitled quest)";
        subtitle.Text = $"Quest {Key} · level {q.I("QuestLevel")} · requires level {q.U("MinLevel")}{(original == null ? " · new in draft" : QuestModel.Tables.Any(t => snapshot.Rows.Get(t, Key) != effective.Get(t, Key)) ? " · edited in draft" : "")}";
        foreach (var value in Enum.GetValues<Tab>())
        {
            var button = new Button
            {
                Text = value == Tab.Chain ? "Chain & requirements" : value.ToString(),
                ToggleMode = true,
                ButtonPressed = tab == value,
                Name = "Tab" + value
            };
            var chosen = value;
            button.Pressed += () =>
            {
                tab = chosen;
                Render();
            };
            tabs.AddChild(button);
        }

        switch (tab)
        {
            case Tab.Overview:
                RenderOverview(q);
                break;
            case Tab.Story:
                RenderStory(q);
                break;
            case Tab.Objectives:
                RenderObjectives(q);
                break;
            case Tab.Rewards:
                RenderRewards(q);
                break;
            case Tab.Chain:
                RenderChain(q);
                break;
            case Tab.Givers:
                RenderGivers();
                break;
            default:
                RenderPreview(q);
                break;
        }

        RenderIssues();
        Rendered?.Invoke();
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
