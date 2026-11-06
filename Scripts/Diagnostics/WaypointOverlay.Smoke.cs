using Godot;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// Hooks for --waypoint-smoke: observe the query and drive the edit workflow without UI.
/// </summary>
public partial class WaypointOverlay
{
    public bool SmokeQueryIdle => query.IsIdle;
    public (uint Path, uint Point)? SmokeSelected => selected;
    public int SmokeVisiblePaths => paths.Views.Values.Count(v => v.Root.Visible);

    /// <summary>
    /// The point of a followed path (three or more points) nearest the camera.
    /// </summary>
    public (WaypointPath Path, WaypointNode Node)? SmokeNodeNear(Camera3D camera)
    {
        (WaypointPath, WaypointNode)? best = null;
        float bestDistance = float.MaxValue;
        foreach (var path in lookup.Values.Where(p => p.Active && DisplayNodes(p).Length >= 3))
        {
            foreach (var node in DisplayNodes(path))
            {
                var view = ViewPosition(PositionOf(path, node));
                float distance = view.DistanceSquaredTo(camera.GlobalPosition);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = (path, node);
                }
            }
        }

        return best;
    }

    /// <summary>
    /// Which property controls the card shows and lets the user change for the selected point.
    /// </summary>
    public (bool SmoothShown, bool ActionShown, bool VelocityEditable, bool MoveTypeEditable, bool DelayEditable, bool FacingEditable) SmokeFields =>
        (smooth.Visible,action.GetParent<Control>().Visible, velocity.Editable, !moveType.Disabled, delay.Editable, facing.Editable);

    /// <summary>Scroll the inspector to the bottom so a screenshot shows the property card.</summary>
    public void SmokeScrollToEnd() => panelScroll.ScrollVertical = int.MaxValue;

    public Vector3? SmokeDotPosition(uint path, uint point) =>
        paths.TryView(path, out var view) && view != null && lookup.TryGetValue(path, out var data) && Array.FindIndex(DisplayNodes(data), n => n.Point == point) is int i and >= 0 ? view.Positions[i] : null;

    /// <summary>
    /// Move the selected point, undo, redo, revert, a delay edit and an append-then-move-twice
    /// round trip through the shared draft, checking the SQL at each step. Prints the failing
    /// step and returns false on a mismatch.
    /// </summary>
    public bool SmokeEditing(Camera3D camera)
    {
        if (SelectedNode() is not { } current || shared?.Draft == null)
        {
            GD.PushError("WAYPOINT_SMOKE_EDIT_FAILED nothing selected or no draft");
            return false;
        }

        IReadOnlyList<(string Source, RowEdit Edit)> WaypointNet() => NetRows()
            .Where(e => e.Edit.Table is "waypoint_data" or "waypoint_path" or "waypoint_path_node").ToList();

        var (path, node) = current;
        var before = PositionOf(path, node);
        SetPosition(before.X + 2, before.Y, before.Z);
        var moved = PositionOf(path, node);
        var net = WaypointNet();
        bool tc = path.Dialect == WorldDialect.TrinityCore;
        if (moved.X != (float)(before.X + 2) || net.Count != 1 || !CanUndo
            || SqlExport.Build(net[0].Source, [], net.Select(e => e.Edit), null, WorldSchemas.For(path.Dialect)).Changes != 1)
        {
            GD.PushError($"WAYPOINT_SMOKE_EDIT_FAILED move before={before} moved={moved} edits={net.Count}");
            return false;
        }

        Undo();
        if (PositionOf(path, node) != before || WaypointNet().Count != 0 || !CanRedo)
        {
            GD.PushError("WAYPOINT_SMOKE_EDIT_FAILED undo");
            return false;
        }

        Redo();
        if (PositionOf(path, node) != moved)
        {
            GD.PushError("WAYPOINT_SMOKE_EDIT_FAILED redo");
            return false;
        }

        var staged = WaypointNet()[0];
        shared.Revert(staged.Source, staged.Edit);
        if (PositionOf(path, node) != before || WaypointNet().Count != 0)
        {
            GD.PushError("WAYPOINT_SMOKE_EDIT_FAILED revert");
            return false;
        }

        // A property edit exports only its own column and restores on undo.
        var props = StateOf(path, node).Props;
        SetDelay(props.Delay + 7000);
        net = WaypointNet();
        string apply = net.Count == 1 ? SqlExport.Build(net[0].Source, [], net.Select(e => e.Edit), null, WorldSchemas.For(path.Dialect)).Apply : "";
        if (StateOf(path, node).Props.Delay != props.Delay + 7000 || !apply.Contains($"`{(tc ? "Delay" : "delay")}` = {props.Delay + 7000}", StringComparison.Ordinal) || apply.Contains(tc ? "PositionX" : "position_x", StringComparison.Ordinal))
        {
            GD.PushError($"WAYPOINT_SMOKE_EDIT_FAILED property edits={net.Count}");
            return false;
        }

        Undo();
        if (StateOf(path, node).Props != props || WaypointNet().Count != 0)
        {
            GD.PushError("WAYPOINT_SMOKE_EDIT_FAILED property undo");
            return false;
        }

        // Regression: an appended point stays movable. Its first move used to seed the snapshot
        // with the draft's own row, so the second move read as a source conflict and refused.
        AddingPoints = true;
        uint last = DisplayNodes(path).Max(n => n.Point);
        if (SmokeDotPosition(path.Id, last) is not Vector3 anchor || camera.IsPositionBehind(anchor))
        {
            GD.PushError("WAYPOINT_SMOKE_EDIT_FAILED add anchor not visible");
            AddingPoints = false;
            return false;
        }

        if (!AddPointAt(camera, camera.UnprojectPosition(anchor) + new Vector2(60, 0), false) || SmokeSelected != (path.Id, last + 1))
        {
            GD.PushError($"WAYPOINT_SMOKE_EDIT_FAILED add selected={SmokeSelected}");
            AddingPoints = false;
            return false;
        }

        var added = PositionOf(path, DisplayNodes(path).Single(n => n.Point == last + 1));
        SetPosition(added.X + 3, added.Y, added.Z);
        var once = PositionOf(path, DisplayNodes(path).Single(n => n.Point == last + 1));
        if (once.X != (float)(added.X + 3))
        {
            GD.PushError($"WAYPOINT_SMOKE_EDIT_FAILED first move of added point {once} vs {added}");
            AddingPoints = false;
            return false;
        }

        SetPosition(once.X + 3, once.Y, once.Z);
        var twice = PositionOf(path, DisplayNodes(path).Single(n => n.Point == last + 1));
        if (twice.X != (float)(once.X + 3) || WaypointNet().Count != 1)
        {
            GD.PushError($"WAYPOINT_SMOKE_EDIT_FAILED second move of added point {twice} vs {once} edits={WaypointNet().Count}");
            AddingPoints = false;
            return false;
        }

        for (int i = 0; i < 10 && WaypointNet().Count > 0; i++)
        {
            Undo();
        }

        AddingPoints = false;
        if (WaypointNet().Count != 0)
        {
            GD.PushError("WAYPOINT_SMOKE_EDIT_FAILED add/move undo");
            return false;
        }

        return true;
    }
}
