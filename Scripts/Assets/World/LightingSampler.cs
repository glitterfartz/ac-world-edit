namespace ACWorldEdit.Assets;

/// <summary>
/// One point-in-time lighting decision: which profiles apply, at what blend weight, and the colors
/// and sun direction the renderer should publish. Plain floats; the Godot view converts them.
/// </summary>
public readonly record struct LightingSample(
    double Tick,
    LightProfile Background,
    LightRegion? Foreground,
    LightProfile? ForegroundProfile,
    float Weight,
    (float R, float G, float B) Diffuse,
    (float R, float G, float B) Ambient,
    (float R, float G, float B) Fog,
    (float X, float Y, float Z) SunDirection,
    uint BackgroundId,
    uint BackgroundParamsId,
    uint ActiveParamsId,
    string ActiveLights);

/// <summary>The inputs to one sample: the clock, the camera in view space, and the map's regions.</summary>
public readonly record struct LightingRequest(
    double Hours,
    float CameraX,
    float CameraY,
    float CameraZ,
    IReadOnlyList<LightRegion> Regions,
    Func<uint, LightProfile?> Profile);

/// <summary>
/// The pure half of the day-night model: advance the clock, pick the default and nearest local
/// light, and interpolate the diffuse/ambient/fog bands. No Godot types, so the region rules are
/// unit-tested without an install. A null result means "keep the last frame", which is what the
/// interior, no-table, no-map and no-profile cases need.
/// </summary>
public static class LightingSampler
{
    /// <summary>Advance the clock by <paramref name="delta"/> seconds at <paramref name="speed"/> (1 = real time).</summary>
    public static double Advance(double hours, double delta, double speed) => (hours + delta / 60.0 * speed) % 24.0;

    public static LightingSample? Sample(LightingRequest request)
    {
        // The base is the map's default light, the one at coordinates (0, 0, 0). A map without one uses
        // Light 1, the Eastern Kingdoms default, as the client does (DayNight::LightLoad), rather than
        // whichever local light is nearest.
        var background = request.Regions.FirstOrDefault(r => r.IsDefault) ?? request.Regions.FirstOrDefault(r => r.Id == 1);
        var backgroundProfile = background != null ? request.Profile(background.ParamsId) : null;
        if (backgroundProfile == null)
        {
            return null;
        }

        // Local lights blend over it by their distance to the camera in three dimensions, height
        // included (DayNight::BlendAreaLight). The client blends every light that covers it, the
        // nearest last; this takes the nearest.
        LightRegion? best = null;
        float bestWeight = 0;
        float bestDistance = float.MaxValue;
        foreach (var region in request.Regions)
        {
            if (region.IsDefault)
            {
                continue;
            }

            float dx = request.CameraX - region.ViewX, dy = request.CameraY - region.ViewY, dz = request.CameraZ - region.ViewZ;
            float dist = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
            float span = Math.Max(1f, region.FalloffEnd - region.FalloffStart);
            float weight = dist <= region.FalloffStart ? 1f : dist >= region.FalloffEnd ? 0f : (region.FalloffEnd - dist) / span;
            if (weight > 0 && dist < bestDistance)
            {
                best = region;
                bestWeight = weight;
                bestDistance = dist;
            }
        }

        var foregroundProfile = best != null ? request.Profile(best.ParamsId) : null;
        string activeLights = $"background={background?.Id}/params {background?.ParamsId} foreground={best?.Id}/params {best?.ParamsId} weight={bestWeight:0.00} regions={request.Regions.Count}";
        double tick = request.Hours * 120.0;
        var diffuse = Blend(request, backgroundProfile, foregroundProfile, bestWeight, p => p.Diffuse, tick);
        var ambient = Blend(request, backgroundProfile, foregroundProfile, bestWeight, p => p.Ambient, tick);
        var fog = Blend(request, backgroundProfile, foregroundProfile, bestWeight, p => p.Fog, tick);
        var (lx, ly, lz) = SkyDome.LightDirection((float)(request.Hours / 24.0));
        var sun = Normalize(-ly, lz, -lx);
        uint activeParamsId = best != null && bestWeight >= 0.999f ? best.ParamsId : background!.ParamsId;
        return new(tick, backgroundProfile, best, foregroundProfile, bestWeight, diffuse, ambient, fog, sun,
            background!.Id, background.ParamsId, activeParamsId, activeLights);
    }

    static (float R, float G, float B) Blend(LightingRequest request, LightProfile background, LightProfile? foreground,
        float weight, Func<LightProfile, LightBand> pick, double tick)
    {
        var (r, g, b) = pick(background).Rgb(tick);
        if (foreground != null && weight > 0)
        {
            var (fr, fg, fb) = pick(foreground).Rgb(tick);
            return (r + (fr - r) * weight, g + (fg - g) * weight, b + (fb - b) * weight);
        }

        return (r, g, b);
    }

    static (float X, float Y, float Z) Normalize(float x, float y, float z)
    {
        float length = MathF.Sqrt(x * x + y * y + z * z);
        return length == 0 ? (0, 0, 0) : (x / length, y / length, z / length);
    }
}
