using ACWorldEdit.Foundation;
using ACWorldEdit.Tests.Support;
using Xunit;
using static Test;

/// <summary>
/// Engine- and database-independent checks of quest and SmartAI editing over the shared
/// InteractionDraft: new-quest rows, quest rules, rule sentences, chain grouping, key-changing
/// (delete + insert) commands, speech rows and the SmartAI enable fix.
/// </summary>
public class ContentDraftTests
{
    [Fact]
    public void Passes() => Core();

    sealed class FakeNames : IContentNames
    {
        public string? Spell(uint id) => id == 133 ? "Fireball" : null;
        public string? Creature(uint id) => id is 6 or 42 ? (id == 6 ? "Kobold Vermin" : "Test NPC") : null;
        public string? GameObject(uint id) => null;
        public string? Quest(uint id) => id is 7 or 900001 ? "Known quest" : null;
        public string? Item(uint id) => id == 2589 ? "Linen Cloth" : null;
        public string? Text(uint entry, uint group) => entry == 42 && group == 0 ? "For the Alliance!" : null;
        public bool CreatureSpawnExists(ulong guid) => guid == 5;
        public string? Emote(uint id) => id == 3 ? "wave" : null;
        public string? TextEmote(uint id) => id == 101 ? "wave" : null;
        public string? FactionTemplate(uint id) => null;
        public string? Faction(uint id) => null;
    }

