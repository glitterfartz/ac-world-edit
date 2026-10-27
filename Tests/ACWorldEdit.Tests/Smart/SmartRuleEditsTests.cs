using ACWorldEdit.Foundation;
using Xunit;

namespace ACWorldEdit.Tests.Smart;

/// <summary>
/// Behavior-editor row arithmetic (extracted from BehaviorEditorWindow.Rules.cs). The asserted edit
/// order matters: deletes precede inserts so re-keyed rows never collide inside one command.
/// </summary>
public class SmartRuleEditsTests
{
    const uint Entry = 295;
    static readonly SmartScope Scope = new((int)Entry, SmartScope.Creature);
    static readonly TableSchema Table = InteractionTables.SmartScripts;

    static Row Rule(uint id, uint link = 0, uint eventType = 4, uint action = 1, SmartScope? scope = null) =>
        SmartCatalog.Ac.NewRow(scope ?? Scope, id, eventType, action, 1).With("link", link);

    static RowSet Rows(params Row[] rows)
    {
        var set = new RowSet();
        foreach (var row in rows)
        {
            set.Set(Table, row);
        }

        return set;
    }

    static RowSet Apply(RowSet rows, IEnumerable<RowEdit> edits)
    {
        var copy = rows.Clone();
        foreach (var edit in edits)
        {
            copy.Apply(edit);
        }

        return copy;
    }

    static string Shape(RowEdit e) => $"{(e.Before == null ? "+" : e.After == null ? "-" : "~")}{(e.After ?? e.Before)!.U("id")}";

    static List<(uint Id, uint Link)> Ids(RowSet rows, SmartScope? scope = null) => SmartRuleEdits.RulesOf(rows, scope ?? Scope).Select(r => (r.Id, r.Link)).ToList();

    [Fact]
    public void NextIdIsZeroForEmptyScopeAndMaxPlusOneWithGaps()
    {
        Assert.Equal(0u, SmartRuleEdits.NextId(Rows(), Scope));
        Assert.Equal(6u, SmartRuleEdits.NextId(Rows(Rule(0), Rule(1), Rule(5)), Scope));
        Assert.Equal(0u, SmartRuleEdits.NextId(Rows(Rule(3, scope: new(999, 0))), Scope));
    }

    [Fact]
    public void RulesAreScopedBySourceTypeAndSortedById()
    {
        var rows = Rows(Rule(2), Rule(0), Rule(1, scope: new((int)Entry, SmartScope.ActionList)));
        Assert.Equal([0u, 2u], SmartRuleEdits.RulesOf(rows, Scope).Select(r => r.Id));
    }

    [Fact]
    public void TemplatesFollowScopeKind()
    {
        Assert.Same(SmartRuleEdits.StepTemplates, SmartRuleEdits.TemplatesFor(new(1, SmartScope.ActionList)));
        Assert.Same(SmartRuleEdits.TriggerTemplates, SmartRuleEdits.TemplatesFor(new(1, SmartScope.AreaTrigger)));
        Assert.Same(SmartRuleEdits.RuleTemplates, SmartRuleEdits.TemplatesFor(Scope));
        Assert.Equal("Blank step", SmartRuleEdits.StepTemplates[^1].Label);
        Assert.Equal("Blank rule", SmartRuleEdits.RuleTemplates[^1].Label);
        Assert.Equal("Blank rule", SmartRuleEdits.TriggerTemplates[^1].Label);
    }

    [Fact]
    public void ThenTemplateTargetsInvokerInAreaTriggers()
    {
        Assert.Equal(SmartCatalog.SelfTarget, SmartRuleEdits.ThenTemplate(Scope).Target);
        Assert.Equal(SmartRuleEdits.InvokerTarget, SmartRuleEdits.ThenTemplate(new(1, SmartScope.AreaTrigger)).Target);
        Assert.Equal(SmartCatalog.LinkEvent, SmartRuleEdits.ThenTemplate(Scope).Event);
    }

