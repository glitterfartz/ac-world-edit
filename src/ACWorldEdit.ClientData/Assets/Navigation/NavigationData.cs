using System.Numerics;
namespace ACWorldEdit.Assets;

public sealed record NavigationPolygon(ushort[] Vertices, ushort[] Neighbors, ushort Flags, byte Area, byte Type, uint FirstLink);
public sealed record NavigationTriangle(int Polygon, Vector3 A, Vector3 B, Vector3 C, byte EdgeFlags);
public sealed record NavigationConnection(Vector3 From, Vector3 To, float Radius, int Polygon, byte Flags, byte Side, uint UserId, uint Map = 0, uint TileX = 0, uint TileY = 0, byte Area = 0, ushort TerrainFlags = 0);
public sealed record NavigationSettings(float Slope, byte Radius, byte Height, byte Climb, uint VerticesPerMap, uint VerticesPerTile, uint TilesPerMap, float BaseUnit, float CellSize, float CellHeight, float Simplification);
public sealed record NavigationParameters(Vector3 Origin, float TileWidth, float TileHeight, int MaxTiles, uint MaxPolygons, NavigationConnection[] Connections);
public sealed record NavigationTile(int RequestedMap, int SourceMap, string File, int FileX, int FileY, int GridX, int GridY, int Layer, uint UserId, bool UsesLiquids,
    Vector3 Minimum, Vector3 Maximum, float AgentHeight, float AgentRadius, float AgentClimb, NavigationSettings? Settings,
    Vector3[] Vertices, NavigationPolygon[] Polygons, NavigationTriangle[] Triangles, NavigationConnection[] Connections)
{
    // Detour stores (server Y, server Z, server X); the viewer reverses horizontal axes.
    public static Vector3 ToView(Vector3 detour) => new(-detour.X, detour.Y, -detour.Z);
}
public interface INavigationReader
{
    string TileName(int map, int x, int y);
    string MapName(int map);
    NavigationTile ReadTile(byte[] bytes, int requestedMap, int sourceMap, string file, int x, int y);
    NavigationParameters ReadParameters(byte[] bytes);
    string AreaName(byte area);
}
public sealed class NavigationSource(string directory, INavigationReader reader, IReadOnlyDictionary<int, int>? parents = null)
{
    public NavigationTile Read(int map, int fileX, int fileY)
    {
        int source = map;
        string path = Path.Combine(directory, reader.TileName(map, fileX, fileY));
        byte[] bytes;
        try { bytes = ReadBounded(path); }
        catch (FileNotFoundException) when (parents != null && parents.ContainsKey(map))
        {
            source = parents[map];
            path = Path.Combine(directory, reader.TileName(source, fileX, fileY));
            bytes = ReadBounded(path);
        }
        return reader.ReadTile(bytes, map, source, path, fileX, fileY);
    }
    public NavigationParameters Parameters(int map) => reader.ReadParameters(ReadBounded(Path.Combine(directory, reader.MapName(map))));
    static byte[] ReadBounded(string path)
    {
        using var stream = File.OpenRead(path);
        if (stream.Length > 64 * 1024 * 1024) throw new InvalidDataException("Navigation file exceeds 64 MiB.");
        byte[] bytes = new byte[checked((int)stream.Length)]; stream.ReadExactly(bytes); return bytes;
    }
}
