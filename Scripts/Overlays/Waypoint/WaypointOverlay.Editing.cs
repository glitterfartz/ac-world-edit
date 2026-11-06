using Godot;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// Waypoint position edits as row-edit commands on the shared content draft (node rows, plus the
/// TrinityCore header for path properties). Nothing is written to the database. Node rows load on
/// demand per path and stay as the command baseline.
/// </summary>
public partial class WaypointOverlay : IPlacementEditor
{
    /// <summary>Default creature speeds in yards per second (speed_walk 1 and speed_run 1.14286 times 7/2.5).</summary>
    const float WalkSpeed = 2.5f, RunSpeed = 8;

    ContentDrafts? shared;
    readonly Dictionary<string, RowSet> snapshots = [];
    readonly Dictionary<string, HashSet<uint>> loaded = [];
    RowSet? effective;
    HashSet<(string Table, string Key)> conflicts = [];
    TransformGizmo gizmo = null!;
    /// <summary>Where editing feedback goes (the status bar); never the inspector.</summary>
    public Action<string>? Report { get; set; }

    void Say(string message) => Report?.Invoke(message);

    /// <summary>
    /// Viewer-supplied ground probe (terrain + WMO floors), shared with creature placement.
    /// </summary>
    public Func<Vector3, GroundHit>? GroundAt { get; set; }
    public bool SnapOnDrop { get; set; }
    public bool HasSelection => selected.HasValue;
    public bool CanEdit => EditableNode() != null;
    public bool CanUndo => shared?.Draft?.CanUndo == true;
    public bool CanRedo => shared?.Draft?.CanRedo == true;
    public bool IsDragging => gizmo.Dragging;
    public bool SupportsRotation => false;

    /// <summary>The net row edits this layer staged, for tests and diagnostics.</summary>
    public IReadOnlyList<(string Source, RowEdit Edit)> NetRows() => shared?.Draft?.NetEdits() ?? [];

    /// <summary>
    /// Waypoints keep their heading in a column this editor leaves alone, so only Select and Move apply.
    /// </summary>
    public GizmoMode Mode
    {
        set => gizmo.Mode = value == GizmoMode.Move ? GizmoMode.Move : GizmoMode.Select;
    }

    /// <summary>
    /// Share the content draft with the spawn editors; any editor's change redraws the paths.
    /// </summary>
    public void Attach(ContentDrafts drafts)
    {
        shared = drafts;
        drafts.Changed += _ =>
        {
            if (IsInsideTree())
            {
                RefreshEffective();
                RefreshStagedPaths();
                RebuildAll();
            }
        };
    }

    /// <summary>Attach the gizmo (the draft arrives through <see cref="Attach"/>).</summary>
    void SetupEditing()
    {
        gizmo = new TransformGizmo();
        AddChild(gizmo);
        gizmo.Changed += MoveSelection;
    }

    RowSet Snapshot(string source) => snapshots.TryGetValue(source, out var rows) ? rows : snapshots[source] = new RowSet();

    bool IsLoaded(string source, uint path) => loaded.TryGetValue(source, out var set) && set.Contains(path);

    /// <summary>Reload the effective rows and the stale-baseline conflicts for the current source.</summary>
    void RefreshEffective()
    {
        PruneStagedBaselines();
        effective = shared?.Draft?.Effective(sourceKey, Snapshot(sourceKey));
        conflicts = shared?.Draft == null ? [] : shared.Draft.Conflicts(sourceKey, Snapshot(sourceKey)).ToHashSet();
    }

