using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace ACWorldEdit.Assets;
/// <summary>Wrath liquid grids; vertices remain at their native heights and omitted cells stay omitted.</summary>
public static class LiquidData
{
    public const float Step = TerrainTile.ChunkSize / 8;
    public static ModelSurface Grid(int width, int height, uint type, Func<int, int, Vector3> point, Func<int, int, bool> visible, Func<int, int, float>? opacity = null, Func<int, int, Vector2>? texcoord = null)
    {
        if (width < 1 || height < 1 || width > 256 || height > 256)
            throw new InvalidDataException("Invalid liquid grid dimensions.");
        Vector3[] positions = new Vector3[(width + 1) * (height + 1)], normals = new Vector3[positions.Length];
        Vector2[] uv = new Vector2[positions.Length];
        Vector4[] colors = new Vector4[positions.Length];
        List<int> indices = [];
        for (int y = 0; y <= height; y++)
            for (int x = 0; x <= width; x++)
            {
                int i = y * (width + 1) + x;
                positions[i] = point(x, y);
                normals[i] = Vector3.UnitY;
                uv[i] = texcoord?.Invoke(x, y) ?? new Vector2(x / 4f, y / 4f);
                colors[i] = new(1, 1, 1, Math.Clamp(opacity?.Invoke(x, y) ?? 1, 0, 1));
            }

        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                if (visible(x, y))
                {
                    int a = y * (width + 1) + x, b = a + 1, c = a + width + 1, d = c + 1;
                    indices.AddRange([a, b, c, b, d, c]);
                }

        return new(positions, normals, uv, indices.ToArray(), $"@liquid/{type}", 2, true, Colors: colors, LiquidType: type);
    }

    public static ModelSurface Merge(IEnumerable<ModelSurface> surfaces)
    {
        var source = surfaces.ToArray();
        List<Vector3> positions = [], normals = [];
        List<Vector2> uv = [];
        List<Vector4> colors = [];
        List<int> indices = [];
        foreach (var s in source)
        {
            int offset = positions.Count;
            positions.AddRange(s.Positions);
            normals.AddRange(s.Normals);
            uv.AddRange(s.Uv);
            colors.AddRange(s.Colors!);
            indices.AddRange(s.Indices.Select(i => i + offset));
        }

        return source[0] with
        {
            Positions = positions.ToArray(),
            Normals = normals.ToArray(),
            Uv = uv.ToArray(),
            Colors = colors.ToArray(),
            Indices = indices.ToArray()
        };
    }

