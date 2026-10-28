using ACWorldEdit.Foundation;
using ACWorldEdit.Tests.Support;
using Xunit;

namespace ACWorldEdit.Tests.Smart;

/// <summary>
/// SmartCatalog.Ac.Check loader rules (SmartScriptMgr.cpp), plus sentence/chain helpers, one rule per
/// test over in-memory rows.
/// </summary>
public class SmartBehaviorTests
{
    sealed class Names : IContentNames
    {
        public string? Spell(uint id) => id == 133 ? "Fireball" : null;
        public string? Creature(uint id) => id == 6 ? "Kobold Vermin" : null;
        public string? GameObject(uint id) => id == 50 ? "Chest" : null;
        public string? Quest(uint id) => id == 7 ? "Known quest" : null;
        public string? Item(uint id) => id == 2589 ? "Linen Cloth" : null;
        public string? Text(uint entry, uint group) => entry == 42 && group == 0 ? "For the Alliance!" : null;
        public bool CreatureSpawnExists(ulong guid) => guid == 5;
        public string? Emote(uint id) => id == 3 ? "wave" : null;
        public string? TextEmote(uint id) => id == 101 ? "wave" : null;
        public string? FactionTemplate(uint id) => id == 35 ? "Friendly" : null;
        public string? Faction(uint id) => null;
    }

    static readonly Names Lookup = new();
    static readonly SmartScope Entry = new(42, SmartScope.Creature);
    const uint Aggro = 4, Cast = 11, Victim = 2;

    static SmartRule Rule(uint id, uint ev = Aggro, uint action = Cast, uint target = Victim, SmartScope? scope = null, Func<Row, Row>? edit = null)
    {
        var row = SmartCatalog.Ac.NewRow(scope ?? Entry, id, ev, action, target);
        return new(edit?.Invoke(row) ?? row);
    }

    static List<InteractionIssue> Check(SmartScope scope, IEnumerable<SmartRule> rules, Row? ai = null) => SmartCatalog.Ac.Check(scope, rules.ToList(), Lookup, ai, 42).ToList();

    static List<InteractionIssue> Check(params SmartRule[] rules) => Check(Entry, rules);

    static void AssertIssue(List<InteractionIssue> issues, bool error, string fragment) =>
        Assert.Contains(issues, i => i.Error == error && i.Message.Contains(fragment, StringComparison.Ordinal));

    [Theory]
    [InlineData(SmartParamKind.Spell, 133u, "Fireball")]
    [InlineData(SmartParamKind.Creature, 6u, "Kobold Vermin")]
    [InlineData(SmartParamKind.GameObject, 50u, "Chest")]
    [InlineData(SmartParamKind.Quest, 7u, "Known quest")]
    [InlineData(SmartParamKind.Item, 2589u, "Linen Cloth")]
    public void TryNameFindsTheNameForEachNamedKind(SmartParamKind kind, uint id, string expected)
    {
        Assert.True(Lookup.TryName(kind, id, out string name));
        Assert.Equal(expected, name);
        Assert.False(Lookup.TryName(kind, 999999, out _));
    }

    [Theory]
    [InlineData(SmartParamKind.Percent)]
    [InlineData(SmartParamKind.Emote)]
    [InlineData(SmartParamKind.FactionTemplate)]
    public void TryNameRejectsKindsThatAreNotNamedReferences(SmartParamKind kind)
    {
        Assert.False(ContentNamesExtensions.IsNamedReference(kind));
        Assert.False(Lookup.TryName(kind, 3, out _));
    }

    static Row Ai(string name) => Row.Of(("entry", 42u), ("AIName", name));

    static SmartType FirstWith(IEnumerable<SmartType> types, SmartParamKind kind) =>
        types.First(t => t.Params.Length > 0 && t.Params[0].Kind == kind && Overlaps(t));

    static bool Overlaps(SmartType t) => t.Id != SmartCatalog.LinkEvent;

    [Fact]
    public void ValidRuleHasNoIssues()
    {
        Assert.Empty(Check(Rule(0, edit: r => r.With("action_param1", 133u))));
    }

