using ACWorldEdit.Foundation;

namespace ACWorldEdit.Assets;

/// <summary>
/// One Light.dbc time-of-day band: up to 16 (time, color) keyframes cycling over a 2880-tick day
/// (tick = in-game minute * 2; hour = tick/120). Packed values are 0x00RRGGBB. The cyclic
/// interpolation is pinned by tests as the current behavior; it is not client-verified. The Godot
/// Color view (At) is the viewer's partial in Scripts/Viewer/ClientLighting.cs.
/// </summary>
public sealed partial class LightBand
{
    public const double CycleLength = 2880;
    readonly uint[] times;
    readonly (float R, float G, float B)[] colors;

    LightBand(uint[] times, (float R, float G, float B)[] colors)
    {
        this.times = times;
        this.colors = colors;
    }

    /// <summary>
    /// Read one positional LightIntBand row. A zero-row band (unused slot) holds a single white
    /// sample so callers always get a defined color.
    /// </summary>
    public static LightBand Read(DbcTable table, int row)
    {
        int n = Math.Clamp((int)table.UAt(row, 1), 0, 16);
        if (n == 0)
        {
            return new([0], [(1f, 1f, 1f)]);
        }

        var times = new uint[n];
        var colors = new (float R, float G, float B)[n];
        for (int i = 0; i < n; i++)
        {
            times[i] = table.UAt(row, 2 + i);
            uint packed = table.UAt(row, 18 + i);
            colors[i] = (((packed >> 16) & 0xFF) / 255f, ((packed >> 8) & 0xFF) / 255f, (packed & 0xFF) / 255f);
        }

        return new(times, colors);
    }

    /// <summary>
    /// Build a band from retail LightData samples (time in ticks, color packed 0x00RRGGBB). Samples are
    /// sorted by time; none at all gives the same single white sample as an unused Wrath slot.
    /// </summary>
    public static LightBand FromSamples(IEnumerable<(uint Time, uint Packed)> samples)
    {
        var ordered = samples.OrderBy(s => s.Time).Take(64).ToArray();
        if (ordered.Length == 0)
        {
            return new([0], [(1f, 1f, 1f)]);
        }

        return new(
            ordered.Select(s => s.Time).ToArray(),
            ordered.Select(s => (((s.Packed >> 16) & 0xFF) / 255f, ((s.Packed >> 8) & 0xFF) / 255f, (s.Packed & 0xFF) / 255f)).ToArray());
    }

    /// <summary>
    /// Interpolate the cyclic piecewise-linear curve at an arbitrary tick, wrapping the final segment
    /// back to the first sample.
    /// </summary>
    public (float R, float G, float B) Rgb(double tick)
    {
        if (colors.Length == 1)
        {
            return colors[0];
        }

        double t = ((tick % CycleLength) + CycleLength) % CycleLength;
        for (int i = 0; i < times.Length; i++)
        {
            int j = (i + 1) % times.Length;
            double a = times[i];
            double b = j == 0 ? times[j] + CycleLength : times[j];
            if (t >= a && t < b)
            {
                float f = b > a ? (float)((t - a) / (b - a)) : 0;
                var from = colors[i];
                var to = colors[j];
                return (from.R + (to.R - from.R) * f, from.G + (to.G - from.G) * f, from.B + (to.B - from.B) * f);
            }
        }

        return colors[^1];
    }
}

/// <summary>
/// Light.dbc position conversion. Light.dbc positions use a 1/36-yard scale with the tile-grid
/// corner as origin (no recentering); this is an assumption checked against WDT tile presence, not
/// client-verified. Output is the viewer's Y-up basis.
/// </summary>
public static class LightCoordinates
{
    public static (double X, double Y, double Z) ToView(double dbcX, double dbcY, double dbcZ)
    {
        double center = 32.0 * WorldCoordinates.TileSize;
        var server = new Point3(center - dbcZ / 36.0, center - dbcX / 36.0, dbcY / 36.0);
        var view = WorldCoordinates.ToView(server);
        return (view.X, view.Y, view.Z);
    }
}
