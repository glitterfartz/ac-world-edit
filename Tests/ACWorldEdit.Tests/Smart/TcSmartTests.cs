using ACWorldEdit.Foundation;
using Xunit;

namespace ACWorldEdit.Tests.Smart;

/// <summary>
/// The two SmartAI dialects: each catalog's type ranges and parameter limits, the rows each builds for
/// its smart_scripts and creature_text tables, and the TrinityCore-only columns (string parameters,
/// Difficulties, SoundPlayType) through the row arithmetic and the SQL export.
/// </summary>
public class TcSmartTests
{
    static readonly SmartCatalog Tc = SmartCatalog.Trinity;
    static readonly SmartScope Scope = new(448, SmartScope.Creature);
    static readonly DateTime Stamp = new(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);

    public static TheoryData<WorldDialect> Dialects => new() { WorldDialect.AzerothCore, WorldDialect.TrinityCore };

    [Fact]
    public void EachDialectHasItsOwnTypeRanges()
    {
        // TrinityCore master: events 0-90, actions 0-159, targets 0-30; its plain END entries are markers.
        Assert.Equal("SMART_EVENT_AREATRIGGER_ENTER", Tc.Event(46)!.Code);
        Assert.Equal("SMART_EVENT_AREATRIGGER_ONTRIGGER", SmartCatalog.Ac.Event(46)!.Code);
        Assert.NotNull(Tc.Event(90));
        Assert.Null(Tc.Event(91));
        Assert.NotNull(Tc.Action(159));
        Assert.Null(Tc.Action(160));
        Assert.NotNull(Tc.Target(30));
        Assert.Null(Tc.Target(31));
        // AzerothCore-only ranges are unknown to TrinityCore, and the reverse.
        Assert.NotNull(SmartCatalog.Ac.Event(101));
        Assert.Null(Tc.Event(101));
        Assert.NotNull(Tc.Event(88));
        Assert.Equal("SMART_EVENT_AREATRIGGER_EXIT", Tc.Event(88)!.Code);
    }

