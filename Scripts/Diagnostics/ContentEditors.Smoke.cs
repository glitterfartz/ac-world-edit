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

    /// <summary>
    /// Press a page button by node name (e.g. "AddRule", "ComposerAdd").
    /// </summary>
    public bool SmokePress(string name)
    {
        // Re-rendered pages leave old nodes queued for deletion until the frame ends; skip them.
        var button = page.FindChildren(name, "BaseButton", true, false).OfType<BaseButton>().FirstOrDefault(Live);
        button?.EmitSignal(BaseButton.SignalName.Pressed);
        return button != null;
    }

    bool Live(Node node)
    {
        for (var current = node; current != null && current != page; current = current.GetParent())
        {
            if (current.IsQueuedForDeletion())
            {
                return false;
            }
        }

        return true;
    }

    Node? LiveComposer => composerCard != null && IsInstanceValid(composerCard) && !composerCard.IsQueuedForDeletion() ? composerCard : null;

    OptionButton? ComposerChooser(string kind) => LiveComposer?.FindChild("Choose" + char.ToUpperInvariant(kind[0]) + kind[1..], true, false) as OptionButton;

    /// <summary>
    /// Selectable (non-heading) entries in the composer's dropdown for a kind.
    /// </summary>
    public int SmokeComposerOptions(string kind) => ComposerChooser(kind) is { } picker ? Enumerable.Range(0, picker.ItemCount).Count(i => !picker.IsItemSeparator(i)) : -1;

    /// <summary>
    /// Pick a type in the composer dropdown the way a click does (select + ItemSelected).
    /// </summary>
    public bool SmokeComposerChoose(string kind, uint id)
    {
        if (ComposerChooser(kind) is not { } picker)
        {
            return false;
        }

        int index = Enumerable.Range(0, picker.ItemCount).FirstOrDefault(i => !picker.IsItemSeparator(i) && picker.GetItemMetadata(i).AsInt64() == id, -1);
        if (index < 0)
        {
            return false;
        }

        picker.Select(index);
        picker.EmitSignal(OptionButton.SignalName.ItemSelected, index);
        return true;
    }

    public string SmokeComposerPreview => (LiveComposer?.FindChild("ComposerPreview", true, false) as Label)?.Text ?? "";

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
