using ACWorldEdit.Foundation;

namespace ACWorldEdit.Data;

/// <summary>
/// Read-only waypoint_data paths of the creatures in the streamed region. waypoint_data has no map
/// column, so paths are found through the creatures: creature_addon.path_id for the spawn, or the
/// template's creature_template_addon.path_id when the spawn has no creature_addon row (the server
/// never consults the template addon once a spawn row exists, even if its path_id is 0).
/// </summary>
public sealed class AcWaypointSource(WorldDatabase database)
{
    public AcWaypointSource(DatabaseSettings settings) : this(new WorldDatabase(settings))
    {
    }

    public const int CreatureLimit = 300, PathLimit = 200, PointLimit = 20000;

    public async Task<WaypointRegionResult> ReadAsync(CreatureRegion region, SpawnQueryPoint? point, CancellationToken token)
    {
        await using var session = await database.OpenAsync(token).ConfigureAwait(false);
        session.Require("creature_addon.path_id", "creature_template_addon.path_id", "waypoint_data.point", "waypoint_data.delay", "waypoint_data.move_type", "waypoint_data.orientation", "waypoint_data.velocity", "waypoint_data.smoothTransition", "waypoint_data.action", "waypoint_data.action_chance");
        var users = await ReadUsers(session, region, point, token).ConfigureAwait(false);
        var ids = users.Keys.Order().Take(PathLimit).ToArray();
        bool truncated = users.Count > PathLimit;
        if (ids.Length == 0)
        {
            return new([], false, database.SourceKey);
        }

        var nodes = await ReadNodes(session, ids, token).ConfigureAwait(false);
        truncated |= nodes.Values.Sum(n => n.Count) >= PointLimit;
        var paths = ids.Where(nodes.ContainsKey)
            .Select(id => new WaypointPath(id, region.Map, nodes[id].ToArray(), users[id].ToArray()))
            .ToArray();
        return new(paths, truncated, database.SourceKey);
    }

    async Task<Dictionary<uint, List<WaypointUser>>> ReadUsers(WorldReadSession session, CreatureRegion region, SpawnQueryPoint? point, CancellationToken token)
    {
        string id = session.Has("creature.id1") ? "c.id1" : "c.id";
        await using var command = session.Command("");
        WorldReadSession.AddRegion(command, region, point, CreatureLimit + 1);
        command.CommandText = $"""
            SELECT guid,entry,name,movement,path_id FROM (
                SELECT c.guid AS guid,{id} AS entry,COALESCE(t.name,'') AS name,c.MovementType AS movement,
                       CASE WHEN ca.guid IS NOT NULL THEN ca.path_id ELSE COALESCE(cta.path_id,0) END AS path_id,
                       {WorldReadSession.Distance("c")} AS dist
                FROM creature c
                JOIN creature_template t ON t.entry={id}
                LEFT JOIN creature_addon ca ON ca.guid=c.guid
                LEFT JOIN creature_template_addon cta ON cta.entry={id}
                WHERE c.map=@map AND c.position_x>@minX AND c.position_x<=@maxX AND c.position_y>@minY AND c.position_y<=@maxY
            ) spawns
            WHERE path_id<>0
            ORDER BY dist,guid LIMIT @limit
            """;
        var users = new Dictionary<uint, List<WaypointUser>>();
        int rows = 0;
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false) && ++rows <= CreatureLimit)
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

    static async Task<Dictionary<uint, List<WaypointNode>>> ReadNodes(WorldReadSession session, uint[] ids, CancellationToken token)
    {
        await using var command = session.Command("");
        string list = WorldReadSession.InList(command, ids, "path");
        command.CommandText = $"SELECT id,`point`,position_x,position_y,position_z,`delay`,move_type,orientation,velocity,smoothTransition,action,action_chance FROM waypoint_data WHERE id IN ({list}) ORDER BY id,`point` LIMIT {PointLimit}";
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

            rows.Add(new(Convert.ToUInt32(reader.GetValue(1)), x, y, z,
                reader.IsDBNull(7) ? null : reader.GetFloat(7), reader.GetFloat(8), Convert.ToUInt32(reader.GetValue(5)),
                Convert.ToInt32(reader.GetValue(9)) != 0, Convert.ToInt32(reader.GetValue(6)), Convert.ToInt32(reader.GetValue(10)), Convert.ToInt32(reader.GetValue(11))));
        }

        return nodes;
    }
}
