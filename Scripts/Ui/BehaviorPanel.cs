using System.Globalization;
using Godot;
using ACWorldEdit.Assets;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// Creature behavior editor body over SmartAI (smart_scripts), the creature's AI selection
/// (creature_template.AIName) and its speech lines (creature_text). Rules read as sentences
/// ("When it enters combat: cast Fireball → its victim"); editing uses searchable pickers instead of
/// type numbers. Scopes: every spawn of the entry, one spawn (negative GUID) and timed action lists.
/// The same panel edits an AreaTrigger's rules (source_type 2) and its areatrigger_scripts name;
/// then <c>entry</c> holds the trigger ID and the creature-only parts (AI, speech, spawn) are hidden.
/// Every change is one validated command in the shared local content draft; nothing is written to
/// the database. Rule cards are in BehaviorPanel.Rules.cs, speech in .Speech.cs.
/// </summary>
public partial class BehaviorPanel : ContentPanel
{
    public Action<uint> QuestEditorRequested { get; set; } = _ => { };

    ContentSnapshot? snapshot;
    RowSet effective = new();
    IReadOnlyList<InteractionIssue> issues = [];
    string sourceKey = "";
    int generation;
    uint entry;
    ulong guid;
    bool areaTrigger;
    /// <summary>
    /// Shown scope; null shows the speech page.
    /// </summary>
    SmartScope? scope;
    uint? expanded;
    VBoxContainer page = null!, issueBox = null!;
    ScrollContainer scroll = null!;
    HFlowContainer scopes = null!;
    Label subtitle = null!, statusLabel = null!;
    /// <summary>Heading for the host window: names the creature or trigger once loaded.</summary>
    public string Title { get; private set; } = "Behavior";
    /// <summary>Raised after every redraw so a host can refresh its heading and Undo/Redo buttons.</summary>
    public event Action? Rendered;
    /// <summary>Offer the Speech page next to the rule scopes (the creature window lists it as its own section).</summary>
    public bool SpeechInScopes { get; set; } = true;
    public bool IsLoading { get; private set; }
    public uint Entry => entry;
    public bool IsAreaTrigger => areaTrigger;
    public SmartScope? Scope => scope;
    public IReadOnlyList<InteractionIssue> Issues => issues;

    public BehaviorPanel()
    {
        Name = "BehaviorPanel";
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        SizeFlagsVertical = SizeFlags.ExpandFill;
    }

    public override void _Ready()
    {
        ReloadOnForeignChange(() => entry != 0, () => _ = Load());
        AddThemeConstantOverride("separation", 6);
        subtitle = ClientStyle.Text("", 12, ClientStyle.Faded);
        subtitle.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        AddChild(subtitle);
        scopes = new HFlowContainer();
        AddChild(scopes);
        scroll = new ScrollContainer
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
        statusLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        AddChild(statusLabel);
        Render();
    }

    /// <summary>
    /// Show the behavior of a creature entry; a non-zero GUID also offers that spawn's own rules.
    /// Entry 0 opens the window with a hint (toolbar without a selection).
    /// </summary>
    public void Open(uint creatureEntry, ulong spawnGuid) => Show(creatureEntry, spawnGuid, false);

    /// <summary>The creature entry or trigger ID shown, 0 when none.</summary>
    public uint OwnerId => entry;

    /// <summary>Show the SmartAI rules (source_type 2) of one AreaTrigger.</summary>
    public void OpenAreaTrigger(uint trigger) => Show(trigger, 0, trigger != 0);

    void Show(uint owner, ulong spawnGuid, bool trigger)
    {
        if (owner != entry || spawnGuid != guid || trigger != areaTrigger)
        {
            entry = owner;
            guid = spawnGuid;
            areaTrigger = trigger;
            snapshot = null;
            expanded = null;
            pending = null;
            pendingError = "";
            scope = entry == 0 ? null : new SmartScope((int)entry, trigger ? SmartScope.AreaTrigger : SmartScope.Creature);
            status = "";
        }

        // Render first so a newly opened owner shows "Loading…" instead of the previous page.
        Render();
        if (entry != 0)
        {
            _ = Load();
        }
    }

