using System.Globalization;
using Godot;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// Node add/insert/delete and TrinityCore path-header edits as shared-draft row commands.
/// </summary>
public partial class WaypointOverlay
{
    // ---- add / insert / delete nodes -------------------------------------------

    /// <summary>Armed by the "Add points" toggle: viewport clicks append to the emphasized path (Alt inserts).</summary>
    public bool AddingPoints { get; set; }

    /// <summary>
    /// Handle a viewport click while adding points. A click on an existing point still selects it;
    /// otherwise the ground under the cursor appends a node, or Alt-click inserts after the nearest
    /// segment. Returns true when the click was used.
    /// </summary>
    public bool AddPointAt(Camera3D camera, Vector2 screen, bool insert)
    {
        if (!AddingPoints || !LayerEnabled || IsDragging)
        {
            return false;
        }

        if (!emphasized.HasValue || !lookup.TryGetValue(emphasized.Value, out var path))
        {
            // A path created this session lives only in the draft until exported.
            RefreshStagedPaths();
            if (!emphasized.HasValue || !lookup.TryGetValue(emphasized.Value, out path))
            {
                Say("Select a path first; Add points extends the shown path.");
                return true;
            }
        }

        var display = DisplayNodes(path);
        if (display.Length == 0)
        {
            Say("The path has no points to extend; add its first point from a creature's Path card.");
            return true;
        }

        if (FindHit(camera, screen).HasValue)
        {
            return false;
        }

        if (shared?.Draft == null)
        {
            Say("Editing is disabled.");
            return true;
        }

        string key = sourceKey;
        if (!IsLoaded(key, path.Id))
        {
            EnsurePathLoaded(path.Id);
            Say("The path's rows are still loading; click again.");
            return true;
        }

        var rows = EffectiveNodeRows(path);
        if (rows.Count == 0)
        {
            Say("The path's rows are still loading; click again.");
            return true;
        }

        uint afterPoint = display.Max(n => n.Point);
        if (!paths.TryView(path.Id, out var view) || view == null || view.Positions.Length == 0)
        {
            Say("The path is not drawn yet; click again.");
            return true;
        }

        Vector3 drop = GroundView(camera, screen, view.Positions);
        if (insert && view.Positions.Length == display.Length)
        {
            afterPoint = NearestSegmentPoint(view.Positions, display, drop);
        }

        var server = ViewSpace.ToServer(drop);
        var neighbor = rows.FirstOrDefault(r => PointOf(path, r) == afterPoint) ?? rows[^1];
        var seed = SeedFromNeighbor(path, neighbor, server);
        try
        {
            var schema = ContentDraftFiles.Sources.SchemaFor(key);
            var edits = MovementEdits.InsertNode(schema, path.Id, rows, afterPoint, seed);
            var (ok, message) = shared.Commit(this, key, Snapshot(key), effective ?? Snapshot(key),
                insert ? $"Insert waypoint path {path.Id} point {afterPoint + 1}" : $"Append waypoint path {path.Id} point {afterPoint + 1}", edits,
                checkRows => SpawnRules.CheckMovement(schema, checkRows));
            Say(ok ? (insert ? $"Inserted point {afterPoint + 1}." : $"Appended point {afterPoint + 1}.") : message);
            if (ok)
            {
                Select(path.Id, afterPoint + 1);
            }
        }
        catch (InvalidDataException error)
        {
            Say("Not saved: " + error.Message);
        }

        return true;
    }

    /// <summary>The ground under the cursor, or the path-height plane when the probe finds nothing.</summary>
    Vector3 GroundView(Camera3D camera, Vector2 screen, Vector3[] positions)
    {
        if (positions.Length == 0)
        {
            return Vector3.Zero;
        }

        var ray = PickRay.From(camera, screen);
        float planeY = positions.Average(v => v.Y);
        if (Math.Abs(ray.Direction.Y) > 1e-6)
        {
            float t = (planeY - ray.Origin.Y) / ray.Direction.Y;
            if (t > 0 && t < camera.Far)
            {
                var view = ray.Origin + ray.Direction * t;
                if (GroundAt?.Invoke(view) is { Height: float height })
                {
                    return new(view.X, height, view.Z);
                }

                return view;
            }
        }

        return positions[^1];
    }

