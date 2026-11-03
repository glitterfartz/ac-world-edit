namespace ACWorldEdit.Assets;
public sealed class AcNavigationReader : INavigationReader
{
    public string TileName(int map, int x, int y) => $"{map:000}{x:00}{y:00}.mmtile";
    public string MapName(int map) => $"{map:000}.mmap";
    public string AreaName(byte area) => area switch { 0 => "Empty", 1 => "Ground", 2 => "Magma", 4 => "Slime", 8 => "Water", _ => $"Unknown area {area}" };
    public NavigationTile ReadTile(byte[] bytes, int requestedMap, int sourceMap, string file, int x, int y)
    {
        DetourReader.Require(bytes.Length >= 56, "Truncated AC MMap header.");
        using var r = new BinaryReader(new MemoryStream(bytes, false));
        DetourReader.Require(r.ReadUInt32()==DetourReader.Magic && r.ReadUInt32()==7 && r.ReadUInt32()==20, "Unsupported AC MMap format; expected MMap 20, Detour 7.");
        r.BaseStream.Position = 20;
        float slope = DetourReader.Float(r); byte radius = r.ReadByte(), height = r.ReadByte(), climb = r.ReadByte(); r.ReadByte();
        var settings = new NavigationSettings(slope, radius, height, climb, r.ReadUInt32(), r.ReadUInt32(), r.ReadUInt32(), DetourReader.Float(r), DetourReader.Float(r), DetourReader.Float(r), DetourReader.Float(r));
        DetourReader.Require(slope >= 0 && slope <= 90 && settings.BaseUnit >= 0 && settings.CellSize >= 0 && settings.CellHeight >= 0 && settings.Simplification >= 0, "Invalid AC generator settings.");
        return DetourReader.Read(bytes, 56, 20, requestedMap, sourceMap, file, x, y, settings);
    }
    public NavigationParameters ReadParameters(byte[] bytes)
    {
        DetourReader.Require(bytes.Length == 28, "Invalid AC map parameter length.");
        using var r = new BinaryReader(new MemoryStream(bytes, false)); return DetourReader.Parameters(r, []);
    }
}
