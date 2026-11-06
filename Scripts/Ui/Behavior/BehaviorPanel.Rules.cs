using System.Globalization;
using Godot;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// Rule cards. A card shows the rule as a sentence; expanding it shows When / Do / On with typed
/// fields. New rules, template starts and "Then" steps open the same editor on a pending row that
/// is drafted only by its Add button, so creating and editing look and work the same.
/// smart_scripts is keyed by (entryorguid, source_type, id, link), so edits that change id or link
/// are recorded as delete + insert within one command.
/// </summary>
public partial class BehaviorPanel
{
    /// <summary>
    /// The rule being created: its row (id assigned on Add), its scope and, for a "Then" step, the
    /// rule it follows. Nothing is drafted until Add; switching owner discards it.
    /// </summary>
    sealed record PendingRule(SmartScope Scope, Row Row, uint? After);

    PendingRule? pending;
    /// <summary>
    /// Why the last Add was rejected, shown inside the pending card until it changes.
    /// </summary>
    string pendingError = "";
    /// <summary>
    /// Card to scroll into view after the next render: a rule id, or uint.MaxValue for the pending card.
    /// </summary>
    uint? reveal;
    const uint PendingCard = uint.MaxValue;

    static RuleTemplate[] TemplatesFor(SmartScope shown) => SmartRuleEdits.TemplatesFor(shown);

    void RenderRules(SmartScope shown)
    {
        var rules = RulesOf(effective, shown);
        bool list = shown.IsActionList;
        string intro = list
            ? "Steps run top to bottom when something runs this list; each step waits its delay after the previous one."
            : shown.IsAreaTrigger ? "Rules run when a living player enters the trigger (the client reports it, the server re-checks the volume). An AreaTrigger has no object of its own: target the player (invoker) or creatures."
            : shown.IsSpawn ? "Rules for this one spawn. A spawn with its own rules ignores the rules for all spawns of this creature."
            : "Rules for every spawn of this creature. Rules run when their event happens; \"Then\" rules run right after the rule above.";
        var help = ClientStyle.Text(intro, 12, ClientStyle.Faded);
        help.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        page.AddChild(help);
        var shownPending = pending is { } p && p.Scope == shown ? p : null;
        var chains = SmartCatalog.Chains(rules);
        var roots = chains.Select(c => c[0]).ToList();
        foreach (var chain in chains)
        {
            for (int i = 0; i < chain.Count; i++)
            {
                RenderCard(shown, chain[i], i > 0, roots, list);
            }

            if (shownPending?.After is uint after && chain.Any(r => r.Id == after))
            {
                RenderPending(shown, shownPending);
            }
        }

        if (rules.Count == 0 && shownPending == null)
        {
            page.AddChild(ClientStyle.Text(list ? "No steps yet." : "No rules yet. Add one below.", 14, ClientStyle.Faded));
        }

        // A "Then" step whose rule disappeared (undo, another editor) still shows, so it can be discarded.
        if (shownPending != null && (shownPending.After == null || !rules.Any(r => r.Id == shownPending.After)))
        {
            RenderPending(shown, shownPending);
        }

        var bottom = new HBoxContainer();
        if (shownPending == null)
        {
            var add = ButtonIn(bottom, list ? "+ Add step" : "+ Add rule", () => StartPending(shown, TemplatesFor(shown)[^1], null));
            add.Name = "AddRule";
            add.TooltipText = "Build a new rule from any event, action and target the server supports.";
            var fromTemplate = ButtonIn(bottom, "+ From template…", () => ChooseTemplate(shown));
            fromTemplate.Name = "AddFromTemplate";
            fromTemplate.TooltipText = "Start a new rule from a ready-made example, then adjust it before adding.";
        }

        if (shown.IsSpawn && rules.Count == 0 && RulesOf(effective, EntryScope).Count > 0)
        {
            ButtonIn(bottom, "Copy rules from all spawns", () => CopyScope(EntryScope, shown));
        }

        page.AddChild(bottom);
    }

