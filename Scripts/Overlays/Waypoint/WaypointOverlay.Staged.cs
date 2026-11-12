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
            DropPath(gone);
            added = true;
        }

        // A database path whose nodes the draft removes is gone too: leaving the stale region
        // record in the lookup would keep drawing and selecting it until the next region query.
        foreach (uint id2 in lookup.Keys.ToArray())
        {
            if (stagedPaths.Contains(id2) || !IsLoaded(sourceKey, id2))
            {
                continue;
            }

            if (EffectiveNodeRows(lookup[id2]).Count == 0 && HasNodeDeletion(id2))
            {
                DropPath(id2);
                added = true;
            }
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

            lookup[pid] = new WaypointPath(pid, map, nodes, StagedUsers(pid), dialect,
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

    /// <summary>Remove a path's record, view and selection (its rows are gone from the draft).</summary>
    void DropPath(uint pathId)
    {
        if (selected?.Path == pathId)
        {
            selected = null;
            if (handle != null)
            {
                handle.Visible = false;
            }

            list?.DeselectAll();
        }

        if (emphasized == pathId)
        {
            emphasized = null;
        }

        if (focusedPath == pathId)
        {
            focusedPath = null;
        }

        if (loaded.TryGetValue(sourceKey, out var set))
        {
            set.Remove(pathId);
        }

        lookup.Remove(pathId);
        stagedPaths.Remove(pathId);
        paths.Remove(pathId);
    }

    /// <summary>True when the draft removes at least one node row of this path.</summary>
    bool HasNodeDeletion(uint pathId)
    {
        if (shared?.Draft == null || string.IsNullOrEmpty(sourceKey))
        {
            return false;
        }

        var schema = ContentDraftFiles.Sources.SchemaFor(sourceKey);
        var table = MovementEdits.NodeTable(schema);
        string id = schema.Dialect == WorldDialect.TrinityCore ? "PathId" : "id";
        return shared.Draft.NetFor(sourceKey).Any(e =>
            e.Edit.Table == table.Name && e.Edit.After == null && e.Edit.Before != null && e.Edit.Before.U(id) == pathId);
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

    /// <summary>
    /// The creatures whose effective addon rows name this path. A staged path has no database
    /// record, so this is derived from the draft instead of claiming whichever creature is focused.
    /// </summary>
    WaypointUser[] StagedUsers(uint pathId)
    {
        if (effective == null || string.IsNullOrEmpty(sourceKey))
        {
            return [];
        }

        var schema = ContentDraftFiles.Sources.SchemaFor(sourceKey);
        var addon = schema.Tables.Find("creature_addon");
        string column = MovementEdits.AddonPathColumn(schema.Dialect);
        ulong? focused = FocusedCreature();
        return effective.All(addon)
            .Where(r => r.U(column) == pathId)
            .Select(r => r.UL("guid"))
            .Distinct()
            .Select(guid => new WaypointUser(guid, 0, guid == focused ? "Selected creature" : $"Creature #{guid}", true))
            .ToArray();
    }

    /// <summary>True when the draft already touches a row: its baseline is the net's Before (or absence for inserts).</summary>
    bool HasNetEdit(TableSchema table, string key) => shared?.Draft?.Touches(sourceKey, table.Name, key) == true;

    /// <summary>
    /// Drop a snapshot baseline this editor seeded from a display record for a key the draft now
    /// inserts. A row the database read loaded for the same key is never in the seeded set, so a
    /// genuine row that appeared later stays and reports its conflict instead of being hidden.
    /// </summary>
    void PruneStagedBaselines()
    {
        if (shared?.Draft == null || seededBaselines.Count == 0)
        {
            return;
        }

        var snapshot = Snapshot(sourceKey);
        var schema = ContentDraftFiles.Sources.SchemaFor(sourceKey);
        foreach (var (_, edit) in shared.Draft.NetFor(sourceKey))
        {
            if (edit.Before != null || edit.After == null || !seededBaselines.Contains((edit.Table, edit.Key)))
            {
                continue;
            }

            snapshot.Remove(schema.Tables.Find(edit.Table), edit.Key);
            seededBaselines.Remove((edit.Table, edit.Key));
        }
    }
}
