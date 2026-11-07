using System.Globalization;
using Godot;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// Draft-aware path row counts: the database rows overlaid with staged net edits,
/// so new, renumbered and deleted points read correctly before anything is exported.
/// </summary>
public partial class CreatureOverlay
{
    /// <summary>Node count with the draft applied (renumbers are delete+insert pairs).</summary>
    int DraftNodeTotal(string key, IWorldSchema schema, uint pathId, int dbCount)
    {
        if (sharedDrafts?.Draft == null)
        {
            return dbCount;
        }

        string table = MovementEdits.NodeTable(schema).Name;
        string id = schema.Dialect == WorldDialect.TrinityCore ? "PathId" : "id";
        int adds = 0, drops = 0;
        bool touches = false;
        foreach (var (source, edit) in sharedDrafts.Draft.NetEdits())
        {
            if (source != key || edit.Table != table)
            {
                continue;
            }

            bool before = edit.Before != null && edit.Before.U(id) == pathId;
            bool after = edit.After != null && edit.After.U(id) == pathId;
            if (!before && !after)
            {
                continue;
            }

            touches = true;
            if (edit.After == null)
            {
                drops++;
            }
            else if (edit.Before == null)
            {
                adds++;
            }
        }

        return touches ? dbCount + adds - drops : dbCount;
    }

    /// <summary>
    /// A path's rows with the draft applied: the database rows overlaid with the net After-rows
    /// for renumbered or staged-insert points (keyed by point, net wins). Deleting exactly these
    /// leaves nothing behind, whether the points were exported or are still staged.
    /// </summary>
    IReadOnlyList<Row> DraftPathRows(string key, IWorldSchema schema, uint pathId, IEnumerable<Row> dbRows)
    {
        var nodeTable = MovementEdits.NodeTable(schema);
        string id = schema.Dialect == WorldDialect.TrinityCore ? "PathId" : "id";
        string point = schema.Dialect == WorldDialect.TrinityCore ? "NodeId" : "point";
        var byPoint = dbRows.ToDictionary(r => r.U(point));
        if (sharedDrafts?.Draft != null)
        {
            foreach (var (source, edit) in sharedDrafts.Draft.NetEdits())
            {
                if (source == key && edit.Table == nodeTable.Name && edit.After != null && edit.After.U(id) == pathId)
                {
                    byPoint[edit.After.U(point)] = edit.After;
                }
            }
        }

        return byPoint.Values.ToArray();
    }

    /// <summary>The path header with the draft applied: the net After-row wins over the database row.</summary>
    Row? DraftHeaderRow(string key, TableSchema headerTable, uint pathId, Row? dbHeader)
    {
        if (sharedDrafts?.Draft != null)
        {
            foreach (var (source, edit) in sharedDrafts.Draft.NetEdits())
            {
                if (source == key && edit.Table == headerTable.Name && edit.After != null && edit.After.U("PathId") == pathId)
                {
                    return edit.After;
                }
            }
        }

        return dbHeader;
    }

}
