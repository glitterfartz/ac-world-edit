using Godot;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// Fill and outline meshes for every AreaTrigger shape, in the volume node's local space (the marker
/// node is at the trigger center, turned by its orientation). Spheres, boxes, bounded planes and
/// cylinders reuse unit meshes scaled by <see cref="Volume.Local"/>, so their rendered corners can be
/// checked against the server test; polygons and disks are built per trigger with an identity
/// transform. A bound the server leaves open (a client polygon has no floor, some no ceiling) is drawn
/// <see cref="OpenDepth"/> below or <see cref="OpenHeight"/> above the center.
/// </summary>
public static class AreaTriggerShapes
{
    public const float OpenDepth = 5, OpenHeight = 20;
    const int Segments = 48;

    // Unit shapes: the box spans ±0.5 on every axis, the sphere and cylinder have radius 0.5.
    static readonly BoxMesh UnitBox = new() { Size = Vector3.One };
    static readonly SphereMesh UnitSphere = new() { Radius = 0.5f, Height = 1, RadialSegments = 32, Rings = 16 };
    static readonly CylinderMesh UnitCylinder = new() { TopRadius = 0.5f, BottomRadius = 0.5f, Height = 1, RadialSegments = 32, Rings = 1 };

    /// <summary>A built volume: its fill, its transform under the marker node, its edges and whether picking treats it as a sphere.</summary>
    public sealed record Volume(Mesh Fill, Transform3D Local, Mesh Outline, bool Sphere);

    /// <summary>The marker node's yaw: polygon corners are already turned to world axes.</summary>
    public static float Yaw(AreaTriggerRecord trigger) => trigger.Volume == AreaTriggerShape.Polygon ? 0 : (float)trigger.Orientation;

    /// <summary>
    /// The finite vertical span drawn for polygons, cylinders and disks (offsets from the center). An
    /// open bound is drawn around the polygon's ground (its corners' height), which for polygons placed
    /// at the map origin is far from the trigger's own Z.
    /// </summary>
    public static (float Bottom, float Top) Span(AreaTriggerRecord trigger)
    {
        float ground = (float)(trigger.Ground ?? 0);
        float bottom = double.IsNegativeInfinity(trigger.Bottom) ? Math.Min(ground, (float)Math.Min(trigger.Top, 1e6)) - OpenDepth : (float)trigger.Bottom;
        float top = double.IsPositiveInfinity(trigger.Top) ? Math.Max(ground, bottom) + OpenHeight : (float)trigger.Top;
        return (bottom, Math.Max(top, bottom + 0.2f));
    }

    /// <summary>The height above the center where the label sits.</summary>
    public static float TopOf(AreaTriggerRecord trigger) => trigger.Volume switch
    {
        AreaTriggerShape.Sphere => (float)trigger.Radius,
        AreaTriggerShape.Box or AreaTriggerShape.BoundedPlane => (float)trigger.Height / 2,
        _ => Span(trigger).Top
    };

    public static Volume Build(AreaTriggerRecord t)
    {
        // Degenerate rows (zero size) still get a visible sliver so they can be found and selected.
        static float Size(double value) => (float)Math.Max(value, 0.2);
        static Transform3D Scaled(Vector3 scale, float y = 0) => new(Basis.FromScale(scale), new Vector3(0, y, 0));
        switch (t.Volume)
        {
            case AreaTriggerShape.Sphere:
                return new(UnitSphere, Scaled(Vector3.One * Size(t.Radius * 2)), SphereOutline(), true);
            case AreaTriggerShape.BoundedPlane:
                // The plane spans the width across the facing (view X after the yaw) and the height; it has no depth.
                return new(UnitBox, Scaled(new(Size(t.Width), Size(t.Height), 0.2f)), BoxOutline(), false);
            case AreaTriggerShape.Cylinder:
            {
                var (bottom, top) = Span(t);
                return new(UnitCylinder, Scaled(new(Size(t.Radius * 2), top - bottom, Size(t.Radius * 2)), (bottom + top) / 2), CylinderOutline(), false);
            }
            case AreaTriggerShape.Disk:
            {
                var (bottom, top) = Span(t);
                return new(Ring((float)t.InnerRadius, Size(t.Radius), bottom, top), Transform3D.Identity, RingOutline((float)t.InnerRadius, Size(t.Radius), bottom, top), false);
            }
            case AreaTriggerShape.Polygon:
            {
                var (bottom, top) = Span(t);
                // Server offset (dx, dy) is view (-dy, -dx) on the ground plane.
                var corners = t.Vertices.Select(v => new Vector2((float)-v.Y, (float)-v.X)).ToArray();
                return new(Prism(corners, bottom, top), Transform3D.Identity, PrismOutline(corners, bottom, top), false);
            }
            default:
                // The server measures length along the facing, which is the node's local -Z after the yaw.
                return new(UnitBox, Scaled(new(Size(t.Width), Size(t.Height), Size(t.Length))), BoxOutline(), false);
        }
    }

    static Vector3 At(Vector2 ground, float y) => new(ground.X, y, ground.Y);

