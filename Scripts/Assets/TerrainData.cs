using System.Text;
namespace ACWorldEdit.Assets;

public readonly record struct TileKey(int X, int Y);
public readonly record struct ChunkHeader(string Tag, int Start, int Data, int Size);
public static class Chunks
{
    public static IEnumerable<ChunkHeader> Read(byte[] b, int start = 0, int? end = null)
    {
        int limit = end ?? b.Length;
        for (int p = start; p < limit;)
        {
            if (p + 8 > limit) throw new InvalidDataException("Truncated chunk header");
            string tag = Encoding.ASCII.GetString(b, p, 4); tag = new string(tag.Reverse().ToArray());
            int size = checked((int)MpqArchive.U(b, p + 4));
            if (size > limit - p - 8) throw new InvalidDataException("Invalid " + tag + " chunk size");
            yield return new(tag, p, p + 8, size); p += size + 8;
        }
    }
    public static float F(byte[] b, int p) => BitConverter.ToSingle(b, p);
}
public sealed class WorldMap
{
    public string Name { get; }
    public bool BigAlpha { get; }
    public HashSet<TileKey> Tiles { get; } = [];
    public WorldMap(string name, byte[] data)
    {
        Name = name;
        foreach (var c in Chunks.Read(data))
        {
            if (c.Tag == "MPHD") BigAlpha = (MpqArchive.U(data, c.Data) & 4) != 0;
            if (c.Tag == "MAIN")
            {
                if (c.Size != 64 * 64 * 8) throw new InvalidDataException("Unexpected WDT MAIN size");
                for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++)
                    if ((MpqArchive.U(data, c.Data + (y * 64 + x) * 8) & 1) != 0) Tiles.Add(new(x, y));
            }
        }
        if (Tiles.Count == 0) throw new NotSupportedException("This map has no ADT terrain (it may be an indoor WMO map).");
    }
    public string TilePath(TileKey key) => $"World/Maps/{Name}/{Name}_{key.X}_{key.Y}.adt";
}
public sealed record TerrainLayer(string Texture, byte[] Alpha);
public sealed class TerrainChunk
{
    public int X, Y;
    public uint Flags, Holes;
    public float Height;
    public float[] Heights = new float[145];
    public sbyte[]? Normals;
    public byte[]? Colors, Shadow;
    public List<TerrainLayer> Layers = [];
}
public sealed class TerrainTile
{
    public const float Size = 1600f / 3f;
    public const float ChunkSize = Size / 16;
    public TileKey Key { get; }
    public List<TerrainChunk> Chunks { get; } = [];
    public TerrainTile(TileKey key, byte[] b, bool bigAlpha)
    {
        Key = key; var top = Assets.Chunks.Read(b).ToList();
        var version = top.FirstOrDefault(c => c.Tag == "MVER");
        if (version.Size != 4 || MpqArchive.U(b, version.Data) != 18) throw new NotSupportedException("Expected ADT version 18");
        var mtex = top.FirstOrDefault(c => c.Tag == "MTEX");
        string[] textures = Encoding.UTF8.GetString(b, mtex.Data, mtex.Size).Split('\0', StringSplitOptions.RemoveEmptyEntries);
        foreach (var c in top.Where(c => c.Tag == "MCNK"))
        {
            if (c.Size < 128) throw new InvalidDataException("Short MCNK header");
            int h = c.Data;
            var chunk = new TerrainChunk { Flags = MpqArchive.U(b, h), X = (int)MpqArchive.U(b, h + 4), Y = (int)MpqArchive.U(b, h + 8), Holes = MpqArchive.U(b, h + 60) & 0xFFFF, Height = Assets.Chunks.F(b, h + 112) };
            if (chunk.X is < 0 or > 15 || chunk.Y is < 0 or > 15) throw new InvalidDataException("MCNK index out of range");
            byte[] Sub(int field, string tag, bool optional = false)
            {
                int offset = checked((int)MpqArchive.U(b, h + field)); if (offset == 0 && optional) return [];
                int p = c.Start + offset;
                if (p < h + 128 || p + 8 > h + c.Size) throw new InvalidDataException("Bad " + tag + " offset");
                string actual = new string(Encoding.ASCII.GetString(b, p, 4).Reverse().ToArray());
                int size = checked((int)MpqArchive.U(b, p + 4));
                // Wrath sometimes leaves the MCAL/MCSH subheader size zero; MCNK carries the authoritative size including its header.
                if (tag == "MCAL") size = checked((int)MpqArchive.U(b, h + 40)) - 8;
                if (tag == "MCSH") size = checked((int)MpqArchive.U(b, h + 48)) - 8;
                if (actual != tag || size < 0 || size > h + c.Size - p - 8) throw new InvalidDataException("Invalid " + tag + " subchunk");
                return b.AsSpan(p + 8, size).ToArray();
            }
            var heights = Sub(20, "MCVT"); if (heights.Length != 145 * 4) throw new InvalidDataException("Expected 145 terrain heights");
            for (int i = 0; i < 145; i++) chunk.Heights[i] = Assets.Chunks.F(heights, i * 4);
            var normals = Sub(24, "MCNR", true); if (normals.Length >= 435) chunk.Normals = normals.Take(435).Select(v => unchecked((sbyte)v)).ToArray();
            var colors = (chunk.Flags & 0x40) != 0 ? Sub(116, "MCCV", true) : []; if (colors.Length >= 580) chunk.Colors = colors;
            var shadow = (chunk.Flags & 1) != 0 ? Sub(44, "MCSH", true) : []; if (shadow.Length >= 512) chunk.Shadow = shadow;
            var layers = Sub(28, "MCLY"); var alpha = Sub(36, "MCAL", true);
            int count = layers.Length / 16;
            if (count is < 1 or > 4 || layers.Length % 16 != 0) throw new InvalidDataException("Invalid terrain layer count");
            for (int i = 0; i < count; i++)
            {
                int p = i * 16, texture = checked((int)MpqArchive.U(layers, p));
                if (texture >= textures.Length) throw new InvalidDataException("Bad terrain texture index");
                uint flags = MpqArchive.U(layers, p + 4);
                byte[] map = i == 0 ? [] : DecodeAlpha(alpha, (int)MpqArchive.U(layers, p + 8), flags, bigAlpha, (chunk.Flags & 0x8000) == 0);
                chunk.Layers.Add(new(textures[texture], map));
            }
            Chunks.Add(chunk);
        }
        if (Chunks.Count != 256) throw new InvalidDataException($"Expected 256 MCNK chunks, found {Chunks.Count}");
    }
    public static byte[] DecodeAlpha(byte[] data, int offset, uint flags, bool big, bool fixEdges)
    {
        byte[] output = new byte[4096];
        if ((flags & 0x100) == 0) { Array.Fill(output, (byte)255); return output; }
        if ((flags & 0x200) != 0)
        {
            int p = offset, o = 0;
            while (o < 4096)
            {
                if (p >= data.Length) throw new InvalidDataException($"Truncated compressed alpha: input={data.Length}, start={offset}, produced={o}, flags={flags:X}");
                int control = data[p++], count = control & 127;
                if (count == 0 || o + count > 4096) throw new InvalidDataException("Invalid alpha run");
                if ((control & 128) != 0)
                { if (p >= data.Length) throw new InvalidDataException("Short alpha fill"); Array.Fill(output, data[p++], o, count); }
                else { data.AsSpan(p, count).CopyTo(output.AsSpan(o)); p += count; }
                o += count;
            }
        }
        else if (big) data.AsSpan(offset, 4096).CopyTo(output);
        else for (int i = 0; i < 2048; i++)
        { byte v = data[offset + i]; output[i * 2] = (byte)((v & 15) * 17); output[i * 2 + 1] = (byte)((v >> 4) * 17); }
        if (fixEdges)
        {
            for (int y = 0; y < 64; y++) output[y * 64 + 63] = output[y * 64 + 62];
            Array.Copy(output, 62 * 64, output, 63 * 64, 64);
        }
        return output;
    }
}