    static void Core()
    {
        string path = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "acwe-tests", Guid.NewGuid().ToString("N"), "content.json"));
        var names = new FakeNames();
        using (var draft = new InteractionDraft(path))
        {
            CheckQuest(draft, names);
            CheckSmart(draft, names);
        }

        CheckCategories();
        CheckAreaTrigger(Path.Combine(Path.GetDirectoryName(path)!, "areatrigger.json"), names);

        Directory.Delete(Path.GetDirectoryName(path)!, true);
        Console.WriteLine("CONTENT_DRAFT_CHECKS_PASSED quest create/edit/rules, SmartAI sentences/chains/rekey/fix, speech rows, type categories, AreaTrigger rules/SmartTrigger");
    }

    /// <summary>
    /// Every selectable SmartAI type appears exactly once in the grouped pickers; server range
    /// markers are neither selectable nor valid.
    /// </summary>
    static void CheckCategories()
    {
        foreach (var (kind, types) in new[] { ("event", SmartCatalog.Ac.Events), ("action", SmartCatalog.Ac.Actions), ("target", SmartCatalog.Ac.Targets) })
        {
            var selectable = types.Where(t => !SmartCatalog.IsMarker(t)).ToList();
            var groups = SmartCatalog.Grouped(types, kind).ToList();
            Check(groups.Sum(g => g.Types.Count) == selectable.Count && groups.SelectMany(g => g.Types).Distinct().Count() == selectable.Count, $"{kind} grouping lost or duplicated types");
            Console.WriteLine($"  {kind}s={selectable.Count} markers={types.Count - selectable.Count}: " + string.Join(", ", groups.Select(g => $"{g.Category} {g.Types.Count}")));
        }

        var marker = SmartCatalog.Ac.Events.First(SmartCatalog.IsMarker);
        Check(SmartCatalog.Ac.Event((uint)marker.Id) == null, "Range marker accepted as an event");
        Check(SmartCatalog.Category(SmartCatalog.Ac.Event(4)!) == "Combat" && SmartCatalog.Category(SmartCatalog.Ac.Action(11)!) == "Spells & combat" && SmartCatalog.Category(SmartCatalog.Ac.Target(2)!) == "Enemies", "Category assignment");
    }

    static void CheckQuest(InteractionDraft draft, IContentNames names)
    {
        const uint id = 900001;
        string key = id.ToString();
        var snapshot = new RowSet();
        var fresh = AcQuestModel.NewQuest(id, "New quest");
        Check(fresh.Length == AcQuestModel.Tables.Length, "New quest row count");
        for (int i = 0; i < fresh.Length; i++)
        {
            fresh[i].Validate(AcQuestModel.Tables[i]);
        }

        draft.Execute("src", snapshot, "Create quest", fresh.Select((row, i) => new RowEdit(AcQuestModel.Tables[i].Name, key, null, row)));
        var view = draft.Effective("src", snapshot);
        Check(view.Get(AcTables.QuestTemplate, key)?.T("LogTitle") == "New quest", "Created quest visible");
        var issues = AcQuestModel.Check(id, view, names, null).ToList();
        Check(issues.All(i => !i.Error), "Fresh quest has errors: " + string.Join("; ", issues.Where(i => i.Error).Select(i => i.Message)));
        Check(issues.Any(i => i.Message.Contains("starts this quest", StringComparison.Ordinal)), "Missing-giver warning");

        var q = view.Get(AcTables.QuestTemplate, key)!;
        var objective = q.With("RequiredNpcOrGo1", 6).With("RequiredNpcOrGoCount1", 8u);
        draft.Execute("src", snapshot, "Set objective", [new RowEdit(AcTables.QuestTemplate.Name, key, q, objective)]);
        view = draft.Effective("src", snapshot);
        Check(AcQuestModel.Check(id, view, names, null).All(i => !i.Error), "Valid objective rejected");
        var missing = objective.With("RequiredNpcOrGo1", 99999);
        var probe = view.Clone();
        probe.Set(AcTables.QuestTemplate, missing);
        Check(AcQuestModel.Check(id, probe, names, null).Any(i => i.Error && i.Message.Contains("99999", StringComparison.Ordinal)), "Missing objective creature not reported");
        probe.Set(AcTables.QuestTemplate, objective.With("RequiredNpcOrGoCount1", 0u));
        Check(AcQuestModel.Check(id, probe, names, null).Any(i => i.Error && i.Message.Contains("count", StringComparison.Ordinal)), "Zero objective count not reported");
        probe.Set(AcTables.QuestTemplate, objective.With("RewardItem1", 2589u).With("RewardAmount1", 0u));
        Check(AcQuestModel.Check(id, probe, names, null).Any(i => i.Error && i.Message.Contains("Reward item 1", StringComparison.Ordinal)), "Zero reward amount not reported");

        var giver = Row.Of(("id", 42u), ("quest", id));
        draft.Execute("src", snapshot, "Add quest giver", [new RowEdit(AcTables.QuestStarter.Name, AcTables.QuestStarter.KeyOf(giver), null, giver)]);
        view = draft.Effective("src", snapshot);
        Check(!AcQuestModel.Check(id, view, names, null).Any(i => i.Message.Contains("starts this quest", StringComparison.Ordinal)), "Giver warning stays after adding a giver");
        draft.Undo();
        draft.Undo();
        Check(draft.Effective("src", snapshot).Get(AcTables.QuestTemplate, key)?.I("RequiredNpcOrGo1") == 0, "Undo objective");
        draft.Redo();
        draft.Redo();
        Check(draft.MaxReserved("src", AcTables.QuestTemplate, "ID") == id, "Quest ID reservation");
    }

    /// <summary>
    /// AreaTrigger scope (source_type 2): only On trigger/Then events load, Self and non-creature
    /// Talk/action-list targets warn, the SmartTrigger script row is offered as a fix (insert, or
    /// replace a C++ name), and the rule + script commands survive undo/redo and a reopen.
    /// </summary>
    static void CheckAreaTrigger(string path, IContentNames names)
    {
        const int trigger = 5140;
        var scope = new SmartScope(trigger, SmartScope.AreaTrigger);
        Check(scope.Describe("") == "AreaTrigger 5140" && !scope.IsSpawn && scope.IsAreaTrigger, "AreaTrigger scope identity");
        // The Utgarde Pinnacle row: On trigger -> set data 1 1 on creature spawn 126115 (Svala).
        var svala = new SmartRule(SmartCatalog.Ac.NewRow(scope, 0, SmartCatalog.AreaTriggerEvent, 45, 10).With("action_param1", 1u).With("action_param2", 1u).With("target_param1", 126115u).With("target_param2", 42u));
        svala.Row.Validate(AcTables.SmartScripts);
        var clean = SmartCatalog.Ac.Check(scope, [svala], names, null, trigger).ToList();
        Check(clean.All(i => !i.Error && i.Fix == null && !i.Message.Contains("AI is", StringComparison.Ordinal)), "Valid AreaTrigger rule reported: " + string.Join("; ", clean.Select(i => i.Message)));
        var aggro = new SmartRule(SmartCatalog.Ac.NewRow(scope, 1, 4, 11, 7));
        Check(SmartCatalog.Ac.Check(scope, [aggro], names, null, trigger).Any(i => i.Error && i.Message.Contains("On trigger", StringComparison.Ordinal)), "Creature event accepted for an AreaTrigger");
        var self = new SmartRule(SmartCatalog.Ac.NewRow(scope, 0, SmartCatalog.AreaTriggerEvent, 11, SmartCatalog.SelfTarget));
        Check(SmartCatalog.Ac.Check(scope, [self], names, null, trigger).Any(i => !i.Error && i.Message.Contains("targets Self", StringComparison.Ordinal)), "Self target not warned");
        var talk = new SmartRule(SmartCatalog.Ac.NewRow(scope, 0, SmartCatalog.AreaTriggerEvent, SmartCatalog.Talk, 7));
        Check(SmartCatalog.Ac.Check(scope, [talk], names, null, trigger).Any(i => i.Message.Contains("only speaks", StringComparison.Ordinal)), "Talk on the player not warned");
        var list = new SmartRule(SmartCatalog.Ac.NewRow(scope, 0, SmartCatalog.AreaTriggerEvent, SmartCatalog.RunActionList, 7));
        Check(SmartCatalog.Ac.Check(scope, [list], names, null, trigger).Any(i => i.Message.Contains("action lists", StringComparison.Ordinal)), "Action list on the player not warned");
        var other = new SmartRule(svala.Row.With("event_param1", 9999u));
        Check(SmartCatalog.Ac.Check(scope, [other], names, null, trigger).Any(i => !i.Error && i.Message.Contains("9999", StringComparison.Ordinal)), "Foreign trigger ID not warned");
        Check(SmartCatalog.Ac.AllowedForAreaTrigger(scope, SmartCatalog.LinkEvent) && !SmartCatalog.Ac.AllowedForAreaTrigger(scope, 0), "Allowed AreaTrigger events");

        var missing = SmartCatalog.Ac.CheckAreaTriggerScript(new((uint)trigger), null, "", true).Single();
        Check(missing.Fix![0].Before == null && missing.Fix[0].After!.T("ScriptName") == AreaTriggerRecord.SmartTrigger && missing.Fix[0].Table == "areatrigger_scripts", "SmartTrigger insert fix");
        var cpp = Row.Of(("entry", trigger), ("ScriptName", "at_some_cpp_script"));
        var replace = SmartCatalog.Ac.CheckAreaTriggerScript(new((uint)trigger), cpp, "", true).Single();
        Check(!replace.Error && replace.Message.Contains("at_some_cpp_script", StringComparison.Ordinal) && replace.Fix![0].Before == cpp, "C++ script warning");
        Check(!SmartCatalog.Ac.CheckAreaTriggerScript(new((uint)trigger), cpp.With("ScriptName", AreaTriggerRecord.SmartTrigger), "", true).Any() && !SmartCatalog.Ac.CheckAreaTriggerScript(new((uint)trigger), null, "", false).Any(), "SmartTrigger or no rules needs no fix");

        var snapshot = new RowSet();
        using (var draft = new InteractionDraft(path))
        {
            var edits = new List<RowEdit> { new(AcTables.SmartScripts.Name, SmartCatalog.Ac.Scripts.KeyOf(svala.Row), null, svala.Row) };
            edits.AddRange(missing.Fix);
            draft.Execute("src", snapshot, "Add rule", edits);
            var view = draft.Effective("src", snapshot);
            Check(view.All(AcTables.SmartScripts).Count == 1 && view.Get(AcTables.AreaTriggerScripts, "5140")?.T("ScriptName") == AreaTriggerRecord.SmartTrigger, "AreaTrigger rule and script drafted");
            draft.Undo();
            Check(draft.Effective("src", snapshot).All(AcTables.AreaTriggerScripts).Count == 0, "Undo AreaTrigger rule");
            draft.Redo();
        }

        using (var reopened = new InteractionDraft(path))
        {
            var view = reopened.Effective("src", snapshot);
            Check(view.Get(AcTables.AreaTriggerScripts, "5140") != null && new SmartRule(view.All(AcTables.SmartScripts).Single()).TargetParam(1) == 126115, "AreaTrigger draft reopen");
        }
    }

    static void CheckSmart(InteractionDraft draft, IContentNames names)
    {
        var scope = new SmartScope(42, SmartScope.Creature);
        var snapshot = new RowSet();
        var aggro = SmartCatalog.Ac.NewRow(scope, 0, 4, SmartCatalog.Talk, 1).With("link", 1u);
        var then = SmartCatalog.Ac.NewRow(scope, 1, SmartCatalog.LinkEvent, 11, 2).With("action_param1", 133u);
        var timer = SmartCatalog.Ac.NewRow(scope, 2, 0, 11, 2).With("action_param1", 133u).With("event_param1", 3000u).With("event_param2", 3000u).With("event_param3", 8000u).With("event_param4", 8000u).With("event_chance", 30u);
        var ai = Row.Of(("entry", 42u), ("AIName", ""));
        foreach (var row in new[] { aggro, then, timer })
        {
            row.Validate(AcTables.SmartScripts);
            snapshot.Set(AcTables.SmartScripts, row);
        }

        snapshot.Set(AcTables.CreatureAI, ai);
        var rules = snapshot.All(AcTables.SmartScripts).Select(r => new SmartRule(r)).OrderBy(r => r.Id).ToList();
        string first = SmartCatalog.Ac.Describe(rules[0], names, 42);
        string second = SmartCatalog.Ac.Describe(rules[1], names, 42);
        string third = SmartCatalog.Ac.Describe(rules[2], names, 42);
        Check(first.StartsWith("When it enters combat", StringComparison.Ordinal) && first.Contains("For the Alliance!", StringComparison.Ordinal), "Aggro/talk sentence: " + first);
        Check(second.StartsWith("Then:", StringComparison.Ordinal) && second.Contains("Fireball", StringComparison.Ordinal) && second.Contains("its victim", StringComparison.Ordinal), "Linked sentence: " + second);
        Check(third.StartsWith("Every 8 s in combat (first after 3 s)", StringComparison.Ordinal) && third.Contains("30% chance", StringComparison.Ordinal), "Timer sentence: " + third);
        var chains = SmartCatalog.Chains(rules);
        Check(chains.Count == 2 && chains[0].Count == 2 && chains[1].Count == 1, "Chain grouping");

        var issues = SmartCatalog.Ac.Check(scope, rules, names, ai, 42).ToList();
        Check(issues.All(i => !i.Error), "Valid rules reported errors: " + string.Join("; ", issues.Where(i => i.Error).Select(i => i.Message)));
        var fix = issues.FirstOrDefault(i => i.Fix != null);
        Check(fix != null && fix.Fix![0].After!.T("AIName") == "SmartAI", "SmartAI enable fix");
        draft.Execute("src", snapshot, "Enable SmartAI", fix.Fix!);

        // Swapping rule order changes keys (id) and the link pointing at the moved rule.
        var table = AcTables.SmartScripts;
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
        Check(SmartCatalog.Ac.Check(scope, after, names, view.Get(AcTables.CreatureAI, "42"), 42).All(i => !i.Error && i.Fix == null), "Swap broke the rules or AI fix stayed");

        var broken = after.Select(r => r.Id == 2 ? new SmartRule(r.Row.With("link", 7u)) : r).ToList();
        Check(SmartCatalog.Ac.Check(scope, broken, names, null, 42).Any(i => i.Error && i.Message.Contains("does not exist", StringComparison.Ordinal)), "Missing link target not reported");
        var orphan = after.Where(r => r.Id != 2).ToList();
        Check(SmartCatalog.Ac.Check(scope, orphan, names, null, 42).Any(i => !i.Error && i.Message.Contains("never runs", StringComparison.Ordinal)), "Orphan Then rule not reported");
        var spawn = new SmartScope(-77, SmartScope.Creature);
        Check(SmartCatalog.Ac.Check(spawn, [new SmartRule(SmartCatalog.Ac.NewRow(spawn, 0, 4, 0, 1))], names, null, 42).Any(i => i.Error && i.Message.Contains("#77", StringComparison.Ordinal)), "Missing spawn not reported");

        var list = new SmartScope(4200, SmartScope.ActionList);
        var step = new SmartRule(SmartCatalog.Ac.NewRow(list, 0, 0, 5, 1).With("action_param1", 3u).With("event_param1", 2000u).With("event_param2", 2000u));
        Check(SmartCatalog.Ac.Describe(step, names, 42) == "After 2 s: play emote wave", "Action list step sentence: " + SmartCatalog.Ac.Describe(step, names, 42));

        var line = Row.Of(("CreatureID", 42u), ("GroupID", 0u), ("ID", 0u), ("Text", "For the Alliance!"), ("Type", 14u), ("Language", 0), ("Probability", 100f), ("Emote", 0u), ("Duration", 0u), ("Sound", 0u), ("BroadcastTextId", 0), ("TextRange", 0u), ("comment", "Test NPC"));
        line.Validate(AcTables.CreatureText);
        draft.Execute("src", snapshot, "Add line", [new RowEdit(AcTables.CreatureText.Name, AcTables.CreatureText.KeyOf(line), null, line)]);
        Check(draft.Effective("src", snapshot).All(AcTables.CreatureText).Count == 1, "Speech line drafted");
        draft.Undo();
        draft.Undo();
        Check(draft.Effective("src", snapshot).All(table).Select(r => new SmartRule(r)).Single(r => r.Id == 0).Event == 4, "Undo swap");
    }
}
