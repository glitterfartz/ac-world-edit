using ACWorldEdit.Foundation;
using Xunit;

namespace ACWorldEdit.Tests.Smart;

/// <summary>
/// The SmartAI editor's save paths without Godot or a database: the same edit builders the window
/// uses, committed through <see cref="ContentDrafts"/> against a hand-built snapshot. Every rule and
/// speech edit must save, persist, undo and export.
/// </summary>
public class SmartEditorFlowTests : IDisposable
{
    const uint Entry = 295;
    const string Source = "db";
    static readonly SmartScope Scope = new((int)Entry, SmartScope.Creature);
    static readonly SmartScope TriggerScope = new(77, SmartScope.AreaTrigger);
    static readonly SmartScope ListScope = new(29500, SmartScope.ActionList);
    static readonly TableSchema Smart = AcTables.SmartScripts;

    sealed class Names : IContentNames
    {
        public string? Spell(uint id) => $"spell {id}";
        public string? Creature(uint id) => $"creature {id}";
        public string? GameObject(uint id) => $"object {id}";
        public string? Quest(uint id) => $"quest {id}";
        public string? Item(uint id) => $"item {id}";
        public string? Text(uint entry, uint group) => "text";
        public bool CreatureSpawnExists(ulong guid) => true;
        public string? Emote(uint id) => $"emote {id}";
        public string? TextEmote(uint id) => $"text emote {id}";
        public string? FactionTemplate(uint id) => $"faction {id}";
        public string? Faction(uint id) => $"faction {id}";
    }

    readonly string dir = Path.Combine(Path.GetTempPath(), "acwe-tests", Guid.NewGuid().ToString("N"));
    readonly ContentDrafts drafts;
    // The database as the editor read it: the creature (SmartAI) and one existing speech group.
    readonly RowSet snapshot = new();
    RowSet effective;

    public SmartEditorFlowTests()
    {
        drafts = new ContentDrafts(Path.Combine(dir, "content.json"));
        snapshot.Set(AcTables.CreatureAI, Row.Of(("entry", Entry), ("AIName", "SmartAI")));
        snapshot.Set(AcTables.CreatureText, SpeechEdits.NewLine(SmartCatalog.Ac, snapshot, Entry, 0, "Test").Edit.After!);
        effective = snapshot.Clone();
    }

    public void Dispose()
    {
        drafts.Dispose();
        try
        {
            Directory.Delete(dir, true);
        }
        catch (IOException)
        {
        }
    }

    IEnumerable<InteractionIssue> Check(RowSet rows)
    {
        foreach (var scope in new[] { Scope, TriggerScope, ListScope })
        {
            foreach (var issue in SmartCatalog.Ac.Check(scope, SmartRuleEdits.RulesOf(SmartCatalog.Ac, rows, scope), new Names(), rows.Get(AcTables.CreatureAI, Entry.ToString()), Entry))
            {
                yield return issue;
            }
        }
    }

    (bool Ok, string Message) Save(string label, params RowEdit[] edits)
    {
        var result = drafts.Commit(this, Source, snapshot, effective, label, edits, Check);
        effective = drafts.Draft!.Effective(Source, snapshot);
        return result;
    }

    void SaveOk(string label, params RowEdit[] edits)
    {
        var (ok, message) = Save(label, edits);
        Assert.True(ok, message);
    }

    static Row CreatureTemplate(uint npcflag = 0, string subname = "") => Row.Of(("entry", 9001u), ("npcflag", npcflag), ("gossip_menu_id", 0u), ("subname", subname));

    static RowSet WithTemplate(Row row)
    {
        var rows = new RowSet();
        rows.Set(AcTables.CreatureTemplate, row);
        return rows;
    }

