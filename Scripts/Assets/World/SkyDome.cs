namespace ACWorldEdit.Assets;

/// <summary>
/// The client's sky tables (DayNight.cpp): the curves that drive the sunrise/sunset highlight on the
/// sky dome and the sun's path across it, plus the dome's ring geometry. Angles are radians and
/// directions use the client's world axes (x, y horizontal, z up); the viewer rotates them into its
/// own basis. The dome itself is drawn by Shaders/sky.gdshader.
/// </summary>
public static class SkyDome
{
    public const float Sunrise = 0.22916667f;
    public const float Noon = 0.5f;
    public const float Sunset = 0.89583331f;

    /// <summary>How strongly the sky highlight shows over the day (s_highlightCurve), keyed by day fraction.</summary>
    public static readonly (float Time, float Value)[] HighlightCurve =
    [
        (0.125f, 0f), (0.27083334f, 1f), (0.29166669f, 0f), (0.85416663f, 0f), (0.89583331f, 1f), (0.99930555f, 0f)
    ];

    /// <summary>
    /// How a ring's color follows the angle around the dome (s_skyFaceCurve): 1 keeps the band color, 0 takes
    /// the full highlight, and negative values darken toward the zenith color.
    /// </summary>
    public static readonly (float Time, float Value)[] FaceCurve =
    [
        (0.125f, 1f), (0.375f, 0f), (0.5f, -0.5f), (0.625f, -0.7f), (0.75f, -0.5f), (0.875f, 0f)
    ];

    // The sun's polar angle from straight up and its azimuth over the day (s_sunTheta, s_sunPhi).
    static readonly (float Time, float Value)[] SunTheta =
    [
        (Sunrise, 1.7453293f), (0.49652779f, 0.087266468f), (Noon, 0.087266468f), (0.50347221f, 0.087266468f), (Sunset, 1.7453293f)
    ];

    static readonly (float Time, float Value)[] SunPhi = [(Sunrise, 0.78539819f), (Noon, 0.78539819f), (Sunset, 0.78539819f)];

    /// <summary>
    /// The polar angle of each dome ring from straight up, in units of pi (s_skyThetaTable): the zenith,
    /// four colored bands, the horizon ring and the pole below.
    /// </summary>
    public static readonly float[] RingTheta = [0f, 0.17f, 0.2f, 0.23f, 0.24f, 0.25f, 1f];

    /// <summary>
    /// A ring's distance from the dome's axis and its height relative to the horizon ring, for a unit sphere
    /// (DNSky::GenSphere shifts the sphere down by cos(pi/4), which puts the horizon ring at height 0).
    /// </summary>
    public static (float Radius, float Height) Ring(int index)
    {
        float theta = RingTheta[index] * MathF.PI;
        return (MathF.Sin(theta), MathF.Cos(theta) - MathF.Cos(MathF.PI / 4));
    }

    /// <summary>
    /// DayNight::InterpTable: linear interpolation over a cyclic day fraction. The input is clamped to
    /// 0..1 and the span from the last key back to the first wraps past midnight.
    /// </summary>
    public static float Interpolate(ReadOnlySpan<(float Time, float Value)> table, float t)
    {
        float value = Math.Clamp(t, 0f, 1f);
        int index = 0;
        while (index < table.Length && value > table[index].Time)
        {
            index++;
        }

        int previous;
        if (index == table.Length)
        {
            index = 0;
            previous = table.Length - 1;
        }
        else
        {
            previous = index > 0 ? index - 1 : table.Length - 1;
        }

        float range = table[index].Time - table[previous].Time;
        if (MathF.Abs(range) < 0.001f)
        {
            return table[previous].Value;
        }

        if (range < 0f)
        {
            range += 1f;
        }

        float offset = value - table[previous].Time;
        if (offset < 0f)
        {
            offset += 1f;
        }

        float from = table[previous].Value, to = table[index].Value;
        return from + offset / range * (to - from);
    }

    /// <summary>The sky highlight strength (0..1) at a day fraction, before the light's HighlightSky flag scales it.</summary>
    public static float Highlight(float dayFraction) => Interpolate(HighlightCurve, dayFraction);

    /// <summary>
    /// The unit vector toward the sun disc at a day fraction (DayNight::SetPlanets): nearly overhead at noon and
    /// just below the horizon at sunrise and sunset, always on the same bearing.
    /// </summary>
    public static (float X, float Y, float Z) SunDirection(float dayFraction)
    {
        float theta = Interpolate(SunTheta, dayFraction);
        float phi = Interpolate(SunPhi, dayFraction);
        return (MathF.Cos(phi) * MathF.Sin(theta), MathF.Sin(theta) * MathF.Sin(phi), MathF.Cos(theta));
    }
}
