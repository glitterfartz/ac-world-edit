using Godot;
using ACWorldEdit.Assets;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>Placing a new spawn from a template with the mouse (see <see cref="PlacementTool"/>).</summary>
public partial class TerrainViewer
{
    /// <summary>The terrain height under a viewer-space point, from the loaded tiles' height maps.</summary>
    float? TerrainHeightAt(float x, float z)
    {
        var key = new TileKey((int)Math.Floor(x / TerrainTile.Size + 32), (int)Math.Floor(z / TerrainTile.Size + 32));
        return terrainHeights.TryGetValue(key, out var tile) && tile.Probe(x - (key.X - 32) * TerrainTile.Size, z - (key.Y - 32) * TerrainTile.Size) is float height ? height : null;
    }

    Task<ulong?> PlaceTemplateAsync(PlacementTool.Template template, WorldPose pose) =>
        template.Kind == SpawnKind.Creature ? creatures.PlaceAsync(template.Entry, pose) : gameObjects.PlaceAsync(template.Entry, pose);
}