    [Fact]
    public void FromTemplateUsesNextIdParametersAndAutoComment()
    {
        var rows = Rows(Rule(0), Rule(1));
        var row = SmartRuleEdits.FromTemplate(SmartCatalog.Ac, rows, Scope, Entry, "Farley", SmartRuleEdits.RuleTemplates[2]);
        var rule = new SmartRule(row);
        Assert.Equal(2u, rule.Id);
        Assert.Equal(2u, rule.Event);
        Assert.Equal(30u, rule.EventParam(2));
        Assert.Equal("Farley - " + SmartCatalog.Ac.Event(2)!.Name + " - " + SmartCatalog.Ac.Action(11)!.Name, rule.Comment);
    }

    [Fact]
    public void TalkTemplateUsesTheCreaturesFirstTextGroup()
    {
        var rows = Rows();
        rows.Set(InteractionTables.CreatureText, Row.Of(("CreatureID", Entry), ("GroupID", 4u), ("ID", 0u)));
        rows.Set(InteractionTables.CreatureText, Row.Of(("CreatureID", Entry), ("GroupID", 2u), ("ID", 0u)));
        rows.Set(InteractionTables.CreatureText, Row.Of(("CreatureID", 777u), ("GroupID", 1u), ("ID", 0u)));
        Assert.Equal(2u, SmartRuleEdits.FirstTextGroup(rows, Entry));
        Assert.Equal(0u, SmartRuleEdits.FirstTextGroup(Rows(), Entry));
        var say = SmartRuleEdits.FromTemplate(SmartCatalog.Ac, rows, Scope, Entry, "N", SmartRuleEdits.RuleTemplates[0]);
        Assert.Equal(2u, new SmartRule(say).ActionParam(1));
    }

    [Fact]
    public void RekeyDropsUnchangedPairsAndKeepsInPlaceUpdatesAsOneEdit()
    {
        var a = Rule(0);
        var edited = a.With("comment", "x");
        var edits = SmartRuleEdits.Rekey([(a, edited), (Rule(1), Rule(1))]);
        var only = Assert.Single(edits);
        Assert.Equal(a, only.Before);
        Assert.Equal(edited, only.After);
    }

    [Fact]
    public void RekeyWithKeyChangeIsAllDeletesThenAllInserts()
    {
        var a = Rule(0);
        var b = Rule(1);
        var edits = SmartRuleEdits.Rekey([(a, a.With("id", 1u)), (b, b.With("id", 0u))]);
        Assert.Equal(["-0", "-1", "+1", "+0"], edits.Select(Shape));
    }

    [Fact]
    public void FirstRuleInCreatureScopeAlsoEnablesSmartAi()
    {
        var enable = new RowEdit("creature_template", "295", null, Row.Of(("entry", 295u)));
        var (row, edits) = SmartRuleEdits.PendingEdits(SmartCatalog.Ac, Rows(), Scope, Rule(9), null, "N", () => [enable]);
        Assert.Equal(0u, row.U("id"));
        Assert.Equal(2, edits.Length);
        Assert.Equal(Table.Name, edits[0].Table);
        Assert.Same(enable, edits[1]);
    }

    [Fact]
    public void ActionListStepsNeverEnableSmartAi()
    {
        var list = new SmartScope(2950, SmartScope.ActionList);
        var (_, edits) = SmartRuleEdits.PendingEdits(SmartCatalog.Ac, Rows(), list, Rule(0, scope: list), null, "N", () => throw new InvalidOperationException("must not be asked"));
        Assert.Single(edits);
    }

    [Fact]
    public void EmptyCommentGetsAutoCommentAndLinkIsReset()
    {
        var (row, _) = SmartRuleEdits.PendingEdits(SmartCatalog.Ac, Rows(), Scope, Rule(7, link: 3), null, "Farley", () => []);
        Assert.Equal(0u, row.U("link"));
        Assert.StartsWith("Farley - ", row.T("comment"));
        var (kept, _) = SmartRuleEdits.PendingEdits(SmartCatalog.Ac, Rows(), Scope, Rule(7).With("comment", "mine"), null, "Farley", () => []);
        Assert.Equal("mine", kept.T("comment"));
    }