    static uint PointOf(WaypointPath path, Row row) => row.U(path.Dialect == WorldDialect.TrinityCore ? "NodeId" : "point");

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
        foreach (var group in effective.All(nodeTable).GroupBy(r => r.U(id)).ToArray())
        {
            if (lookup.ContainsKey(group.Key))
            {
                continue;
            }

            var ordered = group.OrderBy(r => r.U(point)).ToArray();
            Row? header = headerTable != null ? effective.Get(headerTable, group.Key.ToString(CultureInfo.InvariantCulture)) : null;
            var nodes = ordered.Select(r => StagedNodeFromRow(dialect, r, header)).ToArray();
            if (nodes.Length == 0)
            {
                continue;
            }

            lookup[group.Key] = new WaypointPath(group.Key, map, nodes, StagedUsers(group.Key), dialect,
                header != null ? (int)header.U("Flags") : 0,
                header != null && !header.IsNull("Comment") ? header.T("Comment") : "");
            if (!loaded.TryGetValue(sourceKey, out var set))
            {
                loaded[sourceKey] = set = [];
            }

            set.Add(group.Key);
            BuildPath(lookup[group.Key]);
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
    WaypointUser[] StagedUsers(uint pathId) =>
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

    /// <summary>The node point after which an Alt-click inserts: the start of the nearest segment.</summary>
    static uint NearestSegmentPoint(Vector3[] positions, WaypointNode[] nodes, Vector3 drop)
    {
        int best = 0;
        float bestDistance = float.MaxValue;
        for (int i = 0; i < positions.Length; i++)
        {
            int j = (i + 1) % positions.Length;
            var segment = positions[j] - positions[i];
            float lengthSquared = segment.LengthSquared();
            float along = lengthSquared > 0 ? (drop - positions[i]).Dot(segment) / lengthSquared : 0;
            float distance = (positions[i] + segment * Mathf.Clamp(along, 0, 1) - drop).LengthSquared();
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = i;
            }
        }

        // The closing segment (last back to first) appends instead of inserting mid-path.
        return best == positions.Length - 1 ? (uint)nodes.Max(n => n.Point) : nodes[best].Point;
    }

    /// <summary>
    /// The nodes as drawn: the effective rows when the path is loaded (so inserted points show
    /// before the next database read), else the region records. Renumbered points come out in order.
    /// </summary>
    WaypointNode[] DisplayNodes(WaypointPath path)
    {
        if (!IsLoaded(sourceKey, path.Id))
        {
            return path.Nodes;
        }

        var rows = EffectiveNodeRows(path);
        if (rows.Count == 0)
        {
            return path.Nodes;
        }

        return rows.Select(r => NodeFromRow(path, r)).ToArray();
    }

