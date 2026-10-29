using System.Text;

namespace ACWorldEdit.Assets;

/// <summary>
/// Zero-based ADT column/row in a 64-by-64 map. This is asset identity, independent of
/// scene-node lifetime or server coordinates.
/// </summary>
public readonly record struct TileKey(int X, int Y);
/// <summary>
/// Terrain texture and decoded 64-by-64 opacity. The first layer supplies base color and
/// therefore has no alpha map. Scale divides the texture repeats per chunk (retail MTXP: 1, 2, 4, 8).
/// </summary>
public sealed record TerrainLayer(string Texture, byte[] Alpha, float Scale = 1f, string HeightTexture = "", float HeightScale = 0f, float HeightOffset = 1f);

/// <summary>Retail per-texture terrain parameters (MTXP, MHID): texture scale and the height-blend inputs.</summary>
public readonly record struct TerrainTextureParams(float Scale, string HeightTexture, float HeightScale, float HeightOffset);
/// <summary>
/// One MCNK height/normal/color/shadow/layer payload. X/Y locate it in the tile; Height is
/// absolute elevation and Heights are relative native samples.
/// </summary>
public sealed class TerrainChunk
{
    public int X, Y;
    public uint Flags, Holes, AreaId;

    /// <summary>
    /// Per-cell hole mask of the 8-by-8 cells, bit y * 8 + x. Null for the 4-by-4 <see cref="Holes"/>
    /// form (Wrath); retail chunks flagged 0x10000 store this mask directly.
    /// </summary>
    public ulong? HighHoles;

    /// <summary>
    /// Hole mask in the 8-by-8 cell form regardless of how the client stored it; a coarse hole covers
    /// its 2-by-2 block of cells.
    /// </summary>
    public ulong CellHoles => HighHoles ?? ExpandHoles(Holes);

    static ulong ExpandHoles(uint coarse)
    {
        ulong cells = 0;
        for (int y = 0; y < 8; y++)
        {
            for (int x = 0; x < 8; x++)
            {
                if ((coarse & (1u << (y / 2 * 4 + x / 2))) != 0)
                {
                    cells |= 1UL << (y * 8 + x);
                }
            }
        }

        return cells;
    }

    public float Height;
    public float ServerX, ServerY;
    public float[] Heights = new float[145];
    public sbyte[]? Normals;
    public byte[]? Colors, Shadow;
    public List<TerrainLayer> Layers = [];

    /// <summary>
    /// Layers the file defines beyond the four the terrain shader blends; they are not drawn.
    /// Only retail files (up to eight layers) produce this.
    /// </summary>
    public int DroppedLayers;
}

/// <summary>
/// CPU-only ADT version 18 parse with 256 chunks, static placements and liquids batched by type.
/// Workers decode; the main thread builds Godot resources later.
/// </summary>
public sealed class TerrainTile
{
    public const float Size = 1600f / 3f;
    public const float ChunkSize = Size / 16;
    public TileKey Key { get; }
    public List<TerrainChunk> Chunks { get; } = [];
    public List<WorldObject> Objects { get; }
    public List<ModelSurface> Liquids { get; }

    /// <summary>
    /// Assemble a tile whose chunks, placements and liquids another reader already decoded (the
    /// split retail files). Requires the same complete 256-chunk grid as the Wrath reader.
    /// </summary>
    internal TerrainTile(TileKey key, List<TerrainChunk> chunks, List<WorldObject> objects, List<ModelSurface> liquids)
    {
        if (chunks.Count != 256)
        {
            throw new InvalidDataException($"Expected 256 MCNK chunks, found {chunks.Count}");
        }

        Key = key;
        Chunks = chunks;
        Objects = objects;
        Liquids = liquids;
    }