    // An earlier draft on a table the SmartAI editor never loads, as left by the gossip or quest editors.
    void LeaveDraftElsewhere()
    {
        var before = CreatureTemplate();
        drafts.Draft!.Execute(Source, WithTemplate(before), "elsewhere", [new RowEdit(AcTables.CreatureTemplate.Name, "9001", before, CreatureTemplate(1))]);
        effective = drafts.Draft!.Effective(Source, snapshot);
    }

    SmartRule AddRule(SmartScope scope, RuleTemplate template, uint? after = null)
    {
        var row = SmartRuleEdits.FromTemplate(SmartCatalog.Ac, effective, scope, Entry, "Test", template);
        var (pendingRow, edits) = SmartRuleEdits.PendingEdits(SmartCatalog.Ac, effective, scope, row, after, "Test", () => []);
        SaveOk($"Add {template.Label}", edits);
        return new SmartRule(pendingRow);
    }

    Row Current(Row row) => effective.Get(Smart, Smart.KeyOf(row))!;

    [Fact]
    public void DraftOnAnotherTableDoesNotBlockSavingRules()
    {
        LeaveDraftElsewhere();
        Assert.NotEmpty(drafts.Draft!.Conflicts(Source, snapshot));
        AddRule(Scope, SmartRuleEdits.RuleTemplates[0]);
        Assert.Single(SmartRuleEdits.RulesOf(SmartCatalog.Ac, effective, Scope));
    }

    [Fact]
    public void DraftOnAnotherTableDoesNotBlockSpeech()
    {
        LeaveDraftElsewhere();
        var (label, group) = SpeechEdits.NewLine(SmartCatalog.Ac, effective, Entry, SpeechEdits.NextGroup(SmartCatalog.Ac, effective, Entry), "Test");
        SaveOk(label, group);
        var (lineLabel, line) = SpeechEdits.NewLine(SmartCatalog.Ac, effective, Entry, 0, "Test");
        SaveOk(lineLabel, line);
        Assert.Equal(3, SpeechEdits.LinesOf(SmartCatalog.Ac, effective, Entry).Count());
    }

    [Fact]
    public void EditingTheStaleRowItselfStillFails()
    {
        var before = CreatureTemplate();
        var table = AcTables.CreatureTemplate.Name;
        drafts.Draft!.Execute(Source, WithTemplate(before), "elsewhere", [new RowEdit(table, "9001", before, CreatureTemplate(1))]);
        var changedInDb = WithTemplate(CreatureTemplate(8));
        var error = Assert.Throws<InvalidDataException>(() => drafts.Draft!.Execute(Source, changedInDb, "again", [new RowEdit(table, "9001", before, CreatureTemplate(0, "x"))]));
        Assert.Contains("Source rows changed", error.Message);
    }

    public static IEnumerable<object[]> Templates() =>
        SmartRuleEdits.RuleTemplates.Select(t => new object[] { 0, t.Label })
            .Concat(SmartRuleEdits.TriggerTemplates.Select(t => new object[] { 1, t.Label }))
            .Concat(SmartRuleEdits.StepTemplates.Select(t => new object[] { 2, t.Label }));

    [Theory]
    [MemberData(nameof(Templates))]
    public void EveryTemplateSavesAsARuleOrStep(int kind, string label)
    {
        var scope = kind switch { 0 => Scope, 1 => TriggerScope, _ => ListScope };
        var template = SmartRuleEdits.TemplatesFor(scope).Single(t => t.Label == label);
        var rule = AddRule(scope, template);
        Assert.Contains(SmartRuleEdits.RulesOf(SmartCatalog.Ac, effective, scope), r => r.Id == rule.Id);
        // It survives reopening the draft file (the single-writer lock is released first).
        drafts.Dispose();
        using var reopened = new ContentDrafts(Path.Combine(dir, "content.json"));
        Assert.Contains(SmartRuleEdits.RulesOf(SmartCatalog.Ac, reopened.Draft!.Effective(Source, snapshot), scope), r => r.Id == rule.Id);
    }

