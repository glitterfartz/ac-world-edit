using ACWorldEdit.Foundation;

namespace ACWorldEdit.Assets;

/// <summary>A blended colour as plain floats, so the sampler stays free of Godot types.</summary>
public readonly record struct RgbBand(float R, float G, float B)
{
    public static RgbBand Of((float R, float G, float B) value) => new(value.R, value.G, value.B);

    public static RgbBand Lerp(RgbBand from, RgbBand to, float weight) =>
        new(from.R + (to.R - from.R) * weight, from.G + (to.G - from.G) * weight, from.B + (to.B - from.B) * weight);
}

/// <summary>The six dome bands, the sun glow and the highlight term.</summary>
public readonly record struct SkyDetail(
    RgbBand Zenith,
    RgbBand Middle,
    RgbBand Band1,
    RgbBand Band2,
    RgbBand Smog,
    RgbBand Fog,
    RgbBand SunGlow,
    float Highlight);

/// <summary>Cloud density, colors and the planet that lights them.</summary>
public readonly record struct CloudDetail(
    float Density,
    RgbBand Sun,
    RgbBand Shade,
    RgbBand Base,
    float LightX,
    float LightY,
    float LightZ);

/// <summary>Retail's extra fog terms, already blended.</summary>
public readonly record struct RetailFogDetail(
    bool Present,
    float Density,
    RgbBand EndColor,
    float EndDistance,
    float PolyX,
    float PolyY,
    float PolyZ,
    float PolyW,
    float Plane,
    float Scaler,
    float HeightDensity,
    float Layered,
    RgbBand HeightColor,
    RgbBand HeightEndColor,
    float SunR,
    float SunG,
    float SunB,
    float SunStrength);

/// <summary>Fog start/end, colour and the retail extras.</summary>
public readonly record struct FogDetail(float Start, float End, RgbBand Color, RetailFogDetail Retail);

/// <summary>One water gradient end: its colour and alpha.</summary>
public readonly record struct WaterBand(RgbBand Color, float Alpha);

/// <summary>The four water gradients.</summary>
public readonly record struct WaterDetail(WaterBand OceanShallow, WaterBand OceanDeep, WaterBand RiverShallow, WaterBand RiverDeep);

/// <summary>Sun shafts: intensity, colour, reach and whether the quad is shown.</summary>
public readonly record struct ShaftDetail(bool Visible, float Amount, RgbBand Color, float Reach);

/// <summary>The sun/moon discs and the planet that lights the clouds at this hour.</summary>
public readonly record struct CelestialDetail(
    float SunX,
    float SunY,
    float SunZ,
    float MoonX,
    float MoonY,
    float MoonZ,
    float Moon2X,
    float Moon2Y,
    float Moon2Z,
    float LightX,
    float LightY,
    float LightZ);

/// <summary>
/// Everything the renderer publishes for one light sample: sky bands, clouds, fog, water, sun
/// specular, shafts, stars and the celestial positions. All arithmetic lives here; the Godot node only
/// builds Color/Vector values from these floats and sets shader parameters.
/// </summary>
public readonly record struct LightingDetail(
    SkyDetail Sky,
    CloudDetail Clouds,
    FogDetail Fog,
    WaterDetail Water,
    RgbBand SunSpecular,
    ShaftDetail Shafts,
    float StarsFade,
    float DayFraction,
    CelestialDetail Celestials);

/// <summary>
/// The second half of the day-night model: blend every published band from one
/// <see cref="LightingSample"/>. Kept free of Godot types so the same golden rules are unit-tested.
/// </summary>
public static class LightingBlend
{
    /// <summary>The yards a newer retail light's FogDensity is spread over (an estimate).</summary>
    public const float ModernFogSpan = 10000;
    /// <summary>Fog start as a fraction of its end when a light has no fog data (DayNight DEFAULT_FOG_SCALER).</summary>
    public const float DefaultFogStartMultiplier = 0.5f;

