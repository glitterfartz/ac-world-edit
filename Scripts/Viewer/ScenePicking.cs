using Godot;

namespace ACWorldEdit;
/// <summary>Shared local-space slab picking. Ray parameters stay comparable across scaled instances.</summary>
public static class ScenePicking
{
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
