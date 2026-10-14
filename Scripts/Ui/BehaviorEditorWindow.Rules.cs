using System.Globalization;
using Godot;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// Rule cards. A card shows the rule as a sentence; expanding it shows When / Do / On with typed
/// fields. smart_scripts is keyed by (entryorguid, source_type, id, link), so edits that change id
/// or link are recorded as delete + insert within one command.
/// </summary>
public partial class BehaviorEditorWindow
{
    /// <summary>
    /// Starting points for new rules: (label, event, event params, action, action params, target).
    /// </summary>
    /// <summary>
    /// AreaTrigger starting points. "Set data on a creature" is the common pattern for starting a
    /// boss script's event (e.g. Utgarde Pinnacle 5140 sends data 1 = 1 to Svala); target 10 is a
    /// creature by spawn GUID, 19 the closest creature of an entry.
    /// </summary>
    static readonly (string Label, uint Event, uint[] EventParams, uint Action, uint[] ActionParams, uint Target)[] TriggerTemplates =
    [
        ("Send data to a creature (start its script event)", SmartCatalog.AreaTriggerEvent, [], 45, [1, 1], 10),
        ("Make the nearest creature of a kind say a line", SmartCatalog.AreaTriggerEvent, [], 1, [0], 19),
        ("Cast a spell on the player who entered", SmartCatalog.AreaTriggerEvent, [], 11, [], 7),
        ("Make the nearest creature of a kind play a scene (action list)", SmartCatalog.AreaTriggerEvent, [], SmartCatalog.RunActionList, [], 19),
        ("Blank rule", SmartCatalog.AreaTriggerEvent, [], 0, [], 7)
    ];

    static readonly (string Label, uint Event, uint[] EventParams, uint Action, uint[] ActionParams, uint Target)[] RuleTemplates =
    [
        ("Say something when it enters combat", 4, [], 1, [0], 1),
        ("Cast a spell on its victim every few seconds", 0, [2000, 4000, 8000, 12000], 11, [], 2),
        ("Cast a spell when its health drops below 30%", 2, [0, 30, 0, 0], 11, [], 1),
        ("Say something when it dies", 6, [], 1, [0], 1),
        ("Wave back when a player waves", 22, [101, 2000, 2000], 5, [3], 1),
        ("Play a scene (action list) when a quest is completed", 20, [], SmartCatalog.RunActionList, [], 1),
        ("Do something every few seconds out of combat", 1, [5000, 10000, 15000, 30000], 5, [], 1),
        ("Blank rule", 4, [], 0, [], 1)
    ];

    bool composing;
    /// <summary>
    /// The composer card currently shown (re-created on each render; for diagnostics).
    /// </summary>
    PanelContainer? composerCard;
    uint composeEvent = 4, composeAction = 1, composeTarget = 1;
    double composeDelay;

    static readonly (string Label, uint Action, uint[] ActionParams, uint Target)[] StepTemplates =
    [
        ("Say a line", 1, [0], 1),
        ("Play an emote", 5, [], 1),
        ("Cast a spell", 11, [], 1),
        ("Walk to a position", 69, [], 8),
        ("Blank step", 0, [], 1)
    ];

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
        var chains = SmartCatalog.Chains(rules);
        var roots = chains.Select(c => c[0]).ToList();
        foreach (var chain in chains)
        {
            for (int i = 0; i < chain.Count; i++)
            {
                RenderCard(shown, chain[i], i > 0, roots, list);
            }
        }

        if (rules.Count == 0)
        {
            page.AddChild(ClientStyle.Text(list ? "No steps yet." : "No rules yet. Add one below.", 14, ClientStyle.Faded));
        }

        if (composing)
        {
            RenderComposer(shown, list);
        }

        var bottom = new HBoxContainer();
        if (!composing)
        {
            var add = ButtonOf(list ? "+ Add step" : "+ Add rule", () =>
            {
                composing = true;
                expanded = null;
                Render();
            });
            add.Name = "AddRule";
            add.TooltipText = "Build a rule from any event, action and target the server supports.";
            bottom.AddChild(add);
        }

        bottom.AddChild(TemplatesButton(shown, list));
        if (shown.IsSpawn && rules.Count == 0 && RulesOf(effective, EntryScope).Count > 0)
        {
            ButtonIn(bottom, "Copy rules from all spawns", () => CopyScope(EntryScope, shown));
        }

