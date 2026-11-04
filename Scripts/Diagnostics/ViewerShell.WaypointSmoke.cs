using Godot;

namespace ACWorldEdit;

/// <summary>
/// Opt-in --waypoint-smoke: live read-only waypoint paths near the start, a real viewport click on a
/// path point, selection exclusivity, the move/undo/redo/revert workflow on an isolated draft, the
/// walk preview and layer hiding that stops queries. A graphical run also saves
/// artifacts/waypoint-preview.png.
/// </summary>
public partial class ViewerShell
{
    bool waypointSmoke;
    int waypointStage, waypointWait;
    double waypointTime;
    uint waypointPath, waypointPoint;

    void WaypointSmoke(double delta)
    {
        waypointTime += delta;
        if (waypointTime > 60)
        {
            SmokeFail($"WAYPOINT_SMOKE_TIMEOUT stage={waypointStage} {waypoints.QueryStatus}");
            return;
        }

        if (waypointWait > 0)
        {
            waypointWait--;
            return;
        }

        if (waypointStage == 0 && (busy || world == null || !TerrainSettled))
        {
            return;
        }

        switch (waypointStage)
        {
            case 0:
                if (currentMapId != 0 || !waypoints.SmokeQueryIdle || waypoints.SmokeNodeNear(camera) is not { } near)
                {
                    return;
                }

                var (path, node) = near;
                waypoints.ShowAll = true;
                waypointPath = path.Id;
                waypointPoint = node.Point;
                GD.Print($"WAYPOINT_SMOKE_TARGET path={path.Id} point={node.Point} of {path.Nodes.Length} users={path.Users.Length} paths={waypoints.Paths.Count}");
                // Put the point dead center, 20 yd away, so no panel covers the click.
                var target = ViewSpace.ToView(node.X, node.Y, node.Z);
                var away = new Vector3(camera.Position.X - target.X, 0, camera.Position.Z - target.Z);
                away = away.LengthSquared() < 1 ? Vector3.Back : away.Normalized();
                camera.Position = target + away * 20 + Vector3.Up * 6;
                var look = target - camera.Position;
                yaw = Mathf.Atan2(-look.X, -look.Z);
                pitch = Mathf.Asin(look.Y / look.Length());
                camera.Rotation = new Vector3(pitch, yaw, 0);
                waypointStage = 1;
                waypointWait = 10;
                break;
            case 1:
                if (waypoints.SmokeDotPosition(waypointPath, waypointPoint) is not Vector3 dot || camera.IsPositionBehind(dot))
                {
                    SmokeFail("WAYPOINT_SMOKE_DOT_NOT_VISIBLE");
                    return;
                }

                // A real click through the viewport picking path, not a direct Select call.
                var viewportPoint = camera.UnprojectPosition(dot);
                // Input events carry window pixels; the viewport may be scaled to the window.
                var screen = GetViewport().GetFinalTransform() * viewportPoint;
                GD.Print($"WAYPOINT_SMOKE_CLICK screen={screen} viewport={GetViewport().GetVisibleRect().Size} hit={waypoints.FindHit(camera, viewportPoint)} visiblePaths={waypoints.SmokeVisiblePaths}");
                Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = screen, GlobalPosition = screen });
                Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = screen, GlobalPosition = screen });
                waypointStage = 2;
                waypointWait = 5;
                break;
            case 2:
                if (waypoints.SmokeSelected != (waypointPath, waypointPoint) || creatures.SelectedGuid.HasValue || gameObjects.SelectedGuid.HasValue || graveyards.HasSelection)
                {
                    if (DisplayServer.GetName() != "headless")
                    {
                        GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath("res://artifacts/waypoint-preview.png"));
                    }

                    SmokeFail($"WAYPOINT_SMOKE_PICK_FAILED selected={waypoints.SmokeSelected} creature={creatures.SelectedGuid} object={gameObjects.SelectedGuid} graveyard={graveyards.HasSelection} trigger={areaTriggers.HasSelection}");
                    return;
                }

                GD.Print("WAYPOINT_SMOKE_PICK_PASSED viewport click selected the point; other selections cleared");
                if (DisplayServer.GetName() != "headless")
                {
                    GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath("res://artifacts/waypoint-preview.png"));
                }

                if (!waypoints.SmokeEditing())
                {
                    GetTree().Quit(1);
                    return;
                }

                GD.Print("WAYPOINT_SMOKE_EDIT_PASSED move, undo, redo and revert agree with the export");
                waypoints.SetPreview(true);
                waypointStage = 3;
                waypointWait = 30;
                break;
            case 3:
                if (!waypoints.Previewing)
                {
                    SmokeFail("WAYPOINT_SMOKE_PREVIEW_FAILED");
                    return;
                }

                waypoints.ShowAll = false;
                waypoints.SetLayerEnabled(false);
                waypointStage = 4;
                waypointWait = 10;
                break;
            case 4:
                if (waypoints.SmokeVisiblePaths != 0 || waypoints.Paths.Count != 0 || waypoints.IsLoading || waypoints.HasSelection)
                {
                    SmokeFail("WAYPOINT_SMOKE_LAYER_HIDE_FAILED");
                    return;
                }

                GD.Print("WAYPOINT_SMOKE_PASSED hidden layer has no paths, selection or queries");
                GetTree().Quit();
                break;
        }
    }
}