    [Fact]
    public void UnknownTypesAreErrorsAndRangeMarkersAreUnknown()
    {
        var issues = Check(Rule(0, 9999, 9999, 9999));
        AssertIssue(issues, true, "event type 9999");
        AssertIssue(issues, true, "action type 9999");
        AssertIssue(issues, true, "target type 9999");
        var marker = SmartCatalog.Ac.Events.First(SmartCatalog.IsMarker);
        AssertIssue(Check(Rule(0, (uint)marker.Id)), true, "is unknown to this server");
    }

    [Theory]
    [InlineData(100u, false)]
    [InlineData(0u, false)]
    [InlineData(101u, true)]
    public void ChanceAbove100IsAnError(uint chance, bool error)
    {
        Assert.Equal(error, Check(Rule(0, edit: r => r.With("event_chance", chance))).Any(i => i.Error && i.Message.Contains("chance", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData(SmartCatalog.AllPhases, false)]
    [InlineData(SmartCatalog.AllPhases + 1, true)]
    public void PhasesBeyondTwelveAreAnError(uint phases, bool error)
    {
        Assert.Equal(error, Check(Rule(0, edit: r => r.With("event_phase_mask", phases))).Any(i => i.Error && i.Message.Contains("phases 1-12", StringComparison.Ordinal)));
    }

    [Fact]
    public void UnknownEventFlagBitsAreReportedWithTheirValue()
    {
        Assert.DoesNotContain(Check(Rule(0, edit: r => r.With("event_flags", SmartCatalog.AllEventFlags))), i => i.Error);
        AssertIssue(Check(Rule(0, edit: r => r.With("event_flags", 0x400u))), true, "unknown event flags 0x400");
        Assert.Equal(SmartCatalog.AllEventFlags, SmartCatalog.Ac.EventFlags.Aggregate(0u, (m, f) => m | f.Bit) | 0x40u | 0x20u);
    }

    [Fact]
    public void LinkTargetRules()
    {
        AssertIssue(Check(Rule(3, edit: r => r.With("link", 3u))), true, "links to itself");
        AssertIssue(Check(Rule(3, edit: r => r.With("link", 9u))), true, "rule 9, which does not exist");
        AssertIssue(Check(Rule(0, edit: r => r.With("link", 1u)), Rule(1)), true, "not a \"Then (linked)\" rule");
        var good = Check(Rule(0, edit: r => r.With("link", 1u)), Rule(1, SmartCatalog.LinkEvent));
        Assert.DoesNotContain(good, i => i.Error);
        Assert.Empty(good);
    }

    [Fact]
    public void OrphanThenRuleWarnsExceptInActionLists()
    {
        AssertIssue(Check(Rule(1, SmartCatalog.LinkEvent)), false, "never runs");
        var list = new SmartScope(4200, SmartScope.ActionList);
        Assert.DoesNotContain(Check(list, [Rule(1, SmartCatalog.LinkEvent, scope: list)]), i => i.Message.Contains("never runs", StringComparison.Ordinal));
    }

    [Fact]
    public void ReferencedIdsMustExistPerParameterKind()
    {
        var spell = FirstWith(SmartCatalog.Ac.Actions, SmartParamKind.Spell);
        AssertIssue(Check(Rule(0, action: (uint)spell.Id, edit: r => r.With("action_param1", 1u))), true, "does not exist");
        Assert.Empty(Check(Rule(0, action: (uint)spell.Id, edit: r => r.With("action_param1", 133u))));
        Assert.DoesNotContain(Check(Rule(0, action: (uint)spell.Id, edit: r => r.With("action_param1", 0u))), i => i.Error);

        var creature = FirstWith(SmartCatalog.Ac.Events, SmartParamKind.Creature);
        AssertIssue(Check(Rule(0, ev: (uint)creature.Id, edit: r => r.With("event_param1", 1u))), true, "event ");
        Assert.DoesNotContain(Check(Rule(0, ev: (uint)creature.Id, edit: r => r.With("event_param1", 6u))), i => i.Error && i.Message.Contains("does not exist", StringComparison.Ordinal));
    }

    [Fact]
    public void MissingSpawnForGuidScopeIsAnError()
    {
        var spawn = new SmartScope(-77, SmartScope.Creature);
        AssertIssue(Check(spawn, [Rule(0, scope: spawn)]), true, "Spawn #77");
        var known = new SmartScope(-5, SmartScope.Creature);
        Assert.DoesNotContain(Check(known, [Rule(0, scope: known)]), i => i.Message.Contains("Spawn #", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("", "the default")]
    [InlineData("AggressorAI", "AggressorAI")]
    public void NonSmartAiOffersAnEnableFix(string name, string described)
    {
        var issue = Assert.Single(Check(Entry, [Rule(0)], Ai(name)));
        Assert.False(issue.Error);
        Assert.Contains(described, issue.Message);
        var fix = Assert.Single(issue.Fix!);
        Assert.Equal("SmartAI", fix.After!.T("AIName"));
        Assert.Equal(name, fix.Before!.T("AIName"));
    }

    [Fact]
    public void SmartAiNeedsNoFixAndNoRulesOrNoRowNeedNone()
    {
        Assert.Empty(Check(Entry, [Rule(0)], Ai("SmartAI")));
        Assert.Empty(Check(Entry, [], Ai("")));
        Assert.Empty(Check(Entry, [Rule(0)], null));
        var list = new SmartScope(4200, SmartScope.ActionList);
        Assert.Empty(Check(list, [Rule(0, scope: list)], Ai("")));
    }

    [Fact]
    public void AreaTriggerAcceptsOnlyTriggerAndThenEvents()
    {
        var scope = new SmartScope(5140, SmartScope.AreaTrigger);
        var trigger = Rule(0, SmartCatalog.AreaTriggerEvent, scope: scope, target: 7);
        AssertIssue(Check(scope, [Rule(0, Aggro, scope: scope, target: 7)]), true, "only accepts");
        Assert.DoesNotContain(Check(scope, [trigger]), i => i.Error);
        Assert.True(SmartCatalog.Ac.AllowedForAreaTrigger(scope, SmartCatalog.AreaTriggerEvent));
        Assert.True(SmartCatalog.Ac.AllowedForAreaTrigger(scope, SmartCatalog.LinkEvent));
        Assert.False(SmartCatalog.Ac.AllowedForAreaTrigger(scope, Aggro));
        Assert.False(SmartCatalog.Ac.AllowedForAreaTrigger(scope, SmartCatalog.AreaTriggerExitEvent));
    }

    [Fact]
    public void AreaTriggerIdParameterMustMatchWhenSet()
    {
        var scope = new SmartScope(5140, SmartScope.AreaTrigger);
        AssertIssue(Check(scope, [Rule(0, SmartCatalog.AreaTriggerEvent, scope: scope, target: 7, edit: r => r.With("event_param1", 9999u))]), false, "9999");
        Assert.DoesNotContain(Check(scope, [Rule(0, SmartCatalog.AreaTriggerEvent, scope: scope, target: 7, edit: r => r.With("event_param1", 5140u))]), i => i.Message.Contains("only fires", StringComparison.Ordinal));
    }

    [Fact]
    public void AreaTriggerSelfTargetWarnsAndSuppressesTheOtherTargetWarnings()
    {
        var scope = new SmartScope(5140, SmartScope.AreaTrigger);
        var issues = Check(scope, [Rule(0, SmartCatalog.AreaTriggerEvent, SmartCatalog.Talk, SmartCatalog.SelfTarget, scope)]);
        AssertIssue(issues, false, "targets Self");
        Assert.DoesNotContain(issues, i => i.Message.Contains("only speaks", StringComparison.Ordinal));
    }

    [Fact]
    public void AreaTriggerScriptNameFixes()
    {
        var missing = Assert.Single(SmartCatalog.Ac.CheckAreaTriggerScript(new(5140), null, "", true));
        Assert.Equal(AreaTriggerRecord.SmartTrigger, missing.Fix![0].After!.T("ScriptName"));
        Assert.Null(missing.Fix[0].Before);
        Assert.Empty(SmartCatalog.Ac.CheckAreaTriggerScript(new(5140), null, "", false));
        var cpp = Row.Of(("entry", 5140), ("ScriptName", "at_cpp"));
        var replace = Assert.Single(SmartCatalog.Ac.CheckAreaTriggerScript(new(5140), cpp, "", true));
        Assert.Contains("at_cpp", replace.Message);
        Assert.Equal(cpp, replace.Fix![0].Before);
        Assert.Empty(SmartCatalog.Ac.CheckAreaTriggerScript(new(5140), cpp.With("ScriptName", AreaTriggerRecord.SmartTrigger), "", true));
    }

    [Fact]
    public void ChainsFollowLinksInOrderAndListOrphansSeparately()
    {
        var a = Rule(0, edit: r => r.With("link", 1u));
        var b = Rule(1, SmartCatalog.LinkEvent, edit: r => r.With("link", 2u));
        var c = Rule(2, SmartCatalog.LinkEvent);
        var orphan = Rule(5, SmartCatalog.LinkEvent);
        var solo = Rule(3);
        var chains = SmartCatalog.Chains([solo, c, orphan, b, a]);
        Assert.Equal([[0u, 1u, 2u], [3u], [5u]], chains.Select(ch => ch.Select(r => r.Id).ToArray()));
    }

    [Fact]
    public void ChainsSurviveLinkCyclesWithoutLooping()
    {
        var a = Rule(0, SmartCatalog.LinkEvent, edit: r => r.With("link", 1u));
        var b = Rule(1, SmartCatalog.LinkEvent, edit: r => r.With("link", 0u));
        var chains = SmartCatalog.Chains([a, b]);
        Assert.True(chains.Sum(ch => ch.Count) <= 2);
    }

    [Theory]
    [InlineData(0u, "0 s")]
    [InlineData(500u, "500 ms")]
    [InlineData(1000u, "1 s")]
    [InlineData(1500u, "1.5 s")]
    [InlineData(12000u, "12 s")]
    public void MsFormatting(uint value, string expected)
    {
        Assert.Equal(expected, SmartCatalog.FormatValue(SmartParamKind.Ms, value, Lookup));
    }

    [Fact]
    public void NamedKindsFallBackToIdsAndZeroMeansAny()
    {
        Assert.Equal("Fireball", SmartCatalog.FormatValue(SmartParamKind.Spell, 133, Lookup));
        Assert.Equal("spell 7", SmartCatalog.FormatValue(SmartParamKind.Spell, 7, Lookup));
        Assert.Equal("any spell", SmartCatalog.FormatValue(SmartParamKind.Spell, 0, Lookup));
        Assert.Equal("any creature", SmartCatalog.FormatValue(SmartParamKind.Creature, 0, Lookup));
        Assert.Equal("object 9", SmartCatalog.FormatValue(SmartParamKind.GameObject, 9, Lookup));
        Assert.Equal("\"For the Alliance!\"", SmartCatalog.FormatValue(SmartParamKind.TextGroup, 0, Lookup, 42));
        Assert.Equal("text group 1", SmartCatalog.FormatValue(SmartParamKind.TextGroup, 1, Lookup, 42));
        Assert.Equal("its default faction", SmartCatalog.FormatValue(SmartParamKind.FactionTemplate, 0, Lookup));
        Assert.Equal("yes", SmartCatalog.FormatValue(SmartParamKind.Bool, 2, Lookup));
        Assert.Equal("no", SmartCatalog.FormatValue(SmartParamKind.Bool, 0, Lookup));
        Assert.Equal("0x1F", SmartCatalog.FormatValue(SmartParamKind.Flags, 31, Lookup));
        Assert.Equal("#9", SmartCatalog.FormatValue(SmartParamKind.ActionList, 9, Lookup));
    }

    [Fact]
    public void DescribeShowsUnknownTypeIdsInsteadOfHidingThem()
    {
        string text = SmartCatalog.Ac.Describe(Rule(0, 9999, 9998, 9997), Lookup, 42);
        Assert.Contains("9999", text);
        Assert.Contains("9998", text);
        Assert.Contains("9997", text);
    }

    [Fact]
    public void DescribeAppendsChanceOnceAndPhaseExtras()
    {
        string text = SmartCatalog.Ac.Describe(Rule(0, edit: r => r.With("event_chance", 30u).With("event_flags", 1u).With("event_phase_mask", 0b101u)), Lookup, 42);
        Assert.Contains("(30% chance)", text);
        Assert.Contains("(once)", text);
        Assert.Contains("(phases 1,3)", text);
        Assert.DoesNotContain("chance", SmartCatalog.Ac.Describe(Rule(0), Lookup, 42));
    }

    [Fact]
    public void LinkedRuleReadsThen()
    {
        Assert.StartsWith("Then:", SmartCatalog.Ac.Describe(Rule(1, SmartCatalog.LinkEvent), Lookup, 42));
    }

    [Theory]
    [InlineData(0u, 0u, 0u, 0u, "Once, right away in combat: ")]
    [InlineData(3000u, 3000u, 0u, 0u, "Once, after 3 s in combat: ")]
    [InlineData(3000u, 3000u, 8000u, 8000u, "Every 8 s in combat (first after 3 s): ")]
    public void TimerEventsReadNaturally(uint min, uint max, uint repeatMin, uint repeatMax, string prefix)
    {
        var rule = Rule(0, 0, edit: r => r.With("event_param1", min).With("event_param2", max).With("event_param3", repeatMin).With("event_param4", repeatMax));
        Assert.StartsWith(prefix, SmartCatalog.Ac.Describe(rule, Lookup, 42));
    }

    [KnownBug("KB-002", "Timer ranges render each endpoint with its unit ('8 s–12 s'); the SmartCatalog.Timer doc comment promises '8–12 s'.")]
    public void TimerRangesShareOneUnit()
    {
        var rule = Rule(0, 0, edit: r => r.With("event_param3", 8000u).With("event_param4", 12000u));
        Assert.StartsWith("Every 8–12 s in combat: ", SmartCatalog.Ac.Describe(rule, Lookup, 42));
    }

    [Fact]
    public void ActionListTimerIsTheDelayAfterThePreviousStep()
    {
        var list = new SmartScope(4200, SmartScope.ActionList);
        Assert.StartsWith("After 2 s:", SmartCatalog.Ac.Describe(Rule(0, 0, scope: list, edit: r => r.With("event_param1", 2000u).With("event_param2", 2000u)), Lookup, 42));
        Assert.StartsWith("Right away:", SmartCatalog.Ac.Describe(Rule(0, 0, scope: list), Lookup, 42));
    }

    [Fact]
    public void PhaseListNamesSetBitsOneToTwelve()
    {
        Assert.Equal("", SmartCatalog.PhaseList(0));
        Assert.Equal("1,2,12", SmartCatalog.PhaseList(0b1000_0000_0011));
        Assert.Equal("1,2,3,4,5,6,7,8,9,10,11,12", SmartCatalog.PhaseList(SmartCatalog.AllPhases));
    }

    [Fact]
    public void AutoCommentUsesCatalogNamesWithFallbacks()
    {
        Assert.Equal("Kobold - " + SmartCatalog.Ac.Event(Aggro)!.Name + " - " + SmartCatalog.Ac.Action(Cast)!.Name, SmartCatalog.Ac.AutoComment(Rule(0), "Kobold"));
        Assert.Equal("Kobold - Linked - Action 9999", SmartCatalog.Ac.AutoComment(Rule(0, SmartCatalog.LinkEvent, 9999), "Kobold"));
        Assert.Equal("Kobold - Event 9999 - " + SmartCatalog.Ac.Action(Cast)!.Name, SmartCatalog.Ac.AutoComment(Rule(0, 9999), "Kobold"));
    }

    [Fact]
    public void NewRowHasEveryWhitelistedColumnAndDefaultsToFullChance()
    {
        var row = SmartCatalog.Ac.NewRow(Entry, 3, Aggro, Cast, Victim, "note");
        row.Validate(InteractionTables.SmartScripts);
        var rule = new SmartRule(row);
        Assert.Equal(100u, rule.Chance);
        Assert.Equal(0u, rule.Link);
        Assert.Equal("note", rule.Comment);
        Assert.Equal(Entry, rule.Scope);
        Assert.Equal(InteractionTables.SmartScripts.KeyOf(row), rule.Key);
    }

    [Fact]
    public void ScopeDescriptionsAndFlags()
    {
        Assert.Equal("All Kobold spawns", Entry.Describe("Kobold"));
        Assert.Equal("This spawn only (#5)", new SmartScope(-5, 0).Describe("Kobold"));
        Assert.True(new SmartScope(-5, 0).IsSpawn);
        Assert.False(new SmartScope(-5, SmartScope.AreaTrigger).IsSpawn);
        Assert.Equal("Action list 4200", new SmartScope(4200, SmartScope.ActionList).Describe(""));
        Assert.Equal("AreaTrigger 7", new SmartScope(7, SmartScope.AreaTrigger).Describe(""));
    }
}
