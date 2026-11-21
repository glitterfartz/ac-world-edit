namespace ACWorldEdit.Assets;

/// <summary>
/// The viewer's 64-by-64 tile grid in view space. Terrain identity is a <see cref="TileKey"/>;
/// position in that grid is arithmetic on <see cref="TerrainTile.Size"/> and is repeated wherever
/// a camera, a spawn or a minimap click has to become a tile. Two semantics exist and must stay
/// distinct: <see cref="FromView"/> is raw (an interior or an off-grid point may fall outside
/// 0..63), while <see cref="Clamp"/> is for the tile entry fields, which are bounded.
/// </summary>
public static class TileGrid
{
    /// <summary>The tile at the view-space origin is 32,32; the grid therefore spans 0..63.</summary>
    public const int Radius = 32;
    public const int Last = 63;

    /// <summary>Raw tile containing a view-space X/Z. May fall outside 0..63.</summary>
    public static TileKey FromView(float x, float z) => new(
        (int)Math.Floor(x / TerrainTile.Size + Radius),
        (int)Math.Floor(z / TerrainTile.Size + Radius));

    /// <summary>
    /// The same conversion for server-pose doubles. Kept as a separate overload so a float caller
    /// keeps float division (and its rounding) exactly as before.
    /// </summary>
    public static TileKey FromView(double x, double z) => new(
        (int)Math.Floor(x / TerrainTile.Size + Radius),
        (int)Math.Floor(z / TerrainTile.Size + Radius));

    /// <summary>The same tile, bounded to the grid.</summary>
    public static TileKey Clamp(TileKey key) => new(Math.Clamp(key.X, 0, Last), Math.Clamp(key.Y, 0, Last));

    /// <summary>View-space X inside the tile: 0 at its west edge.</summary>
    public static float LocalX(float x, TileKey key) => x - (key.X - Radius) * TerrainTile.Size;

    /// <summary>View-space Z inside the tile: 0 at its north edge.</summary>
    public static float LocalZ(float z, TileKey key) => z - (key.Y - Radius) * TerrainTile.Size;
}
