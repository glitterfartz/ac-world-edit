namespace ACWorldEdit.Foundation;

/// <summary>
/// Where a GameObject spawn stands: its server pose plus the stored rotation quaternion
/// (gameobject.rotation0..3, Z-up server space). The quaternion is kept as stored, so an all-zero
/// rotation stays zero until the object is turned and a revert writes back exactly what was there.
/// </summary>
public sealed record GameObjectPlacement(WorldPose Pose, double RotX, double RotY, double RotZ, double RotW)
{
    /// <summary>A stored quaternion shorter than this is "no rotation": the heading decides the facing.</summary>
    const double EmptyLengthSquared = 0.00001;

    public bool HasRotation => RotX * RotX + RotY * RotY + RotZ * RotZ + RotW * RotW >= EmptyLengthSquared;

    public static GameObjectPlacement From(GameObjectSpawn spawn)
    {
        var q = spawn.StoredRotation ?? spawn.Rotation;
        return new(spawn.Pose, q.X, q.Y, q.Z, q.W);
    }

    public void Validate()
    {
        Pose.Validate();
        if (!double.IsFinite(RotX) || !double.IsFinite(RotY) || !double.IsFinite(RotZ) || !double.IsFinite(RotW))
        {
            throw new ArgumentException("Rotation must be finite.");
        }
    }

    /// <summary>
    /// The unit quaternion the object is drawn with, as (x, y, z, w): the stored rotation
    /// normalized, or a pure turn about Z by the heading when none is stored.
    /// </summary>
    public (double X, double Y, double Z, double W) FacingQuaternion()
    {
        if (!HasRotation)
        {
            return (0, 0, Math.Sin(Pose.Heading / 2), Math.Cos(Pose.Heading / 2));
        }

        double length = Math.Sqrt(RotX * RotX + RotY * RotY + RotZ * RotZ + RotW * RotW);
        return (RotX / length, RotY / length, RotZ / length, RotW / length);
    }

    /// <summary>
    /// Turn to a new heading: the rotation is turned about the vertical axis by the heading change,
    /// so pitch and roll survive. An object without a stored rotation gets a plain turn about Z.
    /// </summary>
    public GameObjectPlacement WithHeading(double heading)
    {
        double normalized = WorldCoordinates.NormalizeHeading(heading);
        double delta = normalized - Pose.Heading;
        double s = Math.Sin(delta / 2), c = Math.Cos(delta / 2);
        var (bx, by, bz, bw) = HasRotation ? (RotX, RotY, RotZ, RotW) : (0, 0, Math.Sin(Pose.Heading / 2), Math.Cos(Pose.Heading / 2));
        // Hamilton product (0, 0, s, c) * (bx, by, bz, bw).
        double x = c * bx - s * by;
        double y = c * by + s * bx;
        double z = c * bz + s * bw;
        double w = c * bw - s * bz;
        return new(Pose with { Heading = normalized }, x, y, z, w);
    }
}

/// <summary>
/// One local history command isolated by database identity and GUID, for GameObject spawns. Before
/// and After are placements; commands never own or write database rows.
/// </summary>
public sealed record GameObjectMove(string Source, ulong Guid, GameObjectPlacement Before, GameObjectPlacement After);

/// <summary>
/// Placement draft for GameObject spawns, independent of scene nodes and the database and persisted
/// by DraftHistory. It follows <see cref="CreatureDraft"/>: the first baseline is remembered so a
/// stale draft never silently wins, and net moves drive the export.
/// </summary>
public sealed class GameObjectDraft : IDisposable
{
    readonly DraftHistory<GameObjectMove> history;
    public bool CanUndo => history.CanUndo;
    public bool CanRedo => history.CanRedo;

    public GameObjectDraft(string path) => history = new(path, "gameobject", Check);

    static GameObjectMove Check(GameObjectMove move)
    {
        move.Before.Validate();
        move.After.Validate();
        if (move.Before.Pose.Map != move.After.Pose.Map)
        {
            throw new InvalidDataException("Cross-map GameObject draft.");
        }

        return move;
    }

    IEnumerable<GameObjectMove> Applied(string source, ulong guid, ushort map) => history.State.AppliedEntries().Where(m => m.Source == source && m.Guid == guid && m.Before.Pose.Map == map);

    /// <summary>The last applied placement when its first baseline still equals the source, else the source.</summary>
    public GameObjectPlacement Effective(string source, ulong guid, GameObjectPlacement original)
    {
        var moves = Applied(source, guid, original.Pose.Map).ToArray();
        return moves.Length > 0 && moves[0].Before == original ? moves[^1].After : original;
    }

    /// <summary>True when the earliest applied command no longer starts from the current database placement.</summary>
    public bool Conflict(string source, ulong guid, GameObjectPlacement original)
    {
        var first = Applied(source, guid, original.Pose.Map).FirstOrDefault();
        return first != null && first.Before != original;
    }

    /// <summary>Validate a same-map change, reject a stale baseline and save one command. No-ops add nothing.</summary>
    public void Move(string source, ulong guid, GameObjectPlacement original, GameObjectPlacement after)
    {
        after.Validate();
        if (after.Pose.Map != original.Pose.Map)
        {
            throw new InvalidDataException("Cross-map move.");
        }

        if (Conflict(source, guid, original))
        {
            throw new InvalidDataException("Source placement changed; draft requires review.");
        }

        var before = Effective(source, guid, original);
        if (after != before)
        {
            history.Append(new GameObjectMove(source, guid, before, after));
        }
    }

    /// <summary>Net change per (source, GUID, map): the first baseline and the last result; returns to baseline are omitted.</summary>
    public IReadOnlyList<GameObjectMove> NetMoves() => history.State.AppliedEntries()
        .GroupBy(m => (m.Source, m.Guid, m.Before.Pose.Map))
        .Select(g => new GameObjectMove(g.Key.Source, g.Key.Guid, g.First().Before, g.Last().After))
        .Where(m => m.Before != m.After)
        .ToList();

    public void Clear() => history.Clear();

    public void Revert(GameObjectMove net) => history.Append(new GameObjectMove(net.Source, net.Guid, net.After, net.Before));

    public void Undo() => history.Undo();

    public void Redo() => history.Redo();

    public void Dispose() => history.Dispose();
}