    public static LightingDetail Describe(LightingSample light, bool retail, float viewDistance)
    {
        double tick = light.Tick;
        float dayFraction = (float)(light.Hours / 24.0);
        var background = light.Background;
        var foreground = light.ForegroundProfile;
        float weight = light.Weight;

        var sky = new SkyDetail(
            Band(background, foreground, weight, tick, p => p.SkyTop),
            Band(background, foreground, weight, tick, p => p.Sky?.Middle ?? p.SkyTop),
            Band(background, foreground, weight, tick, p => p.Sky?.Band1 ?? p.SkyHorizon),
            Band(background, foreground, weight, tick, p => p.Sky?.Band2 ?? p.SkyHorizon),
            Band(background, foreground, weight, tick, p => p.SkyHorizon),
            Band(background, foreground, weight, tick, p => p.Fog),
            Band(background, foreground, weight, tick, p => p.SunColor ?? p.Diffuse),
            SkyDome.Highlight(dayFraction) * HighlightFlag(background, foreground, weight));

        var clouds = new CloudDetail(
            Mix(CloudDensity(background, tick), CloudDensity(foreground, tick), weight),
            Band(background, foreground, weight, tick, p => p.Clouds?.Sun ?? p.Diffuse),
            Band(background, foreground, weight, tick, p => p.Clouds?.Shade ?? p.Ambient),
            Band(background, foreground, weight, tick, p => p.Clouds?.Base ?? p.Ambient),
            LightPlanet(dayFraction).Direction.X,
            LightPlanet(dayFraction).Direction.Y,
            LightPlanet(dayFraction).Direction.Z);

        var (sunX, sunY, sunZ) = SkyDome.SunDirection(dayFraction);
        var (moonX, moonY, moonZ) = SkyDome.Moon(dayFraction).Direction;
        var (moon2X, moon2Y, moon2Z) = SkyDome.SecondMoon(dayFraction).Direction;
        var (lightX, lightY, lightZ) = LightPlanet(dayFraction).Direction;
        var celestials = new CelestialDetail(sunX, sunY, sunZ, moonX, moonY, moonZ, moon2X, moon2Y, moon2Z, lightX, lightY, lightZ);

        return new(sky, clouds, Fog(light, retail, viewDistance), Water(background, foreground, weight, tick),
            SunSpecular(background, foreground, weight, tick), Shafts(background, foreground, weight, tick, dayFraction),
            SkyDome.StarFade(dayFraction), dayFraction, celestials);
    }

    /// <summary>Blend one colour band; a null or zero-weight foreground leaves the background colour.</summary>
    public static RgbBand Band(LightProfile background, LightProfile? foreground, float weight, double tick, Func<LightProfile, LightBand> pick)
    {
        var value = RgbBand.Of(pick(background).Rgb(tick));
        return foreground != null && weight > 0 ? RgbBand.Lerp(value, RgbBand.Of(pick(foreground).Rgb(tick)), weight) : value;
    }

    static float HighlightFlag(LightProfile background, LightProfile? foreground, float weight)
    {
        float flag = background.Sky?.Highlight ?? false ? 1f : 0f;
        return foreground != null && weight > 0 ? Mix(flag, foreground.Sky?.Highlight ?? false ? 1f : 0f, weight) : flag;
    }

    static float CloudDensity(LightProfile? profile, double tick) => profile?.Clouds?.Density.At(tick) ?? 0f;

    static SkyDome.Planet LightPlanet(float dayFraction) =>
        dayFraction < 0.2013889f || dayFraction > 0.9236111f ? SkyDome.Moon(dayFraction) : SkyDome.Sun(dayFraction);

    static float Mix(float from, float to, float weight) => from + (to - from) * weight;

    /// <summary>Fog ramp and colour, including the retail-only terms.</summary>
    static FogDetail Fog(LightingSample light, bool retail, float viewDistance)
    {
        double tick = light.Tick;
        var backgroundFog = light.Background.FogAt(tick);
        var foregroundFog = light.ForegroundProfile?.FogAt(tick);
        (float End, float StartMul)? curve = backgroundFog is { } b && foregroundFog is { } f && light.Weight > 0
            ? (Mix(b.End, f.End, light.Weight), Mix(b.StartMul, f.StartMul, light.Weight))
            : backgroundFog ?? foregroundFog;
        float fogEnd = curve == null && retail ? ModernFogSpan : Math.Min(viewDistance, curve?.End ?? viewDistance);
        float fogStart = fogEnd * (curve?.StartMul ?? (retail ? 0f : DefaultFogStartMultiplier));
        return new(fogStart, fogEnd, RgbBand.Of(light.Fog), RetailFog(light, retail));
    }