    /// <summary>
    /// Read one path's node rows (plus the TrinityCore header) as the baseline of later edits.
    /// Selections and commands wait for this; the region read only carries the display records.
    /// </summary>
    async void EnsurePathLoaded(uint pathId)
    {
        if (shared?.Draft == null)
        {
            return;
        }

        string key = sourceKey;
        if (IsLoaded(key, pathId))
        {
            return;
        }

        if (HasStagedRows(pathId) && lookup.TryGetValue(pathId, out var staged))
        {
            // Created this session: the draft already holds every row, so there is no
            // database baseline to load for conflict checks.
            if (!loaded.TryGetValue(key, out var have))
            {
                loaded[key] = have = [];
            }

            have.Add(pathId);
            BuildPath(staged);
            ShowSelection();
            return;
        }

        try
        {
            var schema = ContentDraftFiles.Sources.SchemaFor(key);
            var rows = await DBSource.Open(Connection()).ReadWaypointPathRowsAsync(pathId, default);
            if (!IsInsideTree() || key != sourceKey || IsLoaded(key, pathId))
            {
                return;
            }

            var snapshot = Snapshot(key);
            foreach (var row in rows.All(MovementEdits.NodeTable(schema)))
            {
                snapshot.Set(MovementEdits.NodeTable(schema), row);
            }

            if (MovementEdits.HeaderTable(schema) is { } header)
            {
                foreach (var row in rows.All(header))
                {
                    snapshot.Set(header, row);
                }
            }

            if (!loaded.TryGetValue(key, out var set))
            {
                loaded[key] = set = [];
            }

            set.Add(pathId);
            RefreshEffective();
            if (lookup.TryGetValue(pathId, out var path))
            {
                BuildPath(path);
            }

            ShowSelection();
        }
        catch (Exception)
        {
            Say("Could not read the path's rows; point editing stays unavailable.");
        }
    }

    (WaypointPath Path, WaypointNode Node)? SelectedNode()
    {
        if (selected is not { } id || !lookup.TryGetValue(id.Path, out var path))
        {
            return null;
        }

        var node = DisplayNodes(path).FirstOrDefault(n => n.Point == id.Point);
        return node == null ? null : (path, node);
    }

    /// <summary>The effective node row, or null while its path's rows are still loading.</summary>
    Row? EffectiveRow(WaypointPath path, WaypointNode node)
    {
        if (effective == null)
        {
            return null;
        }

        var schema = ContentDraftFiles.Sources.SchemaFor(sourceKey);
        return effective.Get(MovementEdits.NodeTable(schema), DbWaypointRows.NodeKey(path.Dialect, path.Id, node.Point));
    }

    /// <summary>
    /// Gizmo previews move the dot and redraw the path; a completed drag commits X/Y/Z, keeping
    /// exact source values for untouched axes and optionally snapping to the ground.
    /// </summary>
    void MoveSelection(GizmoEdit edit)
    {
        if (SelectedNode() is not { } current || EditableNode() == null)
        {
            return;
        }

        var (path, node) = current;
        if (!edit.Commit)
        {
            handle.Position = edit.Position;
            BuildPath(path, (node.Point, edit.Position));
            return;
        }

        var position = edit.Position;
        string success = "Moved waypoint.";
        if (SnapOnDrop)
        {
            var ground = GroundAt?.Invoke(position) ?? new GroundHit(null, "Ground probe unavailable.");
            if (ground.Height is float height)
            {
                position.Y = height;
                success = "Moved waypoint and snapped to ground.";
            }
            else
            {
                success = "Moved waypoint; not snapped: " + ground.Message;
            }
        }

        var start = ViewPosition(PositionOf(path, node));
        var server = ViewSpace.ToServer(position);
        // View X = -server Y, view Y = server Z, view Z = -server X.
        Commit(path, node, s => s with
        {
            Position = new(
                position.Z == start.Z ? s.Position.X : server.X,
                position.X == start.X ? s.Position.Y : server.Y,
                position.Y == start.Y ? s.Position.Z : server.Z)
        }, "Move waypoint", success);
    }

