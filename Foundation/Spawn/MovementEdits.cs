using System.Globalization;

namespace ACWorldEdit.Foundation;

/// <summary>One waypoint node to create: server coordinates plus the columns of its dialect.</summary>
public sealed record WaypointSeed(double X, double Y, double Z, double? Facing, uint Delay, double Velocity = 0, int MoveType = 0, bool Smooth = false, int Action = 0, int ActionChance = 100);

/// <summary>
/// Row edits for waypoint paths and formations: link/unlink a spawn, new/delete path, node
/// insert/delete (renumbering is delete + insert, since a row edit cannot change a primary key),
/// and formation membership. Pure functions of the schema and the effective rows, so the overlays
/// and the tests share them. Points and nodes start at 1 on both servers.
/// </summary>
public static class MovementEdits
{
    public const uint IdleMovement = 0, WaypointMovement = 2, FirstPoint = 1;

    /// <summary>Follow flag and assist bits of groupAI, verified against the server's formation loader and wiki.</summary>
    public const uint GroupFollow = 512, GroupAssistLeader = 1, GroupAssistMember = 2;

    public static string AddonPathColumn(WorldDialect dialect) => dialect == WorldDialect.TrinityCore ? "PathId" : "path_id";

    public static TableSchema NodeTable(IWorldSchema schema) => schema.Tables.Find(schema.Dialect == WorldDialect.TrinityCore ? "waypoint_path_node" : "waypoint_data");

    public static TableSchema? HeaderTable(IWorldSchema schema) => schema.Dialect == WorldDialect.TrinityCore ? schema.Tables.Find("waypoint_path") : null;

    public static TableSchema FormationTable(IWorldSchema schema) => schema.Tables.Find("creature_formations");

    static string PointColumn(WorldDialect dialect) => dialect == WorldDialect.TrinityCore ? "NodeId" : "point";

    /// <summary>The assist mode of a formation, with dialect-aware labels (bit 2 differs between servers).</summary>
    public static string AssistName(WorldDialect dialect, uint assist) => (dialect, assist) switch
    {
        (_, 0) => "No assist",
        (_, GroupAssistLeader) => "Members assist the leader",
        (WorldDialect.TrinityCore, GroupAssistMember) => "Leader assists members",
        (_, GroupAssistMember) => "Members assist each other",
        _ => "Mutual assist"
    };

    public static uint GroupAI(uint assist, bool follow) => (assist & 3) | (follow ? GroupFollow : 0);

    /// <summary>
    /// Rewrite only the assist and follow bits of an existing groupAI value. AzerothCore also defines
    /// functional bits the card does not expose (evade together 0x4, respawn on evade 0x8, do not
    /// respawn the leader 0x10, acquire a new target on evade 0x20), so replacing the whole column
    /// would silently clear them.
    /// </summary>
    public static uint GroupAI(uint current, uint assist, bool follow) =>
        (current & ~(GroupAssistLeader | GroupAssistMember | GroupFollow)) | GroupAI(assist, follow);

    public static (uint Assist, bool Follow) SplitGroupAI(uint groupAI) => (groupAI & 3, (groupAI & GroupFollow) != 0);

    // ---- link / unlink -----------------------------------------------------

    /// <summary>
    /// Point a spawn at a path: MovementType 2 plus the addon path column, in one command. When the
    /// spawn has no addon row the baseline (its template's addon, or blank) decides whether a row is
    /// inserted: a row equal to the baseline is never created.
    /// </summary>
    public static RowEdit[] Link(IWorldSchema schema, Row spawn, Row? addon, Row baselineAddon, ulong guid, uint pathId)
    {
        var edits = new List<RowEdit>();
        var table = SpawnEdits.SpawnTable(schema, SpawnKind.Creature);
        if (spawn.U("MovementType") != WaypointMovement)
        {
            edits.Add(InteractionEdits.Update(table, spawn, spawn.With("MovementType", WaypointMovement)));
        }

        if (SetAddonPath(schema, addon, baselineAddon, guid, pathId) is { } edit)
        {
            edits.Add(edit);
        }

        return edits.ToArray();
    }

    /// <summary>
    /// Detach a spawn from its path (nodes are kept): MovementType back to idle with no wander
    /// radius (a standing creature cannot wander, the loader would reset it), and path 0, deleting
    /// an addon row that is back to its baseline.
    /// </summary>
    public static RowEdit[] Unlink(IWorldSchema schema, Row spawn, Row? addon, Row baselineAddon, ulong guid)
    {
        var edits = new List<RowEdit>();
        var table = SpawnEdits.SpawnTable(schema, SpawnKind.Creature);
        var after = spawn;
        if (after.U("MovementType") != IdleMovement)
        {
            after = after.With("MovementType", IdleMovement);
        }

        if (after.F("wander_distance") != 0)
        {
            after = after.With("wander_distance", 0f);
        }

        if (after != spawn)
        {
            edits.Add(InteractionEdits.Update(table, spawn, after));
        }

        if (SetAddonPath(schema, addon, baselineAddon, guid, 0) is { } edit)
        {
            edits.Add(edit);
        }

        return edits.ToArray();
    }