    public static List<ModelSurface> Terrain(byte[] bytes)
    {
        List<ModelSurface> result = [];
        HashSet<int> covered = [];
        var top = Chunks.Read(bytes).ToArray();
        var h2o = top.FirstOrDefault(c => c.Tag == "MH2O");
        if (h2o.Size > 0)
        {
            var data = new BinaryAsset(bytes.AsSpan(h2o.Data, h2o.Size).ToArray());
            data.Range(0, 256, 12);
            for (int chunk = 0; chunk < 256; chunk++)
            {
                int offset = data.I(chunk * 12), count = data.I(chunk * 12 + 4);
                if (count == 0)
                    continue;
                covered.Add(chunk);
                if (count > 64 || offset < 3072)
                    throw new InvalidDataException("Invalid MH2O layer array.");
                data.Range(offset, count, 24);
                for (int layer = 0; layer < count; layer++)
                {
                    int p = offset + layer * 24;
                    uint type = data.H(p);
                    int format = data.H(p + 2);
                    float level = data.F(p + 4);
                    int ox = data.Bytes[p + 12], oy = data.Bytes[p + 13], w = data.Bytes[p + 14], h = data.Bytes[p + 15];
                    if (w == 0 || h == 0 || ox + w > 8 || oy + h > 8 || format > 2)
                        throw new InvalidDataException("Unsupported MH2O vertex grid.");
                    int mask = data.I(p + 16), vertex = data.I(p + 20), n = (w + 1) * (h + 1);
                    if (mask != 0)
                        data.Range(mask, (w * h + 7) / 8);
                    if (vertex != 0)
                        data.Range(vertex, n, format == 0 ? 5 : format == 1 ? 8 : 1);
                    int depth = vertex + (format == 0 ? n * 4 : 0);
                    result.Add(Grid(w, h, type, (x, y) => new((chunk % 16 * 8 + ox + x) * Step, vertex != 0 && format != 2 ? data.F(vertex + (y * (w + 1) + x) * 4) : level, (chunk / 16 * 8 + oy + y) * Step), (x, y) => mask == 0 || (data.Bytes[mask + (y * w + x) / 8] & (1 << ((y * w + x) % 8))) != 0, (x, y) => vertex != 0 && format != 1 ? data.Bytes[depth + y * (w + 1) + x] / 255f : 1, (x, y) => format == 1 && vertex != 0 ? new(data.H(vertex + n * 4 + (y * (w + 1) + x) * 4) / 255f, data.H(vertex + n * 4 + (y * (w + 1) + x) * 4 + 2) / 255f) : new((ox + x) / 4f, (oy + y) / 4f)));
                }
            }
        }

        // Mixed tiles can retain legacy grids in chunks without MH2O layers.
        foreach (var chunk in top.Where(c => c.Tag == "MCNK"))
        {
            var b = new BinaryAsset(bytes);
            if (chunk.Size < 128)
                throw new InvalidDataException("Short MCNK liquid header.");
            int p = chunk.Data;
            if (covered.Contains(b.I(p + 8) * 16 + b.I(p + 4)))
                continue;
            int off = b.I(p + 96), size = b.I(p + 100);
            if (off == 0 || size <= 8)
                continue;
            int start = chunk.Start + off;
            if (start < p + 128 || start + size > p + chunk.Size)
                throw new InvalidDataException("Invalid MCLQ bounds.");
            var liquid = new BinaryAsset(bytes.AsSpan(start + 8, size - 8).ToArray());
            liquid.Range(0, 720);
            uint flags = b.U(p), type = (flags & 16) != 0 ? 3u : (flags & 32) != 0 ? 4u : (flags & 8) != 0 ? 2u : 1u;
            int cx = b.I(p + 4), cy = b.I(p + 8);
            result.Add(Grid(8, 8, type, (x, y) => new((cx * 8 + x) * Step, liquid.F(12 + (y * 9 + x) * 8), (cy * 8 + y) * Step), (x, y) => (liquid.Bytes[656 + y * 8 + x] & 15) != 15, (x, y) => type <= 2 ? liquid.Bytes[8 + (y * 9 + x) * 8] / 255f : 1));
        }

        return result.Where(s => s.Indices.Length > 0).ToList();
    }

    public static ModelSurface Wmo(byte[] bytes, uint type, uint kind = 0)
    {
        var b = new BinaryAsset(bytes);
        b.Range(0, 30);
        int vx = b.I(0), vy = b.I(4), w = b.I(8), h = b.I(12);
        if (vx != w + 1 || vy != h + 1 || w < 1 || h < 1 || w > 256 || h > 256)
            throw new InvalidDataException("Invalid WMO liquid grid.");
        int n = checked(vx * vy);
        b.Range(30, n, 8);
        b.Range(30 + n * 8, w * h);
        var origin = b.V(16);
        var surface = Grid(w, h, type, (x, y) => BinaryAsset.View(new Vector3(origin.X + x * Step, origin.Y + y * Step, b.F(34 + (y * vx + x) * 8))), (x, y) => (b.Bytes[30 + n * 8 + y * w + x] & 15) != 15, texcoord: (x, y) => kind >= 2 ? new(b.S(30 + (y * vx + x) * 8) / 255f, b.S(32 + (y * vx + x) * 8) / 255f) : new(x / 4f, y / 4f));
        // Axis conversion reverses the grid's top winding.
        for (int i = 0; i < surface.Indices.Length; i += 3)
            (surface.Indices[i + 1], surface.Indices[i + 2]) = (surface.Indices[i + 2], surface.Indices[i + 1]);
        return surface;
    }

    public static uint WmoType(uint value, uint rootFlags, uint groupFlags)
    {
        value = (rootFlags & 4) != 0 ? value : value == 15 ? 0 : value + 1;
        return value is> 0 and < 21 ? ((value - 1) & 3) switch
        {
            0 => 13u + ((groupFlags & 0x80000) != 0 ? 1u : 0u),
            1 => 14u,
            2 => 19u,
            _ => 20u
        } : value;
    }
}