    /// <summary>One region record from an effective node row. Path-level TrinityCore columns come
    /// from the header when the draft changed them, else from the record.</summary>
    WaypointNode NodeFromRow(WaypointPath path, Row row)
    {
        bool tc = path.Dialect == WorldDialect.TrinityCore;
        string point = tc ? "NodeId" : "point";
        var first = path.Nodes.FirstOrDefault();
        var schema = ContentDraftFiles.Sources.SchemaFor(sourceKey);
        Row? header = MovementEdits.HeaderTable(schema) is { } headerTable
            ? effective?.Get(headerTable, path.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)) : null;
        return new(
            row.U(point),
            row.F(tc ? "PositionX" : "position_x"), row.F(tc ? "PositionY" : "position_y"), row.F(tc ? "PositionZ" : "position_z"),
            row.IsNull(tc ? "Orientation" : "orientation") ? null : (double?)row.F(tc ? "Orientation" : "orientation"),
            tc ? (header != null && !header.IsNull("Velocity") ? header.F("Velocity") : first?.Velocity ?? 0) : row.F("velocity"),
            tc ? row.U("Delay") : row.U("delay"),
            tc ? false : row.I("smoothTransition") != 0,
            tc ? (header != null ? (int)header.U("MoveType") : first?.MoveType ?? 0) : row.I("move_type"),
            tc ? 0 : row.I("action"),
            tc ? 0 : row.I("action_chance"));
    }

    /// <summary>Effective node rows of one path in point order (loaded paths only).</summary>
    List<Row> EffectiveNodeRows(WaypointPath path)
    {
        var schema = ContentDraftFiles.Sources.SchemaFor(sourceKey);
        var table = MovementEdits.NodeTable(schema);
        string id = path.Dialect == WorldDialect.TrinityCore ? "PathId" : "id";
        string point = path.Dialect == WorldDialect.TrinityCore ? "NodeId" : "point";
        return (effective?.All(table) ?? []).Where(r => r.U(id) == path.Id).OrderBy(r => r.U(point)).ToList();
    }

    /// <summary>A seed at server coordinates, copying the neighbor's per-node travel columns on AzerothCore.</summary>
    static WaypointSeed SeedFromNeighbor(WaypointPath path, Row neighbor, Foundation.Point3 server)
    {
        bool tc = path.Dialect == WorldDialect.TrinityCore;
        return new(server.X, server.Y, server.Z, null, 0,
            tc ? 0 : neighbor.F("velocity"),
            tc ? 0 : neighbor.I("move_type"),
            !tc && neighbor.I("smoothTransition") != 0,
            tc ? 0 : neighbor.I("action"),
            tc ? 100 : neighbor.I("action_chance"));
    }

    /// <summary>Delete the selected point and select its follower (renumbered down); the last point stays.</summary>
    public void DeleteSelectedPoint()
    {
        if (SelectedNode() is not { } current || shared == null)
        {
            return;
        }

        var (path, node) = current;
        var rows = EffectiveNodeRows(path);
        if (rows.Count < 2)
        {
            Say("A path needs at least one point; delete the path instead.");
            return;
        }

        try
        {
            var schema = ContentDraftFiles.Sources.SchemaFor(sourceKey);
            var edits = MovementEdits.DeleteNode(schema, path.Id, rows, node.Point);
            var (ok, message) = shared.Commit(this, sourceKey, Snapshot(sourceKey), effective ?? Snapshot(sourceKey),
                $"Delete waypoint path {path.Id} point {node.Point}", edits,
                checkRows => SpawnRules.CheckMovement(schema, checkRows));
            Say(ok ? $"Deleted point {node.Point}." : message);
            if (ok)
            {
                Select(path.Id, Math.Min(node.Point, (uint)rows.Count - 1));
            }
        }
        catch (InvalidDataException error)
        {
            Say("Not saved: " + error.Message);
        }
    }

    /// <summary>
    /// Edit the TrinityCore path header (move type, velocity, flags, comment) as one undoable
    /// command. An empty velocity means each creature's own speed; the card warns how many
    /// creatures share the path, since they all change together.
    /// </summary>
    public void SetHeader(int moveType, double? velocity, uint flags, string comment)
    {
        if (!emphasized.HasValue || !lookup.TryGetValue(emphasized.Value, out var path) || path.Dialect != WorldDialect.TrinityCore || shared == null)
        {
            return;
        }

        string key = sourceKey;
        if (!IsLoaded(key, path.Id))
        {
            EnsurePathLoaded(path.Id);
            Say("The path's rows are still loading; try again.");
            return;
        }

        try
        {
            var schema = ContentDraftFiles.Sources.SchemaFor(key);
            var table = MovementEdits.HeaderTable(schema)!;
            var existing = effective?.Get(table, path.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
            var after = (existing ?? MovementEdits.HeaderRow(schema, path.Id, 0, 0, null, "")).With("MoveType", moveType)
                .With("Velocity", velocity is double v ? (float)v : null)
                .With("Flags", flags).With("Comment", comment);
            if (after == existing)
            {
                return;
            }

            RowEdit edit = existing == null ? InteractionEdits.Insert(table, after) : InteractionEdits.Update(table, existing, after);
            var (ok, message) = shared.Commit(this, key, Snapshot(key), effective ?? Snapshot(key),
                $"Edit path {path.Id} properties", [edit],
                checkRows => SpawnRules.CheckMovement(schema, checkRows));
            Say(ok ? "Saved path properties." : message);
            ShowSelection();
        }
        catch (InvalidDataException error)
        {
            Say("Not saved: " + error.Message);
        }
    }
}