    [Fact]
    public void SlotsTheServerMarksUnusedCannotBeSelected()
    {
        // Event 12 (target health) is "UNUSED, DO NOT REUSE" in TrinityCore master.
        Assert.Null(Tc.Event(12));
        Assert.DoesNotContain(Tc.Events, t => !SmartCatalog.IsMarker(t) && t.Id == 12);
        Assert.Null(Tc.Action(15));
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public void ParametersFitTheTableColumnsAndSentences(WorldDialect dialect)
    {
        var smart = SmartCatalog.For(dialect);
        foreach (var (kind, types, max) in new[] { ("event", smart.Events, smart.EventParamCount), ("action", smart.Actions, smart.ActionParamCount), ("target", smart.Targets, SmartCatalog.TargetParamCount) })
        {
            foreach (var type in types.Where(t => !SmartCatalog.IsMarker(t)))
            {
                Assert.True(type.Params.Length <= max, $"{dialect} {kind} {type.Id} {type.Code} declares {type.Params.Length} parameters, the table has {max}");
                for (int i = type.Params.Length + 1; i <= 7; i++)
                {
                    Assert.False(type.Sentence.Contains("{" + i + "}"), $"{dialect} {kind} {type.Id} sentence references undeclared {{{i}}}");
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public void NewRowsMatchTheirTable(WorldDialect dialect)
    {
        var smart = SmartCatalog.For(dialect);
        smart.NewRow(Scope, 0, 4, 1, 1, "note").Validate(smart.Scripts);
        Assert.Equal(dialect == WorldDialect.TrinityCore, smart.NewRow(Scope, 0, 4, 1, 1).Values.ContainsKey("action_param7"));
        Assert.Equal(dialect == WorldDialect.AzerothCore, smart.NewRow(Scope, 0, 4, 1, 1).Values.ContainsKey("event_param6"));
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public void EveryTemplateBuildsAValidRow(WorldDialect dialect)
    {
        var smart = SmartCatalog.For(dialect);
        var rows = new RowSet();
        foreach (var template in SmartRuleEdits.RuleTemplates.Concat(SmartRuleEdits.StepTemplates))
        {
            var row = SmartRuleEdits.FromTemplate(smart, rows, Scope, 448, "Hogger", template);
            row.Validate(smart.Scripts);
            Assert.NotNull(smart.Event(template.Event));
            Assert.NotNull(smart.Action(template.Action));
            Assert.NotNull(smart.Target(template.Target));
        }
    }

    [Fact]
    public void AColumnTheDialectLacksReadsAsZero()
    {
        // A TrinityCore row has no event_param6 and an AzerothCore row no action_param7.
        var rule = new SmartRule(Tc.NewRow(Scope, 0, 4, 1, 1));
        Assert.Equal(0u, rule.EventParam(6));
        Assert.Equal("", rule.ActionString);
        var ac = new SmartRule(SmartCatalog.Ac.NewRow(Scope, 0, 4, 1, 1));
        Assert.Equal(0u, ac.ActionParam(7));
        Assert.StartsWith("When it enters combat: say text group", Tc.Describe(rule, new NoNames(), 448), StringComparison.Ordinal);
    }

    [Fact]
    public void ChangingATypeAlsoClearsTheStringParameter()
    {
        var rule = new SmartRule(Tc.NewRow(Scope, 0, 4, 1, 1).With("action_param_string", "left over").With("action_param3", 1u));
        var values = SmartRuleEdits.ChangeTypeValues(Tc, rule, "action", 11, new RowSet(), 448).ToDictionary(v => v.Column, v => v.Value);
        Assert.Equal("", values["action_param_string"]);
        Assert.Equal(0u, values["action_param7"]);
        Assert.False(values.ContainsKey("action_param8"));
        // Events have five parameters in TrinityCore and six in AzerothCore.
        var eventValues = SmartRuleEdits.ChangeTypeValues(Tc, rule, "event", 6, new RowSet(), 448).Select(v => v.Column).ToList();
        Assert.DoesNotContain("event_param6", eventValues);
        Assert.Contains("event_param5", eventValues);
        Assert.Contains("event_param6", SmartRuleEdits.ChangeTypeValues(SmartCatalog.Ac, rule, "event", 6, new RowSet(), 448).Select(v => v.Column));
    }

    [Fact]
    public void TheDeprecatedDifficultyFlagsWarnInsteadOfFailing()
    {
        var rule = new SmartRule(Tc.NewRow(Scope, 0, 4, 1, 1).With("event_flags", 0x4u));
        var issues = Tc.Check(Scope, [rule], new NoNames(), null, 448).ToList();
        Assert.Contains(issues, i => !i.Error && i.Message.Contains("Difficulties", StringComparison.Ordinal));
        Assert.DoesNotContain(issues, i => i.Error && i.Message.Contains("flags", StringComparison.Ordinal));
        // AzerothCore still reads them as heroic flags.
        var ac = new SmartRule(SmartCatalog.Ac.NewRow(Scope, 0, 4, 1, 1).With("event_flags", 0x4u));
        Assert.DoesNotContain(SmartCatalog.Ac.Check(Scope, [ac], new NoNames(), null, 448), i => i.Message.Contains("Difficulties", StringComparison.Ordinal));
    }

    [Fact]
    public void ARuleWithoutSmartAiOnTheCreatureWarnsAndFixesTheAiName()
    {
        var ai = Row.Of(("entry", 448u), ("AIName", ""));
        var rule = new SmartRule(Tc.NewRow(Scope, 0, 4, 1, 1));
        var issue = Assert.Single(Tc.Check(Scope, [rule], new NoNames(), ai, 448), i => i.Fix != null);
        var edit = Assert.Single(issue.Fix!);
        Assert.Equal("creature_ai", edit.Table);
        Assert.Equal("SmartAI", edit.After!.T("AIName"));
        edit.After.Validate(TcTables.CreatureAI);
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public void SpeechLinesMatchTheirTable(WorldDialect dialect)
    {
        var smart = SmartCatalog.For(dialect);
        var (label, edit) = SpeechEdits.NewLine(smart, new RowSet(), 448, 0, "Hogger");
        Assert.Equal("Add speech group 0", label);
        edit.After!.Validate(smart.CreatureText);
        Assert.Equal(dialect == WorldDialect.TrinityCore, edit.After.Values.ContainsKey("SoundPlayType"));
    }

    [Fact]
    public void TrinityCoreRulesAndSpeechExportAsInsertsAndAiNameUpdates()
    {
        var rule = SmartRuleEdits.FromTemplate(Tc, new RowSet(), Scope, 448, "Hogger", SmartRuleEdits.RuleTemplates[0]).With("Difficulties", "0,1").With("action_param_string", "it's");
        var ai = Row.Of(("entry", 448u), ("AIName", ""));
        var (_, speech) = SpeechEdits.NewLine(Tc, new RowSet(), 448, 0, "Hogger");
        var scripts = SqlExport.Build("src", [], [], [new RowEdit("smart_scripts", Tc.Scripts.KeyOf(rule), null, rule), new RowEdit("creature_ai", "448", ai, ai.With("AIName", "SmartAI")), speech], Stamp, null, TcSchema.Instance);
        Assert.Contains("INSERT INTO `smart_scripts`", scripts.Apply);
        Assert.Contains("`Difficulties`", scripts.Apply);
        Assert.Contains("'0,1'", scripts.Apply);
        Assert.Contains("'it''s'", scripts.Apply.Replace("\\'", "''", StringComparison.Ordinal));
        Assert.Contains("UPDATE `creature_template` SET `AIName` = 'SmartAI' WHERE `entry` = 448", scripts.Apply);
        Assert.Contains("INSERT INTO `creature_text`", scripts.Apply);
        Assert.Contains("`SoundPlayType`", scripts.Apply);
        Assert.Contains("DELETE FROM `smart_scripts`", scripts.Revert);
    }

    [Fact]
    public void TrinityCoreTablesRefuseAzerothCoreRows()
    {
        var ac = SmartCatalog.Ac.NewRow(Scope, 0, 4, 1, 1);
        Assert.Throws<InvalidDataException>(() => ac.Validate(Tc.Scripts));
        Assert.Throws<InvalidDataException>(() => Tc.NewRow(Scope, 0, 4, 1, 1).Validate(SmartCatalog.Ac.Scripts));
    }

    sealed class NoNames : IContentNames
    {
        public string? Spell(uint id) => null;
        public string? Creature(uint id) => null;
        public string? GameObject(uint id) => null;
        public string? Quest(uint id) => null;
        public string? Item(uint id) => null;
        public string? Text(uint entry, uint group) => null;
        public bool CreatureSpawnExists(ulong guid) => true;
        public string? Emote(uint id) => null;
        public string? TextEmote(uint id) => null;
        public string? FactionTemplate(uint id) => null;
        public string? Faction(uint id) => null;
    }
}
