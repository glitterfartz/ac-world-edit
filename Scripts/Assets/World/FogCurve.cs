namespace ACWorldEdit.Assets;

/// <summary>
/// One LightFloatBand time-of-day curve: up to 16 (time, value) keyframes cycling over the same
/// 2880-tick day as LightBand, blended linearly with the last sample wrapping back to the first.
/// </summary>
public sealed class LightFloatBand
{
    readonly uint[] times;
    readonly float[] values;

    LightFloatBand(uint[] times, float[] values)
    {
        this.times = times;
        this.values = values;
    }

    /// <summary>
    /// Read one positional LightFloatBand row (ID, sample count, 16 times, 16 float values). A
    /// zero-sample row holds a single zero so callers always get a defined value.
    /// </summary>
    public static LightFloatBand Read(DbcTable table, int row)
    {
        int n = Math.Clamp((int)table.UAt(row, 1), 0, 16);
        if (n == 0)
        {
            return new([0], [0f]);
        }

        var times = new uint[n];
        var values = new float[n];
        for (int i = 0; i < n; i++)
        {
            times[i] = table.UAt(row, 2 + i);
            values[i] = BitConverter.UInt32BitsToSingle(table.UAt(row, 18 + i));
        }

        return new(times, values);
    }

    /// <summary>
    /// Build a band from keyframes, sorted by time. None at all gives the single zero sample of an
    /// unused slot.
    /// </summary>
    public static LightFloatBand FromSamples(IEnumerable<(uint Time, float Value)> samples)
    {
        var ordered = samples.OrderBy(s => s.Time).Take(64).ToArray();
        return ordered.Length == 0 ? new([0], [0f]) : new(ordered.Select(s => s.Time).ToArray(), ordered.Select(s => s.Value).ToArray());
    }

    /// <summary>
    /// Interpolate the cyclic curve at an arbitrary tick.
    /// </summary>
    public float At(double tick)
    {
        if (values.Length == 1)
        {
            return values[0];
        }

        double t = ((tick % LightBand.CycleLength) + LightBand.CycleLength) % LightBand.CycleLength;
        for (int i = 0; i < times.Length; i++)
        {
            int j = (i + 1) % times.Length;
            double a = times[i];
            double b = j == 0 ? times[j] + LightBand.CycleLength : times[j];
            if (t >= a && t < b)
            {
                float f = b > a ? (float)((t - a) / (b - a)) : 0;
                return values[i] + (values[j] - values[i]) * f;
            }
        }

        return values[^1];
    }
}

/// <summary>
/// The client's per-LightParams fog setting: FogEnd (yards) and the start multiplier. The fog
/// ramps from End * StartMul to End; the renderer then caps End at the view distance
/// (DayNight::UpdateFog).
/// </summary>
public sealed class FogCurve
{
    /// <summary>
    /// The client never lets a light's fog end drop below this many yards (DayNight::CalcColors).
    /// </summary>
    public const float MinimumEnd = 10;
    readonly LightFloatBand end;
    readonly LightFloatBand startMul;

    FogCurve(LightFloatBand end, LightFloatBand startMul)
    {
        this.end = end;
        this.startMul = startMul;
    }

    /// <summary>
    /// Wrath: LightFloatBand bands 0 (FogEnd) and 1 (start multiplier) of one LightParams entry.
    /// </summary>
    public static FogCurve FromBands(LightFloatBand end, LightFloatBand startMul) => new(end, startMul);

    /// <summary>
    /// Retail: one LightData sample per (time, FogEnd, FogScaler).
    /// </summary>
    public static FogCurve FromSamples(IEnumerable<(uint Time, float End, float Scaler)> samples)
    {
        var list = samples.ToArray();
        return new(LightFloatBand.FromSamples(list.Select(s => (s.Time, s.End))), LightFloatBand.FromSamples(list.Select(s => (s.Time, s.Scaler))));
    }

    /// <summary>
    /// Fog end and start multiplier at a tick, or null when the light defines no usable fog distance
    /// (an unset or non-positive end). The end is raised to <see cref="MinimumEnd"/> and the
    /// multiplier clamped to -1..1, as the client does.
    /// </summary>
    public (float End, float StartMul)? At(double tick)
    {
        float e = end.At(tick);
        return e > 0 ? (Math.Max(e, MinimumEnd), Math.Clamp(startMul.At(tick), -1f, 1f)) : null;
    }
}
