using ACWorldEdit.Foundation;

namespace ACWorldEdit.Data;

/// <summary>
/// One waypoint_data row: Point is the order, Delay the pause in milliseconds and MoveType the
/// server's WaypointMoveType (0 walk, 1 run, 2 land, 3 takeoff). Positions are server coordinates.
/// Orientation is null when the column is NULL; wpguid (a runtime marker) is not read.
/// </summary>
public sealed record WaypointNode(uint Point, double X, double Y, double Z, double? Orientation, double Velocity, uint Delay, bool Smooth, int MoveType, int Action, int ActionChance);

/// <summary>
/// A creature spawn that follows a path. Walks is false when creature.MovementType is not 2, in
/// which case the server loads the path but the creature does not follow it.
/// </summary>
public sealed record WaypointUser(ulong Guid, uint Entry, string Name, bool Walks);

/// <summary>
/// One path with the spawns in the region that use it. A path id may be shared by several spawns.
/// TrinityCore keeps move type and velocity on the path, so its nodes repeat them and only the
/// position, facing and delay of a node are editable (<see cref="WaypointProps.EditableOn"/>).
/// </summary>
public sealed record WaypointPath(uint Id, ushort Map, WaypointNode[] Nodes, WaypointUser[] Users, WorldDialect Dialect = WorldDialect.AzerothCore)
{
    /// <summary>
    /// True when at least one user actually follows the path (MovementType 2).
    /// </summary>
    public bool Active => Users.Any(u => u.Walks);
}

public sealed record WaypointRegionResult(WaypointPath[] Paths, bool Truncated, string SourceKey);
