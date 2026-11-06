using Godot;

namespace ACWorldEdit;

/// <summary>
/// The shell's side of <see cref="IViewerInputHost"/>: it exposes the scene nodes and the few
/// decisions the input router asks for, without making the router depend on the shell's state.
/// </summary>
public partial class ViewerShell : IViewerInputHost
{
    Camera3D IViewerInputHost.Camera => camera;
    NavigationOverlay IViewerInputHost.Navigation => navigation;
    PlacementTool IViewerInputHost.Placement => placement;
    CreatureOverlay IViewerInputHost.Creatures => creatures;
    GameObjectOverlay IViewerInputHost.GameObjects => gameObjects;
    GraveyardOverlay IViewerInputHost.Graveyards => graveyards;
    WaypointOverlay IViewerInputHost.Waypoints => waypoints;
    AreaTriggerOverlay IViewerInputHost.AreaTriggers => areaTriggers;
    CreaturePropertiesWindow IViewerInputHost.CreatureProperties => creatureProps;
    GameObjectPropertiesWindow IViewerInputHost.GameObjectProperties => gameObjectProps;
    bool IViewerInputHost.DatabaseEnabled => databaseEnabled;
    IPlacementEditor IViewerInputHost.ActiveEditor => ActiveEditor;
    bool IViewerInputHost.HandleSelectionGesture(InputEvent input) => HandleSelectionGesture(input);
    void IViewerInputHost.CaptureDiagnostics() => CaptureDiagnostics();
    void IViewerInputHost.Report(string text) => statusLog.Post(text);
    void IViewerInputHost.CameraMoved() => cameraFocused = false;
}
