using System.Numerics;

namespace ACWorldEdit.Assets;

/// <summary>
/// Slab and sphere ray tests on plain floats, shared by scene picking. Keeping these here makes the
/// geometry testable without Godot; the viewer's Aabb/Vector3 facade only converts.
/// </summary>
public static class RayBox
{
    /// <summary>Ray parameter where it enters the box, or null on a miss or a box behind the ray.</summary>
    public static float? BoxHit(Vector3 min, Vector3 max, Vector3 origin, Vector3 direction)
    {
        float near = 0, far = float.MaxValue;
        for (int axis = 0; axis < 3; axis++)
        {
            if (Math.Abs(direction[axis]) < 0.000001f)
            {
                if (origin[axis] < min[axis] || origin[axis] > max[axis])
                {
                    return null;
                }

                continue;
            }

            float a = (min[axis] - origin[axis]) / direction[axis], b = (max[axis] - origin[axis]) / direction[axis];
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
        float a = Vector3.Dot(direction, direction), b = 2 * Vector3.Dot(offset, direction), c = Vector3.Dot(offset, offset) - radius * radius;
        float discriminant = b * b - 4 * a * c;
        if (a < 1e-12f || discriminant < 0)
        {
            return null;
        }

        float root = MathF.Sqrt(discriminant);
        float near = (-b - root) / (2 * a), far = (-b + root) / (2 * a);
        return far < 0 ? null : Math.Max(near, 0);
    }
}
