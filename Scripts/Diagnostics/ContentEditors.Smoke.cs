using Godot;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// Opt-in helpers for --content-smoke and --interaction-smoke. They call the same private paths the
/// editors' buttons and pickers use, so validation, drafting and re-rendering are the real ones.
/// </summary>
public partial class QuestEditorWindow
{
    public string SmokeStatus => status;
    public RowSet SmokeEffective => effective;
    public Row? SmokeQuest => Quest;
    public IReadOnlyList<InteractionIssue> SmokeIssues => issues;
    public bool SmokeEditingEnabled => Drafts.Draft != null && Quest != null;

    public void SmokeSearch(string term)
    {
        search.Text = term;
        _ = Search();
    }

    public bool SmokeSet(TableSchema table, string label, params (string Column, object Value)[] values) => Set(table, label, values);

    public bool SmokeCommit(string label, params RowEdit[] edits) => Commit(label, edits);

    public void SmokeHistory(bool forward) => History(forward);

    /// <summary>
    /// Press a page button by its caption (e.g. "Kill a creature…").
    /// </summary>
    public bool SmokePress(string text)
    {
        var button = page.FindChildren("*", "Button", true, false).OfType<Button>().FirstOrDefault(b => b.Text == text);
        button?.EmitSignal(BaseButton.SignalName.Pressed);
        return button != null;
    }

    public string? SmokeObjectiveName(int slot) => Quest is { } q && q.I($"RequiredNpcOrGo{slot}") > 0 ? snapshot?.Names.Creature((uint)q.I($"RequiredNpcOrGo{slot}")) : null;

    public PickerPopup? SmokePicker => GetChildren().OfType<PickerPopup>().LastOrDefault(p => p.Visible);
}

public partial class BehaviorEditorWindow
{
    public string SmokeStatus => status;
    public RowSet SmokeEffective => effective;
    public int SmokeLineCount => effective.All(InteractionTables.CreatureText).Count(t => t.U("CreatureID") == entry);
    public string SmokeAiName => CreatureAi(effective)?.T("AIName") ?? "";
    public string SmokeScriptName => snapshot?.ScriptName ?? "";

    public List<SmartRule> SmokeRules(SmartScope of) => RulesOf(effective, of);

    public string SmokeDescribe(SmartRule rule) => SmartCatalog.Describe(rule, snapshot!.Names, entry);

    public void SmokeAddRule(int template)
    {
        var t = RuleTemplates[template];
        AddRule(scope!.Value, t.Label, t.Event, t.EventParams, t.Action, t.ActionParams, t.Target);
    }

    public void SmokeAddThen(SmartRule rule) => AddThen(scope!.Value, rule);

    public void SmokeSwap(SmartRule a, SmartRule b) => Swap(scope!.Value, a, b);

    public void SmokeDelete(SmartRule rule) => Delete(scope!.Value, rule);

    public bool SmokeUpdate(SmartRule rule, string label, params (string Column, object Value)[] values) => Update(rule, label, values);

    public void SmokeChangeType(SmartRule rule, string part, uint id) => ChangeType(rule, part, id);

    public Task SmokeCreateActionList(SmartRule rule) => CreateActionList(rule);

    public void SmokeHistory(bool forward) => History(forward);

    public void SmokeExpand(uint? id)
    {
        expanded = id;
        Render();
    }

    /// <summary>
    /// Add a speech line through the page's add path, then give it text like the line field does.
    /// </summary>
    public void SmokeAddLine(uint group, string text)
    {
        AddLine(group);
        var table = InteractionTables.CreatureText;
        var line = effective.All(table).Where(t => t.U("CreatureID") == entry && t.U("GroupID") == group).OrderByDescending(t => t.U("ID")).FirstOrDefault();
        if (line != null)
        {
            Commit("Edit line", new RowEdit(table.Name, table.KeyOf(line), line, line.With("Text", text)));
        }
    }
}

public partial class PickerPopup
{
    /// <summary>
    /// Type into the search box (the TextChanged path runs the bounded search).
    /// </summary>
    public void SmokeSearch(string term)
    {
        search.Text = term;
        _ = Search();
    }
}
