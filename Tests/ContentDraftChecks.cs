using ACWorldEdit.Foundation;

/// <summary>
/// Engine- and database-independent checks of quest and SmartAI editing over the shared
/// InteractionDraft: new-quest rows, quest rules, rule sentences, chain grouping, key-changing
/// (delete + insert) commands, speech rows and the SmartAI enable fix.
/// </summary>
public static class ContentDraftChecks
{
    sealed class FakeNames : IContentNames
    {
        public string? Spell(uint id) => id == 133 ? "Fireball" : null;
        public string? Creature(uint id) => id is 6 or 42 ? (id == 6 ? "Kobold Vermin" : "Test NPC") : null;
        public string? GameObject(uint id) => null;
        public string? Quest(uint id) => id is 7 or 900001 ? "Known quest" : null;
        public string? Item(uint id) => id == 2589 ? "Linen Cloth" : null;
        public string? Text(uint entry, uint group) => entry == 42 && group == 0 ? "For the Alliance!" : null;
        public bool CreatureSpawnExists(uint guid) => guid == 5;
        public string? Emote(uint id) => id == 3 ? "wave" : null;
        public string? TextEmote(uint id) => id == 101 ? "wave" : null;
        public string? FactionTemplate(uint id) => null;
        public string? Faction(uint id) => null;
    }

