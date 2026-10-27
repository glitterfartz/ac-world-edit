namespace ACWorldEdit.Foundation;

/// <summary>
/// A starting point for a new rule or step. Zero action parameters stay unset; a Talk group of
/// 0 becomes the creature's first text group.
/// </summary>
public readonly record struct RuleTemplate(string Label, uint Event, uint[] EventParams, uint Action, uint[] ActionParams, uint Target);

/// <summary>
/// The row arithmetic behind the behavior editor's rule cards: templates, id allocation, "Then"
/// chains and the edits for add/delete/reorder/duplicate/copy. Pure functions over the effective
/// rows so they can be tested without Godot; the window owns commands, labels and UI state.
/// smart_scripts is keyed by (entryorguid, source_type, id, link), so a change to id or link is
/// recorded as delete + insert.
/// </summary>
public static class SmartRuleEdits
{
    /// <summary>SMART_TARGET_ACTION_INVOKER: the player who walked into an AreaTrigger.</summary>
    public const uint InvokerTarget = 7;

    /// <summary>
    /// AreaTrigger starting points. "Set data on a creature" is the common pattern for starting a
    /// boss script's event (e.g. Utgarde Pinnacle 5140 sends data 1 = 1 to Svala); target 10 is a
    /// creature by spawn GUID, 19 the closest creature of an entry.
    /// </summary>
    public static readonly RuleTemplate[] TriggerTemplates =
    [
        new("Send data to a creature (start its script event)", SmartCatalog.AreaTriggerEvent, [], 45, [1, 1], 10),
        new("Make the nearest creature of a kind say a line", SmartCatalog.AreaTriggerEvent, [], 1, [0], 19),
        new("Cast a spell on the player who entered", SmartCatalog.AreaTriggerEvent, [], 11, [], InvokerTarget),
        new("Make the nearest creature of a kind play a scene (action list)", SmartCatalog.AreaTriggerEvent, [], SmartCatalog.RunActionList, [], 19),
        new("Blank rule", SmartCatalog.AreaTriggerEvent, [], 0, [], InvokerTarget)
    ];

    public static readonly RuleTemplate[] RuleTemplates =
    [
        new("Say something when it enters combat", 4, [], 1, [0], 1),
        new("Cast a spell on its victim every few seconds", 0, [2000, 4000, 8000, 12000], 11, [], 2),
        new("Cast a spell when its health drops below 30%", 2, [0, 30, 0, 0], 11, [], 1),
        new("Say something when it dies", 6, [], 1, [0], 1),
        new("Wave back when a player waves", 22, [101, 2000, 2000], 5, [3], 1),
        new("Play a scene (action list) when a quest is completed", 20, [], SmartCatalog.RunActionList, [], 1),
        new("Do something every few seconds out of combat", 1, [5000, 10000, 15000, 30000], 5, [], 1),
        new("Blank rule", 4, [], 0, [], 1)
    ];

    public static readonly RuleTemplate[] StepTemplates =
    [
        new("Say a line", 0, [], 1, [0], 1),
        new("Play an emote", 0, [], 5, [], 1),
        new("Cast a spell", 0, [], 11, [], 1),
        new("Walk to a position", 0, [], 69, [], 8),
        new("Blank step", 0, [], 0, [], 1)
    ];

    public static RuleTemplate[] TemplatesFor(SmartScope shown) => shown.IsActionList ? StepTemplates : shown.IsAreaTrigger ? TriggerTemplates : RuleTemplates;

    /// <summary>The empty link-event step that follows another rule.</summary>
    public static RuleTemplate ThenTemplate(SmartScope shown) => new("Then", SmartCatalog.LinkEvent, [], 0, [], shown.IsAreaTrigger ? InvokerTarget : SmartCatalog.SelfTarget);

    public static List<SmartRule> RulesOf(RowSet rows, SmartScope of) => rows.All(InteractionTables.SmartScripts)
        .Where(r => r.I("entryorguid") == of.EntryOrGuid && r.U("source_type") == of.SourceType).Select(r => new SmartRule(r)).OrderBy(r => r.Id).ToList();

    public static uint NextId(RowSet effective, SmartScope of) => RulesOf(effective, of).Select(r => r.Id + 1).DefaultIfEmpty(0u).Max();