    /// <summary>
    /// Apply one change to the point's effective state as one undoable row-edit command and redraw
    /// the path. A stale baseline, an invalid value or a failed save keeps the previous draft and
    /// says why. TrinityCore node commands touch only position, facing and delay; the path's move
    /// type and velocity belong to the header row, edited from the path card.
    /// </summary>
    void Commit(WaypointPath path, WaypointNode node, Func<WaypointState, WaypointState> change, string label, string success)
    {
        gizmo.Finish(false);
        if (shared == null)
        {
            Say("Editing is disabled.");
            return;
        }

        bool tc = path.Dialect == WorldDialect.TrinityCore;
        var schema = ContentDraftFiles.Sources.SchemaFor(sourceKey);
        var table = MovementEdits.NodeTable(schema);
        var before = EffectiveRow(path, node) ?? DbWaypointRows.RowFromNode(path.Dialect, path.Id, node);
        // Seed the baseline only for rows the draft never touched: a staged insert's baseline
        // is absence, and seeding it with the draft's own row reads as a conflict on the next edit.
        if (Snapshot(sourceKey).Get(table, table.KeyOf(before)) == null && !HasNetEdit(table, table.KeyOf(before)))
        {
            Snapshot(sourceKey).Set(table, before);
            RefreshEffective();
        }

        var afterState = change(StateOf(path, node)).ToColumn();
        var after = before
            .With(tc ? "PositionX" : "position_x", (float)afterState.Position.X)
            .With(tc ? "PositionY" : "position_y", (float)afterState.Position.Y)
            .With(tc ? "PositionZ" : "position_z", (float)afterState.Position.Z)
            .With(tc ? "Orientation" : "orientation", afterState.Props.Orientation is double o ? (float)o : null)
            .With(tc ? "Delay" : "delay", afterState.Props.Delay);
        if (!tc)
        {
            after = after
                .With("velocity", (float)afterState.Props.Velocity)
                .With("smoothTransition", afterState.Props.Smooth ? 1 : 0)
                .With("move_type", afterState.Props.MoveType)
                .With("action", afterState.Props.Action)
                .With("action_chance", afterState.Props.ActionChance);
        }

        if (after == before)
        {
            BuildPath(path);
            ShowSelection();
            return;
        }

        try
        {
            var (ok, message) = shared.Commit(this, sourceKey, Snapshot(sourceKey), effective ?? Snapshot(sourceKey),
                label, [InteractionEdits.Update(table, before, after)],
                rows => SpawnRules.CheckMovement(schema, rows));
            Say(ok ? success : message);
        }
        catch (Exception)
        {
            Say("Could not save the waypoint draft; previous draft preserved.");
        }

        BuildPath(path);
        ShowSelection();
    }

    public bool BeginDrag(Camera3D camera, Vector2 screen) => CanEdit && gizmo.Begin(camera, screen);
    public void Drag(Camera3D camera, Vector2 screen) => gizmo.Drag(camera, screen);
    public void EndDrag(bool commit) => gizmo.Finish(commit);


    public void DropToGround()
    {
        if (SelectedNode() is not { } current)
        {
            return;
        }

        var (path, node) = current;
        var position = PositionOf(path, node);
        var ground = GroundAt?.Invoke(ViewPosition(position)) ?? new GroundHit(null, "Ground probe unavailable.");
        if (ground.Height is not float height)
        {
            Say("Not dropped: " + ground.Message);
            return;
        }

        Commit(path, node, s => s with { Position = position with { Z = height } }, "Drop waypoint to ground", "Dropped waypoint to ground.");
    }

    public void ResetToSource()
    {
        if (SelectedNode() is { } current)
        {
            Commit(current.Path, current.Node, _ => SourceState(current.Node), "Reset waypoint to source", "Reset to source waypoint.");
        }
    }

    /// <summary>
    /// Change one property of the selected point. Typed text shows rounded numbers, so a value that
    /// matches the shown one keeps the exact stored number.
    /// </summary>
    void SetProps(Func<WaypointProps, WaypointProps> change, string label, string success)
    {
        if (SelectedNode() is { } current)
        {
            Commit(current.Path, current.Node, s => s with { Props = change(s.Props) }, label, success);
        }
    }

    public void SetDelay(uint milliseconds) => SetProps(p => p with { Delay = milliseconds }, "Waypoint delay", "Saved delay.");

    public void SetMoveType(int moveType) => SetProps(p => p with { MoveType = moveType }, "Waypoint move type", "Saved move type.");

    public void SetSmooth(bool smooth) => SetProps(p => p with { Smooth = smooth }, "Waypoint smooth transition", "Saved smooth transition.");

