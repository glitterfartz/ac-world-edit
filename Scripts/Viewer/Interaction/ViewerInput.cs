using Godot;

namespace ACWorldEdit;

/// <summary>
/// What the input router needs from the shell. Kept as an interface so the router has no dependency
/// on the shell's state and the order rules can be exercised with fakes.
/// </summary>
public interface IViewerInputHost
{
    Camera3D Camera { get; }
    NavigationOverlay Navigation { get; }
    PlacementTool Placement { get; }
    CreatureOverlay Creatures { get; }
    GameObjectOverlay GameObjects { get; }
    GraveyardOverlay Graveyards { get; }
    WaypointOverlay Waypoints { get; }
    AreaTriggerOverlay AreaTriggers { get; }
    CreaturePropertiesWindow CreatureProperties { get; }
    GameObjectPropertiesWindow GameObjectProperties { get; }
    bool DatabaseEnabled { get; }
    IPlacementEditor ActiveEditor { get; }
    bool HandleSelectionGesture(InputEvent input);
    void CaptureDiagnostics();
    void Report(string text);
    void CameraMoved();
}

/// <summary>
/// Viewport input: navigation picks, selection, placement, right-mouse flight, drag ownership and
/// flight keys. The order is load-bearing: navigation, selection and placement first; F12 before
/// the flight-key swallow; Esc still reaches the unhandled handler while flying. The router owns the
/// camera angles, speed, capture flag and focus flag; the shell forwards them.
/// </summary>
public sealed class ViewerInput
{
    readonly IViewerInputHost host;
    (Vector2 Position, ulong Ticks, float Yaw, float Pitch)? rightPress;
    float rightMotion;

    public ViewerInput(IViewerInputHost host) => this.host = host;

    public float Yaw { get; set; }
    public float Pitch { get; set; } = -0.4f;
    public float Speed { get; set; } = 70;
    public bool Capture { get; set; }
    public bool CameraFocused { get; set; }

    /// <summary>
    /// Handle navigation/clicks after UI consumption. Gizmo handles take priority over creature
    /// selection.
    /// </summary>
    public void HandleUnhandled(InputEvent input)
    {
        if (HandleNavigationClick(input))
        {
            return;
        }

        if (host.Placement.Current == null && host.HandleSelectionGesture(input))
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

        if (input is InputEventMouseMotion motion && Capture)
        {
            rightMotion += motion.Relative.Length();
            Yaw -= motion.Relative.X * 0.0025f;
            Pitch = Mathf.Clamp(Pitch - motion.Relative.Y * 0.0025f, -1.55f, 1.55f);
            host.Camera.Rotation = new Vector3(Pitch, Yaw, 0);
        }

        if (input is InputEventKey key && key.Pressed && key.Keycode == Key.Escape)
        {
            if (host.Waypoints.AddingPoints)
            {
                host.Waypoints.AddingPoints = false;
                host.Report("Stopped adding points.");
                return;
            }

            Capture = false;
            Input.MouseMode = Input.MouseModeEnum.Visible;
        }
    }

    /// <summary>A navigation-overlay pick consumes the click before anything else sees it.</summary>
    bool HandleNavigationClick(InputEvent input)
    {
        if (!Capture && host.Navigation.Inspect && host.Navigation.LayerEnabled && input is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } navClick)
        {
            host.Navigation.Pick(host.Camera, navClick.Position);
            GetViewport().SetInputAsHandled();
            return true;
        }

