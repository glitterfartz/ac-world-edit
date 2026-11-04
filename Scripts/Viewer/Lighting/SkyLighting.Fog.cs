using Godot;
using ACWorldEdit.Assets;

namespace ACWorldEdit;

/// <summary>Fog: the client fog terms, retail extras and placed volumes.</summary>
public sealed partial class SkyLighting
{
    /// <summary>
    /// Retail's fog terms beyond the 3.3.5a ones, at one tick: density, the end-fog color and distance, and the
    /// height layer.
    /// </summary>
    readonly record struct RetailFogValues(float Density, Color EndColor, float EndDistance, Vector4 Height, Vector4 Poly, Color HeightColor, Color HeightEndColor, Vector4 Sun)
    {
        public static RetailFogValues None { get; } = new(1, Colors.Black, 0, Vector4.Zero, Vector4.Zero, Colors.Black, Colors.Black, Vector4.Zero);
    }

    void SetClientFog(float start, float end, Color color, RetailFogValues terms)
    {
        clientFog = new(start, end, Retail ? terms.Density : 1, Retail ? 2 : 1);
        ClientShaderGlobals.Set(ClientShaderGlobals.Fog, clientFog);
        ClientShaderGlobals.Set(ClientShaderGlobals.FogColor, new Vector3(color.R, color.G, color.B));
        ClientShaderGlobals.Set(ClientShaderGlobals.FogEnd, new Vector4(terms.EndColor.R, terms.EndColor.G, terms.EndColor.B, terms.EndDistance));
        ClientShaderGlobals.Set(ClientShaderGlobals.FogHeight, terms.Height);
        ClientFogHeightValues = terms.Height;
        ClientShaderGlobals.Set(ClientShaderGlobals.FogPoly, terms.Poly);
        ClientShaderGlobals.Set(ClientShaderGlobals.FogHeightColor, new Vector4(terms.HeightColor.R, terms.HeightColor.G, terms.HeightColor.B, 0));
        ClientShaderGlobals.Set(ClientShaderGlobals.FogSun, terms.Sun);
        ClientShaderGlobals.Set(ClientShaderGlobals.FogHeightEndColor, new Vector4(terms.HeightEndColor.R, terms.HeightEndColor.G, terms.HeightEndColor.B, 0));
    }

    /// <summary>
    /// Retail's extra fog terms at a tick, blended like the other bands: the density (4.5 when a light has
    /// none), the color the fog turns toward with distance and the height layer. The layer counts only when its
    /// transition cubic is not all zero, as the client's shader treats the classic lights. Not used for 3.3.5a clients.
    /// </summary>
    RetailFogValues RetailFogTerms(LightProfile background, LightProfile? foreground, float weight, double tick)
    {
        if (!Retail || background.RetailFog is not { } bg)
        {
            return RetailFogValues.None;
        }

        var fg = foreground?.RetailFog is { } other && weight > 0 ? other : null;
        float Mix(Func<RetailFog, float> pick) => fg == null ? pick(bg) : Mathf.Lerp(pick(bg), pick(fg), weight);
        Color MixColor(Func<RetailFog, LightBand> pick)
        {
            var color = pick(bg).At(tick);
            return fg == null ? color : color.Lerp(pick(fg).At(tick), weight);
        }

        float density = Mix(r => r.Density.At(tick));
        var poly = new Vector4(Mix(r => r.Height.Coefficients[0].At(tick)), Mix(r => r.Height.Coefficients[1].At(tick)), Mix(r => r.Height.Coefficients[2].At(tick)), Mix(r => r.Height.Coefficients[3].At(tick)));
        bool layered = poly != Vector4.Zero;
        var height = new Vector4(Mix(r => r.Height.Plane.At(tick)), Mix(r => r.Height.Scaler.At(tick)), Mix(r => r.Height.Density.At(tick)), layered ? 1 : 0);
        var sunColor = MixColor(r => r.SunColor);
        float sunStrength = Mix(r => r.SunStrength.At(tick));
        // A light with no sun fog color has no scattering; the LightData default of white would otherwise light every fog.
        var sun = sunColor.R + sunColor.G + sunColor.B > 0.0001f ? new Vector4(sunColor.R, sunColor.G, sunColor.B, sunStrength) : Vector4.Zero;
        return new(density > 0 ? density : 4.5f, MixColor(r => r.EndColor), Mix(r => r.EndColorDistance.At(tick)), height, poly, MixColor(r => r.Height.Color), MixColor(r => r.Height.EndColor), sun);
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
