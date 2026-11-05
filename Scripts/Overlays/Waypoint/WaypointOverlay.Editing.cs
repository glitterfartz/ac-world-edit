using Godot;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// Waypoint position edits and the walk preview. Every command records one local WaypointDraft
/// edit; nothing is written to the database.
/// </summary>
public partial class WaypointOverlay : IPlacementEditor
{
    /// <summary>Default creature speeds in yards per second (speed_walk 1 and speed_run 1.14286 times 7/2.5).</summary>
    const float WalkSpeed = 2.5f, RunSpeed = 8;

    WaypointDraft? draft;
    string draftPath = "";
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
    public bool CanUndo => draft?.CanUndo == true;
    public bool CanRedo => draft?.CanRedo == true;
    public bool IsDragging => gizmo.Dragging;
    public bool SupportsRotation => false;
    public IReadOnlyList<WaypointEdit> NetEdits() => draft?.NetEdits() ?? [];

    /// <summary>
    /// Waypoints keep their heading in a column this editor leaves alone, so only Select and Move apply.
    /// </summary>
    public GizmoMode Mode
    {
        set => gizmo.Mode = value == GizmoMode.Move ? GizmoMode.Move : GizmoMode.Select;
    }

    /// <summary>
    /// Open the durable draft (isolated under artifacts/ for smoke runs) and attach the gizmo.
    /// Recovery failures keep the file and disable editing.
    /// </summary>
    void OpenDraft()
    {
        ReopenDraft();
        gizmo = new TransformGizmo();
        AddChild(gizmo);
        gizmo.Changed += MoveSelection;
    }

    /// <summary>Open the draft file of the current client profile, releasing the previous one.</summary>
    public void ReopenDraft()
    {
        draft?.Dispose();
        draft = null;
        draftPath = ContentDraftFiles.LocalPath("waypoints.json");
        try
        {
            draft = new WaypointDraft(draftPath);
        }
        catch (Exception)
        {
            Say("Cannot open waypoint draft. Editing disabled; existing file preserved.");
        }
    }

