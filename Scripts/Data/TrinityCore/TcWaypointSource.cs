using ACWorldEdit.Foundation;

namespace ACWorldEdit.Data;

/// <summary>
/// Read-only waypoint_path / waypoint_path_node paths of the creatures in the streamed region. The
/// tables have no map column, so paths are found through the creatures: creature_addon.PathId for the
/// spawn, or the template's creature_template_addon.PathId when the spawn has no creature_addon row.
/// The path's MoveType, Velocity, Flags and Comment apply to every node, so each node carries the
/// first two; TrinityCore has no smooth transition or node actions, which read as off.
/// </summary>
public sealed class TcWaypointSource(WorldDatabase database)
{
    public async Task<WaypointRegionResult> ReadAsync(CreatureRegion region, SpawnQueryPoint? point, CancellationToken token)
    {
        await using var session = await database.OpenAsync(token).ConfigureAwait(false);
        session.Require("creature_addon.PathId", "creature_template_addon.PathId", "waypoint_path.MoveType", "waypoint_path.Velocity", "waypoint_path.Flags", "waypoint_path.Comment", "waypoint_path_node.NodeId", "waypoint_path_node.Delay", "waypoint_path_node.Orientation");
        var users = await ReadUsers(session, region, point, token).ConfigureAwait(false);
        var ids = users.Keys.Order().Take(AcWaypointSource.PathLimit).ToArray();
        bool truncated = users.Count > AcWaypointSource.PathLimit;
        if (ids.Length == 0)
        {
            return new([], false, database.SourceKey);
        }

        var moves = await ReadPaths(session, ids, token).ConfigureAwait(false);
        var nodes = await ReadNodes(session, ids, moves, token).ConfigureAwait(false);
        truncated |= nodes.Values.Sum(n => n.Count) >= AcWaypointSource.PointLimit;
        var paths = ids.Where(nodes.ContainsKey)
            .Select(id => moves.TryGetValue(id, out var move)
                ? new WaypointPath(id, region.Map, nodes[id].ToArray(), users[id].ToArray(), WorldDialect.TrinityCore, move.Flags, move.Comment)
                : new WaypointPath(id, region.Map, nodes[id].ToArray(), users[id].ToArray(), WorldDialect.TrinityCore))
            .ToArray();
        return new(paths, truncated, database.SourceKey);
    }

    static async Task<Dictionary<uint, List<WaypointUser>>> ReadUsers(WorldReadSession session, CreatureRegion region, SpawnQueryPoint? point, CancellationToken token)
    {
        await using var command = session.Command("");
        WorldReadSession.AddRegion(command, region, point, AcWaypointSource.CreatureLimit + 1);
        command.CommandText = $"""
            SELECT guid,entry,name,movement,path_id FROM (
                SELECT c.guid AS guid,c.id AS entry,COALESCE(t.name,'') AS name,c.MovementType AS movement,
                       CASE WHEN ca.guid IS NOT NULL THEN ca.PathId ELSE COALESCE(cta.PathId,0) END AS path_id,
                       {WorldReadSession.Distance("c")} AS dist
                FROM creature c
                JOIN creature_template t ON t.entry=c.id
                LEFT JOIN creature_addon ca ON ca.guid=c.guid
                LEFT JOIN creature_template_addon cta ON cta.entry=c.id
                WHERE c.map=@map AND c.position_x>@minX AND c.position_x<=@maxX AND c.position_y>@minY AND c.position_y<=@maxY
            ) spawns
            WHERE path_id<>0
            ORDER BY dist,guid LIMIT @limit
            """;
        var users = new Dictionary<uint, List<WaypointUser>>();
        int rows = 0;
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false) && ++rows <= AcWaypointSource.CreatureLimit)
        {
            uint path = Convert.ToUInt32(reader.GetValue(4));
            if (!users.TryGetValue(path, out var list))
            {
                users[path] = list = [];
            }

            list.Add(new(Convert.ToUInt64(reader.GetValue(0)), Convert.ToUInt32(reader.GetValue(1)), reader.GetString(2), Convert.ToInt32(reader.GetValue(3)) == 2));
        }

        return users;
    }

    /// <summary>
    /// The path rows: MoveType and Velocity (NULL reads as 0, the creature's own speed). A path
    /// without a waypoint_path row still has nodes; the server then uses its defaults.
    /// </summary>
    static async Task<Dictionary<uint, (int MoveType, double Velocity, int Flags, string Comment)>> ReadPaths(WorldReadSession session, uint[] ids, CancellationToken token)
    {
        await using var command = session.Command("");
        string list = WorldReadSession.InList(command, ids, "path");
        command.CommandText = $"SELECT PathId,MoveType,Velocity,Flags,Comment FROM waypoint_path WHERE PathId IN ({list})";
        var moves = new Dictionary<uint, (int, double, int, string)>();
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            moves[Convert.ToUInt32(reader.GetValue(0))] = (Convert.ToInt32(reader.GetValue(1)), reader.IsDBNull(2) ? 0 : reader.GetFloat(2),
                reader.IsDBNull(3) ? 0 : Convert.ToInt32(reader.GetValue(3)), reader.IsDBNull(4) ? "" : reader.GetString(4));
        }

        return moves;
    }

    static async Task<Dictionary<uint, List<WaypointNode>>> ReadNodes(WorldReadSession session, uint[] ids, Dictionary<uint, (int MoveType, double Velocity, int Flags, string Comment)> moves, CancellationToken token)
    {
        await using var command = session.Command("");
        string list = WorldReadSession.InList(command, ids, "path");
        command.CommandText = $"SELECT PathId,NodeId,PositionX,PositionY,PositionZ,Orientation,`Delay` FROM waypoint_path_node WHERE PathId IN ({list}) ORDER BY PathId,NodeId LIMIT {AcWaypointSource.PointLimit}";
        var nodes = new Dictionary<uint, List<WaypointNode>>();
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            uint id = Convert.ToUInt32(reader.GetValue(0));
            double x = reader.GetFloat(2), y = reader.GetFloat(3), z = reader.GetFloat(4);
            // The server would load such a node, but it cannot be drawn.
            if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(z))
            {
                continue;
            }

            if (!nodes.TryGetValue(id, out var rows))
            {
                nodes[id] = rows = [];
            }

            var (moveType, velocity, _, _) = moves.GetValueOrDefault(id);
            rows.Add(new(Convert.ToUInt32(reader.GetValue(1)), x, y, z, reader.IsDBNull(5) ? null : reader.GetFloat(5), velocity,
                Convert.ToUInt32(reader.GetValue(6)), false, moveType, 0, 0));
        }

        return nodes;
    }
}
