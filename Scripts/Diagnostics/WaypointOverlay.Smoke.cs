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
    public int SmokeVisiblePaths => views.Values.Count(v => v.Root.Visible);

    /// <summary>
    /// The point of a followed path (three or more points) nearest the camera.
    /// </summary>
    public (WaypointPath Path, WaypointNode Node)? SmokeNodeNear(Camera3D camera)
    {
        (WaypointPath, WaypointNode)? best = null;
        float bestDistance = float.MaxValue;
        foreach (var path in lookup.Values.Where(p => p.Active && p.Nodes.Length >= 3))
        {
            foreach (var node in path.Nodes)
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

    public Vector3? SmokeDotPosition(uint path, uint point) =>
        views.TryGetValue(path, out var view) && lookup.TryGetValue(path, out var data) && Array.FindIndex(data.Nodes, n => n.Point == point) is int i and >= 0 ? view.Positions[i] : null;

    /// <summary>
    /// Move the selected point, undo, redo and revert it, checking the draft and the generated SQL
    /// at each step. Prints the failing step and returns false on a mismatch.
    /// </summary>
    public bool SmokeEditing()
    {
        if (SelectedNode() is not { } current || draft == null)
        {
            GD.PushError("WAYPOINT_SMOKE_EDIT_FAILED nothing selected or no draft");
            return false;
        }

        var (path, node) = current;
        var before = PositionOf(path, node);
        SetPosition(before.X + 2, before.Y, before.Z);
        var moved = PositionOf(path, node);
        var net = NetEdits();
        if (moved.X != (float)(before.X + 2) || net.Count != 1 || !CanUndo || SqlExport.Build(net[0].Source, [], [], [], null, net).Changes != 1)
        {
            GD.PushError($"WAYPOINT_SMOKE_EDIT_FAILED move before={before} moved={moved} edits={net.Count}");
            return false;
        }

        Undo();
        if (PositionOf(path, node) != before || NetEdits().Count != 0 || !CanRedo)
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

        Revert(NetEdits()[0]);
        if (PositionOf(path, node) != before || NetEdits().Count != 0)
        {
            GD.PushError("WAYPOINT_SMOKE_EDIT_FAILED revert");
            return false;
        }

        // A property edit exports only its own column and restores on undo.
        var props = StateOf(path, node).Props;
        SetDelay(props.Delay + 7000);
        net = NetEdits();
        string apply = net.Count == 1 ? SqlExport.Build(net[0].Source, [], [], [], null, net).Apply : "";
        if (StateOf(path, node).Props.Delay != props.Delay + 7000 || !apply.Contains($"`delay` = {props.Delay + 7000}", StringComparison.Ordinal) || apply.Contains("position_x", StringComparison.Ordinal))
        {
            GD.PushError($"WAYPOINT_SMOKE_EDIT_FAILED property edits={net.Count}");
            return false;
        }

        Undo();
        if (StateOf(path, node).Props != props || NetEdits().Count != 0)
        {
            GD.PushError("WAYPOINT_SMOKE_EDIT_FAILED property undo");
            return false;
        }

        return true;
    }
}