    static PanelContainer Card(bool linked, bool highlighted, bool isNew)
    {
        var card = new PanelContainer();
        var style = new StyleBoxFlat
        {
            BgColor = isNew ? new Color("1d3a36") : new Color(highlighted ? "1d303e" : "14212b"),
            BorderColor = isNew ? InspectorUi.Accent : new Color("27404f"),
            ContentMarginLeft = linked ? 34 : 8,
            ContentMarginRight = 8,
            ContentMarginTop = 4,
            ContentMarginBottom = 4
        };
        style.SetBorderWidthAll(isNew ? 2 : 1);
        style.SetCornerRadiusAll(4);
        card.AddThemeStyleboxOverride("panel", style);
        return card;
    }

    void RenderCard(SmartScope shown, SmartRule rule, bool linked, List<SmartRule> roots, bool list)
    {
        var card = Card(linked, expanded == rule.Id, false);
        card.Name = $"Rule{rule.Id}";
        page.AddChild(card);
        if (reveal == rule.Id)
        {
            RevealLater(card);
        }

        var box = new VBoxContainer();
        card.AddChild(box);
        var header = new HBoxContainer();
        box.AddChild(header);
        bool edited = snapshot!.Rows.Get(smart.Scripts, smart.Scripts.KeyOf(rule.Row)) != rule.Row;
        string sentence = (linked ? "↳ " : "") + smart.Describe(rule, snapshot.Names, entry);
        var label = ClientStyle.Text(sentence, 14, edited ? ClientStyle.Edited : ClientStyle.Body);
        label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        label.TooltipText = $"Rule {rule.Id}{(rule.Comment.Length > 0 ? " · " + rule.Comment : "")}{(edited ? " · edited in draft" : "")}";
        label.MouseFilter = MouseFilterEnum.Pass;
        header.AddChild(label);
        var edit = ButtonIn(header, expanded == rule.Id ? "Done" : "Edit", () =>
        {
            expanded = expanded == rule.Id ? null : rule.Id;
            Render();
        });
        edit.Name = $"Edit{rule.Id}";
        edit.TooltipText = expanded == rule.Id ? "Collapse this rule." : "Expand this rule to change its event, action and target.";
        if (!linked)
        {
            int index = roots.FindIndex(r => r.Id == rule.Id);
            var upButton = ButtonIn(header, "▲", () => Swap(shown, rule, roots[index - 1]));
            upButton.Disabled = index <= 0;
            upButton.TooltipText = list ? "Run this step earlier" : "Check this rule earlier";
            var downButton = ButtonIn(header, "▼", () => Swap(shown, rule, roots[index + 1]));
            downButton.Disabled = index < 0 || index >= roots.Count - 1;
            downButton.TooltipText = list ? "Run this step later" : "Check this rule later";
            ButtonIn(header, "Copy", () => Duplicate(shown, rule)).TooltipText = "Duplicate this rule and its \"Then\" steps";
        }

        var then = ButtonIn(header, "+ Then", () => StartThen(shown, rule));
        then.Name = $"Then{rule.Id}";
        then.TooltipText = "Add an action that runs right after this one";
        var delete = ButtonIn(header, "Delete", () => Delete(shown, rule));
        delete.Name = $"Delete{rule.Id}";
        delete.TooltipText = "Delete this rule (and its \"Then\" steps).";
        if (expanded == rule.Id)
        {
            RenderEditor(box, shown, rule, linked, false);
        }
    }

