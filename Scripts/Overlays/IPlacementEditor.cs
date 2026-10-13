using Godot;

namespace ACWorldEdit;

/// <summary>
/// Placement commands shared by overlays with local drafts (creatures, graveyards). The menu bar,
/// toolbar and viewport input route to whichever editor owns the current selection; each editor
/// keeps its own draft history.
/// </summary>
public interface IPlacementEditor
{
    bool HasSelection { get; }
    bool CanEdit { get; }
    bool CanUndo { get; }
    bool CanRedo { get; }
    bool IsDragging { get; }
    /// <summary>
    /// False for records without a heading (graveyards); the Rotate tool is then unavailable.
    /// </summary>
    bool SupportsRotation { get; }
    void Undo();
    void Redo();
    void DropToGround();
    void ResetToSource();
    void CopyGoCommand();
    void FlyToSelection();
    bool BeginDrag(Camera3D camera, Vector2 screen);
    void Drag(Camera3D camera, Vector2 screen);
    void EndDrag(bool commit);
}