    static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception(message);
        }
    }

    public static void Run()
    {
        string path = Path.GetFullPath(Path.Combine("artifacts", "draft-checks", Guid.NewGuid().ToString("N"), "content.json"));
        var names = new FakeNames();
        using (var draft = new InteractionDraft(path))
        {
            CheckQuest(draft, names);
            CheckSmart(draft, names);
        }

        Directory.Delete(Path.GetDirectoryName(path)!, true);
        Console.WriteLine("CONTENT_DRAFT_CHECKS_PASSED quest create/edit/rules, SmartAI sentences/chains/rekey/fix, speech rows");
    }

    static void CheckQuest(InteractionDraft draft, IContentNames names)
    {
        const uint id = 900001;
        string key = id.ToString();
        var snapshot = new RowSet();
        var fresh = QuestModel.NewQuest(id, "New quest");
        Check(fresh.Length == QuestModel.Tables.Length, "New quest row count");
        for (int i = 0; i < fresh.Length; i++)
        {
            fresh[i].Validate(QuestModel.Tables[i]);
        }

        draft.Execute("src", snapshot, "Create quest", fresh.Select((row, i) => new RowEdit(QuestModel.Tables[i].Name, key, null, row)));
        var view = draft.Effective("src", snapshot);
        Check(view.Get(InteractionTables.QuestTemplate, key)?.T("LogTitle") == "New quest", "Created quest visible");
        var issues = QuestModel.Check(id, view, names, null).ToList();
        Check(issues.All(i => !i.Error), "Fresh quest has errors: " + string.Join("; ", issues.Where(i => i.Error).Select(i => i.Message)));
        Check(issues.Any(i => i.Message.Contains("starts this quest", StringComparison.Ordinal)), "Missing-giver warning");

        var q = view.Get(InteractionTables.QuestTemplate, key)!;
        var objective = q.With("RequiredNpcOrGo1", 6).With("RequiredNpcOrGoCount1", 8u);
        draft.Execute("src", snapshot, "Set objective", [new RowEdit(InteractionTables.QuestTemplate.Name, key, q, objective)]);
        view = draft.Effective("src", snapshot);
        Check(QuestModel.Check(id, view, names, null).All(i => !i.Error), "Valid objective rejected");
        var missing = objective.With("RequiredNpcOrGo1", 99999);
        var probe = view.Clone();
        probe.Set(InteractionTables.QuestTemplate, missing);
        Check(QuestModel.Check(id, probe, names, null).Any(i => i.Error && i.Message.Contains("99999", StringComparison.Ordinal)), "Missing objective creature not reported");
        probe.Set(InteractionTables.QuestTemplate, objective.With("RequiredNpcOrGoCount1", 0u));
        Check(QuestModel.Check(id, probe, names, null).Any(i => i.Error && i.Message.Contains("count", StringComparison.Ordinal)), "Zero objective count not reported");
        probe.Set(InteractionTables.QuestTemplate, objective.With("RewardItem1", 2589u).With("RewardAmount1", 0u));
        Check(QuestModel.Check(id, probe, names, null).Any(i => i.Error && i.Message.Contains("Reward item 1", StringComparison.Ordinal)), "Zero reward amount not reported");

        var giver = Row.Of(("id", 42u), ("quest", id));
        draft.Execute("src", snapshot, "Add quest giver", [new RowEdit(InteractionTables.QuestStarter.Name, InteractionTables.QuestStarter.KeyOf(giver), null, giver)]);
        view = draft.Effective("src", snapshot);
        Check(!QuestModel.Check(id, view, names, null).Any(i => i.Message.Contains("starts this quest", StringComparison.Ordinal)), "Giver warning stays after adding a giver");
        draft.Undo();
        draft.Undo();
        Check(draft.Effective("src", snapshot).Get(InteractionTables.QuestTemplate, key)?.I("RequiredNpcOrGo1") == 0, "Undo objective");
        draft.Redo();
        draft.Redo();
        Check(draft.MaxReserved("src", InteractionTables.QuestTemplate, "ID") == id, "Quest ID reservation");
    }

    static void CheckSmart(InteractionDraft draft, IContentNames names)
    {
        var scope = new SmartScope(42, SmartScope.Creature);
        var snapshot = new RowSet();
        var aggro = SmartCatalog.NewRow(scope, 0, 4, SmartCatalog.Talk, 1).With("link", 1u);
        var then = SmartCatalog.NewRow(scope, 1, SmartCatalog.LinkEvent, 11, 2).With("action_param1", 133u);
        var timer = SmartCatalog.NewRow(scope, 2, 0, 11, 2).With("action_param1", 133u).With("event_param1", 3000u).With("event_param2", 3000u).With("event_param3", 8000u).With("event_param4", 8000u).With("event_chance", 30u);
        var ai = Row.Of(("entry", 42u), ("AIName", ""));
        foreach (var row in new[] { aggro, then, timer })
        {
            row.Validate(InteractionTables.SmartScripts);
            snapshot.Set(InteractionTables.SmartScripts, row);
        }

        snapshot.Set(InteractionTables.CreatureAI, ai);
        var rules = snapshot.All(InteractionTables.SmartScripts).Select(r => new SmartRule(r)).OrderBy(r => r.Id).ToList();
        string first = SmartCatalog.Describe(rules[0], names, 42);
        string second = SmartCatalog.Describe(rules[1], names, 42);
        string third = SmartCatalog.Describe(rules[2], names, 42);
        Check(first.StartsWith("When it enters combat", StringComparison.Ordinal) && first.Contains("For the Alliance!", StringComparison.Ordinal), "Aggro/talk sentence: " + first);
        Check(second.StartsWith("Then:", StringComparison.Ordinal) && second.Contains("Fireball", StringComparison.Ordinal) && second.Contains("its victim", StringComparison.Ordinal), "Linked sentence: " + second);
        Check(third.StartsWith("Every 8 s in combat (first after 3 s)", StringComparison.Ordinal) && third.Contains("30% chance", StringComparison.Ordinal), "Timer sentence: " + third);
        var chains = SmartCatalog.Chains(rules);
        Check(chains.Count == 2 && chains[0].Count == 2 && chains[1].Count == 1, "Chain grouping");

        var issues = SmartCatalog.Check(scope, rules, names, ai, 42).ToList();
        Check(issues.All(i => !i.Error), "Valid rules reported errors: " + string.Join("; ", issues.Where(i => i.Error).Select(i => i.Message)));
        var fix = issues.FirstOrDefault(i => i.Fix != null);
        Check(fix != null && fix.Fix![0].After!.T("AIName") == "SmartAI", "SmartAI enable fix");
        draft.Execute("src", snapshot, "Enable SmartAI", fix.Fix!);

        // Swapping rule order changes keys (id) and the link pointing at the moved rule.
        var table = InteractionTables.SmartScripts;
        var swapped = new[]
        {
            (aggro, aggro.With("id", 2u).With("link", 1u)),
            (timer, timer.With("id", 0u))
        };
        var edits = swapped.Select(c => new RowEdit(table.Name, table.KeyOf(c.Item1), c.Item1, null)).Concat(swapped.Select(c => new RowEdit(table.Name, table.KeyOf(c.Item2), null, c.Item2))).ToList();
        draft.Execute("src", snapshot, "Reorder rules", edits);
        var view = draft.Effective("src", snapshot);
        var after = view.All(table).Select(r => new SmartRule(r)).OrderBy(r => r.Id).ToList();
        Check(after.Count == 3 && after[0].Event == 0 && after[2].Event == 4 && after[2].Link == 1, "Rekeyed swap");
        Check(SmartCatalog.Check(scope, after, names, view.Get(InteractionTables.CreatureAI, "42"), 42).All(i => !i.Error && i.Fix == null), "Swap broke the rules or AI fix stayed");

        var broken = after.Select(r => r.Id == 2 ? new SmartRule(r.Row.With("link", 7u)) : r).ToList();
        Check(SmartCatalog.Check(scope, broken, names, null, 42).Any(i => i.Error && i.Message.Contains("does not exist", StringComparison.Ordinal)), "Missing link target not reported");
        var orphan = after.Where(r => r.Id != 2).ToList();
        Check(SmartCatalog.Check(scope, orphan, names, null, 42).Any(i => !i.Error && i.Message.Contains("never runs", StringComparison.Ordinal)), "Orphan Then rule not reported");
        var spawn = new SmartScope(-77, SmartScope.Creature);
        Check(SmartCatalog.Check(spawn, [new SmartRule(SmartCatalog.NewRow(spawn, 0, 4, 0, 1))], names, null, 42).Any(i => i.Error && i.Message.Contains("#77", StringComparison.Ordinal)), "Missing spawn not reported");

        var list = new SmartScope(4200, SmartScope.ActionList);
        var step = new SmartRule(SmartCatalog.NewRow(list, 0, 0, 5, 1).With("action_param1", 3u).With("event_param1", 2000u).With("event_param2", 2000u));
        Check(SmartCatalog.Describe(step, names, 42) == "After 2 s: play emote wave", "Action list step sentence: " + SmartCatalog.Describe(step, names, 42));

        var line = Row.Of(("CreatureID", 42u), ("GroupID", 0u), ("ID", 0u), ("Text", "For the Alliance!"), ("Type", 14u), ("Language", 0), ("Probability", 100f), ("Emote", 0u), ("Duration", 0u), ("Sound", 0u), ("BroadcastTextId", 0), ("TextRange", 0u), ("comment", "Test NPC"));
        line.Validate(InteractionTables.CreatureText);
        draft.Execute("src", snapshot, "Add line", [new RowEdit(InteractionTables.CreatureText.Name, InteractionTables.CreatureText.KeyOf(line), null, line)]);
        Check(draft.Effective("src", snapshot).All(InteractionTables.CreatureText).Count == 1, "Speech line drafted");
        draft.Undo();
        draft.Undo();
        Check(draft.Effective("src", snapshot).All(table).Select(r => new SmartRule(r)).Single(r => r.Id == 0).Event == 4, "Undo swap");
    }
}
