using Godot;
using ACWorldEdit.Assets;

namespace ACWorldEdit;

/// <summary>
/// Fog publication and the retail placed fog volumes: the shader terms come from
/// <see cref="FogDetail"/>, the volumes from the map's WDT.
/// </summary>
public sealed partial class SkyLighting
{
    void SetClientFog(FogDetail fog)
    {
        var terms = fog.Retail;
        clientFog = new(fog.Start, fog.End, Retail && terms.Present ? terms.Density : 1, Retail ? 2 : 1);
        ClientShaderGlobals.Set(ClientShaderGlobals.Fog, clientFog);
        ClientShaderGlobals.Set(ClientShaderGlobals.FogColor, new Vector3(fog.Color.R, fog.Color.G, fog.Color.B));
        ClientShaderGlobals.Set(ClientShaderGlobals.FogEnd, new Vector4(terms.EndColor.R, terms.EndColor.G, terms.EndColor.B, terms.EndDistance));
        var height = new Vector4(terms.Plane, terms.Scaler, terms.HeightDensity, terms.Layered);
        ClientShaderGlobals.Set(ClientShaderGlobals.FogHeight, height);
        ClientFogHeightValues = height;
        ClientShaderGlobals.Set(ClientShaderGlobals.FogPoly, new Vector4(terms.PolyX, terms.PolyY, terms.PolyZ, terms.PolyW));
        ClientShaderGlobals.Set(ClientShaderGlobals.FogHeightColor, new Vector4(terms.HeightColor.R, terms.HeightColor.G, terms.HeightColor.B, 0));
        ClientShaderGlobals.Set(ClientShaderGlobals.FogSun, new Vector4(terms.SunR, terms.SunG, terms.SunB, terms.SunStrength));
        ClientShaderGlobals.Set(ClientShaderGlobals.FogHeightEndColor, new Vector4(terms.HeightEndColor.R, terms.HeightEndColor.G, terms.HeightEndColor.B, 0));
    }

    /// <summary>One fog volume placed in the viewer: a view-space box (center, axes, half sizes), its extinction per yard, color and edge power.</summary>
    readonly record struct FogBox(Vector3 Center, Basis Axes, Vector3 Half, float Thickness, Color Color, float Power, bool Ellipsoid, bool Noise, float SpeedA, float SpeedB, float HeightPower);

    /// <summary>
    /// Hand the shaders a map's fog volumes. The WDT stores them in server axes; the viewer's are (-y, z, -x) of those, so the
    /// rotation is carried over by conjugating it with that axis permutation.
    /// </summary>
    public void SetFogVolumes(IReadOnlyList<MapFogVolume> volumes)
    {
        var permutation = new Basis(new Vector3(0, 0, -1), new Vector3(-1, 0, 0), new Vector3(0, 1, 0));
        fogBoxes = [.. volumes.Where(v => v.FogLevel <= 2 && v.ThicknessPerYard > 0 && v.Size.X > 0 && v.Size.Y > 0 && v.Size.Z > 0).Select(v =>
        {
            var rotation = new Basis(new Quaternion(v.Rotation.X, v.Rotation.Y, v.Rotation.Z, v.Rotation.W).Normalized());
            return new FogBox(ViewSpace.ToView(v.Position.X, v.Position.Y, v.Position.Z), permutation * rotation * permutation.Transposed(),
                new Vector3(v.Size.X, v.Size.Y, v.Size.Z) / 2f, v.ThicknessPerYard, new Color(v.Color.X, v.Color.Y, v.Color.Z), v.EdgePower,
                v.IsEllipsoid, v.UsesNoise, v.NoiseSpeed(0), v.NoiseSpeed(1), v.HeightPower);
        })];
    }

    /// <summary>
    /// The nearest few volumes to the camera into the shared texture. Distance is to the box's bounding sphere, so a vast
    /// volume around the camera always counts.
    /// </summary>
    void UpdateFogVolumes(Camera3D camera)
    {
        if (fogBoxes.Length == 0)
        {
            ClientShaderGlobals.Set(ClientShaderGlobals.FogVolumeCount, 0f);
            return;
        }

        var position = camera.GlobalPosition;
        var chosen = fogBoxes.OrderBy(b => Mathf.Max(0f, position.DistanceTo(b.Center) - b.Half.Length())).Take(ClientShaderGlobals.MaxFogVolumes).ToArray();
        for (int i = 0; i < chosen.Length; i++)
        {
            var b = chosen[i];
            int at = i * ClientShaderGlobals.FogVolumeTexels;
            fogVolumeImage.SetPixel(at, 0, new Color(b.Center.X, b.Center.Y, b.Center.Z, b.Thickness));
            fogVolumeImage.SetPixel(at + 1, 0, new Color(b.Axes.X.X, b.Axes.X.Y, b.Axes.X.Z, b.Half.X));
            fogVolumeImage.SetPixel(at + 2, 0, new Color(b.Axes.Y.X, b.Axes.Y.Y, b.Axes.Y.Z, b.Half.Y));
            fogVolumeImage.SetPixel(at + 3, 0, new Color(b.Axes.Z.X, b.Axes.Z.Y, b.Axes.Z.Z, b.Half.Z));
            fogVolumeImage.SetPixel(at + 4, 0, new Color(b.Color.R, b.Color.G, b.Color.B, b.Power));
            fogVolumeImage.SetPixel(at + 5, 0, new Color((b.Ellipsoid ? 1f : 0f) + (b.Noise ? 2f : 0f), b.SpeedA, b.SpeedB, b.HeightPower));
        }

        if (fogVolumeTexture == null)
        {
            fogVolumeTexture = ImageTexture.CreateFromImage(fogVolumeImage);
            ClientShaderGlobals.Set(ClientShaderGlobals.FogVolumes, fogVolumeTexture);
        }
        else
        {
            fogVolumeTexture.Update(fogVolumeImage);
        }

        ClientShaderGlobals.Set(ClientShaderGlobals.FogVolumeCount, (float)chosen.Length);
        ClientShaderGlobals.Set(ClientShaderGlobals.FogVolumeTime, (float)(Time.GetTicksMsec() / 1000.0 % 100000.0));
    }
}