    public static uint FirstTextGroup(RowSet effective, uint entry) => effective.All(InteractionTables.CreatureText).Where(t => t.U("CreatureID") == entry).Select(t => t.U("GroupID")).DefaultIfEmpty(0u).Min();

    /// <summary>The pending row for a template: a fresh id, the template's parameters and an automatic note.</summary>
    public static Row FromTemplate(SmartCatalog smart, RowSet effective, SmartScope of, uint entry, string creatureName, RuleTemplate template)
    {
        var row = smart.NewRow(of, NextId(effective, of), template.Event, template.Action, template.Target);
        for (int i = 0; i < template.EventParams.Length; i++)
        {
            row = row.With($"event_param{i + 1}", template.EventParams[i]);
        }

        for (int i = 0; i < template.ActionParams.Length; i++)
        {
            row = row.With($"action_param{i + 1}", template.ActionParams[i]);
        }

        if (template.Action == SmartCatalog.Talk && template.ActionParams.Length > 0 && template.ActionParams[0] == 0)
        {
            row = row.With("action_param1", FirstTextGroup(effective, entry));
        }

        return row.With("comment", smart.AutoComment(new SmartRule(row), creatureName));
    }

    /// <summary>
    /// Edits replacing rows whose key may change: all deletes first, then all inserts, so swapped
    /// keys never collide inside the command.
    /// </summary>
    public static RowEdit[] Rekey(IEnumerable<(Row Before, Row After)> changes)
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
    /// The row and edits that add a pending rule: a fresh id, an automatic note when empty, the
    /// predecessor re-linked for a "Then" step, and SmartAI/SmartTrigger enabled for a first rule
    /// (<paramref name="enableSmartAi"/> supplies those edits). No edits when a "Then" step's rule no
    /// longer exists.
    /// </summary>
    public static (Row Row, RowEdit[] Edits) PendingEdits(SmartCatalog smart, RowSet effective, SmartScope scope, Row pendingRow, uint? after, string creatureName, Func<RowEdit[]> enableSmartAi)
    {
        var table = InteractionTables.SmartScripts;
        uint id = NextId(effective, scope);
        var row = pendingRow.With("id", id).With("link", 0u);
        if (row.T("comment").Length == 0)
        {
            row = row.With("comment", smart.AutoComment(new SmartRule(row), creatureName));
        }

        if (after is uint predecessor)
        {
            if (RulesOf(effective, scope).FirstOrDefault(r => r.Id == predecessor) is not { } before)
            {
                return (row, []);
            }

            row = row.With("link", before.Link);
            return (row, Rekey([(before.Row, before.Row.With("link", id))]).Append(new RowEdit(table.Name, table.KeyOf(row), null, row)).ToArray());
        }

        var edits = new List<RowEdit> { new(table.Name, table.KeyOf(row), null, row) };
        if (!scope.IsActionList)
        {
            edits.AddRange(enableSmartAi());
        }

        return (row, edits.ToArray());
    }

    /// <summary>
    /// Errors the edits would introduce: those in the checked result that were not already in the
    /// current rows. <paramref name="check"/> is the editor's loader-rule check over a row set.
    /// </summary>
    public static IEnumerable<string> NewProblems(ITableCatalog tables, RowSet effective, RowEdit[] edits, Func<RowSet, IEnumerable<InteractionIssue>> check)
    {
        if (edits.Length == 0)
        {
            return [];
        }

        var prospective = effective.Clone();
        foreach (var edit in edits)
        {
            prospective.Apply(edit, tables);
        }

        var before = check(effective).Where(i => i.Error).Select(i => i.Message).ToHashSet();
        return check(prospective).Where(i => i.Error && !before.Contains(i.Message)).Select(i => i.Message).ToList();
    }

