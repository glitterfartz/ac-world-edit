namespace ACWorldEdit.Assets;

/// <summary>
/// CPU mirror of the client's liquid texture-slot frame selection, recovered from the
/// Midnight binary (base 0x7ff748f20000, fully decrypted build 70575):
/// <list type="bullet">
/// <item>static slots: <c>FUN_7ff74c6f9960</c> binds texture slots 0..4 with divisor 1, so the
/// frame is always 0 (first authored frame);</item>
/// <item>animated slots: <c>FUN_7ff74c6f9760</c> cycles <c>count</c> frames over
/// <c>rateMs</c> (settings <c>+0x72c</c> seconds × 1000, passed by the azerite render at
/// <c>0x7ff74c6faf20</c>), returning the fractional phase as the two-frame crossfade.</item>
/// </list>
/// The render binds azerite slots in order 0, 1 (body layers), 7, 8 (shared defaults), 9,
/// 2, 3, 4 (masks; slot 3 is the flat <c>azerite_basenormal</c> placeholder), then the slot-5
/// and slot-6 animated sequences. Slot descriptors are contiguous at
/// <c>settings+8+(slot+0x4e)*0x18</c>. Per-slot constants land at CB offsets
/// <c>+0x1f80/+0x1f84/+0x1f88/+0x1f8c</c> (from <c>+0x71c/+0x728</c> reciprocal-guarded,
/// <c>+0x734/+0x738</c>), <c>+0x1f90=3</c>, <c>+0x1f94/+0x1f98</c> (from <c>+0x73c/+0x718</c>),
/// phase at <c>+0x1f9c</c> over period <c>+0x720</c> seconds, and the <c>+0x6e4</c> UV-matrix
/// block is copied to <c>+0x1fc0</c>. Which DB2 floats feed <c>+0x718..+0x73c</c> is still
/// unknown (the settings fill was not identified), so this helper only locks the frame math.
/// </summary>
public static class LiquidAnimPhase
{
    /// <summary>
    /// Animated sequence slot: which two frames to sample and how to blend them at
    /// <paramref name="timeMs"/>. Mirrors <c>FUN_7ff74c6f9760</c> exactly, including the
    /// low-32-bit time, the rate-0-means-1 rule and the last-frame wraps to frame 0.
    /// </summary>
    public static (int First, int Second, float Blend) Sequence(uint timeMs, uint rateMs, int count)
    {
        if (count <= 0)
        {
            return (0, 0, 0);
        }

        uint rate = rateMs == 0 ? 1 : rateMs;
        float f = count * ((timeMs % rate) / (float)rate);
        int first = (int)f;
        int second = first + 1 == count ? 0 : first + 1;
        return (first, second, f - first);
    }
}
