namespace ACWorldEdit.Foundation;

// The waypoint and formation tables of AcTables.
public sealed partial class AcTables
{
    /// <summary>
    /// One waypoint_data row. (id, point) is the key and points start at 1. orientation is NULL
    /// when the point has no facing; wpguid is a runtime marker that reads 0.
    /// </summary>
    public static readonly TableSchema WaypointData = new("waypoint_data", ["id", "point"], [
        ("id", ColumnKind.UInt, 0),
        ("point", ColumnKind.UInt, 0),
        ("position_x", ColumnKind.Float, 0),
        ("position_y", ColumnKind.Float, 0),
        ("position_z", ColumnKind.Float, 0),
        ("orientation", ColumnKind.Float, 0),
        ("velocity", ColumnKind.Float, 0),
        ("delay", ColumnKind.UInt, 0),
        ("move_type", ColumnKind.Int, 0),
        ("smoothTransition", ColumnKind.Int, 0),
        ("action", ColumnKind.Int, 0),
        ("action_chance", ColumnKind.Int, 0),
        ("wpguid", ColumnKind.UInt, 0)
    ], Nullable: ["orientation"]);

    /// <summary>
    /// One creature_formations row, keyed by the member. The leader carries a self-row; dist/angle
    /// are the polar follow offset in yards and degrees, point_1/point_2 are leader path nodes for
    /// return-trip side switching. Same shape on both servers.
    /// </summary>
    public static readonly TableSchema CreatureFormations = new("creature_formations", ["memberGUID"], [
        ("leaderGUID", ColumnKind.UInt, 0),
        ("memberGUID", ColumnKind.UInt, 0),
        ("dist", ColumnKind.Float, 0),
        ("angle", ColumnKind.Float, 0),
        ("groupAI", ColumnKind.UInt, 0),
        ("point_1", ColumnKind.UInt, 0),
        ("point_2", ColumnKind.UInt, 0)
    ]);
}
