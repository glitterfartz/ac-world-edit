using System.Globalization;
using Godot;
using ACWorldEdit.Assets;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>Quest editor: friendly pages over the quest tables and the creature starter/ender links.</summary>
public abstract partial class QuestPanel
{
    /// <summary>
    /// Record one validated command against this quest's rows.
    /// </summary>
    protected bool Commit(string label, params RowEdit[] edits)
    {
        if (snapshot == null || QuestId is not uint id)
        {
            return false;
        }

        var names = snapshot.Names;
        return CommitEdits(sourceKey, snapshot.Rows, effective, label, edits, rows => Check(id, rows, names), Rebuild, UpdateStatus);
    }

    /// <summary>
    /// Change columns on one per-quest row (inserting the row with defaults when missing).
    /// </summary>
    protected bool Set(TableSchema table, string label, params (string Column, object Value)[] values)
    {
        var existing = effective.Get(table, Key);
        var row = RowOf(table);
        foreach (var (column, value) in values)
        {
            row = row.With(column, value);
        }

        if (row == existing)
        {
            return true;
        }

        return Commit(label, new RowEdit(table.Name, Key, existing, row));
    }

    public void History(bool forward)
    {
        ApplyHistory(forward, Rebuild, () =>
        {
            if (QuestId is uint id)
            {
                _ = Load(id);
            }
        });
    }

    /// <summary>
    /// Create a quest (or copy the current one) under the next ID above both the database maximum
    /// and every quest ID already reserved in the draft.
    /// </summary>
    public async Task NewQuest(uint? copyFrom)
    {
        int current = ++generation;
        try
        {
            var source = DBSource.Open(Connection(), Lookup());
            var cat = await EnsureCatalog();
            var draft = Drafts.Draft;
            if (draft == null)
            {
                status = Drafts.Error ?? "Editing is disabled.";
                UpdateStatus();
                return;
            }

            var keys = draft.Keys(source.SourceKey);
            var apply = draft.Frozen(source.SourceKey);
            var probe = await Task.Run(() => source.ReadQuestAsync(copyFrom ?? 0, keys, apply, cat, CancellationToken.None));
            if (current != generation)
            {
                return;
            }

            sourceKey = source.SourceKey;
            uint id = Math.Max(probe.MaxQuestId, draft.MaxReserved(sourceKey, Template, "ID")) + 1;
            string key = id.ToString(CultureInfo.InvariantCulture);
            var probeView = draft.Effective(sourceKey, probe.Rows);
            var defaults = NewQuestRows(id, "New quest");
            List<RowEdit> edits = [];
            for (int i = 0; i < Tables.Length; i++)
            {
                var table = Tables[i];
                var row = copyFrom is uint from && probeView.Get(table, from.ToString(CultureInfo.InvariantCulture)) is { } original ? original.With("ID", id) : defaults[i];
                if (copyFrom != null && i == 0)
                {
                    row = row.With("LogTitle", row.T("LogTitle") + " (copy)");
                }

                edits.Add(new RowEdit(table.Name, key, null, row));
            }

            if (copyFrom is uint source0)
            {
                edits.AddRange(CopyRelated(source0, id, probe, probeView));
            }

            var (ok, message) = Drafts.Commit(this, sourceKey, probe.Rows, probeView, copyFrom == null ? $"Create quest {id}" : $"Copy quest {copyFrom} as {id}", edits, _ => []);
            status = ok ? (copyFrom == null ? $"Created quest {id}. Give it a title and an objective, then pick who offers it." : $"Copied as quest {id}. Givers are not copied.") : message;
            if (ok)
            {
                tab = Tab.Overview;
                await Load(id);
            }
            else
            {
                UpdateStatus();
            }
        }
        catch (Exception error)
        {
            status = DbErrors.Safe(error);
            UpdateStatus();
        }
    }

    /// <summary>
    /// Search picker over creatures, objects, items or quests. The chosen name is remembered so the
    /// edit validates before the next reload.
    /// </summary>
    protected void Pick(SmartParamKind kind, string heading, Action<uint> apply)
    {
        var names = snapshot?.Names;
        PickerPopup.Show(this, heading, term => DBSource.Open(Connection(), Lookup()).SearchAsync(kind, term, CancellationToken.None), (id, name) =>
        {
            var target = kind switch
            {
                SmartParamKind.Creature => names?.Creatures,
                SmartParamKind.GameObject => names?.GameObjects,
                SmartParamKind.Item => names?.Items,
                _ => names?.Quests
            };
            if (target != null)
            {
                target[id] = name;
            }

            apply(id);
        });
    }

    protected void PickSpell(string heading, Action<uint> apply) =>
        PickerPopup.Show(this, heading, term => SearchSpells(term), (id, _) => apply(id));

    /// <summary>Spell search for the spell pickers: the 3.3.5 client's spells, or the retail client's for TrinityCore.</summary>
    protected virtual Task<IReadOnlyList<(uint Id, string Name)>> SearchSpells(string term) => Task.FromResult<IReadOnlyList<(uint Id, string Name)>>(catalog?.SearchSpells(term) ?? []);

    protected void PickFromList(string heading, IEnumerable<(uint Id, string Name)> all, Action<uint> apply)
    {
        var list = all.Where(e => e.Name.Length > 0).ToList();
        PickerPopup.Show(this, heading, term => Task.FromResult<IReadOnlyList<(uint Id, string Name)>>(list
            .Where(e => term.Length == 0 || e.Name.Contains(term, StringComparison.OrdinalIgnoreCase) || e.Id.ToString(CultureInfo.InvariantCulture) == term.Trim())
            .OrderBy(e => e.Name.Length).Take(200).ToList()), (id, _) => apply(id));
    }

    protected void UpdateStatus()
    {
        if (statusLabel != null)
        {
            statusLabel.Text = status;
            statusLabel.Visible = status.Length > 0;
        }
    }
}