    /// <summary>
    /// The pending rule: same editor as an existing rule, with a live sentence, problems that would
    /// block Add, and Add / Template / Discard.
    /// </summary>
    void RenderPending(SmartScope shown, PendingRule p)
    {
        bool linked = p.After != null;
        var card = Card(linked, true, true);
        card.Name = "PendingRule";
        page.AddChild(card);
        if (reveal == PendingCard)
        {
            RevealLater(card);
        }

        var box = new VBoxContainer();
        card.AddChild(box);
        var header = new HBoxContainer();
        box.AddChild(header);
        string noun = linked ? "\"Then\" step" : shown.IsActionList ? "step" : "rule";
        var title = ClientStyle.Text($"New {noun}", 16, ClientStyle.Header);
        title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        header.AddChild(title);
        if (!linked)
        {
            var template = ButtonIn(header, "Template…", () => ChooseTemplate(shown));
            template.Name = "PendingTemplate";
            template.TooltipText = "Replace this new rule with a ready-made example";
        }

        var add = ButtonIn(header, $"Add {noun}", () => AddPending());
        add.Name = "PendingAdd";
        add.TooltipText = $"Save this {noun} to the draft.";
        var discard = ButtonIn(header, "Discard", () =>
        {
            pending = null;
            pendingError = "";
            Render();
        });
        discard.Name = "PendingDiscard";
        discard.TooltipText = $"Throw away this unsaved {noun}.";
        var rule = new SmartRule(p.Row);
        var sentence = ClientStyle.Text("Reads: " + (linked ? "↳ " : "") + smart.Describe(rule, snapshot!.Names, entry), 14, ClientStyle.Edited);
        sentence.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        sentence.Name = "PendingPreview";
        box.AddChild(sentence);
        foreach (string problem in PendingProblems(p).Prepend(pendingError).Where(m => m.Length > 0).Distinct())
        {
            var label = ClientStyle.Text("⛔ " + problem, 12, ClientStyle.Error);
            label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            label.Name = "PendingProblem";
            box.AddChild(label);
        }

        RenderEditor(box, shown, rule, linked, true);
    }

