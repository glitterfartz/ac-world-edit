namespace ACWorldEdit.Foundation;

// The waypoint and formation tables of TcTables.
public sealed partial class TcTables
{
    /// <summary>
    /// One waypoint_path row: the path-level move type, flags, speed and comment shared by every
    /// creature on the path. Velocity NULL means each creature's own speed.
    /// </summary>
    public static readonly TableSchema WaypointPath = new("waypoint_path", ["PathId"], [
        ("PathId", ColumnKind.UInt, 0),
        ("MoveType", ColumnKind.UInt, 0),
        ("Flags", ColumnKind.UInt, 0),
        ("Velocity", ColumnKind.Float, 0),
        ("Comment", ColumnKind.Text, 255)
    ], Nullable: ["Velocity", "Comment"]);

    /// <summary>
    /// One waypoint_path_node row. (PathId, NodeId) is the key and nodes start at 1. Only the
    /// position, facing and delay are per-node; the rest lives on the path.
    /// </summary>
    public static readonly TableSchema WaypointPathNode = new("waypoint_path_node", ["PathId", "NodeId"], [
        ("PathId", ColumnKind.UInt, 0),
        ("NodeId", ColumnKind.UInt, 0),
        ("PositionX", ColumnKind.Float, 0),
        ("PositionY", ColumnKind.Float, 0),
        ("PositionZ", ColumnKind.Float, 0),
        ("Orientation", ColumnKind.Float, 0),
        ("Delay", ColumnKind.UInt, 0)
    ], Nullable: ["Orientation"]);

    /// <summary>
    /// One creature_formations row, keyed by the member. Same shape as AzerothCore's, but both
    /// GUIDs are 64-bit here.
    /// </summary>
    public static readonly TableSchema CreatureFormations = new("creature_formations", ["memberGUID"], [
        ("leaderGUID", ColumnKind.ULong, 0),
        ("memberGUID", ColumnKind.ULong, 0),
        ("dist", ColumnKind.Float, 0),
        ("angle", ColumnKind.Float, 0),
        ("groupAI", ColumnKind.UInt, 0),
        ("point_1", ColumnKind.UInt, 0),
        ("point_2", ColumnKind.UInt, 0)
    ]);
}
