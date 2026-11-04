using Godot;
using ACWorldEdit.Assets;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>Placing a new spawn from a template with the mouse (see <see cref="PlacementTool"/>).</summary>
public partial class ViewerShell
{
    /// <summary>The terrain height under a viewer-space point, from the loaded tiles' height maps.</summary>
    float? TerrainHeightAt(float x, float z) => ProbeTerrain(x, z).Height;

    Task<ulong?> PlaceTemplateAsync(PlacementTool.Template template, WorldPose pose) =>
        template.Kind == SpawnKind.Creature ? creatures.PlaceAsync(template.Entry, pose) : gameObjects.PlaceAsync(template.Entry, pose);
}
