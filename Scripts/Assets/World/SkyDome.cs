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

    public const float Moonrise = 0.91666669f;
    public const float Moonset = 0.16666667f;

    /// <summary>How far from the camera the sun and moon quads sit (DayNight::SetPlanets).</summary>
    public const float PlanetDistance = 12f;

    // Polar angle and azimuth of the moons over their cycle (s_moonTheta, s_moonPhi, s_moon2Phi) and the quad
    // scale curves (s_sunScale, s_moonScale).
    static readonly (float Time, float Value)[] MoonTheta =
    [
        (0f, 0.61086524f), (0.0034722222f, 0.61086524f), (Moonset, 1.7453293f), (Moonrise, 1.7453293f), (0.99652779f, 0.61086524f)
    ];

    static readonly (float Time, float Value)[] MoonPhi = [(0f, 0.78539819f), (Moonset, 0.78539819f), (Moonrise, 0.78539819f)];
    static readonly (float Time, float Value)[] SecondMoonPhi = [(0f, 2.3561945f), (Moonset, 2.6179938f), (Moonrise, 2.8797934f)];
    static readonly (float Time, float Value)[] SunScaleCurve = [(0.25f, 2f), (0.28125f, 1f), (0.84375f, 1f), (0.875f, 2f)];
    static readonly (float Time, float Value)[] MoonScaleCurve = [(0.041666672f, 1f), (Moonset, 1.5f), (Moonrise, 1.5f), (0.99930561f, 1f)];

    /// <summary>
    /// One celestial quad: the unit direction toward it (client axes) and its width in the units the quad
    /// is drawn at, <see cref="PlanetDistance"/> from the eye.
    /// </summary>
    public readonly record struct Planet((float X, float Y, float Z) Direction, float Scale);

    /// <summary>The sun disc (texture sunCenter, base scale 1, doubled at sunrise and sunset).</summary>
    public static Planet Sun(float dayFraction) => new(SunDirection(dayFraction), Interpolate(SunScaleCurve, dayFraction));

    /// <summary>The moon (texture moon, base scale 1.75, one and a half times that while it is low).</summary>
    public static Planet Moon(float dayFraction) =>
        new(Direction(Interpolate(MoonTheta, dayFraction), Interpolate(MoonPhi, dayFraction)), Interpolate(MoonScaleCurve, dayFraction) * 1.75f);

    /// <summary>
    /// The second moon (texture moon02, base scale 1), which crosses the sky once every 1.7 days. The
    /// editor has no calendar, so the cycle starts at the clock's midnight.
    /// </summary>
    public static Planet SecondMoon(float dayFraction)
    {
        float cycle = dayFraction / 1.7f;
        return new(Direction(Interpolate(MoonTheta, cycle), Interpolate(SecondMoonPhi, cycle)), Interpolate(MoonScaleCurve, cycle));
    }

    // The unit vector for a polar angle from straight up and an azimuth (DayNight::SetPlanets).
    static (float X, float Y, float Z) Direction(float theta, float phi) =>
        (MathF.Cos(phi) * MathF.Sin(theta), MathF.Sin(theta) * MathF.Sin(phi), MathF.Cos(theta));

    // How opaque the cloud layer is toward its rim (s_cloudAlphaTable over s_cloudThetaTable), by polar angle from
    // the cloud sphere's axis in units of pi.
    static readonly (float Theta, float Alpha)[] CloudEdge = [(0.205f, 1f), (0.23f, 0.5f), (0.245f, 0f)];

    /// <summary>
    /// The cloud layer's opacity at a polar angle (radians) from the top of the dome: full up to 0.205 pi, fading to
    /// half at 0.23 pi and to nothing at 0.245 pi, where the clouds meet the horizon.
    /// </summary>
    public static float CloudEdgeAlpha(float theta)
    {
        float turns = theta / MathF.PI;
        if (turns <= CloudEdge[0].Theta)
        {
            return 1f;
        }

        for (int i = 1; i < CloudEdge.Length; i++)
        {
            if (turns <= CloudEdge[i].Theta)
            {
                float t = (turns - CloudEdge[i - 1].Theta) / (CloudEdge[i].Theta - CloudEdge[i - 1].Theta);
                return CloudEdge[i - 1].Alpha + (CloudEdge[i].Alpha - CloudEdge[i - 1].Alpha) * t;
            }
        }

        return 0f;
    }

    /// <summary>
    /// A cloud texel's opacity from the summed noise (DNClouds::UpdateTextureAnimation): the sum scaled to a level
    /// around 128, minus the density threshold, through the sharpness curve (0.96 to the power of the index times
    /// the step the client computes once at its default density, whose byte truncates to 101).
    /// </summary>
    public static float CloudAlpha(float noiseSum, float density)
    {
        const float sharpness = 0.96f;
        const float step = (255 - 101) * 0.00390625f;
        float level = MathF.Round(noiseSum * 64f + 128f);
        float index = level - MathF.Floor((1f - density) * 255f);
        return index < 0f ? 0f : 1f - MathF.Pow(sharpness, MathF.Min(index, 255f) * step);
    }

    // How strongly the stars show over the day (s_starFadeTable) and the glare's alpha over the day (the sun's
    // and the moon's m_alphaCurve in InitSunGlare and InitMoonGlare).
    static readonly (float Time, float Value)[] StarFadeCurve = [(0.125f, 1f), (0.1875f, 0f), (0.9375f, 0f), (1f, 1f)];
    static readonly (float Time, float Value)[] SunGlareCurve = [(0.27083334f, 0f), (0.3125f, 1f), (0.8125f, 1f), (0.875f, 0f)];
    static readonly (float Time, float Value)[] MoonGlareCurve = [(0.083333336f, 1f), (0.13541667f, 0f), (0.94791669f, 0f), (0.99930561f, 1f)];

    /// <summary>The star model's opacity at a day fraction: full at night, gone by day (DNStars::Update).</summary>
    public static float StarFade(float dayFraction) => (Interpolate(StarFadeCurve, dayFraction) * 254f + 1f) / 255f;

    /// <summary>
    /// A glare quad's width (in the units the planets use) and alpha before the cloud, occlusion and fade-rate
    /// terms (DNGlare::Update).
    /// </summary>
    public readonly record struct Glare(float Scale, float Alpha);

    // The glare grows and brightens as the camera turns toward the body, from a cosine of 0.7 up to 1.
    static float GlareFacing(float cosine) => Math.Clamp((cosine - 0.7f) / (1f - 0.7f), 0f, 1f);

    /// <summary>The sun's glare: 3 to 20 units wide, 0.5 to 1 alpha, shown from sunrise to sunset.</summary>
    public static Glare SunGlare(float dayFraction, float cosineToCamera)
    {
        float t = GlareFacing(cosineToCamera);
        return new((20f - 3f) * t + 3f, (t * (1f - 0.5f) + 0.5f) * Interpolate(SunGlareCurve, dayFraction));
    }

    /// <summary>The moon's glare: twice the moon quad's width, 0.1 to 1 alpha, shown at night.</summary>
    public static Glare MoonGlare(float dayFraction, float cosineToCamera, float moonScale)
    {
        float t = GlareFacing(cosineToCamera);
        return new(moonScale * 2f, (t * (1f - 0.1f) + 0.1f) * Interpolate(MoonGlareCurve, dayFraction));
    }

    /// <summary>The sky highlight strength (0..1) at a day fraction, before the light's HighlightSky flag scales it.</summary>
    public static float Highlight(float dayFraction) => Interpolate(HighlightCurve, dayFraction);

    /// <summary>
    /// The unit vector toward the sun disc at a day fraction (DayNight::SetPlanets): nearly overhead at noon and
    /// just below the horizon at sunrise and sunset, always on the same bearing.
    /// </summary>
    public static (float X, float Y, float Z) SunDirection(float dayFraction)
    {
        return Direction(Interpolate(SunTheta, dayFraction), Interpolate(SunPhi, dayFraction));
    }

    // The polar angle from straight up of the direction the sun's light travels, over the day (DayNight::SetDirection).
    // It is steeper at noon and midnight than at dawn and dusk, and its bearing (225 degrees) never moves.
    static readonly (float Time, float Value)[] LightElevation = [(0f, 2.2165682f), (0.25f, 1.9198623f), (0.5f, 2.2165682f), (0.75f, 1.9198623f)];
    const float LightAzimuth = 3.926991f;

    /// <summary>
    /// The unit vector the sun's light travels along at a day fraction, in client axes (DayNight::SetDirection). This
    /// is what every lit surface uses; it is not the direction toward the sun disc, which rises and sets.
    /// </summary>
    public static (float X, float Y, float Z) LightDirection(float dayFraction) =>
        Direction(Interpolate(LightElevation, dayFraction), LightAzimuth);
}