    void RenderEditor(VBoxContainer box, SmartScope shown, SmartRule rule, bool linked, bool isNew)
    {
        var e = smart.Event(rule.Event);
        var a = smart.Action(rule.Action);
        var t = smart.Target(rule.Target);
        bool Put(string label, params (string Column, object Value)[] values) => Change(rule, isNew, label, values);
        if (linked)
        {
            box.AddChild(EditorFields.Row("When", "This step does not have its own event: it runs right after the step above.", ClientStyle.Text("right after the step above", 14)));
        }
        else if (shown.IsActionList && rule.Event is 0 or 1 or 60)
        {
            var wait = EditorFields.Decimal(rule.EventParam(1) / 1000.0, seconds =>
            {
                uint ms = (uint)Math.Round(Math.Clamp(seconds, 0, uint.MaxValue / 1000.0) * 1000);
                Put("Change step delay", ("event_param1", ms), ("event_param2", ms));
            }, 60, "How many seconds to wait after the previous step before this step runs.");
            wait.Name = "StepDelay";
            box.AddChild(EditorFields.Row("Wait", "How long this step waits after the previous step, in seconds.", wait, ClientStyle.Tag("seconds after the previous step")));
        }
        else
        {
            box.AddChild(EditorFields.Row("When", "The event that starts this rule, such as entering combat, a health percentage or a spell hit.", TypeChooser(rule.Event, "event", id => ChangeType(rule, isNew, "event", id))));
            box.AddChild(Parameters(rule, isNew, e, "event_param", rule.EventParam, smart.EventParamCount));
            RenderStringParam(box, rule, isNew, e, "event_param_string", rule.EventString);
        }

        box.AddChild(EditorFields.Row("Do", "The action this rule performs when its event fires, such as casting a spell, talking or summoning a creature.", TypeChooser(rule.Action, "action", id => ChangeType(rule, isNew, "action", id))));
        box.AddChild(Parameters(rule, isNew, a, "action_param", rule.ActionParam, smart.ActionParamCount));
        RenderStringParam(box, rule, isNew, a, "action_param_string", rule.ActionString);
        box.AddChild(EditorFields.Row("On", "Who or what the action applies to, such as itself, its victim or a random player.", TypeChooser(rule.Target, "target", id => ChangeType(rule, isNew, "target", id))));
        box.AddChild(Parameters(rule, isNew, t, "target_param", i => i <= SmartCatalog.TargetParamCount ? rule.TargetParam(i) : 0, SmartCatalog.TargetParamCount));
        RenderStringParam(box, rule, isNew, t, "target_param_string", rule.TargetString);
        if (t != null && (t.Sentence.Contains("{x}", StringComparison.Ordinal) || t.Help.Contains("xyz", StringComparison.OrdinalIgnoreCase)))
        {
            box.AddChild(EditorFields.Row("Position", "The world position the target uses, in map coordinates, plus the facing angle in radians.",
                ClientStyle.Tag("x"), EditorFields.Decimal(rule.X, v => Put("Change target position", ("target_x", (float)v)), 80, "World X coordinate."),
                ClientStyle.Tag("y"), EditorFields.Decimal(rule.Y, v => Put("Change target position", ("target_y", (float)v)), 80, "World Y coordinate."),
                ClientStyle.Tag("z"), EditorFields.Decimal(rule.Z, v => Put("Change target position", ("target_z", (float)v)), 70, "World Z coordinate (height)."),
                ClientStyle.Tag("facing"), EditorFields.Decimal(rule.O, v => Put("Change target facing", ("target_o", (float)v)), 50, "Facing angle in radians.")));
        }

        if (!linked)
        {
            var once = new CheckBox
            {
                Text = "Only once",
                ButtonPressed = (rule.Flags & 1) != 0,
                TooltipText = "Run once per spawn (until it respawns, or resets unless \"Keep after reset\")."
            };
            once.Toggled += on => Put("Change only-once", ("event_flags", on ? rule.Flags | 1 : rule.Flags & ~1u));
            box.AddChild(EditorFields.Row("Chance", "The percent chance the rule runs each time its event fires. 100 always runs it; 0 never runs it.",
                EditorFields.Number(rule.Chance, value => Put("Change chance", ("event_chance", (uint)Math.Clamp(value, 0, uint.MaxValue))), 50, "Percent chance from 0 to 100; 100 always runs it."), ClientStyle.Tag("%"), once));
            var phases = Enumerable.Range(1, 12).Select(p => (1u << (p - 1), p.ToString(CultureInfo.InvariantCulture)));
            var phaseRow = EditorFields.Row("Only in phases", "Tick the phases in which the rule may run. None ticked means every phase.");
            phaseRow.AddChild(EditorFields.Flags(phases, rule.Phases, value => Put("Change phases", ("event_phase_mask", value)), 12, "Tick each phase where this rule may run; none ticked means every phase."));
            phaseRow.AddChild(ClientStyle.Tag(rule.Phases == 0 ? "(none ticked = always)" : ""));
            box.AddChild(phaseRow);
            box.AddChild(EditorFields.Row(smart.HasStringParams ? "More options" : "Difficulty & more", "Extra rule flags such as only once, keep after reset, debug builds only, or difficulty-specific flags.",
                EditorFields.Flags(smart.EventFlags.Where(f => f.Bit != 1), rule.Flags, value => Put("Change rule options", ("event_flags", value)), 4, "Extra rule flags; the raw value keeps every bit the list does not name.")));
            if (smart.HasStringParams)
            {
                var difficulties = EditorFields.Line(rule.Row.T("Difficulties"), text => Put("Change difficulties", ("Difficulties", text.Trim())), "Difficulty IDs separated by commas; empty = every difficulty", 100, "Comma-separated difficulty IDs where the rule runs; empty means every difficulty.");
                difficulties.Name = "Difficulties";
                box.AddChild(EditorFields.Row("Difficulties", "Comma-separated difficulty IDs where the rule runs; empty means every difficulty.", difficulties));
            }
        }

        var comment = EditorFields.Line(rule.Comment, text => Put("Edit note", ("comment", text)), isNew ? "Note for other editors (written automatically if left empty)" : "Note for other editors", 255, "A short note shown in the collapsed rule list. Auto writes one from the event, action and target.");
        comment.Name = "Note";
        box.AddChild(EditorFields.Row("Note", "A note for other editors, shown under the collapsed rule sentence. Auto writes one for you.",
            comment, ButtonOf("Auto", () => Put("Write note automatically", ("comment", smart.AutoComment(rule, snapshot!.CreatureName))))));
        foreach (var type in new[] { e, a, t }.Where(x => x is { ParamsKnown: false }))
        {
            var help = ClientStyle.Text($"{type!.Name}: {type.Help}", 11, ClientStyle.Faded);
            help.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            box.AddChild(help);
        }
    }