    /// <summary>
    /// Read the creature's rules, reachable action lists and speech (plus draft-touched rows) on a
    /// worker; stale results are dropped.
    /// </summary>
    public async Task Load()
    {
        int current = ++generation;
        IsLoading = true;
        uint loadEntry = entry;
        ulong loadGuid = guid;
        bool loadTrigger = areaTrigger;
        try
        {
            var source = DBSource.Open(Connection());
            var cat = await EnsureCatalog();
            var draft = Drafts.Draft;
            var keys = draft?.Keys(source.SourceKey) ?? [];
            var apply = draft?.Frozen(source.SourceKey) ?? (rows => rows);
            var result = await Task.Run(() => loadTrigger
                ? source.ReadAreaTriggerBehaviorAsync(loadEntry, keys, apply, cat, CancellationToken.None)
                : source.ReadBehaviorAsync(loadEntry, loadGuid, keys, apply, cat, CancellationToken.None));
            if (current != generation)
            {
                return;
            }

            sourceKey = source.SourceKey;
            snapshot = result;
            var stale = draft?.Conflicts(sourceKey, result.Rows).Where(c => loadTrigger ? c.Table is "smart_scripts" or "areatrigger_scripts" : c.Table is "smart_scripts" or "creature_ai" or "creature_text").ToList() ?? [];
            if (stale.Count > 0)
            {
                string rows = string.Join(", ", stale.Take(3).Select(c => $"{c.Table} {c.Key}"));
                GD.Print($"[draft conflict] {string.Join("; ", stale.Select(c => $"{c.Table} {c.Key}"))}");
                status = $"Some draft rows changed in the database since you edited them ({rows}{(stale.Count > 3 ? ", …" : "")}); undo or re-edit them.";
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
        ContentNames.FillTexts(snapshot.Names, effective, entry);
        issues = CheckAll(effective).ToArray();
        Render();
    }

    /// <summary>SMART_TARGET_ACTION_INVOKER: the player who walked into an AreaTrigger.</summary>
    const uint InvokerTarget = SmartRuleEdits.InvokerTarget;
    SmartScope EntryScope => new((int)entry, areaTrigger ? SmartScope.AreaTrigger : SmartScope.Creature);
    SmartScope SpawnScope => new(-(int)guid, SmartScope.Creature);
    Row? CreatureAi(RowSet rows) => areaTrigger ? null : rows.Get(InteractionTables.CreatureAI, entry.ToString(CultureInfo.InvariantCulture));
    Row? TriggerScript(RowSet rows) => areaTrigger ? rows.Get(InteractionTables.AreaTriggerScripts, entry.ToString(CultureInfo.InvariantCulture)) : null;

    static List<SmartRule> RulesOf(RowSet rows, SmartScope of) => SmartRuleEdits.RulesOf(rows, of);

    /// <summary>
    /// The creature's scopes: entry, spawn (when opened from one), then every action list reachable
    /// from them through "Run action list", in discovery order.
    /// </summary>
    List<SmartScope> Scopes(RowSet rows)
    {
        var list = new List<SmartScope> { EntryScope };
        if (guid != 0)
        {
            list.Add(SpawnScope);
        }

        for (int i = 0; i < list.Count && list.Count < 64; i++)
        {
            foreach (var rule in RulesOf(rows, list[i]).Where(r => r.Action == SmartCatalog.RunActionList && r.ActionParam(1) != 0))
            {
                var next = new SmartScope((int)rule.ActionParam(1), SmartScope.ActionList);
                if (!list.Contains(next))
                {
                    list.Add(next);
                }
            }
        }

        if (scope is { IsActionList: true } shown && !list.Contains(shown))
        {
            list.Add(shown);
        }

        return list;
    }

    /// <summary>
    /// Loader rules over every scope, plus checks the per-scope rules cannot see.
    /// </summary>
    IEnumerable<InteractionIssue> CheckAll(RowSet rows)
    {
        if (snapshot == null)
        {
            yield break;
        }

        foreach (var each in Scopes(rows))
        {
            var rules = RulesOf(rows, each);
            foreach (var issue in SmartCatalog.Check(each, rules, snapshot.Names, CreatureAi(rows), entry))
            {
                yield return each == EntryScope ? issue : issue with { Message = $"{each.Describe(snapshot.CreatureName)}: {issue.Message}" };
            }

            if (each.IsActionList && rules.Count == 0)
            {
                yield return new(false, $"Action list #{each.EntryOrGuid} has no steps yet.");
            }

            // Talk lines belong to the speaking creature; an AreaTrigger has none of its own.
            foreach (var rule in rules.Where(r => !areaTrigger && r.Action == SmartCatalog.Talk && !rows.All(InteractionTables.CreatureText).Any(t => t.U("CreatureID") == entry && t.U("GroupID") == r.ActionParam(1))))
            {
                if (rule.Target is 1 or 0 || each.IsActionList)
                {
                    yield return new(false, $"Rule {rule.Id} says text group {rule.ActionParam(1)}, which has no lines (add them under Speech).");
                }
            }
        }

        if (areaTrigger)
        {
            foreach (var issue in SmartCatalog.CheckAreaTriggerScript((int)entry, TriggerScript(rows), RulesOf(rows, EntryScope).Count > 0))
            {
                yield return issue;
            }
        }
        else if (snapshot.ScriptName.Length > 0 && RulesOf(rows, EntryScope).Count > 0)
        {
            yield return new(false, $"This creature has the C++ script {snapshot.ScriptName}; it replaces SmartAI, so these rules may never run.");
        }
    }

    /// <summary>
    /// Record one validated command.
    /// </summary>
    bool Commit(string label, params RowEdit[] edits)
    {
        if (snapshot == null)
        {
            return false;
        }

        return CommitEdits(sourceKey, snapshot.Rows, effective, label, edits, rows => CheckAll(rows).ToList(), Rebuild, UpdateStatus);
    }

    public void History(bool forward)
    {
        ApplyHistory(forward, Rebuild, () =>
        {
            if (entry != 0)
            {
                _ = Load();
            }
        });
    }

    void UpdateStatus()
    {
        if (statusLabel != null)
        {
            statusLabel.Text = status;
            statusLabel.Visible = status.Length > 0;
        }
    }

    /// <summary>
    /// Switch to a scope (entry, spawn or action list) or, with null, to the speech page.
    /// </summary>
    public void ShowSpeech() => ShowScope(null);

    /// <summary>Switch to the rules of the entry (keeps an already shown scope).</summary>
    public void ShowRules()
    {
        if (scope == null && entry != 0)
        {
            ShowScope(EntryScope);
        }
    }

    public void ShowScope(SmartScope? value)
    {
        scope = value;
        expanded = null;
        pending = null;
        pendingError = "";
        Render();
    }

    void Render()
    {
        if (page == null)
        {
            return;
        }

        foreach (var child in page.GetChildren().Concat(scopes.GetChildren()).Concat(issueBox.GetChildren()))
        {
            child.QueueFree();
        }

        EditorFields.Error = message =>
        {
            status = message;
            UpdateStatus();
        };
        UpdateStatus();
        if (entry == 0 || snapshot == null)
        {
            Title = "Behavior";
            subtitle.Text = entry == 0 ? "Select a creature or an AreaTrigger in the world, then choose Behavior… (or Edit behavior) in its inspector." : "Loading…";
            Rendered?.Invoke();
            return;
        }

        Title = $"{snapshot.CreatureName} · Behavior";
        RenderAiBanner();
        foreach (var each in Scopes(effective))
        {
            int count = RulesOf(effective, each).Count;
            string text = each.IsActionList ? $"Action list #{each.EntryOrGuid} · {count} step(s)" : each.IsAreaTrigger ? $"On trigger · {count}" : each.IsSpawn ? $"This spawn only · {count}" : $"All {snapshot.CreatureName} spawns · {count}";
            ScopeButton(text, each);
        }

        if (!areaTrigger && SpeechInScopes)
        {
            int lines = effective.All(InteractionTables.CreatureText).Count(t => t.U("CreatureID") == entry);
            ScopeButton($"Speech · {lines} line(s)", null);
        }

        scope ??= areaTrigger ? EntryScope : null;
        scopes.Visible = SpeechInScopes || scope != null;
        if (scope is { } shown)
        {
            RenderRules(shown);
        }
        else
        {
            RenderSpeech();
        }

        foreach (var issue in issues.OrderByDescending(i => i.Error).Take(5))
        {
            var row = new HBoxContainer();
            var label = ClientStyle.Text((issue.Error ? "⛔ " : "⚠ ") + issue.Message, 12, issue.Error ? ClientStyle.Error : ClientStyle.Warning);
            label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            row.AddChild(label);
            if (issue.Fix is { } fix)
            {
                var button = new Button
                {
                    Text = "Fix"
                };
                button.Pressed += () => Commit(fix.Any(f => f.Table == InteractionTables.AreaTriggerScripts.Name) ? "Run SmartAI rules (SmartTrigger)" : "Enable SmartAI", fix);
                row.AddChild(button);
            }

            issueBox.AddChild(row);
        }

        if (issues.Count > 5)
        {
            issueBox.AddChild(ClientStyle.Text($"…and {issues.Count - 5} more.", 11, ClientStyle.Faded));
        }

        Rendered?.Invoke();
    }

    void ScopeButton(string text, SmartScope? value)
    {
        var button = new Button
        {
            Text = text,
            ToggleMode = true,
            ButtonPressed = scope == value
        };
        button.Pressed += () => ShowScope(value);
        scopes.AddChild(button);
    }

    /// <summary>
    /// Which brain the creature uses, in words, with a one-click switch to SmartAI.
    /// </summary>
    void RenderAiBanner()
    {
        if (areaTrigger)
        {
            string name = TriggerScript(effective)?.T("ScriptName") ?? "";
            string runs = name == AreaTriggerRecord.SmartTrigger ? ""
                : name.Length == 0 ? " · no script: the rules below do not run until SmartTrigger is set" : $" · C++ script {name}: the rules below are ignored";
            subtitle.Text = $"AreaTrigger {entry}{runs}";
            return;
        }

        string ai = CreatureAi(effective)?.T("AIName") ?? "";
        string warning = snapshot!.ScriptName.Length > 0 ? $" · C++ script {snapshot.ScriptName} (SmartAI rules below are ignored by the server)"
            : ai == "SmartAI" ? "" : ai.Length == 0 ? " · default AI: rules below do not run until SmartAI is enabled" : $" · {ai}: rules below do not run";
        subtitle.Text = $"Entry {entry}{(guid != 0 ? $" · spawn #{guid}" : "")}{warning}";
    }

    /// <summary>
    /// The AIName change that makes rules run, when the creature uses the default AI. Other AIs are
    /// a deliberate choice, so they only get a warning with a Fix button.
    /// </summary>
    RowEdit[] EnableSmartAiIfDefault()
    {
        // An AreaTrigger without any script gets SmartTrigger with its first rule; a C++ script name
        // is left alone (the warning offers the switch).
        if (areaTrigger)
        {
            return TriggerScript(effective) == null ? [SmartCatalog.EnableSmartTrigger((int)entry, null)] : [];
        }

        if (CreatureAi(effective) is { } ai && ai.T("AIName").Length == 0 && snapshot!.ScriptName.Length == 0)
        {
            return [new RowEdit(InteractionTables.CreatureAI.Name, ai["entry"], ai, ai.With("AIName", "SmartAI"))];
        }

        return [];
    }
}
