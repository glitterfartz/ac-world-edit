using System.Globalization;
using Godot;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// Draft-staged paths: surfacing paths the database region read cannot return,
/// plus the snapshot-baseline guards that keep staged inserts movable.
/// </summary>
public partial class WaypointOverlay
{
    /// <summary>Staged-only paths currently surfaced in the lookup (no database record yet).</summary>
    readonly HashSet<uint> stagedPaths = [];

    /// <summary>True when the draft holds node rows for a path the database region read lacks (a path created this session).</summary>
    bool HasStagedRows(uint pathId)
    {
        if (shared?.Draft == null || effective == null || string.IsNullOrEmpty(sourceKey))
        {
            return false;
        }

        var schema = ContentDraftFiles.Sources.SchemaFor(sourceKey);
        var table = MovementEdits.NodeTable(schema);
        string id = schema.Dialect == WorldDialect.TrinityCore ? "PathId" : "id";
        return effective.All(table).Any(r => r.U(id) == pathId);
    }

    /// <summary>
    /// Surface draft-created paths the database region read cannot return: group the effective
    /// node rows by path and synthesize a lookup entry for ids with no record, so a brand-new
    /// path can be listed, drawn, selected and extended before anything is exported.
    /// </summary>
    void RefreshStagedPaths()
    {
        if (shared?.Draft == null || effective == null || string.IsNullOrEmpty(sourceKey))
        {
            return;
        }

        var schema = ContentDraftFiles.Sources.SchemaFor(sourceKey);
        var dialect = schema.Dialect;
        var nodeTable = MovementEdits.NodeTable(schema);
        string id = dialect == WorldDialect.TrinityCore ? "PathId" : "id";
        string point = dialect == WorldDialect.TrinityCore ? "NodeId" : "point";
        var headerTable = MovementEdits.HeaderTable(schema);
        ushort map = region?.Map ?? lookup.Values.FirstOrDefault()?.Map ?? 0;
        bool added = false;
        var staged = effective.All(nodeTable).GroupBy(r => r.U(id)).ToDictionary(g => g.Key, g => g.OrderBy(r => r.U(point)).ToArray());
        // Drop fully-deleted staged paths: their views and selection go with them.
        foreach (uint gone in stagedPaths.Where(s => !staged.ContainsKey(s)).ToArray())
        {
            stagedPaths.Remove(gone);
            lookup.Remove(gone);
            paths.Remove(gone);
            if (selected?.Path == gone)
            {
                Deselect();
            }

            added = true;
        }

        foreach (var (pid, ordered) in staged)
        {
            // Database paths keep their record; the effective rows already layer over it.
            if (lookup.ContainsKey(pid) && !stagedPaths.Contains(pid))
            {
                continue;
            }

            Row? header = headerTable != null ? effective.Get(headerTable, pid.ToString(CultureInfo.InvariantCulture)) : null;
            var nodes = ordered.Select(r => StagedNodeFromRow(dialect, r, header)).ToArray();
            if (nodes.Length == 0)
            {
                continue;
            }

            lookup[pid] = new WaypointPath(pid, map, nodes, StagedUsers(), dialect,
                header != null ? (int)header.U("Flags") : 0,
                header != null && !header.IsNull("Comment") ? header.T("Comment") : "");
            stagedPaths.Add(pid);
            if (!loaded.TryGetValue(sourceKey, out var set))
            {
                loaded[sourceKey] = set = [];
            }

            set.Add(pid);
            BuildPath(lookup[pid]);
            added = true;
        }

        if (added)
        {
            PopulateList();
        }
    }

    /// <summary>One region record from a draft-staged node row (no database record to fall back to).</summary>
    static WaypointNode StagedNodeFromRow(WorldDialect dialect, Row row, Row? header)
    {
        bool tc = dialect == WorldDialect.TrinityCore;
        return new(row.U(tc ? "NodeId" : "point"),
            row.F(tc ? "PositionX" : "position_x"), row.F(tc ? "PositionY" : "position_y"), row.F(tc ? "PositionZ" : "position_z"),
            row.IsNull(tc ? "Orientation" : "orientation") ? null : (double?)row.F(tc ? "Orientation" : "orientation"),
            tc ? (header != null && !header.IsNull("Velocity") ? header.F("Velocity") : 0) : row.F("velocity"),
            tc ? row.U("Delay") : row.U("delay"),
            tc ? false : row.I("smoothTransition") != 0,
            tc ? (header != null ? (int)header.U("MoveType") : 0) : row.I("move_type"),
            tc ? 0 : row.I("action"),
            tc ? 0 : row.I("action_chance"));
    }

    /// <summary>The creating creature follows its staged path; the database read cannot know that yet.</summary>
    WaypointUser[] StagedUsers() =>
        FocusedCreature() is ulong guid ? [new WaypointUser(guid, 0, "Selected creature", true)] : [];

    /// <summary>True when the draft already touches a row: its baseline is the net's Before (or absence for inserts).</summary>
    bool HasNetEdit(TableSchema table, string key)
    {
        if (shared?.Draft == null)
        {
            return false;
        }

        return shared.Draft.NetEdits().Any(e => e.Source == sourceKey && e.Edit.Table == table.Name && e.Edit.Key == key);
    }

    /// <summary>
    /// Drop snapshot baselines for staged inserts (net Before null): an insert has no database
    /// row, so any snapshot entry for its key is a stale seed that reads as a conflict and blocks
    /// later moves of the point. Heals drafts seeded before the Commit guard below existed.
    /// </summary>
    void PruneStagedBaselines()
    {
        if (shared?.Draft == null)
        {
            return;
        }

        var snapshot = Snapshot(sourceKey);
        var schema = ContentDraftFiles.Sources.SchemaFor(sourceKey);
        foreach (var (source, edit) in shared.Draft.NetEdits())
        {
            if (source != sourceKey || edit.Before != null || edit.After == null)
            {
                continue;
            }

            var table = schema.Tables.Find(edit.Table);
            if (snapshot.Get(table, edit.Key) != null)
            {
                snapshot.Remove(table, edit.Key);
            }
        }
    }
}
