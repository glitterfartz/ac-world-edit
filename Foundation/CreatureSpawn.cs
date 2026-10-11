namespace ACWorldEdit.Foundation;

public sealed record CreatureSpawn(uint Guid, uint TemplateId, string Name, WorldPose Pose, uint SpawnMask, uint PhaseMask);
public sealed record CreatureRegion(ushort Map, int MinTileX, int MaxTileX, int MinTileY, int MaxTileY)
{
    public double MinServerX => (31 - MaxTileY) * WorldCoordinates.TileSize;
    public double MaxServerX => (32 - MinTileY) * WorldCoordinates.TileSize;
    public double MinServerY => (31 - MaxTileX) * WorldCoordinates.TileSize;
    public double MaxServerY => (32 - MinTileX) * WorldCoordinates.TileSize;
}
public sealed record CreatureRegionResult(IReadOnlyList<CreatureSpawn> Spawns, bool Truncated);
