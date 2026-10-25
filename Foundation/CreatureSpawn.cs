namespace ACWorldEdit.Foundation;

/// <summary>
/// Immutable database spawn projection. SpawnMask and PhaseMask are metadata currently shown in
/// details; event, pool, player-phase and script visibility are not simulated.
/// </summary>
public sealed record CreatureSpawn(ulong Guid, uint TemplateId, string Name, WorldPose Pose, uint SpawnMask, uint PhaseMask, uint DisplayId = 0, float DisplayScale = 1, SpawnVisibility? Visibility = null, SpawnPhasing? Phasing = null);
/// <summary>
/// Inclusive tile region defining a bounded server query rectangle. Lower spatial edges are
/// exclusive and upper edges inclusive, matching CreatureSource predicates.
/// </summary>
public sealed record CreatureRegion(ushort Map, int MinTileX, int MaxTileX, int MinTileY, int MaxTileY)
{
    public double MinServerX => (31 - MaxTileY) * WorldCoordinates.TileSize;
    public double MaxServerX => (32 - MinTileY) * WorldCoordinates.TileSize;
    public double MinServerY => (31 - MaxTileX) * WorldCoordinates.TileSize;
    public double MaxServerY => (32 - MinTileX) * WorldCoordinates.TileSize;
}

/// <summary>
/// Read-only regional snapshot with result-limit status and a credential-free source identity
/// for draft isolation.
/// </summary>
public sealed record CreatureRegionResult(IReadOnlyList<CreatureSpawn> Spawns, bool Truncated, string SourceKey = "", WorldEventInfo[]? Events = null);