    [Fact]
    public void ThenStepLinksToItsPredecessor()
    {
        var first = AddRule(Scope, SmartRuleEdits.RuleTemplates[0]);
        var then = AddRule(Scope, SmartRuleEdits.ThenTemplate(Scope), first.Id);
        Assert.Equal(then.Id, SmartRuleEdits.RulesOf(SmartCatalog.Ac, effective, Scope).Single(r => r.Id == first.Id).Link);
    }

    [Fact]
    public void RuleFieldEditsSaveAndUndo()
    {
        var rule = AddRule(Scope, SmartRuleEdits.RuleTemplates[1]);
        foreach (var (column, value) in new (string, object)[] { ("event_chance", 50u), ("event_flags", 1u), ("comment", "edited"), ("event_param1", 1000u), ("action_param1", 133u) })
        {
            var current = Current(rule.Row);
            SaveOk($"Edit {column}", new RowEdit(Smart.Name, Smart.KeyOf(current), current, current.With(column, value)));
            Assert.Equal(value.ToString(), Current(rule.Row)[column]);
        }

        drafts.History(this, false);
        effective = drafts.Draft!.Effective(Source, snapshot);
        Assert.Equal("1000", Current(rule.Row)["event_param1"]);
        Assert.NotEqual("133", Current(rule.Row)["action_param1"]);
        drafts.History(this, true);
        effective = drafts.Draft!.Effective(Source, snapshot);
        Assert.Equal("133", Current(rule.Row)["action_param1"]);
    }

    [Fact]
    public void DuplicateSwapAndDeleteSave()
    {
        AddRule(Scope, SmartRuleEdits.RuleTemplates[0]);
        var second = AddRule(Scope, SmartRuleEdits.RuleTemplates[3]);
        var (_, duplicate) = SmartRuleEdits.DuplicateEdits(SmartCatalog.Ac, effective, Scope, second);
        SaveOk("Duplicate", duplicate);
        Assert.Equal(3, SmartRuleEdits.RulesOf(SmartCatalog.Ac, effective, Scope).Count);
        var rules = SmartRuleEdits.RulesOf(SmartCatalog.Ac, effective, Scope);
        SaveOk("Swap", SmartRuleEdits.SwapEdits(SmartCatalog.Ac, effective, Scope, rules[0], rules[1]).Edits);
        var (label, delete) = SmartRuleEdits.DeleteEdits(SmartCatalog.Ac, effective, Scope, SmartRuleEdits.RulesOf(SmartCatalog.Ac, effective, Scope)[0]);
        SaveOk(label, delete);
        Assert.Equal(2, SmartRuleEdits.RulesOf(SmartCatalog.Ac, effective, Scope).Count);
    }

    [Fact]
    public void NewSpeechGroupAndLineSave()
    {
        uint group = SpeechEdits.NextGroup(SmartCatalog.Ac, effective, Entry);
        Assert.Equal(1u, group);
        var (label, edit) = SpeechEdits.NewLine(SmartCatalog.Ac, effective, Entry, group, "Test");
        Assert.StartsWith("Add speech group", label);
        SaveOk(label, edit);
        var (lineLabel, second) = SpeechEdits.NewLine(SmartCatalog.Ac, effective, Entry, group, "Test");
        Assert.StartsWith("Add line to group", lineLabel);
        SaveOk(lineLabel, second);
        Assert.Equal([0u, 1u], SpeechEdits.LinesOf(SmartCatalog.Ac, effective, Entry).Where(t => t.U("GroupID") == group).Select(t => t.U("ID")).OrderBy(i => i));
    }