    static RowEdit? SetAddonPath(IWorldSchema schema, Row? addon, Row baseline, ulong guid, uint pathId)
    {
        var table = SpawnEdits.AddonTable(schema, SpawnKind.Creature);
        var after = (addon ?? baseline).With(AddonPathColumn(schema.Dialect), pathId);
        if (after == (addon ?? baseline))
        {
            return null;
        }

        if (addon == null)
        {
            return InteractionEdits.Insert(table, after);
        }

        return SpawnEdits.SameAddon(table, after, baseline) ? InteractionEdits.Delete(table, addon) : InteractionEdits.Update(table, addon, after);
    }

    // ---- new / delete path -------------------------------------------------

    /// <summary>A whole new path: the header row on TrinityCore plus one node row per seed, numbered from 1.</summary>
    public static RowEdit[] NewPath(IWorldSchema schema, uint pathId, IReadOnlyList<WaypointSeed> seeds, int moveType = 0, uint flags = 0, double? velocity = null, string comment = "")
    {
        if (seeds.Count == 0)
        {
            throw new InvalidDataException("A new path needs at least one point.");
        }

        var edits = new List<RowEdit>();
        if (HeaderTable(schema) is { } header)
        {
            edits.Add(InteractionEdits.Insert(header, HeaderRow(schema, pathId, moveType, flags, velocity, comment)));
        }

        for (int i = 0; i < seeds.Count; i++)
        {
            edits.Add(InteractionEdits.Insert(NodeTable(schema), NodeRow(schema, pathId, FirstPoint + (uint)i, seeds[i])));
        }

        return edits.ToArray();
    }

    /// <summary>Delete a path: its node rows, then its header row.</summary>
    public static RowEdit[] DeletePath(IWorldSchema schema, IReadOnlyList<Row> nodes, Row? header)
    {
        var table = NodeTable(schema);
        var edits = nodes.OrderBy(r => r.U(PointColumn(schema.Dialect))).Select(r => InteractionEdits.Delete(table, r)).ToList();
        if (header != null && HeaderTable(schema) is { } headerTable)
        {
            edits.Add(InteractionEdits.Delete(headerTable, header));
        }

        return edits.ToArray();
    }

    /// <summary>
    /// A path's node rows with the draft's net edits applied: the database rows keyed by point, with
    /// an edit's Before point removed and its After point stored. Net deletions therefore disappear
    /// instead of lingering as the raw database row, which matters because a renumber leaves its
    /// highest key net-deleted (delete 3, delete 2, insert 2(old 3)).
    /// </summary>
    public static IReadOnlyList<Row> EffectivePathRows(IWorldSchema schema, uint pathId, IEnumerable<Row> dbRows, IEnumerable<RowEdit> net)
    {
        var table = NodeTable(schema);
        string id = schema.Dialect == WorldDialect.TrinityCore ? "PathId" : "id";
        string point = PointColumn(schema.Dialect);
        var byPoint = new Dictionary<uint, Row>();
        foreach (var row in dbRows)
        {
            byPoint[row.U(point)] = row;
        }

        foreach (var edit in net)
        {
            if (edit.Table != table.Name)
            {
                continue;
            }

            if (edit.Before != null && edit.Before.U(id) == pathId)
            {
                byPoint.Remove(edit.Before.U(point));
            }

            if (edit.After != null && edit.After.U(id) == pathId)
            {
                byPoint[edit.After.U(point)] = edit.After;
            }
        }

        return byPoint.Values.OrderBy(r => r.U(point)).ToArray();
    }

    /// <summary>A path's header row with the draft's net edits applied (TrinityCore only).</summary>
    public static Row? EffectiveHeaderRow(IWorldSchema schema, uint pathId, Row? dbHeader, IEnumerable<RowEdit> net)
    {
        if (HeaderTable(schema) is not { } table)
        {
            return dbHeader;
        }

        string key = pathId.ToString(CultureInfo.InvariantCulture);
        var row = dbHeader;
        foreach (var edit in net)
        {
            if (edit.Table == table.Name && edit.Key == key)
            {
                row = edit.After;
            }
        }

        return row;
    }

    // ---- node insert / delete ----------------------------------------------

