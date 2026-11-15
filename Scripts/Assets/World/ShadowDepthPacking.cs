using System.Numerics;

namespace ACWorldEdit.Assets;

/// <summary>
/// Packs a sun-space depth into an RGB8 texel the caster pass renders and the receiver unpacks. Depth
/// is <c>dot(world, sunDirection)</c> over a fixed global range, so one caster material serves every
/// cascade and the packed value does not depend on the cascade at all.
/// <para>
/// The clear value is white (the far end of the range), which reads as "lit": a receiver farther than
/// everything in the map is never in shadow. Three 8-bit channels carry 24 bits, so the error is at
/// most <see cref="MaxError"/> yards (about 0.004).
/// </para>
/// </summary>
public static class ShadowDepthPacking
{
    public const float Min = -32768f;
    public const float Max = 32768f;
    public const float Range = Max - Min;

    /// <summary>The most precise 24-bit percentage of the range.</summary>
    public const uint Levels = 16777215;

    /// <summary>Largest error a round trip can introduce: half of one 24-bit step.</summary>
    public const float MaxError = Range / 16777216f;

    /// <summary>Normalize a sun-space depth to 0..1 over the global range.</summary>
    public static float Normalize(float depth) => Math.Clamp((depth - Min) / Range, 0f, 1f);

    /// <summary>Turn a 0..1 normalized depth back into a sun-space depth.</summary>
    public static float Denormalize(float normalized) => Min + Math.Clamp(normalized, 0f, 1f) * Range;

    /// <summary>Pack a sun-space depth into the normalized RGB the caster shader writes.</summary>
    public static Vector3 Pack(float depth)
    {
        uint bits = (uint)MathF.Round(Normalize(depth) * Levels);
        return new Vector3(bits & 255, (bits >> 8) & 255, (bits >> 16) & 255) / 255f;
    }

    /// <summary>Unpack the RGB the caster shader wrote back into a sun-space depth.</summary>
    public static float Unpack(Vector3 rgb)
    {
        uint bits = (uint)MathF.Round(Math.Clamp(rgb.X, 0f, 1f) * 255f)
            | (uint)MathF.Round(Math.Clamp(rgb.Y, 0f, 1f) * 255f) << 8
            | (uint)MathF.Round(Math.Clamp(rgb.Z, 0f, 1f) * 255f) << 16;
        return Denormalize(bits / (float)Levels);
    }
}