    [Fact]
    public void ThenStepInTheMiddleInheritsThePredecessorsLink()
    {
        // 0 -> 1, add a step after 0: 0 -> new(2) -> 1
        var rows = Rows(Rule(0, link: 1), Rule(1, eventType: SmartCatalog.LinkEvent));
        var (row, edits) = SmartRuleEdits.PendingEdits(SmartCatalog.Ac, rows, Scope, Rule(0, eventType: SmartCatalog.LinkEvent), 0u, "N", () => throw new InvalidOperationException());
        Assert.Equal(2u, row.U("id"));
        Assert.Equal(1u, row.U("link"));
        // The key includes link, so re-linking the predecessor is delete + insert.
        Assert.Equal(["-0", "+0", "+2"], edits.Select(Shape));
        Assert.Equal([(0u, 2u), (1u, 0u), (2u, 1u)], Ids(Apply(rows, edits)));
    }

    [Fact]
    public void ThenStepWithVanishedPredecessorProducesNoEdits()
    {
        var (_, edits) = SmartRuleEdits.PendingEdits(SmartCatalog.Ac, Rows(Rule(0)), Scope, Rule(0), 5u, "N", () => []);
        Assert.Empty(edits);
        Assert.Empty(SmartRuleEdits.NewProblems(AcTables.Instance, Rows(), edits, _ => throw new InvalidOperationException()));
    }

    [Fact]
    public void NewProblemsReportsOnlyErrorsIntroducedByTheEdits()
    {
        var rows = Rows(Rule(0));
        var edits = new[] { new RowEdit(Table.Name, Table.KeyOf(Rule(1)), null, Rule(1)) };
        IEnumerable<InteractionIssue> Check(RowSet set) =>
            new InteractionIssue[] { new(true, "old error"), new(false, "a warning") }
                .Concat(set.All(Table).Count > 1 ? [new InteractionIssue(true, "new error")] : []);
        Assert.Equal(["new error"], SmartRuleEdits.NewProblems(AcTables.Instance, rows, edits, Check));
    }

    [Fact]
    public void ChangeTypeKeepsTimerParametersAndResetsOthers()
    {
        var timer = new SmartRule(Rule(0, eventType: 0));
        var keep = SmartRuleEdits.ChangeTypeValues(SmartCatalog.Ac, timer, "event", 60, Rows(), Entry);
        Assert.Equal([("event_type", (object)60u)], keep);

        var combat = new SmartRule(Rule(0, eventType: 4));
        var reset = SmartRuleEdits.ChangeTypeValues(SmartCatalog.Ac, combat, "event", 0, Rows(), Entry);
        Assert.Equal(7, reset.Length);
        Assert.All(reset.Skip(1), v => Assert.Equal(0u, v.Value));

        var target = SmartRuleEdits.ChangeTypeValues(SmartCatalog.Ac, combat, "target", 2, Rows(), Entry);
        Assert.Equal(["target_type", "target_param1", "target_param2", "target_param3", "target_param4", "target_x", "target_y", "target_z", "target_o"], target.Select(v => v.Column));
    }

    [Fact]
    public void ChangingToTalkSelectsTheFirstTextGroup()
    {
        var rows = Rows();
        rows.Set(InteractionTables.CreatureText, Row.Of(("CreatureID", Entry), ("GroupID", 3u), ("ID", 0u)));
        var values = SmartRuleEdits.ChangeTypeValues(SmartCatalog.Ac, new SmartRule(Rule(0)), "action", SmartCatalog.Talk, rows, Entry);
        Assert.Equal(("action_param1", (object)3u), values[^1]);
        Assert.Equal(8, values.Length);
    }

    [Fact]
    public void DeletingARootRemovesItsWholeChain()
    {
        var rows = Rows(Rule(0, link: 1), Rule(1, link: 2, eventType: SmartCatalog.LinkEvent), Rule(2, eventType: SmartCatalog.LinkEvent), Rule(3));
        var (label, edits) = SmartRuleEdits.DeleteEdits(rows, Scope, new SmartRule(Rule(0, link: 1)));
        Assert.Equal("Delete rule and 2 \"Then\" step(s)", label);
        Assert.Equal(["-0", "-1", "-2"], edits.Select(Shape));
        Assert.Equal([(3u, 0u)], Ids(Apply(rows, edits)));
        Assert.Equal("Delete rule", SmartRuleEdits.DeleteEdits(rows, Scope, new SmartRule(Rule(3))).Label);
    }

