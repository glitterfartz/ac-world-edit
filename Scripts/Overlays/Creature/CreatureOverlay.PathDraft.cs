using System.Globalization;
using Godot;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// Draft-aware path row reconciliation: the database rows overlaid with staged net edits, so new,
/// renumbered and deleted points read correctly before anything is exported, and the users a draft
/// links to a path are seen by the delete decision.
/// </summary>
public partial class CreatureOverlay
{
    /// <summary>Node count with the draft applied (renumbers are delete+insert pairs).</summary>
    int DraftNodeTotal(string key, IWorldSchema schema, uint pathId, IEnumerable<Row> dbRows)
    {
        if (sharedDrafts?.Draft == null)
        {
            return dbRows.Count();
        }

        return MovementEdits.EffectivePathRows(schema, pathId, dbRows, NetEdits(key)).Count;
    }

    /// <summary>
    /// A path's rows with the draft applied: the database rows with net After-rows stored by point
    /// and net-deleted points removed. Deleting exactly these leaves nothing behind, whether the
    /// points were exported or are still staged.
    /// </summary>
    IReadOnlyList<Row> DraftPathRows(string key, IWorldSchema schema, uint pathId, IEnumerable<Row> dbRows) =>
        MovementEdits.EffectivePathRows(schema, pathId, dbRows, NetEdits(key));

    /// <summary>The path header with the draft applied: the net After-row wins over the database row.</summary>
    Row? DraftHeaderRow(string key, IWorldSchema schema, uint pathId, Row? dbHeader) =>
        MovementEdits.EffectiveHeaderRow(schema, pathId, dbHeader, NetEdits(key));

    IEnumerable<RowEdit> NetEdits(string key) => sharedDrafts?.Draft?.NetFor(key).Select(e => e.Edit) ?? [];

    /// <summary>
    /// Users the draft links to the path but the database read cannot know yet: creature addon rows
    /// whose effective path is this path (created this session) and template addon rows changed this
    /// session, excluding spawns the draft deletes and users it points elsewhere.
    /// </summary>
    IReadOnlyList<string> DraftPathUsers(string key, IWorldSchema schema, uint pathId, ulong self, IReadOnlyCollection<ulong> known)
    {
        if (sharedDrafts?.Draft == null)
        {
            return [];
        }

        var users = new List<string>();
        var addon = SpawnEdits.AddonTable(schema, SpawnKind.Creature);
        var template = schema.Tables.Find("creature_template_addon");
        string column = MovementEdits.AddonPathColumn(schema.Dialect);
        foreach (var (_, edit) in sharedDrafts.Draft.NetFor(key))
        {
            bool isAddon = edit.Table == addon.Name, isTemplate = edit.Table == template.Name;
            if ((!isAddon && !isTemplate) || edit.After == null
                || !uint.TryParse(edit.After[column], NumberStyles.None, CultureInfo.InvariantCulture, out uint path) || path != pathId)
            {
                continue;
            }

            if (isAddon)
            {
                ulong guid = edit.After.UL("guid");
                if (guid != self && !known.Contains(guid) && !(poses?.Deleted(key, guid) ?? false))
                {
                    string name = markers.TryGetValue(guid, out var marker) ? marker.Spawn.Name : "creature";
                    users.Add($"{name} #{guid} (this session)");
                }
            }
            else
            {
                users.Add($"template {edit.After.U("entry")} (this session)");
            }
        }

        return users;
    }

    /// <summary>True when the draft removes this spawn, so a database-read path user no longer counts.</summary>
    bool DraftDeletes(ulong guid) => poses?.Deleted(sourceKey, guid) == true;

    /// <summary>The path a draft edit leaves this addon/addon-like row at, when the draft touches it.</summary>
    uint? DraftAddonPath(string key, IWorldSchema schema, string table, ulong guid)
    {
        if (sharedDrafts?.Draft == null)
        {
            return null;
        }

        string column = MovementEdits.AddonPathColumn(schema.Dialect);
        foreach (var (_, edit) in sharedDrafts.Draft.NetFor(key))
        {
            if (edit.Table == table && edit.After != null && ulong.TryParse(edit.After["guid"], NumberStyles.None, CultureInfo.InvariantCulture, out ulong rowGuid) && rowGuid == guid)
            {
                return uint.TryParse(edit.After[column], NumberStyles.None, CultureInfo.InvariantCulture, out uint path) ? path : 0;
            }

            if (edit.Table == table && edit.After == null && edit.Before != null && ulong.TryParse(edit.Before["guid"], NumberStyles.None, CultureInfo.InvariantCulture, out ulong gone) && gone == guid)
            {
                return 0;
            }
        }

        return null;
    }
}
