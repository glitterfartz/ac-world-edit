using Godot;

namespace ACWorldEdit;
/// <summary>A world-space pick ray from the camera through a screen point.</summary>
public readonly record struct PickRay(Vector3 Origin, Vector3 Direction, float Far)
{
    public static PickRay From(Camera3D camera, Vector2 screen) => new(camera.ProjectRayOrigin(screen), camera.ProjectRayNormal(screen), camera.Far);
}

/// <summary>Shared local-space slab picking. Ray parameters stay comparable across scaled instances.</summary>
public static class ScenePicking
{
    /// <summary>
    /// Nearest instance across model batches, by bounding box in each instance's local space so the
    /// ray parameter stays comparable between scaled instances. Batches whose model is not ready are
    /// skipped; the GUIDs of the ready ones are added to <paramref name="readyGuids"/> so callers can
    /// skip their fallback markers for them. No terrain or triangle occlusion.
    /// </summary>
    public static (ulong Guid, float Distance)? NearestInBatches(PickRay ray, IEnumerable<(ModelInstance Node, ulong[] Guids)> batches, ICollection<ulong>? readyGuids = null)
    {
        (ulong Guid, float Distance)? best = null;
        foreach (var (node, guids) in batches)
        {
            if (!node.ModelReady)
            {
                continue;
            }

            for (int i = 0; i < guids.Length; i++)
            {
                readyGuids?.Add(guids[i]);
                var inverse = node.Transforms![i].AffineInverse();
                float? hit = BoxHit(node.Bounds, inverse * ray.Origin, inverse.Basis * ray.Direction);
                if (hit.HasValue && hit.Value < ray.Far && (best == null || hit.Value < best.Value.Distance))
                {
                    best = (guids[i], hit.Value);
                }
            }
        }

        return best;
    }

    public static float? BoxHit(Aabb box, Vector3 origin, Vector3 direction)
    {
        float near = 0, far = float.MaxValue;
        for (int axis = 0; axis < 3; axis++)
        {
            if (Math.Abs(direction[axis]) < 0.000001f)
            {
                if (origin[axis] < box.Position[axis] || origin[axis] > box.End[axis])
                {
                    return null;
                }

                continue;
            }

            float a = (box.Position[axis] - origin[axis]) / direction[axis], b = (box.End[axis] - origin[axis]) / direction[axis];
            near = Math.Max(near, Math.Min(a, b));
            far = Math.Min(far, Math.Max(a, b));
            if (far < near)
            {
                return null;
            }
        }

        return near;
    }

    /// <summary>
    /// Ray/sphere entry parameter in the units of <paramref name="direction"/> (which need not be
    /// normalized), 0 when the origin is inside, null on a miss or a sphere behind the ray.
    /// </summary>
    public static float? SphereHit(Vector3 center, float radius, Vector3 origin, Vector3 direction)
    {
        var offset = origin - center;
        float a = direction.Dot(direction), b = 2 * offset.Dot(direction), c = offset.Dot(offset) - radius * radius;
        float discriminant = b * b - 4 * a * c;
        if (a < 1e-12f || discriminant < 0)
        {
            return null;
        }

        float root = Mathf.Sqrt(discriminant);
        float near = (-b - root) / (2 * a), far = (-b + root) / (2 * a);
        return far < 0 ? null : Math.Max(near, 0);
    }
}