    [Fact]
    public void DeletingAMiddleStepRelinksItsPredecessor()
    {
        var rows = Rows(Rule(0, link: 1), Rule(1, link: 2, eventType: SmartCatalog.LinkEvent), Rule(2, eventType: SmartCatalog.LinkEvent));
        var middle = SmartRuleEdits.RulesOf(rows, Scope)[1];
        var (label, edits) = SmartRuleEdits.DeleteEdits(rows, Scope, middle);
        Assert.Equal("Delete \"Then\" step", label);
        Assert.Equal("-1", Shape(edits[0]));
        Assert.Equal([(0u, 2u), (2u, 0u)], Ids(Apply(rows, edits)));
    }

    [Fact]
    public void SwapRewritesIdsAndLinksPointingAtEither()
    {
        // 0 -> 1 chain and a separate root 2; swap roots 0 and 2.
        var rows = Rows(Rule(0, link: 1), Rule(1, link: 0, eventType: SmartCatalog.LinkEvent), Rule(2));
        var rules = SmartRuleEdits.RulesOf(rows, Scope);
        var (map, edits) = SmartRuleEdits.SwapEdits(rows, Scope, rules[0], rules[2]);
        Assert.Equal(new Dictionary<uint, uint> { [0] = 2, [2] = 0 }, map);
        // Keys change, so all deletes come first.
        Assert.StartsWith("-", Shape(edits[0]));
        Assert.True(edits.TakeWhile(e => e.After == null).Count() == edits.Length / 2, "deletes precede inserts");
        Assert.Equal([(0u, 0u), (1u, 0u), (2u, 1u)], Ids(Apply(rows, edits)));
    }

    [Fact]
    public void SwapRemapsLinksToSwappedIds()
    {
        var rows = Rows(Rule(1, link: 2), Rule(2));
        var rules = SmartRuleEdits.RulesOf(rows, Scope);
        var (_, edits) = SmartRuleEdits.SwapEdits(rows, Scope, rules[0], rules[1]);
        Assert.Equal([(1u, 0u), (2u, 1u)], Ids(Apply(rows, edits)));
    }

    [Fact]
    public void DuplicateCopiesTheChainUnderFreshIdsWithRemappedLinks()
    {
        var rows = Rows(Rule(0, link: 1), Rule(1, link: 2, eventType: SmartCatalog.LinkEvent), Rule(2, eventType: SmartCatalog.LinkEvent), Rule(3));
        var (first, edits) = SmartRuleEdits.DuplicateEdits(rows, Scope, new SmartRule(Rule(0, link: 1)));
        Assert.Equal(4u, first);
        Assert.Equal(["+4", "+5", "+6"], edits.Select(Shape));
        Assert.Equal([(4u, 5u), (5u, 6u), (6u, 0u)], Ids(Apply(rows, edits)).Where(x => x.Id >= 4));
    }

    [Fact]
    public void DuplicatingANonRootThrows()
    {
        var rows = Rows(Rule(0, link: 1), Rule(1, eventType: SmartCatalog.LinkEvent));
        Assert.ThrowsAny<InvalidOperationException>(() => SmartRuleEdits.DuplicateEdits(rows, Scope, SmartRuleEdits.RulesOf(rows, Scope)[1]));
    }

    [Fact]
    public void CopyScopeRewritesEntryOrGuidAndAppendsEnableEdits()
    {
        var rows = Rows(Rule(0), Rule(1));
        var spawn = new SmartScope(-42, SmartScope.Creature);
        var enable = new RowEdit("creature_template", "295", null, Row.Of(("entry", 295u)));
        var edits = SmartRuleEdits.CopyScopeEdits(rows, Scope, spawn, [enable]);
        Assert.Equal(3, edits.Length);
        Assert.All(edits.Take(2), e => Assert.Equal(-42, e.After!.I("entryorguid")));
        Assert.Same(enable, edits[2]);
        Assert.Equal([(0u, 0u), (1u, 0u)], Ids(Apply(rows, edits), spawn));
    }
}