    /// <summary>Walls between consecutive corners plus caps triangulated by Geometry2D (concave corners allowed).</summary>
    static ArrayMesh Prism(Vector2[] corners, float bottom, float top)
    {
        var surface = new SurfaceTool();
        surface.Begin(Mesh.PrimitiveType.Triangles);
        for (int i = 0; i < corners.Length; i++)
        {
            var (a, b) = (corners[i], corners[(i + 1) % corners.Length]);
            foreach (var point in new[] { At(a, bottom), At(b, bottom), At(b, top), At(a, bottom), At(b, top), At(a, top) })
            {
                surface.AddVertex(point);
            }
        }

        int[] caps = Geometry2D.TriangulatePolygon(corners);
        foreach (float y in new[] { bottom, top })
        {
            foreach (int index in caps)
            {
                surface.AddVertex(At(corners[index], y));
            }
        }

        return surface.Commit();
    }

    static Vector2 OnCircle(int step, float radius) => new(Mathf.Cos(step * Mathf.Tau / Segments) * radius, Mathf.Sin(step * Mathf.Tau / Segments) * radius);

    /// <summary>An annulus between the radii, extruded from bottom to top.</summary>
    static ArrayMesh Ring(float inner, float outer, float bottom, float top)
    {
        var surface = new SurfaceTool();
        surface.Begin(Mesh.PrimitiveType.Triangles);
        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            foreach (var point in new[] { a, b, c, a, c, d })
            {
                surface.AddVertex(point);
            }
        }

        for (int i = 0; i < Segments; i++)
        {
            var (innerA, innerB, outerA, outerB) = (OnCircle(i, inner), OnCircle(i + 1, inner), OnCircle(i, outer), OnCircle(i + 1, outer));
            Quad(At(outerA, bottom), At(outerB, bottom), At(outerB, top), At(outerA, top));
            Quad(At(innerA, bottom), At(innerB, bottom), At(innerB, top), At(innerA, top));
            Quad(At(innerA, top), At(innerB, top), At(outerB, top), At(outerA, top));
            Quad(At(innerA, bottom), At(innerB, bottom), At(outerB, bottom), At(outerA, bottom));
        }

        return surface.Commit();
    }

    static ImmediateMesh Lines(Action<Action<Vector3, Vector3>> draw)
    {
        var mesh = new ImmediateMesh();
        mesh.SurfaceBegin(Mesh.PrimitiveType.Lines);
        draw((from, to) =>
        {
            mesh.SurfaceAddVertex(from);
            mesh.SurfaceAddVertex(to);
        });
        mesh.SurfaceEnd();
        return mesh;
    }

    static void Circle(Action<Vector3, Vector3> line, float radius, float y)
    {
        for (int i = 0; i < Segments; i++)
        {
            line(At(OnCircle(i, radius), y), At(OnCircle(i + 1, radius), y));
        }
    }

    /// <summary>Three great circles of the unit sphere.</summary>
    static ImmediateMesh SphereOutline() => Lines(line =>
    {
        for (int plane = 0; plane < 3; plane++)
        {
            for (int i = 0; i < Segments; i++)
            {
                var (a, b) = (OnCircle(i, 0.5f), OnCircle(i + 1, 0.5f));
                Vector3 Lift(Vector2 p) => plane == 0 ? new(p.X, p.Y, 0) : plane == 1 ? new(p.X, 0, p.Y) : new(0, p.X, p.Y);
                line(Lift(a), Lift(b));
            }
        }
    });

    /// <summary>The 12 edges of the unit box.</summary>
    static ImmediateMesh BoxOutline() => Lines(line =>
    {
        for (int axis = 0; axis < 3; axis++)
        {
            for (int corner = 0; corner < 4; corner++)
            {
                float u = (corner & 1) == 0 ? -0.5f : 0.5f, v = (corner & 2) == 0 ? -0.5f : 0.5f;
                var from = axis == 0 ? new Vector3(-0.5f, u, v) : axis == 1 ? new Vector3(u, -0.5f, v) : new Vector3(u, v, -0.5f);
                var to = from;
                to[axis] = 0.5f;
                line(from, to);
            }
        }
    });

    /// <summary>The unit cylinder's end circles and four sides.</summary>
    static ImmediateMesh CylinderOutline() => Lines(line =>
    {
        Circle(line, 0.5f, -0.5f);
        Circle(line, 0.5f, 0.5f);
        for (int i = 0; i < Segments; i += Segments / 4)
        {
            line(At(OnCircle(i, 0.5f), -0.5f), At(OnCircle(i, 0.5f), 0.5f));
        }
    });

    static ImmediateMesh RingOutline(float inner, float outer, float bottom, float top) => Lines(line =>
    {
        foreach (float radius in new[] { inner, outer })
        {
            Circle(line, radius, bottom);
            Circle(line, radius, top);
        }
    });

    static ImmediateMesh PrismOutline(Vector2[] corners, float bottom, float top) => Lines(line =>
    {
        for (int i = 0; i < corners.Length; i++)
        {
            var (a, b) = (corners[i], corners[(i + 1) % corners.Length]);
            line(At(a, bottom), At(b, bottom));
            line(At(a, top), At(b, top));
            line(At(a, bottom), At(a, top));
        }
    });
}