    [Fact]
    public void EverySpeechColumnEditsAndSaves()
    {
        var line = SpeechEdits.LinesOf(SmartCatalog.Ac, effective, Entry).Single();
        foreach (var (column, value) in new (string, object)[]
        {
            ("Text", "Hello, $n!"), ("Type", 14u), ("Language", 7), ("Probability", 40f), ("Emote", 3u), ("Duration", 2000u), ("Sound", 5u), ("TextRange", 2u), ("comment", "note")
        })
        {
            var edit = SpeechEdits.Change(SmartCatalog.Ac, effective, line, (column, value));
            Assert.NotNull(edit);
            SaveOk($"Edit {column}", edit!);
            line = SpeechEdits.LinesOf(SmartCatalog.Ac, effective, Entry).Single();
        }

        Assert.Equal("Hello, $n!", line.T("Text"));
        Assert.Equal(14u, line.U("Type"));
        Assert.Equal(40f, line.F("Probability"));
        Assert.Null(SpeechEdits.Change(SmartCatalog.Ac, effective, line, ("Text", "Hello, $n!")));
    }

    [Fact]
    public void BroadcastTextLinksChangesAndUnlinks()
    {
        var line = SpeechEdits.LinesOf(SmartCatalog.Ac, effective, Entry).Single();
        SaveOk("Link", SpeechEdits.SetBroadcast(SmartCatalog.Ac, effective, line, 12345)!);
        Assert.Equal(12345, SpeechEdits.LinesOf(SmartCatalog.Ac, effective, Entry).Single().I("BroadcastTextId"));
        SaveOk("Link another", SpeechEdits.SetBroadcast(SmartCatalog.Ac, effective, SpeechEdits.LinesOf(SmartCatalog.Ac, effective, Entry).Single(), 777)!);
        Assert.Equal(777, SpeechEdits.LinesOf(SmartCatalog.Ac, effective, Entry).Single().I("BroadcastTextId"));
        SaveOk("Unlink", SpeechEdits.SetBroadcast(SmartCatalog.Ac, effective, SpeechEdits.LinesOf(SmartCatalog.Ac, effective, Entry).Single(), 0)!);
        Assert.Equal(0, SpeechEdits.LinesOf(SmartCatalog.Ac, effective, Entry).Single().I("BroadcastTextId"));
    }

    [Fact]
    public void RemovingALineSaves()
    {
        var (label, add) = SpeechEdits.NewLine(SmartCatalog.Ac, effective, Entry, 0, "Test");
        SaveOk(label, add);
        SaveOk("Remove", SpeechEdits.Remove(SmartCatalog.Ac, SpeechEdits.LinesOf(SmartCatalog.Ac, effective, Entry).First()));
        Assert.Single(SpeechEdits.LinesOf(SmartCatalog.Ac, effective, Entry));
    }

    [Fact]
    public void LongCreatureNamesFitTheCommentColumn()
    {
        var (label, edit) = SpeechEdits.NewLine(SmartCatalog.Ac, effective, Entry, 5, new string('x', 400));
        SaveOk(label, edit);
    }

    [Fact]
    public void RuleAndSpeechEditsExportAsSqlWithReverts()
    {
        AddRule(Scope, SmartRuleEdits.RuleTemplates[0]);
        var (label, edit) = SpeechEdits.NewLine(SmartCatalog.Ac, effective, Entry, 1, "Test");
        SaveOk(label, edit);
        SaveOk("Link", SpeechEdits.SetBroadcast(SmartCatalog.Ac, effective, SpeechEdits.LinesOf(SmartCatalog.Ac, effective, Entry).Single(t => t.U("GroupID") == 1), 99)!);
        var net = drafts.Draft!.NetEdits();
        var scripts = SqlExport.Build(Source, [], net.Select(n => n.Edit), new DateTime(2026, 1, 1));
        Assert.Contains("INSERT INTO `smart_scripts`", scripts.Apply);
        Assert.Contains("INSERT INTO `creature_text`", scripts.Apply);
        Assert.Contains("DELETE FROM `creature_text`", scripts.Revert);
        Assert.Contains("DELETE FROM `smart_scripts`", scripts.Revert);
        Assert.Equal(net.Count, new PendingChanges(Path.Combine(dir, "exported.json")).Collect([], net).Count);
    }
}