    /// <summary>
    /// Read MCNK fields and relative subchunk offsets, preserve holes/materials and batch liquid
    /// grids. Duplicate/out-of-range chunks or unsupported layouts fail explicitly.
    /// </summary>
    public TerrainTile(TileKey key, byte[] b, bool bigAlpha)
    {
        Key = key;
        var top = Assets.Chunks.Read(b).ToList();
        Objects = WorldObjects.Read(b, top: top);
        var version = top.FirstOrDefault(c => c.Tag == "MVER");
        if (version.Size != 4 || LittleEndian.U32(b, version.Data) != 18)
        {
            throw new NotSupportedException("Expected ADT version 18");
        }

        var mtex = top.FirstOrDefault(c => c.Tag == "MTEX");
        string[] textures = Encoding.UTF8.GetString(b, mtex.Data, mtex.Size).Split('\0', StringSplitOptions.RemoveEmptyEntries);
        var chunkKeys = new HashSet<(int X, int Y)>();
        foreach (var c in top.Where(c => c.Tag == "MCNK"))
        {
            if (c.Size < 128)
            {
                throw new InvalidDataException("Short MCNK header");
            }

            int h = c.Data;
            var chunk = new TerrainChunk
            {
                Flags = LittleEndian.U32(b, h),
                X = (int)LittleEndian.U32(b, h + 4),
                Y = (int)LittleEndian.U32(b, h + 8),
                Holes = LittleEndian.U32(b, h + 60) & 0xFFFF,
                AreaId = LittleEndian.U32(b, h + 52),
                Height = Assets.LittleEndian.F32(b, h + 112)
            };
            chunk.ServerX = Assets.LittleEndian.F32(b, h + 104);
            chunk.ServerY = Assets.LittleEndian.F32(b, h + 108);
            if (chunk.X is < 0 or > 15 || chunk.Y is < 0 or > 15)
            {
                throw new InvalidDataException("MCNK index out of range");
            }

            if (!chunkKeys.Add((chunk.X, chunk.Y)))
            {
                throw new InvalidDataException("Duplicate MCNK index");
            }

            // MCNK offsets start at its chunk header, not at the payload (h). The parent
            // boundary stays authoritative even when a nested size header is unreliable.
            byte[] Sub(int field, string tag, bool optional = false)
            {
                int offset = checked((int)LittleEndian.U32(b, h + field));
                if (offset == 0 && optional)
                {
                    return [];
                }

                int p = c.Start + offset;
                if (p < h + 128 || p + 8 > h + c.Size)
                {
                    throw new InvalidDataException("Bad " + tag + " offset");
                }

                string actual = ACWorldEdit.Assets.Chunks.Tag(b, p);
                int size = checked((int)LittleEndian.U32(b, p + 4));
                // Wrath sometimes leaves the MCAL/MCSH subheader size zero; MCNK carries the authoritative size including its header.
                if (tag == "MCAL")
                {
                    size = checked((int)LittleEndian.U32(b, h + 40)) - 8;
                }

                if (tag == "MCSH")
                {
                    size = checked((int)LittleEndian.U32(b, h + 48)) - 8;
                }

                if (actual != tag || size < 0 || size > h + c.Size - p - 8)
                {
                    throw new InvalidDataException("Invalid " + tag + " subchunk");
                }

                return b.AsSpan(p + 8, size).ToArray();
            }

            var heights = Sub(20, "MCVT");
            if (heights.Length != 145 * 4)
            {
                throw new InvalidDataException("Expected 145 terrain heights");
            }

            for (int i = 0; i < 145; i++)
            {
                chunk.Heights[i] = Assets.LittleEndian.F32(heights, i * 4);
            }

            var normals = Sub(24, "MCNR", true);
            if (normals.Length >= 435)
            {
                chunk.Normals = normals.Take(435).Select(v => unchecked((sbyte)v)).ToArray();
            }

            var colors = (chunk.Flags & 0x40) != 0 ? Sub(116, "MCCV", true) : [];
            if (colors.Length >= 580)
            {
                chunk.Colors = colors;
            }

            var shadow = (chunk.Flags & 1) != 0 ? Sub(44, "MCSH", true) : [];
            if (shadow.Length >= 512)
            {
                chunk.Shadow = shadow;
            }

            AddLayers(chunk, Sub(28, "MCLY"), Sub(36, "MCAL", true), textures, bigAlpha);

            Chunks.Add(chunk);
        }

        // Batch only within this tile/type so streaming keeps ownership simple and
        // transparent surfaces do not create a draw call for every 8-by-8 liquid grid.
        Liquids = LiquidData.Terrain(b, shared: top).GroupBy(s => s.LiquidType).Select(LiquidData.Merge).ToList();
        if (Chunks.Count != 256)
        {
            throw new InvalidDataException($"Expected 256 MCNK chunks, found {Chunks.Count}");
        }
    }

