using Godot;
using ACWorldEdit.Assets;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>Placing a new spawn from a template with the mouse (see <see cref="PlacementTool"/>).</summary>
public partial class ViewerShell
{
    /// <summary>The terrain height under a viewer-space point, from the loaded tiles' height maps.</summary>
    float? TerrainHeightAt(float x, float z)
    {
        var key = TileGrid.FromView(x, z);
        return terrainHeights.TryGetValue(key, out var tile) && tile.Probe(TileGrid.LocalX(x, key), TileGrid.LocalZ(z, key)) is float height ? height : null;
    }

    Task<ulong?> PlaceTemplateAsync(PlacementTool.Template template, WorldPose pose) =>
        template.Kind == SpawnKind.Creature ? creatures.PlaceAsync(template.Entry, pose) : gameObjects.PlaceAsync(template.Entry, pose);
}
