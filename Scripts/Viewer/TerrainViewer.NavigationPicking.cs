using Godot;
using ACWorldEdit.Assets;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

public partial class TerrainViewer
{
    /// <summary>Bound navigation picks by loaded terrain and visible building render triangles.</summary>
    float? NavigationOcclusion(PickRay ray)
    {
        float nearest = TerrainRayHit(ray) is { } terrain ? ray.Origin.DistanceTo(terrain) : ray.Far;
        foreach (var placement in objects.Placements())
        {
            if (!placement.Ready || !AssetRef.IsWmo(placement.Model.Path)) continue;
            var inverse = placement.Transform.AffineInverse();
            var origin = inverse * ray.Origin; var direction = inverse.Basis * ray.Direction;
            if (ScenePicking.BoxHit(placement.Bounds, origin, direction) is not float entry || entry > nearest) continue;
            var data = models.ReadyData(placement.Model);
            if (data == null) continue;
            foreach (var surface in data.Surfaces)
            {
                if (surface.LiquidType != null) continue;
                for (int i = 0; i + 2 < surface.Indices.Length; i += 3)
                {
                    var a = surface.Positions[surface.Indices[i]]; var b = surface.Positions[surface.Indices[i + 1]]; var c = surface.Positions[surface.Indices[i + 2]];
                    double? hit = WorldCoordinates.Hit(new(origin.X, origin.Y, origin.Z), new(direction.X, direction.Y, direction.Z), new(a.X, a.Y, a.Z), new(b.X, b.Y, b.Z), new(c.X, c.Y, c.Z));
                    if (hit is double t && t < nearest) nearest = (float)t;
                }
            }
        }
        return nearest < ray.Far ? nearest : null;
    }
}
