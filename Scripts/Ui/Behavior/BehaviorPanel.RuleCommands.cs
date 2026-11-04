using System.Globalization;
using Godot;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>One-command rule changes: add, edit, delete, swap, duplicate, copy and reveal.</summary>
public partial class BehaviorPanel
{
    /// <summary>
    /// Apply column changes to an existing rule (one draft command) or to the pending rule (in
    /// memory; re-rendered so its sentence and fields follow).
    /// </summary>
    bool Change(SmartRule rule, bool isNew, string label, params (string Column, object Value)[] values)
    {
        if (!isNew)
        {
            return Update(rule, label, values);
        }

        if (pending is not { } p)
        {
            return false;
        }

        var row = p.Row;
        foreach (var (column, value) in values)
        {
            row = row.With(column, value);
        }

        pending = p with { Row = row };
        pendingError = "";
        Render();
        return true;
    }

    /// <summary>
    /// Change columns of one rule. A change to the key (id/link) becomes delete + insert.
    /// </summary>
    bool Update(SmartRule rule, string label, params (string Column, object Value)[] values)
    {
        var current = effective.Get(smart.Scripts, smart.Scripts.KeyOf(rule.Row));
        if (current == null)
        {
            return false;
        }

        var after = current;
        foreach (var (column, value) in values)
        {
            after = after.With(column, value);
        }

        return after == current || Commit(label, Rekey([(current, after)]));
    }

    /// <summary>
    /// Edits replacing rows whose key may change: all deletes first, then all inserts, so swapped
    /// keys never collide inside the command.
    /// </summary>
    RowEdit[] Rekey(IEnumerable<(Row Before, Row After)> changes) => SmartRuleEdits.Rekey(smart, changes);

    /// <summary>
    /// A new event/action/target type resets its parameters (their meaning changes), except timer
    /// events, which share the same four timing fields.
    /// </summary>
    void ChangeType(SmartRule rule, bool isNew, string part, uint id) => Change(rule, isNew, $"Change {part}", SmartRuleEdits.ChangeTypeValues(smart, rule, part, id, effective, entry));

    /// <summary>
    /// Delete a rule. A root takes its "Then" chain with it (those never run alone); a "Then" step
    /// is unlinked and its predecessor re-linked to whatever followed it.
    /// </summary>
    void Delete(SmartScope of, SmartRule rule)
    {
        var (label, edits) = SmartRuleEdits.DeleteEdits(smart, effective, of, rule);
        Commit(label, edits);
    }

    /// <summary>
    /// Swap the order of two root rules (ids), rewriting any link that points at either.
    /// </summary>
    void Swap(SmartScope of, SmartRule a, SmartRule b)
    {
        var (map, edits) = SmartRuleEdits.SwapEdits(smart, effective, of, a, b);
        // A pending "Then" step follows its rule to the new id.
        uint? after = pending?.After is uint id && pending.Scope == of && map.TryGetValue(id, out uint moved) ? moved : pending?.After;
        if (Commit(of.IsActionList ? "Reorder steps" : "Reorder rules", edits))
        {
            expanded = expanded == a.Id ? b.Id : expanded == b.Id ? a.Id : expanded;
            if (pending != null)
            {
                pending = pending with { After = after };
            }

            Render();
        }
    }

    /// <summary>
    /// Copy a rule and its "Then" chain under fresh ids at the end.
    /// </summary>
    void Duplicate(SmartScope of, SmartRule rule)
    {
        var (next, edits) = SmartRuleEdits.DuplicateEdits(smart, effective, of, rule);
        if (Commit("Duplicate rule", edits))
        {
            reveal = next;
            Render();
        }
    }

    /// <summary>
    /// Give a spawn its own copy of the entry rules (a spawn with rules ignores the entry's). Like
    /// any first rule, this enables SmartAI when the creature uses the default AI.
    /// </summary>
    void CopyScope(SmartScope from, SmartScope to) => Commit("Copy rules to this spawn", SmartRuleEdits.CopyScopeEdits(smart, effective, from, to, EnableSmartAiIfDefault()));

    /// <summary>
    /// Scroll a card into view once it has been laid out (positions are only valid next frame).
    /// </summary>
    async void RevealLater(Control card)
    {
        reveal = null;
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (IsInstanceValid(card) && card.IsInsideTree() && !card.IsQueuedForDeletion())
        {
            scroll.EnsureControlVisible(card);
        }
    }
}
