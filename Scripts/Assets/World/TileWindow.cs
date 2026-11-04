
namespace ACWorldEdit.Assets;

/// <summary>
/// The camera's terrain window: which tiles are wanted, which stay loaded under the hysteresis ring,
/// and which missing tile to read next. Pure tile arithmetic, unit-tested without a scene.
/// </summary>
public static class TileWindow
{
    /// <summary>Every map tile in the square radius around <paramref name="center"/>.</summary>
    public static HashSet<TileKey> Wanted(IReadOnlySet<TileKey> mapTiles, TileKey center, int radius)
    {
        var wanted = new HashSet<TileKey>();
        for (int y = -radius; y <= radius; y++)
        {
            for (int x = -radius; x <= radius; x++)
            {
                var key = new TileKey(center.X + x, center.Y + y);
                if (mapTiles.Contains(key))
                {
                    wanted.Add(key);
                }
            }
        }

        return wanted;
    }

    /// <summary>
    /// One extra ring of hysteresis beyond the load radius: turning or wobbling at the boundary must
    /// not repeatedly tear down and reload the same tile.
    /// </summary>
    public static bool KeepLoaded(TileKey key, TileKey center, int radius) =>
        Math.Abs(key.X - center.X) <= radius + 1 && Math.Abs(key.Y - center.Y) <= radius + 1;

    /// <summary>
    /// The nearest wanted tile that is neither loaded nor failed; ties keep the first in
    /// <paramref name="wanted"/> order, as the viewer's HashSet enumeration did.
    /// </summary>
    public static TileKey? NextMissing(IEnumerable<TileKey> wanted, Func<TileKey, bool> isLoaded, Func<TileKey, bool> isFailed, TileKey center)
    {
        TileKey? next = null;
        int nearest = int.MaxValue;
        foreach (var key in wanted)
        {
            int distance = (key.X - center.X) * (key.X - center.X) + (key.Y - center.Y) * (key.Y - center.Y);
            if (distance < nearest && !isLoaded(key) && !isFailed(key))
            {
                nearest = distance;
                next = key;
            }
        }

        return next;
    }
}