        return false;
    }

    /// <summary>Placement hover, click and disarm; a click or disarm consumes the event.</summary>
    bool HandlePlacement(InputEvent input)
    {
        if (host.Placement.Current == null || Capture)
        {
            return false;
        }

        if (input is InputEventMouseMotion hover)
        {
            host.Placement.Hover(host.Camera, hover.Position);
        }
        else if (input is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click)
        {
            _ = host.Placement.ClickAsync(host.Camera, click.Position, click.ShiftPressed, host.Report);
            GetViewport().SetInputAsHandled();
            return true;
        }
        else if (input is InputEventKey { Pressed: true, Keycode: Key.Escape })
        {
            host.Placement.Disarm();
            host.Report("Stopped placing.");
            return true;
        }

        return false;
    }

    /// <summary>Left drag/pick, right-mouse flight capture and the speed wheel.</summary>
    void HandleMouseButton(InputEventMouseButton mouse)
    {
        if (mouse.Pressed && mouse.ButtonIndex == MouseButton.Left && !Capture)
        {
            // Waypoint add mode consumes ground clicks (Alt inserts after the nearest segment);
            // clicks on existing points still select them.
            if (host.Waypoints.AddPointAt(host.Camera, mouse.Position, mouse.AltPressed))
            {
                return;
            }

            if (!host.ActiveEditor.BeginDrag(host.Camera, mouse.Position))
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
            Speed = Math.Min(1500, Speed * 1.25f);
        }

        if (mouse.Pressed && mouse.ButtonIndex == MouseButton.WheelDown)
        {
            Speed = Math.Max(5, Speed / 1.25f);
        }
    }

    /// <summary>
    /// Nearest ray hit wins across creatures, GameObjects, graveyard icons and AreaTrigger volumes;
    /// a volume loses ties so models inside it stay clickable.
    /// </summary>
    void PickAt(Vector2 position)
    {
        var creatureHit = host.Creatures.FindHit(host.Camera, position);
        var objectHit = host.GameObjects.FindHit(host.Camera, position);
        var graveyardHit = host.Graveyards.FindHit(host.Camera, position);
        var triggerHit = host.AreaTriggers.FindHit(host.Camera, position);
        var waypointHit = host.Waypoints.FindHit(host.Camera, position);
        float creatureDistance = creatureHit?.Distance ?? float.MaxValue;
        float objectDistance = objectHit?.Distance ?? float.MaxValue;
        float graveyardDistance = graveyardHit?.Distance ?? float.MaxValue;
        float triggerDistance = triggerHit?.Distance ?? float.MaxValue;
        float waypointDistance = waypointHit?.Distance ?? float.MaxValue;
        if (waypointHit.HasValue && waypointDistance <= creatureDistance && waypointDistance <= objectDistance && waypointDistance <= graveyardDistance && waypointDistance <= triggerDistance)
        {
            host.Waypoints.Select(waypointHit.Value.Path, waypointHit.Value.Point);
        }
        else if (triggerHit.HasValue && triggerDistance < creatureDistance && triggerDistance < objectDistance && triggerDistance < graveyardDistance)
        {
            host.AreaTriggers.Select(triggerHit.Value.Key);
        }
        else if (graveyardHit.HasValue && graveyardDistance <= creatureDistance && graveyardDistance <= objectDistance)
        {
            host.Graveyards.Select(graveyardHit.Value.Id);
        }
        else if (creatureHit.HasValue && creatureDistance <= objectDistance)
        {
            host.Creatures.Select(creatureHit.Value.Guid);
        }
        else if (objectHit.HasValue)
        {
            host.GameObjects.Select(objectHit.Value.Guid);
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
            rightPress = (mouse.Position, Time.GetTicksMsec(), Yaw, Pitch);
            rightMotion = 0;
        }
        else if (rightPress is { } press && rightMotion < 6 && Time.GetTicksMsec() - press.Ticks < 350)
        {
            Yaw = press.Yaw;
            Pitch = press.Pitch;
            host.Camera.Rotation = new Vector3(Pitch, Yaw, 0);
            var creatureHit = host.Creatures.FindHit(host.Camera, press.Position);
            var objectHit = host.GameObjects.FindHit(host.Camera, press.Position);
            if (objectHit is { } obj && (creatureHit == null || obj.Distance <= creatureHit.Value.Distance))
            {
                host.GameObjects.Select(obj.Guid);
                if (host.DatabaseEnabled && host.GameObjects.SelectedSpawn is { } spawn)
                {
                    host.GameObjectProperties.Open(spawn, "template");
                }
            }
            else if (creatureHit is { } hit)
            {
                host.Creatures.Select(hit.Guid);
                if (host.DatabaseEnabled && host.Creatures.SelectedSpawn is { } spawn)
                {
                    host.CreatureProperties.Open(spawn, "gossip");
                }
            }
        }

        Capture = mouse.Pressed;
        Input.MouseMode = Capture ? Input.MouseModeEnum.Captured : Input.MouseModeEnum.Visible;
    }

    /// <summary>Release camera capture and cancel an active draft drag when window focus is lost.</summary>
    public void HandleNotification(int what)
    {
        if (what == Godot.Node.NotificationWMWindowFocusOut)
        {
            Capture = false;
            Input.MouseMode = Input.MouseModeEnum.Visible;
            host.Creatures?.EndDrag(false);
            host.GameObjects?.EndDrag(false);
            host.Graveyards?.EndDrag(false);
            host.Waypoints?.EndDrag(false);
        }
    }

    /// <summary>
    /// Keep flight keys away from shortcuts, and own an active drag through release/Escape even
    /// across UI. Completion saves locally; cancellation restores the starting pose.
    /// </summary>
    public void HandleInput(InputEvent input)
    {
        // F12 works while flying too, so it sits before the flight-key swallow.
        if (input is InputEventKey { Keycode: Key.F12, Pressed: true, Echo: false })
        {
            host.CaptureDiagnostics();
            GetViewport().SetInputAsHandled();
            return;
        }

        // While right-mouse flight is active, W/A/S/D/Q/E drive the camera by polling. Consume key
        // events here (before shortcut input) so they cannot also switch tools or run menu
        // commands. Esc still reaches the unhandled handler to release the mouse.
        if (Capture && input is InputEventKey { Keycode: not Key.Escape })
        {
            GetViewport().SetInputAsHandled();
            return;
        }

        var editor = host.ActiveEditor;
        if (editor == null || !editor.IsDragging)
        {
            return;
        }

        if (input is InputEventMouseMotion motion)
        {
            editor.Drag(host.Camera, motion.Position);
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
    public void Move(double delta)
    {
        Vector3 move = Vector3.Zero;
        if (Input.IsPhysicalKeyPressed(Key.W))
        {
            move -= host.Camera.Basis.Z;
        }

        if (Input.IsPhysicalKeyPressed(Key.S))
        {
            move += host.Camera.Basis.Z;
        }

        if (Input.IsPhysicalKeyPressed(Key.A))
        {
            move -= host.Camera.Basis.X;
        }

        if (Input.IsPhysicalKeyPressed(Key.D))
        {
            move += host.Camera.Basis.X;
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
            host.CameraMoved();
        }

        host.Camera.Position += move.Normalized() * Speed * (Input.IsPhysicalKeyPressed(Key.Shift) ? 4 : 1) * (float)delta;
    }

    Viewport GetViewport() => host.Camera.GetViewport();
}