    /// <summary>
    /// A text field for TrinityCore's string parameter (a script's trigger name, a StringId) when the type
    /// reads one, or when the rule already holds a value so it stays visible.
    /// </summary>
    void RenderStringParam(VBoxContainer box, SmartRule rule, bool isNew, SmartType? type, string column, string value)
    {
        if (!smart.HasStringParams || value.Length == 0 && (type == null || !type.Help.Contains("string", StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var line = EditorFields.Line(value, text => Change(rule, isNew, "Change string parameter", (column, text.Trim())), "Text parameter (for example a StringId)", 255, "A text parameter for this type, such as a script trigger name or a StringId.");
        line.Name = "Str" + column;
        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 130);
        var field = new HBoxContainer();
        var caption = ClientStyle.Tag("Text");
        caption.TooltipText = "A text parameter for this type, such as a script trigger name or a StringId.";
        caption.MouseFilter = MouseFilterEnum.Stop;
        field.AddChild(caption);
        line.TooltipText = caption.TooltipText;
        field.AddChild(line);
        margin.AddChild(field);
        box.AddChild(margin);
    }

    /// <summary>
    /// Every selectable type of a kind for the chooser. "Then (linked)" is not offered as an event:
    /// "+ Then" creates linked steps so their link stays consistent.
    /// </summary>
    IEnumerable<SmartType> TypesOf(string kind) => kind switch
    {
        // AreaTrigger scripts load only "On trigger" (and linked "Then") events.
        "event" when scope is { IsAreaTrigger: true } => smart.Events.Where(x => x.Id == SmartCatalog.AreaTriggerEvent),
        "event" => smart.Events.Where(x => x.Id != SmartCatalog.LinkEvent),
        "action" => smart.Actions,
        _ => smart.Targets
    };

    SmartType? TypeOf(string kind, uint id) => kind switch
    {
        "event" => smart.Event(id),
        "action" => smart.Action(id),
        _ => smart.Target(id)
    };

    /// <summary>
    /// One button per field showing the chosen type; it opens a searchable picker of every type of
    /// the kind, by category (search covers names, sentences, categories, server codes and IDs).
    /// The picker belongs to the window, not the page, so a re-render cannot close it mid-choice.
    /// </summary>
    Button TypeChooser(uint id, string kind, Action<uint> choose)
    {
        var type = TypeOf(kind, id);
        var button = new Button
        {
            Text = (type?.Name ?? $"Unknown {kind} {id}") + "  ▾",
            CustomMinimumSize = new(280, 0),
            Alignment = HorizontalAlignment.Left,
            TooltipText = type == null ? "Not a type this server knows; choose another." : type.Sentence.Length > 0 ? type.Sentence : type.Help,
            Name = "Choose" + char.ToUpperInvariant(kind[0]) + kind[1..]
        };
        if (type == null)
        {
            button.AddThemeColorOverride("font_color", ClientStyle.Error);
        }

        var all = SmartCatalog.Grouped(TypesOf(kind), kind).SelectMany(g => g.Types.Select(t => (g.Category, Type: t))).ToList();
        button.Pressed += () => PickerPopup.Show(this, $"Choose {kind}", term => Task.FromResult<IReadOnlyList<(uint Id, string Name)>>(all
            .Where(x => term.Length == 0 || x.Type.Name.Contains(term, StringComparison.OrdinalIgnoreCase) || x.Type.Code.Contains(term, StringComparison.OrdinalIgnoreCase) || x.Type.Sentence.Contains(term, StringComparison.OrdinalIgnoreCase) || x.Category.Contains(term, StringComparison.OrdinalIgnoreCase) || x.Type.Id.ToString(CultureInfo.InvariantCulture) == term.Trim())
            .Select(x => ((uint)x.Type.Id, $"{x.Category}: {x.Type.Name}{(x.Type.Sentence.Length > 0 ? " — " + x.Type.Sentence : "")}")).ToList()), (picked, _) => choose(picked), current: id, singleClick: true);
        return button;
    }

    /// <summary>
    /// Pick a template for a new rule in this scope; it opens (or replaces) the pending card.
    /// </summary>
    void ChooseTemplate(SmartScope shown)
    {
        var templates = TemplatesFor(shown);
        PickerPopup.Show(this, shown.IsActionList ? "Start a step from a template" : "Start a rule from a template", term => Task.FromResult<IReadOnlyList<(uint Id, string Name)>>(templates
            .Select((t, i) => ((uint)i, t.Label)).Where(x => x.Label.Contains(term, StringComparison.OrdinalIgnoreCase)).ToList()), (index, _) => StartPending(shown, templates[(int)index], null), singleClick: true);
    }

    /// <summary>
    /// Open the pending card for a new rule (or a "Then" step after <paramref name="after"/>).
    /// </summary>
    void StartPending(SmartScope shown, RuleTemplate template, uint? after)
    {
        var row = SmartRuleEdits.FromTemplate(smart, effective, shown, entry, snapshot!.CreatureName, template);
        pending = new PendingRule(shown, row.With("comment", ""), after);
        pendingError = "";
        expanded = null;
        reveal = PendingCard;
        Render();
    }

    void StartThen(SmartScope shown, SmartRule rule) => StartPending(shown, SmartRuleEdits.ThenTemplate(shown), rule.Id);

    /// <summary>
    /// Errors the pending rule would introduce if added now (the same check Commit applies).
    /// </summary>
    IEnumerable<string> PendingProblems(PendingRule p) => SmartRuleEdits.NewProblems(smart.Tables, effective, PendingEdits(p).Edits, CheckAll);

    /// <summary>
    /// The row and edits that add the pending rule: a fresh id, an automatic note when empty, the
    /// predecessor re-linked for a "Then" step, and SmartAI/SmartTrigger enabled for a first rule.
    /// No edits when a "Then" step's rule no longer exists.
    /// </summary>
    (Row Row, RowEdit[] Edits) PendingEdits(PendingRule p) => SmartRuleEdits.PendingEdits(smart, effective, p.Scope, p.Row, p.After, snapshot!.CreatureName, EnableSmartAiIfDefault);

    /// <summary>
    /// Draft the pending rule as one command; on success it becomes a normal, expanded card.
    /// </summary>
    SmartRule? AddPending()
    {
        if (pending is not { } p || snapshot == null)
        {
            return null;
        }

        var (row, edits) = PendingEdits(p);
        if (edits.Length == 0)
        {
            pendingError = "The rule this step follows no longer exists; discard it or add it to another rule.";
            Render();
            return null;
        }

        var rule = new SmartRule(row);
        string label = p.After != null ? "Add a \"Then\" step" : $"Add {(p.Scope.IsActionList ? "step" : "rule")}: {smart.Describe(rule, snapshot.Names, entry)}";
        if (!Commit(label, edits))
        {
            pendingError = status;
            status = "";
            Render();
            return null;
        }

        pending = null;
        pendingError = "";
        expanded = rule.Id;
        reveal = rule.Id;
        Render();
        return rule;
    }

}
