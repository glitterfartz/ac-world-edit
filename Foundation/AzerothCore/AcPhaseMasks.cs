using System.Globalization;

namespace ACWorldEdit.Foundation;

/// <summary>
/// AzerothCore's phase mask: bit 0 (value 1) is phase 1, the normal world, bit 1 is phase 2, and so on. Two
/// things see each other when their masks share a bit, and a mask of all bits is visible in every phase.
/// </summary>
public static class AcPhaseMasks
{
    public const uint Normal = 1;
    public const uint All = uint.MaxValue;

    /// <summary>How many phases the preview lists as checkboxes; higher bits stay reachable through the typed mask.</summary>
    public const int Listed = 16;

    /// <summary>Decimal or 0x hex; the empty string and anything above 32 bits are refused.</summary>
    public static bool TryParse(string text, out uint mask)
    {
        text = text.Trim();
        bool hex = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
        return uint.TryParse(hex ? text[2..] : text, hex ? NumberStyles.HexNumber : NumberStyles.Integer, CultureInfo.InvariantCulture, out mask);
    }

    /// <summary>The phase numbers (1 to 32) whose bit is set.</summary>
    public static IEnumerable<int> Phases(uint mask) => Enumerable.Range(1, 32).Where(phase => Has(mask, phase));

    public static bool Has(uint mask, int phase) => phase is >= 1 and <= 32 && (mask & (1u << (phase - 1))) != 0;

    public static uint With(uint mask, int phase, bool on) => on ? mask | (1u << (phase - 1)) : mask & ~(1u << (phase - 1));

    /// <summary>The mask in words: "phase 1 (normal world)", "phases 1 and 3", "all phases", "no phase".</summary>
    public static string Describe(uint mask)
    {
        if (mask == All)
        {
            return "all phases";
        }

        var phases = Phases(mask).ToArray();
        return phases.Length switch
        {
            0 => "no phase",
            1 when phases[0] == 1 => "phase 1 (normal world)",
            1 => $"phase {phases[0]}",
            2 => $"phases {phases[0]} and {phases[1]}",
            <= 6 => $"phases {string.Join(", ", phases[..^1])} and {phases[^1]}",
            _ => $"{phases.Length} phases"
        };
    }

    /// <summary>
    /// Count spawns per phase from (mask, spawns) groups. A spawn in two phases counts in both; one with every bit set
    /// counts as <see cref="PhaseCatalog.EveryPhase"/> instead of in all 32 phases.
    /// </summary>
    public static PhaseCatalog Catalog(IEnumerable<(uint Mask, int Spawns)> groups)
    {
        var perPhase = new Dictionary<int, int>();
        int everyPhase = 0;
        foreach (var (mask, spawns) in groups)
        {
            if (mask == All)
            {
                everyPhase += spawns;
                continue;
            }

            foreach (int phase in Phases(mask))
            {
                perPhase[phase] = perPhase.GetValueOrDefault(phase) + spawns;
            }
        }

        return new(perPhase.OrderBy(p => p.Key).Select(p => new PhaseInfo((uint)p.Key, p.Key == 1 ? "Normal world" : "", p.Value)).ToArray(), everyPhase);
    }
}