    /// <summary>
    /// Column values for a new event/action/target type: parameters reset (their meaning changes),
    /// except timer events, which share the same four timing fields.
    /// </summary>
    public static (string Column, object Value)[] ChangeTypeValues(SmartCatalog smart, SmartRule rule, string part, uint id, RowSet effective, uint entry)
    {
        var values = new List<(string, object)> { ($"{part}_type", id) };
        bool timers = part == "event" && rule.Event is 0 or 1 or 60 && id is 0 or 1 or 60;
        if (!timers)
        {
            int count = part == "event" ? smart.EventParamCount : part == "action" ? smart.ActionParamCount : SmartCatalog.TargetParamCount;
            for (int i = 1; i <= count; i++)
            {
                values.Add(($"{part}_param{i}", 0u));
            }

            if (smart.HasStringParams)
            {
                values.Add(($"{part}_param_string", ""));
            }
        }

        if (part == "target")
        {
            values.AddRange([("target_x", 0f), ("target_y", 0f), ("target_z", 0f), ("target_o", 0f)]);
        }

        if (part == "action" && id == SmartCatalog.Talk)
        {
            values.Add(("action_param1", FirstTextGroup(effective, entry)));
        }

        return values.ToArray();
    }

    /// <summary>
    /// Delete a rule. A root takes its "Then" chain with it (those never run alone); a "Then" step
    /// is unlinked and its predecessor re-linked to whatever followed it.
    /// </summary>
    public static (string Label, RowEdit[] Edits) DeleteEdits(RowSet effective, SmartScope of, SmartRule rule)
    {
        var rules = RulesOf(effective, of);
        var chain = SmartCatalog.Chains(rules).FirstOrDefault(c => c.Any(r => r.Id == rule.Id)) ?? [rule];
        var table = InteractionTables.SmartScripts;
        if (chain[0].Id == rule.Id)
        {
            return (chain.Count > 1 ? $"Delete rule and {chain.Count - 1} \"Then\" step(s)" : "Delete rule", chain.Select(r => new RowEdit(table.Name, r.Key, r.Row, null)).ToArray());
        }

        var before = chain[chain.FindIndex(r => r.Id == rule.Id) - 1];
        var edits = Rekey([(before.Row, before.Row.With("link", rule.Link))]).ToList();
        edits.Insert(0, new RowEdit(table.Name, rule.Key, rule.Row, null));
        return ("Delete \"Then\" step", edits.ToArray());
    }

    /// <summary>
    /// Swap the order of two root rules (ids), rewriting any link that points at either. The map
    /// (old id to new id) lets the caller follow a pending "Then" step or the expanded card.
    /// </summary>
    public static (Dictionary<uint, uint> Map, RowEdit[] Edits) SwapEdits(RowSet effective, SmartScope of, SmartRule a, SmartRule b)
    {
        var map = new Dictionary<uint, uint> { [a.Id] = b.Id, [b.Id] = a.Id };
        var changes = RulesOf(effective, of).Where(r => map.ContainsKey(r.Id) || map.ContainsKey(r.Link))
            .Select(r => (r.Row, r.Row.With("id", map.GetValueOrDefault(r.Id, r.Id)).With("link", r.Link == 0 ? 0u : map.GetValueOrDefault(r.Link, r.Link))));
        return (map, Rekey(changes));
    }

    /// <summary>
    /// Copy a rule and its "Then" chain under fresh ids at the end; <c>First</c> is the new root id.
    /// </summary>
    public static (uint First, RowEdit[] Edits) DuplicateEdits(RowSet effective, SmartScope of, SmartRule rule)
    {
        var chain = SmartCatalog.Chains(RulesOf(effective, of)).First(c => c[0].Id == rule.Id);
        uint next = NextId(effective, of);
        var map = chain.Select((r, i) => (r.Id, New: next + (uint)i)).ToDictionary(p => p.Id, p => p.New);
        var table = InteractionTables.SmartScripts;
        var edits = chain.Select(r => r.Row.With("id", map[r.Id]).With("link", r.Link == 0 ? 0u : map.GetValueOrDefault(r.Link, 0u)))
            .Select(row => new RowEdit(table.Name, table.KeyOf(row), null, row)).ToArray();
        return (next, edits);
    }

    /// <summary>
    /// Give a spawn its own copy of the entry rules (a spawn with rules ignores the entry's). Like
    /// any first rule, this enables SmartAI when the creature uses the default AI.
    /// </summary>
    public static RowEdit[] CopyScopeEdits(RowSet effective, SmartScope from, SmartScope to, RowEdit[] enableSmartAi)
    {
        var table = InteractionTables.SmartScripts;
        var edits = RulesOf(effective, from).Select(r => r.Row.With("entryorguid", to.EntryOrGuid)).Select(row => new RowEdit(table.Name, table.KeyOf(row), null, row));
        return edits.Concat(enableSmartAi).ToArray();
    }
}
