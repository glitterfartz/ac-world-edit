using Godot;

namespace ACWorldEdit;

/// <summary>Selecting several spawns: Ctrl-click adds or removes one, Shift-drag draws a box around many.</summary>
public partial class TerrainViewer
{
    /// <summary>Where a Shift-drag box started, in viewport coordinates; null when no box is being drawn.</summary>
    Vector2? boxStart;

    Panel selectionBox = null!;

    void BuildSelectionBox(Node ui)
    {
        selectionBox = new Panel { Name = "SelectionBox", Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        selectionBox.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new(1f, 0.81f, 0.41f, 0.14f),
            BorderColor = new(1f, 0.81f, 0.41f, 0.9f),
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1
        });
        ui.AddChild(selectionBox);
    }

    static Rect2 BoxBetween(Vector2 a, Vector2 b) => new(new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y)), new(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y)));

    /// <summary>
    /// Handle the events of a selection gesture. Returns true when the event was used: a Ctrl-click, or any
    /// event of a Shift-drag box once it has started.
    /// </summary>
    bool HandleSelectionGesture(InputEvent input)
    {
        if (boxStart is Vector2 start)
        {
            if (input is InputEventMouseMotion move)
            {
                var box = BoxBetween(start, move.Position);
                selectionBox.Position = box.Position;
                selectionBox.Size = box.Size;
                selectionBox.Visible = box.Size.Length() > 4;
                return true;
            }

            if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false } release)
            {
                var box = BoxBetween(start, release.Position);
                boxStart = null;
                selectionBox.Visible = false;
                if (box.Size.X > 6 && box.Size.Y > 6)
                {
                    SelectInBox(box);
                }

                return true;
            }

            return false;
        }

        if (input is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } press || capture)
        {
            return false;
        }

        // A press on a gizmo handle starts its drag whatever key is held (Ctrl snaps a rotation).
        if ((press.ShiftPressed || press.CtrlPressed) && ActiveEditor.BeginDrag(camera, press.Position))
        {
            return true;
        }

        if (press.ShiftPressed)
        {
            boxStart = press.Position;
            return true;
        }

        if (press.CtrlPressed)
        {
            ToggleUnderCursor(press.Position);
            return true;
        }

        return false;
    }

    /// <summary>Ctrl-click: the nearest creature or GameObject under the cursor joins the selection or leaves it.</summary>
    void ToggleUnderCursor(Vector2 screen)
    {
        var creature = creatures.FindHit(camera, screen);
        var gameObject = gameObjects.FindHit(camera, screen);
        if (creature.HasValue && (!gameObject.HasValue || creature.Value.Distance <= gameObject.Value.Distance))
        {
            gameObjects.Deselect();
            creatures.ToggleInGroup(creature.Value.Guid);
        }
        else if (gameObject.HasValue)
        {
            creatures.Deselect();
            gameObjects.ToggleInGroup(gameObject.Value.Guid);
        }
    }

    /// <summary>Add everything inside the box: creatures, or GameObjects when there are none in it and no creature is selected.</summary>
    void SelectInBox(Rect2 box)
    {
        int count = gameObjects.Group.Count > 0 && creatures.Group.Count == 0 ? 0 : creatures.SelectInBox(camera, box);
        if (count > 0)
        {
            gameObjects.Deselect();
            statusLog.Post(creatures.Group.Count == 1 ? "Selected 1 creature." : $"Selected {creatures.Group.Count} creatures.");
            return;
        }

        if (creatures.Group.Count == 0)
        {
            int objects = gameObjects.SelectInBox(camera, box);
            statusLog.Post(objects == 0 ? "Nothing in the box." : gameObjects.Group.Count == 1 ? "Selected 1 GameObject." : $"Selected {gameObjects.Group.Count} GameObjects.");
        }
    }
}
