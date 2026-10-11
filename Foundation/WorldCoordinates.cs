namespace ACWorldEdit.Foundation;

public readonly record struct Point3(double X, double Y, double Z)
{
    public static Point3 operator +(Point3 a, Point3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Point3 operator -(Point3 a, Point3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Point3 operator *(Point3 a, double b) => new(a.X * b, a.Y * b, a.Z * b);
    public double Dot(Point3 b) => X * b.X + Y * b.Y + Z * b.Z;
    public Point3 Cross(Point3 b) => new(Y * b.Z - Z * b.Y, Z * b.X - X * b.Z, X * b.Y - Y * b.X);
}
public sealed record WorldPose(ushort Map, double X, double Y, double Z, double Heading)
{
    public void Validate()
    {
        if (!double.IsFinite(X) || !double.IsFinite(Y) || !double.IsFinite(Z) || !double.IsFinite(Heading))
            throw new ArgumentException("Pose must be finite.");
    }
}

/// <summary>Provisional stock-client basis. Algebraic checks do not establish in-client landmark parity.</summary>
public static class WorldCoordinates
{
    public const double TileSize = 1600.0 / 3.0;
    public static Point3 ToView(Point3 server) => new(-server.Y, server.Z, -server.X);
    public static Point3 ToServer(Point3 view) => new(-view.Z, -view.X, view.Y);
    public static (int X, int Y) Tile(Point3 server) => ((int)Math.Floor(32 - server.Y / TileSize), (int)Math.Floor(32 - server.X / TileSize));
    public static Point3 Heading(double radians) => new(-Math.Sin(radians), 0, -Math.Cos(radians));

    /// <summary>Two-sided Moller-Trumbore hit, returning ray parameter; holes must be omitted from triangles.</summary>
    public static double? Hit(Point3 origin, Point3 direction, Point3 a, Point3 b, Point3 c)
    {
        var edge1 = b - a; var edge2 = c - a; var p = direction.Cross(edge2);
        double determinant = edge1.Dot(p);
        if (Math.Abs(determinant) < 1e-12) return null;
        double inverse = 1 / determinant; var t = origin - a;
        double u = t.Dot(p) * inverse; if (u < -1e-10 || u > 1 + 1e-10) return null;
        var q = t.Cross(edge1); double v = direction.Dot(q) * inverse;
        if (v < -1e-10 || u + v > 1 + 1e-10) return null;
        double distance = edge2.Dot(q) * inverse;
        return distance >= 0 ? distance : null;
    }
}