    /// <summary>
    /// Insert one node after <paramref name="afterPoint"/> (0 prepends): nodes above shift up by one
    /// (delete + insert each), then the new row. Existing gaps elsewhere are left alone.
    /// </summary>
    public static RowEdit[] InsertNode(IWorldSchema schema, uint pathId, IReadOnlyList<Row> ordered, uint afterPoint, WaypointSeed seed)
    {
        var table = NodeTable(schema);
        string point = PointColumn(schema.Dialect);
        var above = ordered.Where(r => r.U(point) > afterPoint).OrderBy(r => r.U(point)).ToArray();
        var edits = above.OrderByDescending(r => r.U(point)).Select(r => InteractionEdits.Delete(table, r)).ToList();
        foreach (var row in above)
        {
            edits.Add(InteractionEdits.Insert(table, row.With(point, row.U(point) + 1)));
        }

        edits.Add(InteractionEdits.Insert(table, NodeRow(schema, pathId, afterPoint + 1, seed)));
        return edits.ToArray();
    }

    /// <summary>Delete one node: it and every node above it go, and the nodes above come back shifted
    /// down by one. The last node cannot be deleted; delete the path instead.</summary>
    public static RowEdit[] DeleteNode(IWorldSchema schema, uint pathId, IReadOnlyList<Row> ordered, uint point)
    {
        if (ordered.Count < 2)
        {
            throw new InvalidDataException("A path needs at least one point; delete the path instead.");
        }

        var table = NodeTable(schema);
        string column = PointColumn(schema.Dialect);
        if (ordered.All(r => r.U(column) != point))
        {
            throw new InvalidDataException($"Path {pathId} has no point {point}.");
        }

        var edits = ordered.Where(r => r.U(column) >= point).OrderByDescending(r => r.U(column)).Select(r => InteractionEdits.Delete(table, r)).ToList();
        foreach (var row in ordered.Where(r => r.U(column) > point).OrderBy(r => r.U(column)))
        {
            edits.Add(InteractionEdits.Insert(table, row.With(column, row.U(column) - 1)));
        }

        return edits.ToArray();
    }

    /// <summary>One node row from a seed, validated the way the loader reads it.</summary>
    public static Row NodeRow(IWorldSchema schema, uint pathId, uint point, WaypointSeed seed)
    {
        if (point < FirstPoint)
        {
            throw new InvalidDataException("Waypoint points start at 1.");
        }

        if (!double.IsFinite(seed.X) || !double.IsFinite(seed.Y) || !double.IsFinite(seed.Z))
        {
            throw new InvalidDataException("Waypoint coordinates must be finite.");
        }

        if (seed.Facing is double facing && !double.IsFinite(facing))
        {
            throw new InvalidDataException("Facing must be a number.");
        }

        if (!double.IsFinite(seed.Velocity) || seed.Velocity < 0)
        {
            throw new InvalidDataException("Velocity must be zero or more.");
        }

        if (seed.MoveType is < 0 or > WaypointProps.MaxMoveType)
        {
            throw new InvalidDataException("Move type must be 0 (walk), 1 (run), 2 (land) or 3 (take off).");
        }

        if (seed.ActionChance is < short.MinValue or > short.MaxValue)
        {
            throw new InvalidDataException("Action chance must fit a 16-bit number.");
        }

        return schema.Dialect == WorldDialect.TrinityCore
            ? Row.Of(("PathId", pathId), ("NodeId", point),
                ("PositionX", (float)seed.X), ("PositionY", (float)seed.Y), ("PositionZ", (float)seed.Z),
                ("Orientation", seed.Facing is double f ? (object)(float)f : null), ("Delay", seed.Delay))
            : Row.Of(("id", pathId), ("point", point),
                ("position_x", (float)seed.X), ("position_y", (float)seed.Y), ("position_z", (float)seed.Z),
                ("orientation", seed.Facing is double o ? (object)(float)o : null), ("velocity", (float)seed.Velocity),
                ("delay", seed.Delay), ("move_type", seed.MoveType), ("smoothTransition", seed.Smooth ? 1 : 0),
                ("action", seed.Action), ("action_chance", seed.ActionChance), ("wpguid", 0u));
    }

    /// <summary>One TrinityCore waypoint_path row. A null velocity means each creature's own speed.</summary>
    public static Row HeaderRow(IWorldSchema schema, uint pathId, int moveType, uint flags, double? velocity, string comment)
    {
        if (schema.Dialect != WorldDialect.TrinityCore)
        {
            throw new InvalidDataException("Only TrinityCore keeps a path header row.");
        }

        if (moveType is < 0 or > WaypointProps.MaxMoveType)
        {
            throw new InvalidDataException("Move type must be 0 (walk), 1 (run), 2 (land) or 3 (take off).");
        }

        if (velocity is double v && (!double.IsFinite(v) || v < 0))
        {
            throw new InvalidDataException("Velocity must be zero or more.");
        }

        return Row.Of(("PathId", pathId), ("MoveType", moveType), ("Flags", flags),
            ("Velocity", velocity is double w ? (object)(float)w : null), ("Comment", comment));
    }

