using Godot;

namespace ACWorldEdit;

/// <summary>
/// The tool strip: mode buttons, edit actions, snap options and the editors the viewer can open.
/// Every button mirrors a source of truth each frame, so menu and code changes show here too.
/// </summary>
public partial class ViewerShell
{
    /// <summary>
    /// Toolbar mirrors the creature tool state each frame. Mode keys are blocked while the camera is
    /// captured so Q/W/E keep flying the camera (see _Input).
    /// </summary>
    void BuildToolbar()
    {
        var modes = new ButtonGroup();
        toolbar.Mode("Select", "Pick only; hide handles", modes, () => SetMode(GizmoMode.Select), () => creatures.Mode == GizmoMode.Select, EditorToolbar.Shortcut(Key.Q));
        toolbar.Mode("Move", "Drag colored axes to move", modes, () => SetMode(GizmoMode.Move), () => creatures.Mode == GizmoMode.Move, EditorToolbar.Shortcut(Key.W));
        toolbar.Mode("Rotate", "Drag the yellow ring to change heading (not for graveyards or waypoints)", modes, () => SetMode(GizmoMode.Rotate), () => creatures.Mode == GizmoMode.Rotate, EditorToolbar.Shortcut(Key.E), () => ActiveEditor.SupportsRotation);
        toolbar.Separator();
        toolbar.Action("Undo", "Undo the last local change for the selected kind (Ctrl+Z)", () => ActiveEditor.Undo(), () => ActiveEditor.CanUndo && !ActiveEditor.IsDragging);
        toolbar.Action("Redo", "Redo (Ctrl+Y)", () => ActiveEditor.Redo(), () => ActiveEditor.CanRedo && !ActiveEditor.IsDragging, EditorToolbar.Shortcut(Key.Z, ctrl: true, shift: true));
        toolbar.Separator();
        toolbar.Action("Duplicate", "Copy the selected spawn two yards ahead of it (Ctrl+D)", () => ActiveEditor.Duplicate(), () => ActiveEditor.CanDuplicate && !ActiveEditor.IsDragging);
        toolbar.Action("Delete", "Delete the selected spawn (Del)", () => ActiveEditor.Delete(), () => ActiveEditor.CanDelete && !ActiveEditor.IsDragging);
        toolbar.Separator();
        toolbar.Action("Drop to ground", "Snap the selection to terrain or a building floor below it (End)", () => ActiveEditor.DropToGround(), () => ActiveEditor.CanEdit);
        toolbar.Toggle("Snap on drop", "Snap to the ground when you release a move drag", value =>
        {
            creatures.SnapOnDrop = value;
            gameObjects.SnapOnDrop = value;
            graveyards.SnapOnDrop = value;
            waypoints.SnapOnDrop = value;
        }, () => creatures.SnapOnDrop);
        toolbar.Label("Rotation step");
        float[] steps = [0, 5, 15, 45];
        toolbar.Choice("Fixed heading increments while rotating",
            steps.Select(degrees => degrees == 0 ? "Free" : $"{degrees:0}°").ToArray(), 0, index =>
            {
                creatures.RotationStep = Mathf.DegToRad(steps[index]);
                gameObjects.RotationStep = Mathf.DegToRad(steps[index]);
            });
        toolbar.Separator();
        toolbar.Action("Fly to selection", "Move the camera to the selection (F)", FlyToSelection, () => HasSelection);
        toolbar.Separator();
        toolbar.Action("Quests", "Open the quest editor", () => questEditor.Open(0), () => databaseEnabled);
        toolbar.Action("Behavior", "Edit the selected creature's SmartAI rules and speech", OpenBehaviorEditor, () => BehaviorEditors);
        toolbar.Action("Creature Templates", "Search the database for a creature template by name or ID", OpenCreatureTemplateSearch, () => databaseEnabled);
    }
}
