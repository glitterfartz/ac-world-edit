namespace ACWorldEdit.Assets;

/// <summary>
/// Dynamic sun-shadow quality. Off renders no cascade maps; Low, Medium and High differ in cascade
/// count, distance, map resolution and filter taps.
/// </summary>
public enum ShadowQuality
{
    Off = 0,
    Low,
    Medium,
    High,
}

/// <summary>
/// One quality's cascade settings. <see cref="Cascades"/> is how many orthographic maps the caster
/// pass renders (and the receiver samples); <see cref="MaxDistance"/> is the farthest view depth the
/// maps cover in yards; <see cref="Resolution"/> is the square map size; <see cref="PcfTaps"/> is how
/// many depth comparisons the receiver averages per fragment.
/// </summary>
public readonly record struct ShadowPreset(int Cascades, float MaxDistance, int Resolution, int PcfTaps)
{
    public static ShadowPreset Off { get; } = new(0, 0, 0, 1);
}

/// <summary>
/// The viewer's shadow presets and the saved/CLI spelling of each quality. A value that does not name
/// a preset parses as <see cref="ShadowQuality.Off"/>, so an unknown setting never enables shadows.
/// </summary>
public static class ShadowQualityPresets
{
    /// <summary>
    /// Low keeps one near cascade and no filtering; Medium covers the combat range with 5 taps; High
    /// reaches out to the settled view distance with a 2048 map and 9 taps. The costs are measured in
    /// <c>docs/status/performance.md</c>.
    /// </summary>
    public static ShadowPreset For(ShadowQuality quality) => quality switch
    {
        ShadowQuality.Low => new(1, 100, 1024, 1),
        ShadowQuality.Medium => new(3, 180, 1024, 5),
        ShadowQuality.High => new(4, 300, 2048, 9),
        _ => ShadowPreset.Off,
    };

    /// <summary>The stable name of a quality (as saved in settings.cfg and accepted on the command line).</summary>
    public static string Name(ShadowQuality quality) => quality switch
    {
        ShadowQuality.Low => "Low",
        ShadowQuality.Medium => "Medium",
        ShadowQuality.High => "High",
        _ => "Off",
    };

    /// <summary>Parse a quality name without regard to case; anything unknown is Off.</summary>
    public static bool TryParse(string? text, out ShadowQuality quality)
    {
        switch (text?.Trim().ToLowerInvariant())
        {
            case "low":
                quality = ShadowQuality.Low;
                return true;
            case "medium":
                quality = ShadowQuality.Medium;
                return true;
            case "high":
                quality = ShadowQuality.High;
                return true;
            case "off":
            case "":
            case null:
                quality = ShadowQuality.Off;
                return true;
            default:
                quality = ShadowQuality.Off;
                return false;
        }
    }

    /// <summary>Parse a quality name; an unreadable value is Off rather than an error.</summary>
    public static ShadowQuality Parse(string? text) => TryParse(text, out var quality) ? quality : ShadowQuality.Off;

    /// <summary>The status line posted when the quality changes ("Shadows: High (4 cascades).").</summary>
    public static string StatusMessage(ShadowQuality quality)
    {
        int cascades = For(quality).Cascades;
        return quality == ShadowQuality.Off
            ? "Shadows: Off."
            : $"Shadows: {Name(quality)} ({cascades} cascade{(cascades == 1 ? "" : "s")}).";
    }
}