    /// <summary>
    /// Decode MCLY layer records and their MCAL opacity into <paramref name="chunk"/>. The texture names
    /// are whatever identifies a texture for the client (paths for Wrath, FileDataID references for
    /// retail); an empty name marks an unusable slot (FileDataID 0): the layer is kept so the caller can
    /// report it and the renderer shows its fallback texture.
    /// Wrath allows four layers; retail files may define up to eight, of which the shader blends the
    /// first four and the rest are counted in <see cref="TerrainChunk.DroppedLayers"/>.
    /// </summary>
    internal static void AddLayers(TerrainChunk chunk, byte[] layers, byte[] alpha, IReadOnlyList<string> textures, bool bigAlpha, int maxDefined = 4, IReadOnlyList<TerrainTextureParams>? parameters = null)
    {
        int count = layers.Length / 16;
        // Shipped ADTs contain untextured chunks with an empty MCLY (for example
        // ChamberOfAspectsRed_30_25); they still carry heights and render untextured.
        if (count > maxDefined || layers.Length % 16 != 0)
        {
            throw new InvalidDataException("Invalid terrain layer count");
        }

        chunk.DroppedLayers = Math.Max(0, count - 4);
        count -= chunk.DroppedLayers;
        for (int i = 0; i < count; i++)
        {
            int p = i * 16, texture = checked((int)LittleEndian.U32(layers, p));
            if (texture >= textures.Count)
            {
                throw new InvalidDataException("Bad terrain texture index");
            }

            uint flags = LittleEndian.U32(layers, p + 4);
            byte[] map = i == 0 ? [] : DecodeAlpha(alpha, (int)LittleEndian.U32(layers, p + 8), flags, bigAlpha, (chunk.Flags & 0x8000) == 0);
            var extra = parameters != null && texture < parameters.Count ? parameters[texture] : new TerrainTextureParams(1f, "", 0f, 1f);
            chunk.Layers.Add(new(textures[texture], map, extra.Scale, extra.HeightTexture, extra.HeightScale, extra.HeightOffset));
        }
    }

    /// <summary>
    /// Expand packed nibbles, full bytes or RLE opacity into a 64-by-64 map. Flags choose
    /// compression and optional edge repair; base layers bypass this path.
    /// </summary>
    public static byte[] DecodeAlpha(byte[] data, int offset, uint flags, bool big, bool fixEdges)
    {
        byte[] output = new byte[4096];
        if ((flags & 0x100) == 0)
        {
            Array.Fill(output, (byte)255);
            return output;
        }

        if ((flags & 0x200) != 0)
        {
            int p = offset, o = 0;
            while (o < 4096)
            {
                if (p >= data.Length)
                {
                    throw new InvalidDataException($"Truncated compressed alpha: input={data.Length}, start={offset}, produced={o}, flags={flags:X}");
                }

                int control = data[p++], count = control & 127;
                if (count == 0 || o + count > 4096)
                {
                    throw new InvalidDataException("Invalid alpha run");
                }

                if ((control & 128) != 0)
                {
                    if (p >= data.Length)
                    {
                        throw new InvalidDataException("Short alpha fill");
                    }

                    Array.Fill(output, data[p++], o, count);
                }
                else
                {
                    data.AsSpan(p, count).CopyTo(output.AsSpan(o));
                    p += count;
                }

                o += count;
            }
        }
        else if (big)
        {
            data.AsSpan(offset, 4096).CopyTo(output);
        }
        else
        {
            for (int i = 0; i < 2048; i++)
            {
                byte v = data[offset + i];
                output[i * 2] = (byte)((v & 15) * 17);
                output[i * 2 + 1] = (byte)((v >> 4) * 17);
            }
        }

        if (fixEdges)
        {
            for (int y = 0; y < 64; y++)
            {
                output[y * 64 + 63] = output[y * 64 + 62];
            }

            Array.Copy(output, 62 * 64, output, 63 * 64, 64);
        }

        return output;
    }
}
