namespace ACWorldEdit.Foundation;

/// <summary>
/// Engine-independent double-precision point/vector. Database positions stay precise until the
/// Godot boundary casts them to floats; vector operations also support CPU picking checks.
/// </summary>
public readonly record struct Point3(double X, double Y, double Z)
{
    public static Point3 operator -(Point3 a, Point3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public double Dot(Point3 b) => X * b.X + Y * b.Y + Z * b.Z;
    public Point3 Cross(Point3 b) => new(Y * b.Z - Z * b.Y, Z * b.X - X * b.Z, X * b.Y - Y * b.X);
}

/// <summary>
/// Source or draft creature pose in server coordinates: X/Y horizontal, Z up, heading in
/// radians. Map identity is part of a pose and local movement cannot change it.
/// </summary>
public sealed record WorldPose(ushort Map, double X, double Y, double Z, double Heading)
{
    /// <summary>
    /// Reject non-finite coordinates or headings before they reach persistence or rendering.
    /// </summary>
    public void Validate()
    {
        if (!double.IsFinite(X) || !double.IsFinite(Y) || !double.IsFinite(Z) || !double.IsFinite(Heading))
        {
            throw new ArgumentException("Pose must be finite.");
        }
    }
}

/// <summary>
/// The server-to-view coordinate boundary: Godot X=-server Y, Y=server Z, Z=-server X. ADT tile
/// indices are centered around tile 32. Algebraic/source checks do not establish complete
/// in-client landmark parity.
/// </summary>
public static class WorldCoordinates
{
    public const double TileSize = 1600.0 / 3.0;
    /// <summary>
    /// Convert a Z-up server point to the Y-up viewer basis without losing double precision.
    /// </summary>
    public static Point3 ToView(Point3 server) => new(-server.Y, server.Z, -server.X);
    /// <summary>
    /// Invert the viewer basis when saving a local movement preview into server-coordinate draft
    /// values.
    /// </summary>
    public static Point3 ToServer(Point3 view) => new(-view.Z, -view.X, view.Y);
    /// <summary>
    /// Find the ADT tile containing a server position using floor rather than truncation, including
    /// negative coordinates.
    /// </summary>
    public static (int X, int Y) Tile(Point3 server) => ((int)Math.Floor(32 - server.Y / TileSize), (int)Math.Floor(32 - server.X / TileSize));
    /// <summary>
    /// Two-sided Moller-Trumbore triangle intersection. Returns the nonnegative ray parameter, not
    /// necessarily world distance unless direction is normalized. Callers must omit hole triangles.
    /// </summary>
    public static double? Hit(Point3 origin, Point3 direction, Point3 a, Point3 b, Point3 c)
    {
        var edge1 = b - a;
        var edge2 = c - a;
        var p = direction.Cross(edge2);
        double determinant = edge1.Dot(p);
        if (Math.Abs(determinant) < 1e-12)
        {
            return null;
        }

        double inverse = 1 / determinant;
        var t = origin - a;
        double u = t.Dot(p) * inverse;
        if (u < -1e-10 || u > 1 + 1e-10)
        {
            return null;
        }

        var q = t.Cross(edge1);
        double v = direction.Dot(q) * inverse;
        if (v < -1e-10 || u + v > 1 + 1e-10)
        {
            return null;
        }

        double distance = edge2.Dot(q) * inverse;
        return distance >= 0 ? distance : null;
    }
}
