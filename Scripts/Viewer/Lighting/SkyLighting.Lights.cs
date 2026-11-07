using ACWorldEdit.Assets;
using Godot;

namespace ACWorldEdit;

/// <summary>
/// Retail WMO point/spot lights (MNLD): the nearest few to the camera, published like the
/// fog volumes. Snapshots are static (no flicker curves) and ranges ignore placement scale.
/// A light joins when its doodad set is the default set or the placement's own set.
/// </summary>
public sealed partial class SkyLighting
{
    /// <summary>Placed world models with their view-space transforms; unset means no light collection.</summary>
    public Func<IEnumerable<WorldObjectScene.Placed>>? LightPlacements { get; set; }

    readonly Image pointLightImage = Image.CreateEmpty(ClientShaderGlobals.MaxPointLights * ClientShaderGlobals.PointLightTexels, 1, false, Image.Format.Rgbaf);
    ImageTexture? pointLightTexture;
    Vector3 lastLightCenter = new(float.NaN, float.NaN, float.NaN);

    /// <summary>
    /// Hand the shaders the nearest placed MNLD lights. Recomputed when the eye moves;
    /// placements and decodes are shared caches, so the pass is a few dictionary lookups.
    /// </summary>
    void UpdatePointLights(Camera3D camera)
    {
        var provide = LightPlacements;
        if (provide == null)
        {
            return;
        }

        var eye = camera.GlobalPosition;
        if ((eye - lastLightCenter).LengthSquared() < 25f && pointLightTexture != null)
        {
            return;
        }

        lastLightCenter = eye;
        var chosen = new List<(float DistSq, Vector3 Position, Vector3 Color, float Start, float End, Vector3 Direction, float Type, float CosInner, float CosOuter)>();
        foreach (var placed in provide())
        {
            if (!placed.Ready || !AssetRef.IsWmo(placed.Model.Path))
            {
                continue;
            }

            var lights = models.ReadyData(placed.Model)?.Lights;
            if (lights == null || lights.Length == 0)
            {
                continue;
            }

            foreach (var light in lights)
            {
                if (light.DoodadSet != 0 && light.DoodadSet != placed.Item.DoodadSet)
                {
                    continue;
                }

                if (light.AttenEnd <= 0 || light.Intensity <= 0)
                {
                    continue;
                }

                var local = new Vector3(light.Position.X, light.Position.Y, light.Position.Z);
                Vector3 world = placed.Transform * local;
                var localDir = new Vector3(light.Direction.X, light.Direction.Y, light.Direction.Z);
                Vector3 direction = (placed.Transform.Basis * localDir).Normalized();
                float distSq = (world - eye).LengthSquared();
                var color = light.Color * light.Intensity;
                chosen.Add((distSq, world, new Vector3(color.X, color.Y, color.Z), light.AttenStart, light.AttenEnd, direction, light.Type,
                    MathF.Cos(light.InnerAngle), MathF.Cos(light.OuterAngle)));
            }
        }

        int count = 0;
        foreach (var (_, position, color, start, end, direction, type, cosInner, cosOuter) in chosen.OrderBy(c => c.DistSq).Take(ClientShaderGlobals.MaxPointLights))
        {
            int at = count * ClientShaderGlobals.PointLightTexels;
            pointLightImage.SetPixel(at, 0, new Color(position.X, position.Y, position.Z, end));
            pointLightImage.SetPixel(at + 1, 0, new Color(color.X, color.Y, color.Z, start));
            pointLightImage.SetPixel(at + 2, 0, new Color(direction.X, direction.Y, direction.Z, type));
            pointLightImage.SetPixel(at + 3, 0, new Color(cosInner, cosOuter, 0, 0));
            count++;
        }

        if (pointLightTexture == null)
        {
            pointLightTexture = ImageTexture.CreateFromImage(pointLightImage);
            ClientShaderGlobals.Set(ClientShaderGlobals.PointLights, pointLightTexture);
        }
        else
        {
            pointLightTexture.Update(pointLightImage);
        }

        ClientShaderGlobals.Set(ClientShaderGlobals.PointLightCount, (float)count);
    }
}
