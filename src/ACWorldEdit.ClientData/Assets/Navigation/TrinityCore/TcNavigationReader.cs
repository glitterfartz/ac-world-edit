namespace ACWorldEdit.Assets;
public sealed class TcNavigationReader : INavigationReader
{
    public string TileName(int map, int x, int y) => $"{map:0000}_{x:00}_{y:00}.mmtile";
    public string MapName(int map) => $"{map:0000}.mmap";
    public string AreaName(byte area) => area switch { 0 => "Empty", 11 => "Ground", 10 => "Steep ground", 9 => "Water", 8 => "Magma / slime", _ => $"Unknown area {area}" };
    public NavigationTile ReadTile(byte[] bytes, int requestedMap, int sourceMap, string file, int x, int y) => DetourReader.Read(bytes, 20, 16, requestedMap, sourceMap, file, x, y, null);
    public NavigationParameters ReadParameters(byte[] bytes)
    {
        DetourReader.Require(bytes.Length >= 40, "Truncated TC map header."); using var r = new BinaryReader(new MemoryStream(bytes, false));
        DetourReader.Require(r.ReadUInt32() == DetourReader.Magic && r.ReadUInt32() == 16, "Unsupported TC MMap map version.");
        var parameters = DetourReader.Parameters(r, []); uint count = r.ReadUInt32();
        DetourReader.Require(40L + count * 44L == bytes.Length, "Invalid TC map off-mesh record length.");
        var connections = new NavigationConnection[count];
        for (int i = 0; i < count; i++)
        {
            uint map = r.ReadUInt32(), x = r.ReadUInt32(), y = r.ReadUInt32(); var from = DetourReader.Vector(r); var to = DetourReader.Vector(r); float radius = DetourReader.Float(r);
            byte flags = r.ReadByte(), area = r.ReadByte(); ushort terrain = r.ReadUInt16(); DetourReader.Require(radius >= 0 && area <= 63, "Invalid TC off-mesh record.");
            connections[i] = new(from, to, radius, -1, flags, 255, 0, map, x, y, area, terrain);
        }
        return parameters with { Connections = connections };
    }
}