    /// <summary>Retail's density, end colour/distance, height layer and sun scattering, blended like the other bands.</summary>
    static RetailFogDetail RetailFog(LightingSample light, bool retail)
    {
        if (!retail || light.Background.RetailFog is not { } bg)
        {
            return default;
        }

        double tick = light.Tick;
        var fg = light.ForegroundProfile?.RetailFog is { } other && light.Weight > 0 ? other : null;
        float MixNumber(Func<RetailFog, float> pick) => fg == null ? pick(bg) : Mix(pick(bg), pick(fg), light.Weight);
        RgbBand MixColor(Func<RetailFog, LightBand> pick)
        {
            var color = RgbBand.Of(pick(bg).Rgb(tick));
            return fg == null ? color : RgbBand.Lerp(color, RgbBand.Of(pick(fg).Rgb(tick)), light.Weight);
        }

        float density = MixNumber(r => r.Density.At(tick));
        float polyX = MixNumber(r => r.Height.Coefficients[0].At(tick));
        float polyY = MixNumber(r => r.Height.Coefficients[1].At(tick));
        float polyZ = MixNumber(r => r.Height.Coefficients[2].At(tick));
        float polyW = MixNumber(r => r.Height.Coefficients[3].At(tick));
        bool layered = polyX != 0 || polyY != 0 || polyZ != 0 || polyW != 0;
        var sunColor = MixColor(r => r.SunColor);
        float sunStrength = MixNumber(r => r.SunStrength.At(tick));
        // A light with no sun fog color has no scattering; the LightData default of white would otherwise light every fog.
        bool scatter = sunColor.R + sunColor.G + sunColor.B > 0.0001f;
        return new(true, density > 0 ? density : 4.5f, MixColor(r => r.EndColor), MixNumber(r => r.EndColorDistance.At(tick)),
            polyX, polyY, polyZ, polyW,
            MixNumber(r => r.Height.Plane.At(tick)), MixNumber(r => r.Height.Scaler.At(tick)), MixNumber(r => r.Height.Density.At(tick)),
            layered ? 1 : 0, MixColor(r => r.Height.Color), MixColor(r => r.Height.EndColor),
            scatter ? sunColor.R : 0, scatter ? sunColor.G : 0, scatter ? sunColor.B : 0, scatter ? sunStrength : 0);
    }

    /// <summary>The four water gradients, blended the same way.</summary>
    static WaterDetail Water(LightProfile background, LightProfile? foreground, float weight, double tick)
    {
        if (background.Water is not { } bw || background.WaterAlpha is not { } ba)
        {
            return default;
        }

        WaterBand Gradient(Func<WaterColors, LightBand> band, Func<WaterAlphas, float> alpha)
        {
            var color = RgbBand.Of(band(bw).Rgb(tick));
            float a = alpha(ba);
            if (foreground is { Water: { } fw, WaterAlpha: { } fa } && weight > 0)
            {
                color = RgbBand.Lerp(color, RgbBand.Of(band(fw).Rgb(tick)), weight);
                a = Mix(a, alpha(fa), weight);
            }

            return new(color, a);
        }

        return new(
            Gradient(w => w.OceanShallow, a => a.OceanShallow),
            Gradient(w => w.OceanDeep, a => a.OceanDeep),
            Gradient(w => w.RiverShallow, a => a.RiverShallow),
            Gradient(w => w.RiverDeep, a => a.RiverDeep));
    }

    /// <summary>The sun band, used by self-lit shaders (water).</summary>
    static RgbBand SunSpecular(LightProfile background, LightProfile? foreground, float weight, double tick)
    {
        var value = RgbBand.Of((background.SunColor?.Rgb(tick) ?? (1f, 1f, 1f)));
        if (foreground?.SunColor is { } sunBand && weight > 0)
        {
            value = RgbBand.Lerp(value, RgbBand.Of(sunBand.Rgb(tick)), weight);
        }

        return value;
    }

    /// <summary>Shaft intensity, colour and reach; the quad is shown only above the 0.001 floor.</summary>
    static ShaftDetail Shafts(LightProfile background, LightProfile? foreground, float weight, double tick, float dayFraction)
    {
        float Intensity(LightProfile p) => p.Shafts?.Intensity.At(tick) ?? 0f;
        float intensity = Intensity(background);
        if (foreground != null && weight > 0)
        {
            intensity = Mix(intensity, Intensity(foreground), weight);
        }

        var (_, _, sz) = SkyDome.SunDirection(dayFraction);
        float amount = intensity * SkyDome.ShaftSunVisibility(sz);
        float Radius(LightProfile p) => p.Shafts?.Radius.At(tick) ?? 0f;
        float radius = Radius(background);
        var color = RgbBand.Of((background.Shafts?.Color.Rgb(tick) ?? (1f, 1f, 1f)));
        if (foreground != null && weight > 0)
        {
            radius = Mix(radius, Radius(foreground), weight);
            color = RgbBand.Lerp(color, RgbBand.Of(foreground.Shafts?.Color.Rgb(tick) ?? (1f, 1f, 1f)), weight);
        }

        return new(amount > 0.001f, amount, color, SkyDome.ShaftReach(radius));
    }
}
