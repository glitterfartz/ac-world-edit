using System.Buffers.Binary;

namespace ACWorldEdit.Assets;

/// <summary>
/// Builds a <see cref="TerrainTile"/> from the split retail ADT files. CPU-only and Godot-free; runs on
/// a worker. The root file holds geometry, the texture file holds layers and opacity, and both list
/// their 256 MCNK chunks in the same order (the texture MCNKs carry no index of their own).
/// Placements come from the optional obj0 file; liquids (MH2O) are not read yet, so the tile reports none.
/// </summary>
public static class RetailTerrain
{
    // The MCNK header is 128 bytes in the root file; its sub-chunk offsets are all zero in split files,
    // so sub-chunks are found by walking the chunk stream after the header.
    const int HeaderSize = 128;
    const uint HighResHoles = 0x10000;
    const uint HasVertexColors = 0x40;
    const int MaxDefinedLayers = 8;

    public static TerrainTile Read(TileKey key, byte[] root, byte[] textures, bool bigAlpha, byte[]? objects = null)
    {
        RequireVersion(root, "root");
        RequireVersion(textures, "texture");
        var textureIds = TextureNames(textures);
        var rootChunks = Chunks.Read(root).Where(c => c.Tag == "MCNK").ToList();
        var textureChunks = Chunks.Read(textures).Where(c => c.Tag == "MCNK").ToList();
        if (rootChunks.Count != 256 || textureChunks.Count != 256)
        {
            throw new InvalidDataException($"Expected 256 MCNK chunks in each file, found {rootChunks.Count} and {textureChunks.Count}");
        }

        var seen = new HashSet<(int X, int Y)>();
        var result = new List<TerrainChunk>(256);
        for (int i = 0; i < 256; i++)
        {
            var chunk = ReadGeometry(root, rootChunks[i]);
            if (!seen.Add((chunk.X, chunk.Y)))
            {
                throw new InvalidDataException("Duplicate MCNK index");
            }

            var layers = SubChunk(textures, textureChunks[i], 0, "MCLY");
            var alpha = SubChunk(textures, textureChunks[i], 0, "MCAL");
            TerrainTile.AddLayers(chunk, layers ?? [], alpha ?? [], textureIds, bigAlpha, MaxDefinedLayers);
            result.Add(chunk);
        }

        return new TerrainTile(key, result, objects == null ? [] : WorldObjects.Read(objects), []);
    }

    static void RequireVersion(byte[] file, string name)
    {
        var version = Chunks.Read(file).FirstOrDefault(c => c.Tag == "MVER");
        if (version.Size != 4 || MpqArchive.U(file, version.Data) != 18)
        {
            throw new NotSupportedException($"Expected ADT version 18 in the {name} file");
        }
    }

    /// <summary>
    /// MDID lists the diffuse texture FileDataID for each MCLY texture index. A zero entry is an unused
    /// slot and stays empty; a layer that refers to it keeps the empty name. Untextured tiles (empty
    /// MDID and empty MCLY) are valid; a layer index beyond the list still fails.
    /// </summary>
    static string[] TextureNames(byte[] textures)
    {
        var mdid = Chunks.Read(textures).FirstOrDefault(c => c.Tag == "MDID");
        if (mdid.Size % 4 != 0)
        {
            throw new InvalidDataException("Malformed MDID");
        }

        var names = new string[mdid.Size / 4];
        for (int i = 0; i < names.Length; i++)
        {
            uint id = MpqArchive.U(textures, mdid.Data + i * 4);
            names[i] = id == 0 ? "" : AssetRef.FromFileId(id);
        }

        return names;
    }

    static TerrainChunk ReadGeometry(byte[] b, ChunkHeader c)
    {
        if (c.Size < HeaderSize)
        {
            throw new InvalidDataException("Short MCNK header");
        }

        int h = c.Data;
        var chunk = new TerrainChunk
        {
            Flags = MpqArchive.U(b, h),
            X = (int)MpqArchive.U(b, h + 4),
            Y = (int)MpqArchive.U(b, h + 8),
            AreaId = MpqArchive.U(b, h + 52),
            Holes = MpqArchive.U(b, h + 60) & 0xFFFF,
            ServerX = Chunks.F(b, h + 104),
            ServerY = Chunks.F(b, h + 108),
            Height = Chunks.F(b, h + 112)
        };
        if (chunk.X is < 0 or > 15 || chunk.Y is < 0 or > 15)
        {
            throw new InvalidDataException("MCNK index out of range");
        }

        // With the high-resolution flag the 8 bytes at +20 (formerly the MCVT/MCNR offsets) are one
        // hole bit per cell, row-major (bit y * 8 + x).
        if ((chunk.Flags & HighResHoles) != 0)
        {
            chunk.HighHoles = BinaryPrimitives.ReadUInt64LittleEndian(b.AsSpan(h + 20, 8));
        }

        var heights = SubChunk(b, c, HeaderSize, "MCVT") ?? throw new InvalidDataException("Missing MCVT");
        if (heights.Length != 145 * 4)
        {
            throw new InvalidDataException("Expected 145 terrain heights");
        }

        for (int i = 0; i < 145; i++)
        {
            chunk.Heights[i] = Chunks.F(heights, i * 4);
        }

        // The stored payload is padded past the 435 normals the grid needs.
        var normals = SubChunk(b, c, HeaderSize, "MCNR");
        if (normals != null && normals.Length >= 435)
        {
            chunk.Normals = normals.Take(435).Select(v => unchecked((sbyte)v)).ToArray();
        }

        if ((chunk.Flags & HasVertexColors) != 0)
        {
            var colors = SubChunk(b, c, HeaderSize, "MCCV");
            if (colors != null && colors.Length >= 580)
            {
                chunk.Colors = colors;
            }
        }

        return chunk;
    }

    /// <summary>
    /// First sub-chunk with the tag inside an MCNK, starting <paramref name="skip"/> bytes after its
    /// payload start, or null when absent. Chunk walking enforces that each fits the MCNK.
    /// </summary>
    static byte[]? SubChunk(byte[] b, ChunkHeader mcnk, int skip, string tag)
    {
        foreach (var sub in Chunks.Read(b, mcnk.Data + skip, mcnk.Data + mcnk.Size))
        {
            if (sub.Tag == tag)
            {
                return b.AsSpan(sub.Data, sub.Size).ToArray();
            }
        }

        return null;
    }
}
