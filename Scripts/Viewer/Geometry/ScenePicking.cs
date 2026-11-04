using Godot;
using ACWorldEdit.Assets;

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

    public static float? BoxHit(Aabb box, Vector3 origin, Vector3 direction) =>
        RayBox.BoxHit(Plain(box.Position), Plain(box.End), Plain(origin), Plain(direction));

    /// <summary>
    /// Ray/sphere entry parameter in the units of <paramref name="direction"/> (which need not be
    /// normalized), 0 when the origin is inside, null on a miss or a sphere behind the ray.
    /// </summary>
    public static float? SphereHit(Vector3 center, float radius, Vector3 origin, Vector3 direction) =>
        RayBox.SphereHit(Plain(center), radius, Plain(origin), Plain(direction));

    static System.Numerics.Vector3 Plain(Vector3 value) => new(value.X, value.Y, value.Z);
}
