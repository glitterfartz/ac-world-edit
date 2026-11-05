using Godot;
using ACWorldEdit.Assets;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>Placing a new spawn from a template with the mouse (see <see cref="PlacementTool"/>).</summary>
public partial class ViewerShell
{
    /// <summary>The terrain height under a viewer-space point, from the loaded tiles' height maps.</summary>
    float? TerrainHeightAt(float x, float z) => ProbeTerrain(x, z).Height;

    /// <summary>A template from the content browser is being dragged over the world: show the ghost under it.</summary>
    void HoverTemplateDrag(PlacementTool.Template template, Vector2 screen)
    {
        if (placement.Current != template)
        {
            placement.Arm(template);
        }

        placement.Hover(camera, screen, force: true);
    }

    /// <summary>A template was dropped on the world: place it there. Holding Shift keeps it armed for more clicks.</summary>
    void DropTemplate(PlacementTool.Template template, Vector2 screen)
    {
        if (placement.Current != template)
        {
            placement.Arm(template);
        }

        _ = placement.ClickAsync(camera, screen, Input.IsKeyPressed(Key.Shift), statusLog.Post);
    }

    /// <summary>The drag finished; if nothing was placed (cancelled, or dropped on a panel) the tool goes idle.</summary>
    void EndTemplateDrag()
    {
        if (!Input.IsKeyPressed(Key.Shift))
        {
            placement.Disarm();
        }
    }

    Task<ulong?> PlaceTemplateAsync(PlacementTool.Template template, WorldPose pose) =>
        template.Kind == SpawnKind.Creature ? creatures.PlaceAsync(template.Entry, pose) : gameObjects.PlaceAsync(template.Entry, pose);
}
