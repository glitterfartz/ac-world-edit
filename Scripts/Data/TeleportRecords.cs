using ACWorldEdit.Foundation;

namespace ACWorldEdit.Data;

/// <summary>One game_tele row: a named server position (orientation in radians, as the server stores it).</summary>
public sealed record TeleportLocation(uint Id, string Name, ushort Map, double X, double Y, double Z, double Orientation);

/// <summary>Helpers over game_tele rows that do not depend on the dialect.</summary>
public static class TeleportLocations
{
    /// <summary>The Wrath world continents: Eastern Kingdoms, Kalimdor, Outland, Northrend.</summary>
    public static readonly IReadOnlySet<ushort> Continents = new HashSet<ushort> { 0, 1, 530, 571 };

    /// <summary>A random location on any of the given maps, or null when the table has none there.</summary>
    public static TeleportLocation? Pick(IReadOnlyList<TeleportLocation> rows, IReadOnlySet<ushort> maps, Random random)
    {
        var usable = rows.Where(r => maps.Contains(r.Map)).ToArray();
        return usable.Length == 0 ? null : usable[random.Next(usable.Length)];
    }
}