    (WaypointPath Path, WaypointNode Node)? SelectedNode()
    {
        if (selected is not { } id || !lookup.TryGetValue(id.Path, out var path))
        {
            return null;
        }

        var node = path.Nodes.FirstOrDefault(n => n.Point == id.Point);
        return node == null ? null : (path, node);
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
        }, success);
    }

    /// <summary>
    /// Apply one change to the point's effective state, record it and redraw the path. A stale
    /// baseline, an invalid value or a failed save keeps the previous draft and says why.
    /// </summary>
    void Commit(WaypointPath path, WaypointNode node, Func<WaypointState, WaypointState> change, string success)
    {
        gizmo.Finish(false);
        var before = StateOf(path, node);
        var after = change(before).ToColumn();
        if (!before.Props.EditableOn(path.Dialect, after.Props))
        {
            Say("Not saved: TrinityCore stores only position, facing and delay on a waypoint.");
            after = before;
        }

        if (draft != null && after != before)
        {
            try
            {
                draft.Edit(sourceKey, path.Id, node.Point, SourceState(node), after);
                Say(success);
            }
            catch (InvalidDataException error)
            {
                Say("Not saved: " + error.Message);
            }
            catch (Exception)
            {
                Say("Could not save waypoint draft. Previous draft preserved.");
            }
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

        Commit(path, node, s => s with { Position = position with { Z = height } }, "Dropped waypoint to ground.");
    }

    public void ResetToSource()
    {
        if (SelectedNode() is { } current)
        {
            Commit(current.Path, current.Node, _ => SourceState(current.Node), "Reset to source waypoint.");
        }
    }

    /// <summary>
    /// Change one property of the selected point. Typed text shows rounded numbers, so a value that
    /// matches the shown one keeps the exact stored number.
    /// </summary>
    void SetProps(Func<WaypointProps, WaypointProps> change, string success)
    {
        if (SelectedNode() is { } current)
        {
            Commit(current.Path, current.Node, s => s with { Props = change(s.Props) }, success);
        }
    }

    public void SetDelay(uint milliseconds) => SetProps(p => p with { Delay = milliseconds }, "Saved delay.");

    public void SetMoveType(int moveType) => SetProps(p => p with { MoveType = moveType }, "Saved move type.");

    public void SetSmooth(bool smooth) => SetProps(p => p with { Smooth = smooth }, "Saved smooth transition.");

    public void SetAction(int action) => SetProps(p => p with { Action = action }, "Saved action.");

    public void SetActionChance(int chance) => SetProps(p => p with { ActionChance = chance }, "Saved action chance.");

    public void SetVelocity(double velocity) => SetProps(p => p with { Velocity = Math.Abs(velocity - p.Velocity) < 0.5e-4 ? p.Velocity : velocity }, "Saved velocity.");

    /// <summary>
    /// Set the facing in radians, or clear it (NULL) with null. The server only applies it to points
    /// with a delay.
    /// </summary>
    public void SetFacing(double? radians) => SetProps(p => p with { Orientation = radians is double r ? (p.Orientation is double old && Math.Abs(WorldCoordinates.NormalizeHeading(r) - old) < 0.5e-4 ? old : WorldCoordinates.NormalizeHeading(r)) : null }, radians.HasValue ? "Saved facing." : "Cleared facing.");

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
            }, "Saved typed position.");
        }
    }

    void History(bool redo)
    {
        gizmo.Finish(false);
        try
        {
            if (redo)
            {
                draft?.Redo();
            }
            else
            {
                draft?.Undo();
            }

            Say(redo ? "Redid last waypoint change." : "Undid last waypoint change.");
        }
        catch (Exception)
        {
            Say("Could not save waypoint draft history. Previous draft preserved.");
        }

        RebuildAll();
    }

    public void Undo() => History(false);
    public void Redo() => History(true);

    /// <summary>
    /// Discard one waypoint's net move (Pending Changes window) as an undoable command.
    /// </summary>
    public void Revert(WaypointEdit net)
    {
        gizmo.Finish(false);
        try
        {
            draft?.Revert(net);
            Say("Discarded the waypoint move.");
        }
        catch (Exception)
        {
            Say("Could not save waypoint draft history. Previous draft preserved.");
        }

        RebuildAll();
    }

    /// <summary>
    /// Drop all saved waypoint moves and their undo history.
    /// </summary>
    public void ClearDraft()
    {
        gizmo.Finish(false);
        try
        {
            draft?.Clear();
            Say("Cleared the draft history.");
        }
        catch (Exception)
        {
            Say("Could not clear the draft history.");
        }

        RebuildAll();
    }

    void RebuildAll()
    {
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

    // ---- walk preview ----------------------------------------------------------------------

    bool previewing;
    int segment;
    float traveled;
    double waitLeft;
    ulong lastTicks;

    public bool Previewing => previewing;

    /// <summary>
    /// A path can be previewed when the emphasized path has at least two points.
    /// </summary>
    public bool CanPreview => emphasized.HasValue && lookup.TryGetValue(emphasized.Value, out var path) && path.Nodes.Length > 1;

    /// <summary>
    /// Start or stop a marker that walks the emphasized path the way the server would: leg by leg at
    /// the destination point's walk or run speed, pausing for its delay, and back to the first point
    /// after the last. Default creature speeds; terrain is ignored, as it is on the server.
    /// </summary>
    public void SetPreview(bool on)
    {
        previewing = on && CanPreview;
        segment = 0;
        traveled = 0;
        waitLeft = 0;
        lastTicks = Time.GetTicksMsec();
        walker.Visible = false;
    }

    void StopPreview() => SetPreview(false);

    void UpdatePreview()
    {
        if (!previewing)
        {
            walker.Visible = false;
            return;
        }

        if (!CanPreview || !views.TryGetValue(emphasized!.Value, out var view) || !lookup.TryGetValue(emphasized.Value, out var path) || view.Positions.Length != path.Nodes.Length)
        {
            StopPreview();
            return;
        }

        ulong now = Time.GetTicksMsec();
        double dt = Math.Min((now - lastTicks) / 1000.0, 0.25);
        lastTicks = now;
        int count = path.Nodes.Length;
        segment %= count;
        int to = (segment + 1) % count;
        walker.Visible = LayerEnabled;
        if (waitLeft > 0)
        {
            waitLeft -= dt;
            walker.Position = view.Positions[segment];
            return;
        }

        float length = view.Positions[segment].DistanceTo(view.Positions[to]);
        var props = StateOf(path, path.Nodes[to]).Props;
        traveled += (props.Velocity > 0 ? (float)props.Velocity : props.MoveType == 1 ? RunSpeed : WalkSpeed) * (float)dt;
        if (traveled >= length)
        {
            segment = to;
            traveled = 0;
            waitLeft = props.Delay / 1000.0;
            walker.Position = view.Positions[to];
            return;
        }

        walker.Position = view.Positions[segment].Lerp(view.Positions[to], length > 0 ? traveled / length : 1);
    }
}
