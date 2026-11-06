using ACWorldEdit.Foundation;

namespace ACWorldEdit.Data;

/// <summary>
/// One waypoint path as editable rows: its node rows plus the TrinityCore header row, loaded as the
/// baseline of row-edit commands. A missing table or column reads nothing, so a trimmed schema
/// reports what it can. Also converts between the region read's records and the rows commands use.
/// </summary>
internal static class DbWaypointRows
{
    public static async Task<RowSet> ReadPathAsync(WorldDatabase database, ITableCatalog tables, WorldDialect dialect, uint pathId, CancellationToken token)
    {
        var rows = new RowSet();
        bool tc = dialect == WorldDialect.TrinityCore;
        var nodes = tables.Find(tc ? "waypoint_path_node" : "waypoint_data");
        string point = tc ? "NodeId" : "point", id = tc ? "PathId" : "id";
        await using var session = await database.OpenAsync(token).ConfigureAwait(false);
        if (session.Has($"{nodes.SqlName}.{id}"))
        {
            await AcInteractionSource.Load(session, rows, nodes, $"`{id}`=@k0", [pathId], token).ConfigureAwait(false);
        }

        if (tc && session.Has("waypoint_path.PathId"))
        {
            await AcInteractionSource.Load(session, rows, tables.Find("waypoint_path"), "`PathId`=@k0", [pathId], token).ConfigureAwait(false);
        }

        return rows;
    }

    /// <summary>The row key of one node (both dialects join their two key columns with a pipe).</summary>
    public static string NodeKey(WorldDialect dialect, uint pathId, uint point) =>
        $"{pathId.ToString(System.Globalization.CultureInfo.InvariantCulture)}|{point.ToString(System.Globalization.CultureInfo.InvariantCulture)}";

    /// <summary>A region record as the row commands use (wpguid reads 0; a null facing stays null).</summary>
    public static Row RowFromNode(WorldDialect dialect, uint pathId, WaypointNode node) => MovementEdits.NodeRow(
        WorldSchemas.For(dialect), pathId, node.Point,
        new WaypointSeed(node.X, node.Y, node.Z, node.Orientation, node.Delay, node.Velocity, node.MoveType, node.Smooth, node.Action, node.ActionChance));
}
