using System.Globalization;
using Godot;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>The typed parameter controls of the rule editor, including the named pickers.</summary>
public partial class BehaviorPanel
{
    /// <summary>
    /// Labelled fields for a type's parameters; unknown types show raw numbered fields.
    /// </summary>
    Control Parameters(SmartRule rule, bool isNew, SmartType? type, string prefix, Func<int, uint> value, int max)
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
            string tip = ParamTip(param, type);
            caption.TooltipText = tip;
            caption.MouseFilter = MouseFilterEnum.Stop;
            var control = ParamControl(rule, isNew, param, $"{prefix}{i}", value(i));
            control.TooltipText = tip;
            EditorFields.Tip(control, tip);
            field.AddChild(caption);
            field.AddChild(control);
            flow.AddChild(field);
        }

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 130);
        margin.AddChild(flow);
        return margin;
    }

    /// <summary>What a parameter means, so hovering over its caption explains the field.</summary>
    static string ParamTip(SmartParam param, SmartType? type) => param.Tip(type?.Name ?? "", type?.Help ?? "");

    Control ParamControl(SmartRule rule, bool isNew, SmartParam param, string column, uint value)
    {
        string label = $"Change {param.Label.ToLowerInvariant()}";
        void Put(uint v) => Change(rule, isNew, label, (column, v));
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
                return With(EditorFields.Number(value, v => Put((uint)Math.Clamp(v, 0, uint.MaxValue)), 50), "%");
            case SmartParamKind.Yards:
                return With(EditorFields.Number(value, v => Put((uint)Math.Clamp(v, 0, uint.MaxValue)), 50), "yd");
            case SmartParamKind.Bool:
                var check = new CheckBox
                {
                    ButtonPressed = value != 0
                };
                check.Toggled += on => Put(on ? 1u : 0u);
                return check;
            case SmartParamKind.Spell:
                return EditorFields.Reference(value == 0 ? "None" : text, () => PickerPopup.Show(this, $"Choose {param.Label.ToLowerInvariant()}", term => SearchSpells(term), (id, _) => Put(id)), value == 0 ? null : () => Put(0), value != 0 && !names.TryName(param.Kind, value, out _));
            case SmartParamKind.Creature or SmartParamKind.GameObject or SmartParamKind.Quest or SmartParamKind.Item:
                return EditorFields.Reference(value == 0 ? "None" : text, () => PickNamed(param.Kind, param.Label, Put), value == 0 ? null : () => Put(0), value != 0 && !names.TryName(param.Kind, value, out _));
            case SmartParamKind.Emote or SmartParamKind.TextEmote or SmartParamKind.FactionTemplate:
                var all = (catalog?.All(param.Kind.ToString()) ?? []).Where(x => x.Name.Length > 0).ToList();
                return EditorFields.Reference(text, () => PickerPopup.Show(this, $"Choose {param.Label.ToLowerInvariant()}", term => Task.FromResult<IReadOnlyList<(uint Id, string Name)>>(all
                    .Where(x => term.Length == 0 || x.Name.Contains(term, StringComparison.OrdinalIgnoreCase) || x.Id.ToString(CultureInfo.InvariantCulture) == term.Trim()).OrderBy(x => x.Name.Length).Take(200).ToList()), (id, _) => Put(id), current: value), value == 0 ? null : () => Put(0));
            case SmartParamKind.TextGroup:
                return TextGroupChoice(value, Put);
            case SmartParamKind.ActionList:
                return ActionListControl(rule, isNew, value, Put);
            default:
                return EditorFields.Number(value, v => Put((uint)Math.Clamp(v, 0, uint.MaxValue)), 70, param.Kind == SmartParamKind.Flags ? "Bit flags; 0x hex accepted" : "");
        }
    }

    /// <summary>Spell search for the spell pickers: the 3.3.5 client's spells, or the retail client's for TrinityCore.</summary>
    Task<IReadOnlyList<(uint Id, string Name)>> SearchSpells(string term) => smart.Dialect == WorldDialect.TrinityCore
        ? Task.FromResult(Lookup()?.SearchSpells(term, DbLimits.MaxSearch) ?? [])
        : Task.FromResult<IReadOnlyList<(uint Id, string Name)>>(catalog?.SearchSpells(term) ?? []);

    void PickNamed(SmartParamKind kind, string caption, Action<uint> apply)
    {
        var names = snapshot?.Names;
        PickerPopup.Show(this, $"Choose {caption.ToLowerInvariant()}", term => DBSource.Open(Connection(), Lookup()).SearchAsync(kind, term, CancellationToken.None), (id, name) =>
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
        var groups = effective.All(smart.CreatureText).Where(t => t.U("CreatureID") == entry).GroupBy(t => t.U("GroupID")).OrderBy(g => g.Key)
            .Select(g => (g.Key, $"{g.Key}: \"{Shorten(g.OrderBy(t => t.U("ID")).First().T("Text"))}\"{(g.Count() > 1 ? $" (+{g.Count() - 1})" : "")}")).ToList();
        var row = new HBoxContainer();
        row.AddChild(EditorFields.Choice(groups, value, put));
        row.AddChild(ButtonOf("Edit lines…", () => ShowScope(null)));
        return row;
    }

    static string Shorten(string text) => text.Length > 48 ? text[..45] + "…" : text;

    Control ActionListControl(SmartRule rule, bool isNew, uint value, Action<uint> put)
    {
        var row = new HBoxContainer();
        if (value != 0)
        {
            int steps = RulesOf(effective, new SmartScope((int)value, SmartScope.ActionList)).Count;
            row.AddChild(ClientStyle.Text($"#{value} · {steps} step(s)", 14, steps == 0 ? ClientStyle.Warning : null));
            row.AddChild(ButtonOf("Open", () => ShowScope(new SmartScope((int)value, SmartScope.ActionList))));
        }

        // A new rule is added first, so the list has a drafted rule to run it.
        var create = ButtonOf(value == 0 ? "Create list" : "New list", () =>
        {
            if ((isNew ? AddPending() : rule) is { } owner)
            {
                _ = CreateActionList(owner);
            }
        });
        create.Name = "CreateActionList";
        create.TooltipText = isNew ? "Add this rule and create an empty action list with a free ID for it to run." : "Create an empty action list with a free ID and run it from this rule.";
        row.AddChild(create);
        row.AddChild(EditorFields.Number(value, v => put((uint)Math.Clamp(v, 0, uint.MaxValue)), 80, "Or type an existing list ID"));
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
            var used = await DBSource.Open(Connection(), Lookup()).UsedActionListsAsync(first, first + 99, CancellationToken.None);
            used.UnionWith(effective.All(smart.Scripts).Where(r => r.U("source_type") == SmartScope.ActionList).Select(r => r.I("entryorguid")));
            used.UnionWith(effective.All(smart.Scripts).Select(r => new SmartRule(r)).Where(r => r.Action == SmartCatalog.RunActionList).Select(r => (int)r.ActionParam(1)));
            int id = Enumerable.Range(first, 100).FirstOrDefault(i => !used.Contains(i));
            if (id == 0)
            {
                status = "No free action list ID near this creature's entry; type an ID instead.";
                UpdateStatus();
                return;
            }

            var current = effective.Get(smart.Scripts, smart.Scripts.KeyOf(rule.Row));
            if (current == null)
            {
                return;
            }

            var list = new SmartScope(id, SmartScope.ActionList);
            var step = smart.NewRow(list, 0, 0, 0, 1, "First step");
            if (Commit($"Create action list #{id}", new RowEdit(smart.Scripts.Name, smart.Scripts.KeyOf(rule.Row), current, current.With("action_param1", (uint)id)), new RowEdit(smart.Scripts.Name, smart.Scripts.KeyOf(step), null, step)))
            {
                ShowScope(list);
                expanded = 0;
                Render();
            }
        }
        catch (Exception error)
        {
            status = DbErrors.Safe(error);
            UpdateStatus();
        }
    }

}
