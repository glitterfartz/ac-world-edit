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
}