        page.AddChild(bottom);
    }

    void RenderCard(SmartScope shown, SmartRule rule, bool linked, List<SmartRule> roots, bool list)
    {
        var card = new PanelContainer();
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.36f, 0.25f, 0.12f, expanded == rule.Id ? 0.16f : 0.08f),
            BorderColor = new Color(0.36f, 0.25f, 0.12f, 0.5f),
            ContentMarginLeft = linked ? 34 : 8,
            ContentMarginRight = 8,
            ContentMarginTop = 4,
            ContentMarginBottom = 4
        };
        style.SetBorderWidthAll(1);
        style.SetCornerRadiusAll(4);
        card.AddThemeStyleboxOverride("panel", style);
        page.AddChild(card);
        var box = new VBoxContainer();
        card.AddChild(box);
        var header = new HBoxContainer();
        box.AddChild(header);
        bool edited = snapshot!.Rows.Get(InteractionTables.SmartScripts, rule.Key) != rule.Row;
        string sentence = (linked ? "↳ " : "") + SmartCatalog.Describe(rule, snapshot.Names, entry);
        var label = ClientStyle.Text(sentence, 14, edited ? ClientStyle.Edited : ClientStyle.Ink);
        label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        label.TooltipText = $"Rule {rule.Id}{(rule.Comment.Length > 0 ? " · " + rule.Comment : "")}{(edited ? " · edited in draft" : "")}";
        label.MouseFilter = MouseFilterEnum.Pass;
        header.AddChild(label);
        ButtonIn(header, expanded == rule.Id ? "Done" : "Edit", () =>
        {
            expanded = expanded == rule.Id ? null : rule.Id;
            Render();
        }).Name = $"Edit{rule.Id}";
        if (!linked)
        {
            int index = roots.FindIndex(r => r.Id == rule.Id);
            var upButton = ButtonIn(header, "▲", () => Swap(shown, rule, roots[index - 1]));
            upButton.Disabled = index <= 0;
            upButton.TooltipText = list ? "Run this step earlier" : "Check this rule earlier";
            var downButton = ButtonIn(header, "▼", () => Swap(shown, rule, roots[index + 1]));
            downButton.Disabled = index < 0 || index >= roots.Count - 1;
            ButtonIn(header, "Copy", () => Duplicate(shown, rule)).TooltipText = "Duplicate this rule and its \"Then\" steps";
        }

        ButtonIn(header, "+ Then", () => AddThen(shown, rule)).TooltipText = "Add an action that runs right after this one";
        ButtonIn(header, "Delete", () => Delete(shown, rule));
        if (expanded == rule.Id)
        {
            RenderEditor(box, shown, rule, linked);
        }
    }

    void RenderEditor(VBoxContainer box, SmartScope shown, SmartRule rule, bool linked)
    {
        var e = SmartCatalog.Event(rule.Event);
        var a = SmartCatalog.Action(rule.Action);
        var t = SmartCatalog.Target(rule.Target);
        if (linked)
        {
            box.AddChild(EditorFields.Row("When", ClientStyle.Text("right after the step above", 14)));
        }
        else if (shown.IsActionList && rule.Event is 0 or 1 or 60)
        {
            box.AddChild(EditorFields.Row("Wait", EditorFields.Decimal(rule.EventParam(1) / 1000.0, seconds =>
            {
                uint ms = (uint)Math.Round(Math.Max(0, seconds) * 1000);
                Update(rule, "Change step delay", ("event_param1", ms), ("event_param2", ms));
            }, 60), ClientStyle.Tag("seconds after the previous step")));
        }
        else
        {
            box.AddChild(EditorFields.Row("When", TypeChooser(rule.Event, "event", id => ChangeType(rule, "event", id))));
            box.AddChild(Parameters(rule, e, "event_param", rule.EventParam, 6));
        }

        box.AddChild(EditorFields.Row("Do", TypeChooser(rule.Action, "action", id => ChangeType(rule, "action", id))));
        box.AddChild(Parameters(rule, a, "action_param", rule.ActionParam, 6));
        box.AddChild(EditorFields.Row("On", TypeChooser(rule.Target, "target", id => ChangeType(rule, "target", id))));
        box.AddChild(Parameters(rule, t, "target_param", i => i <= 4 ? rule.TargetParam(i) : 0, 4));
        if (t != null && (t.Sentence.Contains("{x}", StringComparison.Ordinal) || t.Help.Contains("xyz", StringComparison.OrdinalIgnoreCase)))
        {
            box.AddChild(EditorFields.Row("Position",
                ClientStyle.Tag("x"), EditorFields.Decimal(rule.X, v => Update(rule, "Change target position", ("target_x", (float)v)), 80),
                ClientStyle.Tag("y"), EditorFields.Decimal(rule.Y, v => Update(rule, "Change target position", ("target_y", (float)v)), 80),
                ClientStyle.Tag("z"), EditorFields.Decimal(rule.Z, v => Update(rule, "Change target position", ("target_z", (float)v)), 70),
                ClientStyle.Tag("facing"), EditorFields.Decimal(rule.O, v => Update(rule, "Change target facing", ("target_o", (float)v)), 50)));
        }

        if (!linked)
        {
            var once = new CheckBox
            {
                Text = "Only once",
                ButtonPressed = (rule.Flags & 1) != 0,
                TooltipText = "Run once per spawn (until it respawns, or resets unless \"Keep after reset\")."
            };
            once.Toggled += on => Update(rule, "Change only-once", ("event_flags", on ? rule.Flags | 1 : rule.Flags & ~1u));
            box.AddChild(EditorFields.Row("Chance", EditorFields.Number(rule.Chance, value => Update(rule, "Change chance", ("event_chance", value)), 50), ClientStyle.Tag("%"), once));
            var phases = Enumerable.Range(1, 12).Select(p => (1u << (p - 1), p.ToString(CultureInfo.InvariantCulture)));
            var phaseRow = EditorFields.Row("Only in phases");
            phaseRow.AddChild(EditorFields.Flags(phases, rule.Phases, value => Update(rule, "Change phases", ("event_phase_mask", value)), 12));
            phaseRow.AddChild(ClientStyle.Tag(rule.Phases == 0 ? "(none ticked = always)" : ""));
            box.AddChild(phaseRow);
            box.AddChild(EditorFields.Row("Difficulty & more", EditorFields.Flags(SmartCatalog.EventFlags.Where(f => f.Bit != 1), rule.Flags, value => Update(rule, "Change rule options", ("event_flags", value)), 4)));
        }

        var comment = EditorFields.Line(rule.Comment, text => Update(rule, "Edit note", ("comment", text)), "Note for other editors", 255);
        box.AddChild(EditorFields.Row("Note", comment, ButtonOf("Auto", () => Update(rule, "Write note automatically", ("comment", SmartCatalog.AutoComment(rule, snapshot!.CreatureName))))));
        foreach (var type in new[] { e, a, t }.Where(x => x is { ParamsKnown: false }))
        {
            var help = ClientStyle.Text($"{type!.Name}: {type.Help}", 11, ClientStyle.Faded);
            help.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            box.AddChild(help);
        }
    }

    /// <summary>
    /// Every selectable type of a kind, for dropdowns and search. "Then (linked)" is not offered as
    /// an event: "+ Then" creates linked steps so their link stays consistent.
    /// </summary>
    IEnumerable<SmartType> TypesOf(string kind) => kind switch
    {
        // AreaTrigger scripts load only "On trigger" (and linked "Then") events.
        "event" when scope is { IsAreaTrigger: true } => SmartCatalog.Events.Where(x => x.Id == SmartCatalog.AreaTriggerEvent),
        "event" => SmartCatalog.Events.Where(x => x.Id != SmartCatalog.LinkEvent),
        "action" => SmartCatalog.Actions,
        _ => SmartCatalog.Targets
    };

    static SmartType? Lookup(string kind, uint id) => kind switch
    {
        "event" => SmartCatalog.Event(id),
        "action" => SmartCatalog.Action(id),
        _ => SmartCatalog.Target(id)
    };

    /// <summary>
    /// A dropdown of every type of a kind under category headings (hover shows what it does), plus
    /// a search button over names, sentences, categories, server codes and IDs.
    /// </summary>
    HBoxContainer TypeChooser(uint id, string kind, Action<uint> choose)
    {
        var row = new HBoxContainer();
        var picker = new OptionButton
        {
            FitToLongestItem = false,
            CustomMinimumSize = new(280, 0),
            Name = "Choose" + char.ToUpperInvariant(kind[0]) + kind[1..]
        };
        int selected = -1;
        if (Lookup(kind, id) == null)
        {
            picker.AddItem($"Unknown {kind} {id}");
            picker.SetItemMetadata(0, (long)id);
            selected = 0;
        }

        foreach (var (category, types) in SmartCatalog.Grouped(TypesOf(kind), kind))
        {
            picker.AddSeparator(category);
            foreach (var type in types)
            {
                picker.AddItem(type.Name);
                int index = picker.ItemCount - 1;
                picker.SetItemMetadata(index, (long)type.Id);
                picker.SetItemTooltip(index, type.Sentence.Length > 0 ? type.Sentence : type.Help);
                if (type.Id == id)
                {
                    selected = index;
                }
            }
        }

        picker.Select(selected);
        picker.ItemSelected += index => choose((uint)picker.GetItemMetadata((int)index).AsInt64());
        row.AddChild(picker);
        var all = SmartCatalog.Grouped(TypesOf(kind), kind).SelectMany(g => g.Types.Select(t => (g.Category, Type: t))).ToList();
        var search = ButtonOf("Search…", () => PickerPopup.Show(this, $"Find {kind}", term => Task.FromResult<IReadOnlyList<(uint Id, string Name)>>(all
            .Where(x => term.Length == 0 || x.Type.Name.Contains(term, StringComparison.OrdinalIgnoreCase) || x.Type.Code.Contains(term, StringComparison.OrdinalIgnoreCase) || x.Type.Sentence.Contains(term, StringComparison.OrdinalIgnoreCase) || x.Category.Contains(term, StringComparison.OrdinalIgnoreCase) || x.Type.Id.ToString(CultureInfo.InvariantCulture) == term.Trim())
            .Select(x => ((uint)x.Type.Id, $"{x.Category}: {x.Type.Name}{(x.Type.Sentence.Length > 0 ? " — " + x.Type.Sentence : "")}")).Take(200).ToList()), (picked, _) => choose(picked)));
        search.TooltipText = $"Search every {kind} by name, description or server code";
        row.AddChild(search);
        return row;
    }

    /// <summary>
    /// The new-rule composer: pick When / Do / On from the full lists, read the rule as a sentence,
    /// then add it (its details open for the parameters). Nothing is drafted until Add.
    /// </summary>
    void RenderComposer(SmartScope shown, bool list)
    {
        var card = new PanelContainer
        {
            Name = "Composer"
        };
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.98f, 0.93f, 0.78f, 0.9f),
            BorderColor = new Color(0.55f, 0.4f, 0.1f),
            ContentMarginLeft = 10,
            ContentMarginRight = 10,
            ContentMarginTop = 6,
            ContentMarginBottom = 6
        };
        style.SetBorderWidthAll(2);
        style.SetCornerRadiusAll(4);
        card.AddThemeStyleboxOverride("panel", style);
        page.AddChild(card);
        composerCard = card;
        var box = new VBoxContainer();
        card.AddChild(box);
        box.AddChild(ClientStyle.Text(list ? "New step" : "New rule", 16, ClientStyle.Header));
        if (list)
        {
            box.AddChild(EditorFields.Row("Wait", EditorFields.Decimal(composeDelay, seconds =>
            {
                composeDelay = Math.Max(0, seconds);
                Render();
            }, 60), ClientStyle.Tag("seconds after the previous step")));
        }
        else
        {
            box.AddChild(EditorFields.Row("When", TypeChooser(composeEvent, "event", id =>
            {
                composeEvent = id;
                Render();
            })));
        }

        box.AddChild(EditorFields.Row("Do", TypeChooser(composeAction, "action", id =>
        {
            composeAction = id;
            Render();
        })));
        box.AddChild(EditorFields.Row("On", TypeChooser(composeTarget, "target", id =>
        {
            composeTarget = id;
            Render();
        })));
        var preview = new SmartRule(ComposedRow(shown, list));
        var sentence = ClientStyle.Text("Reads: " + SmartCatalog.Describe(preview, snapshot!.Names, entry), 14, ClientStyle.Edited);
        sentence.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        sentence.Name = "ComposerPreview";
        box.AddChild(sentence);
        var chosen = new[] { list ? null : SmartCatalog.Event(composeEvent), SmartCatalog.Action(composeAction), SmartCatalog.Target(composeTarget) };
        foreach (var type in chosen.Where(t => t != null && t.Help.Length > 0 && !t.Help.Equals("NONE", StringComparison.OrdinalIgnoreCase)))
        {
            var help = ClientStyle.Text($"{type!.Name}: {type.Help}", 11, ClientStyle.Faded);
            help.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            box.AddChild(help);
        }

        box.AddChild(ClientStyle.Text("Details such as the spell, text or timing are set after adding.", 11, ClientStyle.Faded));
        var buttons = new HBoxContainer();
        var add = ButtonOf(list ? "Add step" : "Add rule", () => AddComposed(shown, list));
        add.Name = "ComposerAdd";
        buttons.AddChild(add);
        buttons.AddChild(ButtonOf("Cancel", () =>
        {
            composing = false;
            Render();
        }));
        box.AddChild(buttons);
    }

    Row ComposedRow(SmartScope shown, bool list)
    {
        uint delay = (uint)Math.Round(composeDelay * 1000);
        return Template(shown, NextId(shown), list ? 0 : composeEvent, list ? [delay, delay] : [], composeAction, [], composeTarget);
    }

    void AddComposed(SmartScope shown, bool list)
    {
        var rule = new SmartRule(ComposedRow(shown, list));
        composing = false;
        AddRule(shown, SmartCatalog.Describe(rule, snapshot!.Names, entry), rule.Event, Enumerable.Range(1, 6).Select(rule.EventParam).ToArray(), rule.Action, rule.Action == SmartCatalog.Talk ? [0] : [], rule.Target);
    }

    /// <summary>
    /// Ready-made rules, kept apart from Add so it is clear they are starting points.
    /// </summary>
    MenuButton TemplatesButton(SmartScope shown, bool list)
    {
        var templates = new MenuButton
        {
            Text = "Templates ▾",
            Flat = false,
            Name = "Templates",
            TooltipText = "Start from a ready-made rule, then adjust it."
        };
        var popup = templates.GetPopup();
        popup.AddSeparator("Start from a template");
        var rules = shown.IsAreaTrigger ? TriggerTemplates : RuleTemplates;
        var labels = list ? StepTemplates.Select(t => t.Label) : rules.Select(t => t.Label);
        int index = 0;
        foreach (string label in labels)
        {
            popup.AddItem(label, index++);
        }

        popup.IdPressed += id =>
        {
            composing = false;
            if (list)
            {
                var t = StepTemplates[id];
                AddRule(shown, t.Label, 0, [], t.Action, t.ActionParams, t.Target);
            }
            else
            {
                var t = rules[id];
                AddRule(shown, t.Label, t.Event, t.EventParams, t.Action, t.ActionParams, t.Target);
            }
        };
        return templates;
    }

    /// <summary>
    /// Labelled fields for a type's parameters; unknown types show raw numbered fields.
    /// </summary>
    Control Parameters(SmartRule rule, SmartType? type, string prefix, Func<int, uint> value, int max)
    {
        var flow = new HFlowContainer();
        int count = type == null || !type.ParamsKnown && type.Params.Length == 0 ? 0 : Math.Min(type.Params.Length, max);
        if (type == null)
        {
            count = max;
        }

        for (int i = 1; i <= count; i++)
        {
            var param = type != null && i <= type.Params.Length ? type.Params[i - 1] : new SmartParam($"Param {i}", SmartParamKind.Number);
            var field = new HBoxContainer();
            // Times are edited in seconds; drop the server comment's "(ms)" unit from the caption.
            var caption = ClientStyle.Tag(param.Kind == SmartParamKind.Ms ? param.Label.Replace(" (ms)", "", StringComparison.Ordinal) : param.Label);
            caption.CustomMinimumSize = new(0, 0);
            field.AddChild(caption);
            field.AddChild(ParamControl(rule, param, $"{prefix}{i}", value(i)));
            flow.AddChild(field);
        }

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 130);
        margin.AddChild(flow);
        return margin;
    }

    Control ParamControl(SmartRule rule, SmartParam param, string column, uint value)
    {
        string label = $"Change {param.Label.ToLowerInvariant()}";
        void Put(uint v) => Update(rule, label, (column, v));
        HBoxContainer With(Control control, string unit)
        {
            var row = new HBoxContainer();
            row.AddChild(control);
            row.AddChild(ClientStyle.Tag(unit));
            return row;
        }

        var names = snapshot!.Names;
        string text = SmartCatalog.FormatValue(param.Kind, value, names, entry);
        switch (param.Kind)
        {
            case SmartParamKind.Ms:
                return With(EditorFields.Decimal(value / 1000.0, s => Put((uint)Math.Round(Math.Clamp(s, 0, uint.MaxValue / 1000.0) * 1000)), 60), "s");
            case SmartParamKind.Percent:
                return With(EditorFields.Number(value, v => Put((uint)v), 50), "%");
            case SmartParamKind.Yards:
                return With(EditorFields.Number(value, v => Put((uint)v), 50), "yd");
            case SmartParamKind.Bool:
                var check = new CheckBox
                {
                    ButtonPressed = value != 0
                };
                check.Toggled += on => Put(on ? 1u : 0u);
                return check;
            case SmartParamKind.Spell:
                return EditorFields.Reference(value == 0 ? "None" : text, () => PickerPopup.Show(this, $"Choose {param.Label.ToLowerInvariant()}", term => Task.FromResult(catalog?.SearchSpells(term) ?? []), (id, _) => Put(id)), value == 0 ? null : () => Put(0), value != 0 && names.Spell(value) == null);
            case SmartParamKind.Creature or SmartParamKind.GameObject or SmartParamKind.Quest or SmartParamKind.Item:
                return EditorFields.Reference(value == 0 ? "None" : text, () => PickNamed(param.Kind, param.Label, Put), value == 0 ? null : () => Put(0), value != 0 && text.StartsWith(param.Kind.ToString().ToLowerInvariant(), StringComparison.Ordinal));
            case SmartParamKind.Emote or SmartParamKind.TextEmote or SmartParamKind.FactionTemplate:
                var all = (catalog?.All(param.Kind.ToString()) ?? []).Where(x => x.Name.Length > 0).ToList();
                return EditorFields.Reference(text, () => PickerPopup.Show(this, $"Choose {param.Label.ToLowerInvariant()}", term => Task.FromResult<IReadOnlyList<(uint Id, string Name)>>(all
                    .Where(x => term.Length == 0 || x.Name.Contains(term, StringComparison.OrdinalIgnoreCase) || x.Id.ToString(CultureInfo.InvariantCulture) == term.Trim()).OrderBy(x => x.Name.Length).Take(200).ToList()), (id, _) => Put(id)), value == 0 ? null : () => Put(0));
            case SmartParamKind.TextGroup:
                return TextGroupChoice(value, Put);
            case SmartParamKind.ActionList:
                return ActionListControl(rule, value, Put);
            default:
                return EditorFields.Number(value, v => Put((uint)v), 70, param.Kind == SmartParamKind.Flags ? "Bit flags; 0x hex accepted" : "");
        }
    }

    void PickNamed(SmartParamKind kind, string caption, Action<uint> apply)
    {
        var names = snapshot?.Names;
        PickerPopup.Show(this, $"Choose {caption.ToLowerInvariant()}", term => new ContentSource(ConnectionPath()).SearchAsync(kind, term, CancellationToken.None), (id, name) =>
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

    Control TextGroupChoice(uint value, Action<uint> put)
    {
        var groups = effective.All(InteractionTables.CreatureText).Where(t => t.U("CreatureID") == entry).GroupBy(t => t.U("GroupID")).OrderBy(g => g.Key)
            .Select(g => (g.Key, $"{g.Key}: \"{Shorten(g.OrderBy(t => t.U("ID")).First().T("Text"))}\"{(g.Count() > 1 ? $" (+{g.Count() - 1})" : "")}")).ToList();
        var row = new HBoxContainer();
        row.AddChild(EditorFields.Choice(groups, value, put));
        row.AddChild(ButtonOf("Edit lines…", () => ShowScope(null)));
        return row;
    }

    static string Shorten(string text) => text.Length > 48 ? text[..45] + "…" : text;

    Control ActionListControl(SmartRule rule, uint value, Action<uint> put)
    {
        var row = new HBoxContainer();
        if (value != 0)
        {
            int steps = RulesOf(effective, new SmartScope((int)value, SmartScope.ActionList)).Count;
            row.AddChild(ClientStyle.Text($"#{value} · {steps} step(s)", 14, steps == 0 ? ClientStyle.Warning : null));
            row.AddChild(ButtonOf("Open", () => ShowScope(new SmartScope((int)value, SmartScope.ActionList))));
        }

        var create = ButtonOf(value == 0 ? "Create list" : "New list", () => _ = CreateActionList(rule));
        create.TooltipText = "Create an empty action list with a free ID and run it from this rule.";
        row.AddChild(create);
        row.AddChild(EditorFields.Number(value, v => put((uint)v), 80, "Or type an existing list ID"));
        return row;
    }

    /// <summary>
    /// Allocate an action list ID in the conventional entry×100 range that neither the database nor
    /// the draft uses, point the rule at it and open it.
    /// </summary>
    async Task CreateActionList(SmartRule rule)
    {
        try
        {
            int first = (int)Math.Min(entry * 100L, int.MaxValue - 100);
            var used = await new ContentSource(ConnectionPath()).UsedActionListsAsync(first, first + 99, CancellationToken.None);
            used.UnionWith(effective.All(InteractionTables.SmartScripts).Where(r => r.U("source_type") == SmartScope.ActionList).Select(r => r.I("entryorguid")));
            used.UnionWith(effective.All(InteractionTables.SmartScripts).Select(r => new SmartRule(r)).Where(r => r.Action == SmartCatalog.RunActionList).Select(r => (int)r.ActionParam(1)));
            int id = Enumerable.Range(first, 100).FirstOrDefault(i => !used.Contains(i));
            if (id == 0)
            {
                status = "No free action list ID near this creature's entry; type an ID instead.";
                UpdateStatus();
                return;
            }

            var current = effective.Get(InteractionTables.SmartScripts, rule.Key);
            if (current == null)
            {
                return;
            }

            var list = new SmartScope(id, SmartScope.ActionList);
            var step = SmartCatalog.NewRow(list, 0, 0, 0, 1, "First step");
            if (Commit($"Create action list #{id}", new RowEdit(InteractionTables.SmartScripts.Name, rule.Key, current, current.With("action_param1", (uint)id)), new RowEdit(InteractionTables.SmartScripts.Name, InteractionTables.SmartScripts.KeyOf(step), null, step)))
            {
                ShowScope(list);
                expanded = 0;
                Render();
            }
        }
        catch (Exception error)
        {
            status = CreatureSource.SafeError(error);
            UpdateStatus();
        }
    }

    /// <summary>
    /// Change columns of one rule. A change to the key (id/link) becomes delete + insert.
    /// </summary>
    bool Update(SmartRule rule, string label, params (string Column, object Value)[] values)
    {
        var current = effective.Get(InteractionTables.SmartScripts, rule.Key);
        if (current == null)
        {
            return false;
        }

        var after = current;
        foreach (var (column, value) in values)
        {
            after = after.With(column, value);
        }

        return after == current || Commit(label, Rekey([(current, after)]));
    }

    /// <summary>
    /// Edits replacing rows whose key may change: all deletes first, then all inserts, so swapped
    /// keys never collide inside the command.
    /// </summary>
    static RowEdit[] Rekey(IEnumerable<(Row Before, Row After)> changes)
    {
        var table = InteractionTables.SmartScripts;
        var list = changes.Where(c => c.Before != c.After).ToList();
        if (list.All(c => table.KeyOf(c.Before) == table.KeyOf(c.After)))
        {
            return list.Select(c => new RowEdit(table.Name, table.KeyOf(c.Before), c.Before, c.After)).ToArray();
        }

        return list.Select(c => new RowEdit(table.Name, table.KeyOf(c.Before), c.Before, null))
            .Concat(list.Select(c => new RowEdit(table.Name, table.KeyOf(c.After), null, c.After))).ToArray();
    }

    /// <summary>
    /// A new event/action/target type resets its parameters (their meaning changes), except timer
    /// events, which share the same four timing fields.
    /// </summary>
    void ChangeType(SmartRule rule, string part, uint id)
    {
        var values = new List<(string, object)> { ($"{part}_type", id) };
        bool timers = part == "event" && rule.Event is 0 or 1 or 60 && id is 0 or 1 or 60;
        if (!timers)
        {
            for (int i = 1; i <= (part == "target" ? 4 : 6); i++)
            {
                values.Add(($"{part}_param{i}", 0u));
            }
        }

        if (part == "target")
        {
            values.AddRange([("target_x", 0f), ("target_y", 0f), ("target_z", 0f), ("target_o", 0f)]);
        }

        if (part == "action" && id == SmartCatalog.Talk)
        {
            values.Add(("action_param1", FirstTextGroup()));
        }

        Update(rule, $"Change {part}", values.ToArray());
    }

    uint FirstTextGroup() => effective.All(InteractionTables.CreatureText).Where(t => t.U("CreatureID") == entry).Select(t => t.U("GroupID")).DefaultIfEmpty(0u).Min();

    uint NextId(SmartScope of) => RulesOf(effective, of).Select(r => r.Id + 1).DefaultIfEmpty(0u).Max();

    Row Template(SmartScope of, uint id, uint eventType, uint[] eventParams, uint action, uint[] actionParams, uint target)
    {
        var row = SmartCatalog.NewRow(of, id, eventType, action, target);
        for (int i = 0; i < eventParams.Length; i++)
        {
            row = row.With($"event_param{i + 1}", eventParams[i]);
        }

        for (int i = 0; i < actionParams.Length; i++)
        {
            row = row.With($"action_param{i + 1}", actionParams[i]);
        }

        if (action == SmartCatalog.Talk && actionParams.Length > 0 && actionParams[0] == 0)
        {
            row = row.With("action_param1", FirstTextGroup());
        }

        return row.With("comment", SmartCatalog.AutoComment(new SmartRule(row), snapshot!.CreatureName));
    }

    void AddRule(SmartScope of, string label, uint eventType, uint[] eventParams, uint action, uint[] actionParams, uint target)
    {
        uint id = NextId(of);
        var row = Template(of, id, eventType, eventParams, action, actionParams, target);
        var edits = new List<RowEdit> { new(InteractionTables.SmartScripts.Name, InteractionTables.SmartScripts.KeyOf(row), null, row) };
        if (!of.IsActionList)
        {
            edits.AddRange(EnableSmartAiIfDefault());
        }

        if (Commit($"Add rule: {label}", edits.ToArray()))
        {
            expanded = id;
            Render();
        }
    }

    /// <summary>
    /// Insert a linked "Then" rule right after this one, keeping any existing chain behind it.
    /// </summary>
    void AddThen(SmartScope of, SmartRule rule)
    {
        uint id = NextId(of);
        var then = Template(of, id, SmartCatalog.LinkEvent, [], 0, [], of.IsAreaTrigger ? InvokerTarget : 1).With("link", rule.Link);
        if (Commit("Add a \"Then\" step", Rekey([(rule.Row, rule.Row.With("link", id))]).Append(new RowEdit(InteractionTables.SmartScripts.Name, InteractionTables.SmartScripts.KeyOf(then), null, then)).ToArray()))
        {
            expanded = id;
            Render();
        }
    }

    /// <summary>
    /// Delete a rule. A root takes its "Then" chain with it (those never run alone); a "Then" step
    /// is unlinked and its predecessor re-linked to whatever followed it.
    /// </summary>
    void Delete(SmartScope of, SmartRule rule)
    {
        var rules = RulesOf(effective, of);
        var chain = SmartCatalog.Chains(rules).FirstOrDefault(c => c.Any(r => r.Id == rule.Id)) ?? [rule];
        var table = InteractionTables.SmartScripts;
        if (chain[0].Id == rule.Id)
        {
            Commit(chain.Count > 1 ? $"Delete rule and {chain.Count - 1} \"Then\" step(s)" : "Delete rule", chain.Select(r => new RowEdit(table.Name, r.Key, r.Row, null)).ToArray());
            return;
        }

        var before = chain[chain.FindIndex(r => r.Id == rule.Id) - 1];
        var edits = Rekey([(before.Row, before.Row.With("link", rule.Link))]).ToList();
        edits.Insert(0, new RowEdit(table.Name, rule.Key, rule.Row, null));
        Commit("Delete \"Then\" step", edits.ToArray());
    }

    /// <summary>
    /// Swap the order of two root rules (ids), rewriting any link that points at either.
    /// </summary>
    void Swap(SmartScope of, SmartRule a, SmartRule b)
    {
        var map = new Dictionary<uint, uint> { [a.Id] = b.Id, [b.Id] = a.Id };
        var changes = RulesOf(effective, of).Where(r => map.ContainsKey(r.Id) || map.ContainsKey(r.Link))
            .Select(r => (r.Row, r.Row.With("id", map.GetValueOrDefault(r.Id, r.Id)).With("link", r.Link == 0 ? 0u : map.GetValueOrDefault(r.Link, r.Link))));
        if (Commit(of.IsActionList ? "Reorder steps" : "Reorder rules", Rekey(changes)))
        {
            expanded = expanded == a.Id ? b.Id : expanded == b.Id ? a.Id : expanded;
            Render();
        }
    }

    /// <summary>
    /// Copy a rule and its "Then" chain under fresh ids at the end.
    /// </summary>
    void Duplicate(SmartScope of, SmartRule rule)
    {
        var chain = SmartCatalog.Chains(RulesOf(effective, of)).First(c => c[0].Id == rule.Id);
        uint next = NextId(of);
        var map = chain.Select((r, i) => (r.Id, New: next + (uint)i)).ToDictionary(p => p.Id, p => p.New);
        var table = InteractionTables.SmartScripts;
        var edits = chain.Select(r => r.Row.With("id", map[r.Id]).With("link", r.Link == 0 ? 0u : map.GetValueOrDefault(r.Link, 0u)))
            .Select(row => new RowEdit(table.Name, table.KeyOf(row), null, row)).ToArray();
        Commit("Duplicate rule", edits);
    }

    /// <summary>
    /// Give a spawn its own copy of the entry rules (a spawn with rules ignores the entry's).
    /// </summary>
    void CopyScope(SmartScope from, SmartScope to)
    {
        var table = InteractionTables.SmartScripts;
        var edits = RulesOf(effective, from).Select(r => r.Row.With("entryorguid", to.EntryOrGuid)).Select(row => new RowEdit(table.Name, table.KeyOf(row), null, row)).ToArray();
        Commit("Copy rules to this spawn", edits);
    }
}
