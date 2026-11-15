using System.Numerics;

namespace ACWorldEdit.Assets;

/// <summary>
/// How much of the sun's diffuse and specular a fully shadowed fragment keeps. Zero at night and
/// near the horizon, so a low sun, an interior or a map without a light table leaves the world lit
/// exactly as before; the strength also dims a bright overcast sun whose diffuse band is dark.
/// <para>
/// The gate uses the sun <em>disc</em>'s elevation, not the light direction: the client's
/// <c>DayNight::SetDirection</c> keeps the light's travel direction fixed (and always downward) at
/// every hour, so only the disc actually rises and sets (<c>SkyDome.SunDirection</c>, Z-up).
/// </para>
/// </summary>
public static class ShadowSunStrength
{
    /// <param name="sunUp">The sine of the sun disc's elevation (positive above the horizon).</param>
    /// <param name="sunDiffuse">The light's diffuse band (gamma space).</param>
    public static float Compute(float sunUp, Vector3 sunDiffuse)
    {
        float horizon = SmoothStep(-0.02f, 0.12f, sunUp);
        float luminance = 0.2126f * sunDiffuse.X + 0.7152f * sunDiffuse.Y + 0.0722f * sunDiffuse.Z;
        return horizon * Math.Clamp(luminance * 2f, 0f, 1f);
    }

    static float SmoothStep(float edge0, float edge1, float x)
    {
        float t = Math.Clamp((x - edge0) / (edge1 - edge0), 0f, 1f);
        return t * t * (3f - 2f * t);
    }
}
