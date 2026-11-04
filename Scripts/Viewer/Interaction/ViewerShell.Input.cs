using Godot;

namespace ACWorldEdit;

/// <summary>
/// Viewport input: navigation picks, selection, placement, right-mouse flight, drag ownership and
/// flight keys. The order is load-bearing: navigation, selection and placement first; F12 before
/// the flight-key swallow; Esc still reaches this handler while flying.
/// </summary>
public partial class ViewerShell
{
    (Vector2 Position, ulong Ticks, float Yaw, float Pitch)? rightPress;
    float rightMotion;

    /// <summary>
    /// The draft editor that owns the current selection; creatures when nothing editable is
    /// selected. Menus, toolbar and viewport drags route through it.
    /// </summary>
    IPlacementEditor ActiveEditor => waypoints != null && waypoints.HasSelection ? waypoints : graveyards != null && graveyards.HasSelection ? graveyards : gameObjects != null && gameObjects.HasSelection ? gameObjects : creatures;

    /// <summary>
    /// Handle navigation/clicks after UI consumption. Gizmo handles take priority over creature
    /// selection.
    /// </summary>
    public override void _UnhandledInput(InputEvent input)
    {
        if (HandleNavigationClick(input))
        {
            return;
        }

        if (placement.Current == null && HandleSelectionGesture(input))
        {
            return;
        }

        if (HandlePlacement(input))
        {
            return;
        }

        if (input is InputEventMouseButton mouse)
        {
            HandleMouseButton(mouse);
        }

        if (input is InputEventMouseMotion motion && capture)
        {
            rightMotion += motion.Relative.Length();
            yaw -= motion.Relative.X * 0.0025f;
            pitch = Mathf.Clamp(pitch - motion.Relative.Y * 0.0025f, -1.55f, 1.55f);
            camera.Rotation = new Vector3(pitch, yaw, 0);
        }

        if (input is InputEventKey key && key.Pressed && key.Keycode == Key.Escape)
        {
            capture = false;
            Input.MouseMode = Input.MouseModeEnum.Visible;
        }
    }

    /// <summary>A navigation-overlay pick consumes the click before anything else sees it.</summary>
    bool HandleNavigationClick(InputEvent input)
    {
        if (!capture && navigation.Inspect && navigation.LayerEnabled && input is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } navClick)
        {
            navigation.Pick(camera, navClick.Position);
            GetViewport().SetInputAsHandled();
            return true;
        }

