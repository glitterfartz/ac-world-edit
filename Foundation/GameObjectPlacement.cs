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