    // ---- formations --------------------------------------------------------

    /// <summary>One formation row: the member's offset (yards, degrees) from its leader. Mirrors the
    /// loader, which requires existing guids and reads the turn-around points as 16-bit.</summary>
    public static Row FormationRow(IWorldSchema schema, ulong member, ulong leader, float dist, float angle, uint groupAI, uint point1, uint point2)
    {
        if (member == 0 || leader == 0)
        {
            throw new InvalidDataException("A formation needs a leader and a member.");
        }

        if (!float.IsFinite(dist) || !float.IsFinite(angle))
        {
            throw new InvalidDataException("Formation distance and angle must be numbers.");
        }

        if (point1 > ushort.MaxValue || point2 > ushort.MaxValue)
        {
            throw new InvalidDataException("Formation turn-around points must fit a 16-bit number.");
        }

        return Row.Of(("leaderGUID", leader), ("memberGUID", member),
            ("dist", dist), ("angle", angle), ("groupAI", groupAI), ("point_1", point1), ("point_2", point2));
    }

    /// <summary>
    /// Offset of a member from its leader: yards plus the angle the server stores. Both servers add
    /// the leader's heading when placing the member, and TrinityCore's loader also adds π to the
    /// stored angle (<c>CreatureGroup::LeaderStartedMoving</c>), so the row holds
    /// <c>bearing − heading − 180°</c> on TrinityCore and <c>bearing − heading</c> on AzerothCore,
    /// where bearing is the world bearing from the leader to the member.
    /// </summary>
    public static (float Dist, float Angle) FormationOffset(WorldDialect dialect, WorldPose leader, WorldPose member)
    {
        float dx = (float)(member.X - leader.X), dy = (float)(member.Y - leader.Y);
        float dist = MathF.Sqrt(dx * dx + dy * dy);
        if (dist < 0.001f)
        {
            return (dist, 0);
        }

        double bearing = Math.Atan2(dy, dx) * 180 / Math.PI;
        double heading = leader.Heading * 180 / Math.PI;
        double turn = dialect == WorldDialect.TrinityCore ? 180 : 0;
        double angle = (bearing - heading - turn) % 360;
        return (dist, (float)(angle < 0 ? angle + 360 : angle));
    }

    /// <summary>
    /// The same offset for a leader with heading 0 and the TrinityCore convention (where the number
    /// is the member-to-leader bearing). Kept for tests and callers without a pose; editors pass the
    /// leader's pose and the source dialect instead.
    /// </summary>
    public static (float Dist, float Angle) FormationOffset(double leaderX, double leaderY, double memberX, double memberY) =>
        FormationOffset(WorldDialect.TrinityCore, new WorldPose(0, leaderX, leaderY, 0, 0), new WorldPose(0, memberX, memberY, 0, 0));

    /// <summary>Join or move a formation member: insert, update, or nothing when unchanged.</summary>
    public static RowEdit? JoinFormation(IWorldSchema schema, Row? existing, ulong member, ulong leader, float dist, float angle, uint groupAI, uint point1, uint point2)
    {
        var table = FormationTable(schema);
        var after = FormationRow(schema, member, leader, dist, angle, groupAI, point1, point2);
        if (after == existing)
        {
            return null;
        }

        return existing == null ? InteractionEdits.Insert(table, after) : InteractionEdits.Update(table, existing, after);
    }

    /// <summary>Make a creature lead its own formation (the self-row the loader expects). Pass the
    /// creature's existing row in any role: creature_formations is keyed by memberGUID, so a member
    /// row and the self-row share the same key and the member row is the update baseline.</summary>
    public static RowEdit? MakeLeader(IWorldSchema schema, Row? existing, ulong guid, uint groupAI)
    {
        var table = FormationTable(schema);
        var after = FormationRow(schema, guid, guid, 0, 0, groupAI, 0, 0);
        if (after == existing)
        {
            return null;
        }

        return existing == null ? InteractionEdits.Insert(table, after) : InteractionEdits.Update(table, existing, after);
    }

    /// <summary>Remove a member (or a leader's self-row) from its formation.</summary>
    public static RowEdit LeaveFormation(IWorldSchema schema, Row existing) => InteractionEdits.Delete(FormationTable(schema), existing);
}
