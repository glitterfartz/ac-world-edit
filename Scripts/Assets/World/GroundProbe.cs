using System.Numerics;
using System.Runtime.CompilerServices;
using ACWorldEdit.Foundation;

namespace ACWorldEdit.Assets;

/// <summary>
/// Engine-independent vertical ground probes in viewer space (Y up). Uses decoded render triangles,
/// which approximate but are not the client's collision meshes; M2 doodads are not probed.
/// </summary>
public static class GroundProbe
{
    /// <summary>
    /// Highest step a snap may climb above the current height. Anything higher is treated as a
    /// ceiling or upper floor and ignored.
    /// </summary>
    public const float StepUp = 3;

    // Model-space bounds per decoded surface, computed on first probe and released with the
    // surface (ModelLibrary eviction). Lets a probe skip most of a large WMO such as Stormwind.
    static readonly ConditionalWeakTable<ModelSurface, StrongBox<(Vector3 Min, Vector3 Max)>> bounds = new();

    /// <summary>
    /// Return every height where a vertical line through viewPoint X/Z crosses a solid (non-liquid)
    /// surface of one placed WMO. The line is moved into model space so non-uniform placement
    /// rotations are handled exactly; heights come back in world view space.
    /// </summary>
    public static List<float> WmoHeights(WorldObject item, ModelData data, Vector3 viewPoint)
    {
        List<float> result = [];
        var world = WorldObjects.Transform(item);
        if (!Matrix4x4.Invert(world, out var inverse))
        {
            return result;
        }

        var top = new Vector3(viewPoint.X, 100000, viewPoint.Z);
        var localOrigin = Vector3.Transform(top, inverse);
        var localDirection = Vector3.TransformNormal(-Vector3.UnitY, inverse);
        var origin = new Point3(localOrigin.X, localOrigin.Y, localOrigin.Z);
        var direction = new Point3(localDirection.X, localDirection.Y, localDirection.Z);
        foreach (var surface in data.Surfaces)
        {
            if (surface.LiquidType != null)
            {
                continue;
            }

            var box = bounds.GetValue(surface, s => new(Bounds(s.Positions))).Value;
            if (!Crosses(box.Min, box.Max, localOrigin, localDirection))
            {
                continue;
            }

            var p = surface.Positions;
            var indices = surface.Indices;
            for (int i = 0; i + 2 < indices.Length; i += 3)
            {
                Vector3 a = p[indices[i]], b = p[indices[i + 1]], c = p[indices[i + 2]];
                double? t = WorldCoordinates.Hit(origin, direction, new(a.X, a.Y, a.Z), new(b.X, b.Y, b.Z), new(c.X, c.Y, c.Z));
                if (t.HasValue)
                {
                    var local = localOrigin + localDirection * (float)t.Value;
                    result.Add(Vector3.Transform(local, world).Y);
                }
            }
        }

        return result;
    }

    static (Vector3 Min, Vector3 Max) Bounds(Vector3[] positions)
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var point in positions)
        {
            min = Vector3.Min(min, point);
            max = Vector3.Max(max, point);
        }

        return (min, max);
    }

    /// <summary>
    /// Slab test for the probe line against a surface box, padded slightly so edge-touching
    /// triangles are never culled.
    /// </summary>
    static bool Crosses(Vector3 min, Vector3 max, Vector3 origin, Vector3 direction)
    {
        const float pad = 0.01f;
        float near = float.MinValue, far = float.MaxValue;
        for (int axis = 0; axis < 3; axis++)
        {
            float o = origin[axis], d = direction[axis], lo = min[axis] - pad, hi = max[axis] + pad;
            if (Math.Abs(d) < 1e-9f)
            {
                if (o < lo || o > hi)
                {
                    return false;
                }

                continue;
            }

            float a = (lo - o) / d, b = (hi - o) / d;
            near = Math.Max(near, Math.Min(a, b));
            far = Math.Min(far, Math.Max(a, b));
            if (far < near)
            {
                return false;
            }
        }

        return far >= 0;
    }

    /// <summary>
    /// True when a placement's MODF bounds (viewer space) contain the X/Z point. Placements without
    /// bounds are always candidates.
    /// </summary>
    public static bool Covers(WorldObject item, Vector3 viewPoint)
    {
        if (item.BoundsMinimum is not { } min || item.BoundsMaximum is not { } max)
        {
            return true;
        }

        // MODF bounds are axis-aligned but corners may arrive in either order after conversion.
        return viewPoint.X >= Math.Min(min.X, max.X) && viewPoint.X <= Math.Max(min.X, max.X) && viewPoint.Z >= Math.Min(min.Z, max.Z) && viewPoint.Z <= Math.Max(min.Z, max.Z);
    }

    /// <summary>
    /// Pick the highest surface at or below currentY + StepUp. Returns null when nothing qualifies
    /// so callers can fail closed instead of guessing.
    /// </summary>
    public static float? Choose(IEnumerable<float> heights, float currentY)
    {
        float? best = null;
        foreach (float height in heights)
        {
            if (float.IsFinite(height) && height <= currentY + StepUp && (best == null || height > best))
            {
                best = height;
            }
        }

        return best;
    }
}