        return false;
    }

    /// <summary>Placement hover, click and disarm; a click or disarm consumes the event.</summary>
    bool HandlePlacement(InputEvent input)
    {
        if (placement.Current == null || capture)
        {
            return false;
        }

        if (input is InputEventMouseMotion hover)
        {
            placement.Hover(camera, hover.Position);
        }
        else if (input is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click)
        {
            _ = placement.ClickAsync(camera, click.Position, click.ShiftPressed, statusLog.Post);
            GetViewport().SetInputAsHandled();
            return true;
        }
        else if (input is InputEventKey { Pressed: true, Keycode: Key.Escape })
        {
            placement.Disarm();
            statusLog.Post("Stopped placing.");
            return true;
        }

        return false;
    }

    /// <summary>Left drag/pick, right-mouse flight capture and the speed wheel.</summary>
    void HandleMouseButton(InputEventMouseButton mouse)
    {
        if (mouse.Pressed && mouse.ButtonIndex == MouseButton.Left && !capture)
        {
            if (!ActiveEditor.BeginDrag(camera, mouse.Position))
            {
                PickAt(mouse.Position);
            }

            return;
        }

        if (mouse.ButtonIndex == MouseButton.Right)
        {
            HandleRightButton(mouse);
        }

        if (mouse.Pressed && mouse.ButtonIndex == MouseButton.WheelUp)
        {
            speed = Math.Min(1500, speed * 1.25f);
        }

        if (mouse.Pressed && mouse.ButtonIndex == MouseButton.WheelDown)
        {
            speed = Math.Max(5, speed / 1.25f);
        }
    }

    /// <summary>
    /// Nearest ray hit wins across creatures, GameObjects, graveyard icons and AreaTrigger volumes;
    /// a volume loses ties so models inside it stay clickable.
    /// </summary>
    void PickAt(Vector2 position)
    {
        var creatureHit = creatures.FindHit(camera, position);
        var objectHit = gameObjects.FindHit(camera, position);
        var graveyardHit = graveyards.FindHit(camera, position);
        var triggerHit = areaTriggers.FindHit(camera, position);
        var waypointHit = waypoints.FindHit(camera, position);
        float creatureDistance = creatureHit?.Distance ?? float.MaxValue;
        float objectDistance = objectHit?.Distance ?? float.MaxValue;
        float graveyardDistance = graveyardHit?.Distance ?? float.MaxValue;
        float triggerDistance = triggerHit?.Distance ?? float.MaxValue;
        float waypointDistance = waypointHit?.Distance ?? float.MaxValue;
        if (waypointHit.HasValue && waypointDistance <= creatureDistance && waypointDistance <= objectDistance && waypointDistance <= graveyardDistance && waypointDistance <= triggerDistance)
        {
            waypoints.Select(waypointHit.Value.Path, waypointHit.Value.Point);
        }
        else if (triggerHit.HasValue && triggerDistance < creatureDistance && triggerDistance < objectDistance && triggerDistance < graveyardDistance)
        {
            areaTriggers.Select(triggerHit.Value.Key);
        }
        else if (graveyardHit.HasValue && graveyardDistance <= creatureDistance && graveyardDistance <= objectDistance)
        {
            graveyards.Select(graveyardHit.Value.Id);
        }
        else if (creatureHit.HasValue && creatureDistance <= objectDistance)
        {
            creatures.Select(creatureHit.Value.Guid);
        }
        else if (objectHit.HasValue)
        {
            gameObjects.Select(objectHit.Value.Guid);
        }
    }

    /// <summary>
    /// Right press captures the mouse for flight; a short press without dragging restores the pose
    /// and opens the creature under it, like the client.
    /// </summary>
    void HandleRightButton(InputEventMouseButton mouse)
    {
        if (mouse.Pressed)
        {
            rightPress = (mouse.Position, Time.GetTicksMsec(), yaw, pitch);
            rightMotion = 0;
        }
        else if (rightPress is { } press && rightMotion < 6 && Time.GetTicksMsec() - press.Ticks < 350)
        {
            yaw = press.Yaw;
            pitch = press.Pitch;
            camera.Rotation = new Vector3(pitch, yaw, 0);
            if (creatures.FindHit(camera, press.Position) is { } hit)
            {
                creatures.Select(hit.Guid);
                if (databaseEnabled && creatures.SelectedSpawn is { } spawn)
                {
                    creatureProps.Open(spawn, "gossip");
                }
            }
        }

        capture = mouse.Pressed;
        Input.MouseMode = capture ? Input.MouseModeEnum.Captured : Input.MouseModeEnum.Visible;
    }

    /// <summary>
    /// Release camera capture and cancel an active draft drag when window focus is lost.
    /// </summary>
    public override void _Notification(int what)
    {
        if (what == NotificationWMWindowFocusOut)
        {
            capture = false;
            Input.MouseMode = Input.MouseModeEnum.Visible;
            creatures?.EndDrag(false);
            gameObjects?.EndDrag(false);
            graveyards?.EndDrag(false);
            waypoints?.EndDrag(false);
        }
    }

    /// <summary>
    /// Keep flight keys away from shortcuts, and own an active drag through release/Escape even
    /// across UI. Completion saves locally; cancellation restores the starting pose.
    /// </summary>
    public override void _Input(InputEvent input)
    {
        // F12 works while flying too, so it sits before the flight-key swallow.
        if (input is InputEventKey { Keycode: Key.F12, Pressed: true, Echo: false })
        {
            CaptureDiagnostics();
            GetViewport().SetInputAsHandled();
            return;
        }

        // While right-mouse flight is active, W/A/S/D/Q/E drive the camera by polling. Consume key
        // events here (before shortcut input) so they cannot also switch tools or run menu
        // commands. Esc still reaches _UnhandledInput to release the mouse.
        if (capture && input is InputEventKey { Keycode: not Key.Escape })
        {
            GetViewport().SetInputAsHandled();
            return;
        }

        if (creatures == null || gameObjects == null || graveyards == null || waypoints == null || !ActiveEditor.IsDragging)
        {
            return;
        }

        var editor = ActiveEditor;
        if (input is InputEventMouseMotion motion)
        {
            editor.Drag(camera, motion.Position);
        }
        else if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false })
        {
            editor.EndDrag(true);
        }
        else if (input is InputEventKey { Keycode: Key.Escape, Pressed: true })
        {
            editor.EndDrag(false);
        }

        GetViewport().SetInputAsHandled();
    }

    /// <summary>
    /// Translate captured keyboard input along camera/world axes. Normalize combined movement so
    /// diagonal flight has the same base speed.
    /// </summary>
    void MoveCamera(double delta)
    {
        Vector3 move = Vector3.Zero;
        if (Input.IsPhysicalKeyPressed(Key.W))
        {
            move -= camera.Basis.Z;
        }

        if (Input.IsPhysicalKeyPressed(Key.S))
        {
            move += camera.Basis.Z;
        }

        if (Input.IsPhysicalKeyPressed(Key.A))
        {
            move -= camera.Basis.X;
        }

        if (Input.IsPhysicalKeyPressed(Key.D))
        {
            move += camera.Basis.X;
        }

        if (Input.IsPhysicalKeyPressed(Key.Q))
        {
            move -= Vector3.Up;
        }

        if (Input.IsPhysicalKeyPressed(Key.E))
        {
            move += Vector3.Up;
        }

        if (move != Vector3.Zero)
        {
            cameraFocused = false;
        }

        camera.Position += move.Normalized() * speed * (Input.IsPhysicalKeyPressed(Key.Shift) ? 4 : 1) * (float)delta;
    }
}