    public void SetAction(int action) => SetProps(p => p with { Action = action }, "Waypoint action", "Saved action.");

    public void SetActionChance(int chance) => SetProps(p => p with { ActionChance = chance }, "Waypoint action chance", "Saved action chance.");

    public void SetVelocity(double velocity) => SetProps(p => p with { Velocity = Math.Abs(velocity - p.Velocity) < 0.5e-4 ? p.Velocity : velocity }, "Waypoint velocity", "Saved velocity.");

    /// <summary>
    /// Set the facing in radians, or clear it (NULL) with null. The server only applies it to points
    /// with a delay.
    /// </summary>
    public void SetFacing(double? radians) => SetProps(p => p with { Orientation = radians is double r ? (p.Orientation is double old && Math.Abs(WorldCoordinates.NormalizeHeading(r) - old) < 0.5e-4 ? p.Orientation : WorldCoordinates.NormalizeHeading(r)) : null }, "Waypoint facing", radians.HasValue ? "Saved facing." : "Cleared facing.");

    public void SetPosition(double x, double y, double z)
    {
        if (SelectedNode() is { } current)
        {
            // Fields show rounded text; matching values keep the exact stored numbers.
            Commit(current.Path, current.Node, s => s with
            {
                Position = new(
                    Math.Abs(x - s.Position.X) < 0.5e-4 ? s.Position.X : x,
                    Math.Abs(y - s.Position.Y) < 0.5e-4 ? s.Position.Y : y,
                    Math.Abs(z - s.Position.Z) < 0.5e-4 ? s.Position.Z : z)
            }, "Move waypoint", "Saved typed position.");
        }
    }

    void History(bool redo)
    {
        gizmo.Finish(false);
        if (shared == null)
        {
            Say("Editing is disabled.");
            return;
        }

        Say(shared.History(this, redo));
        RebuildAll();
    }

    public void Undo() => History(false);
    public void Redo() => History(true);

    /// <summary>
    /// Discard every waypoint row change as normal undoable commands (the pending-changes window
    /// discards them one by one through the shared draft).
    /// </summary>
    public void ClearDraft()
    {
        gizmo.Finish(false);
        if (shared?.Draft == null)
        {
            Say("Editing is disabled.");
            return;
        }

        int count = 0;
        foreach (var (source, edit) in shared.Draft.NetEdits())
        {
            if (edit.Table is "waypoint_data" or "waypoint_path" or "waypoint_path_node" && shared.Revert(source, edit))
            {
                count++;
            }
        }

        Say(count == 0 ? "No waypoint changes to discard." : $"Discarded {count} waypoint change(s).");
        RebuildAll();
    }

    void RebuildAll()
    {
        RefreshEffective();
        RefreshStagedPaths();
        foreach (var path in lookup.Values)
        {
            BuildPath(path);
        }

        ShowSelection();
    }

    /// <summary>
    /// GM teleport for checking the effective position in the client: `.go xyz X Y Z MAP`.
    /// </summary>
    public string? GoCommand() => SelectedNode() is { } current
        ? PoseText.GoCommand(current.Path.Map, PositionOf(current.Path, current.Node).X, PositionOf(current.Path, current.Node).Y, PositionOf(current.Path, current.Node).Z)
        : null;

    public void CopyGoCommand()
    {
        if (GoCommand() is { } command)
        {
            DisplayServer.ClipboardSet(command);
            Say("Copied " + command);
        }
    }

    public void FlyToSelection()
    {
        if (SelectedNode() is { } current)
        {
            var p = PositionOf(current.Path, current.Node);
            FocusRequested?.Invoke(new WorldPose(current.Path.Map, p.X, p.Y, p.Z, 0));
        }
        else if (emphasized.HasValue && lookup.TryGetValue(emphasized.Value, out var path) && path.Nodes.Length > 0)
        {
            var p = PositionOf(path, path.Nodes[0]);
            FocusRequested?.Invoke(new WorldPose(path.Map, p.X, p.Y, p.Z, 0));
        }
    }
}
