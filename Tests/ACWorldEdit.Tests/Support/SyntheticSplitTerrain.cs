using System.Text;

namespace ACWorldEdit.Assets;

/// <summary>
/// Deterministic split (retail) ADT and WDT generator: a root file with 128-byte MCNK headers whose
/// sub-chunk offsets are zero, a texture file with header-less MCNKs, and a MAID-bearing WDT.
/// </summary>
public static class SyntheticSplitTerrain
{
    public sealed record Options
    {
        public uint ChunkFlags { get; init; } = 0x8000;
        public ulong? HighHoles { get; init; }
        public uint[] Textures { get; init; } = [1000, 1001];
        public byte[]? LayerAlpha { get; init; }
        public uint LayerFlags { get; init; } = 0x100;
        public int LayerTexture { get; init; } = 1;
        public int ChunkCount { get; init; } = 256;

        /// <summary>Additional layers after the blended one, each reusing the same alpha map and texture index.</summary>
        public int ExtraLayers { get; init; }
        public bool SkipMdid { get; init; }

        /// <summary>Emit empty MCLY chunks, as untextured retail tiles do.</summary>
        public bool NoLayers { get; init; }
        public uint Version { get; init; } = 18;
    }

    static void Chunk(BinaryWriter w, string tag, byte[] payload)
    {
        w.Write(Encoding.ASCII.GetBytes(new string(tag.Reverse().ToArray())));
        w.Write(payload.Length);
        w.Write(payload);
    }

    static byte[] Sub(string tag, byte[] payload)
    {
        using var m = new MemoryStream();
        using var w = new BinaryWriter(m);
        Chunk(w, tag, payload);
        return m.ToArray();
    }

    /// <summary>Root file: height of chunk (x, y) is 100 + y * 16 + x; every vertex sample is the sample index.</summary>
    public static byte[] Root(Options? options = null)
    {
        options ??= new Options();
        using var m = new MemoryStream();
        using var w = new BinaryWriter(m);
        Chunk(w, "MVER", BitConverter.GetBytes(options.Version));
        Chunk(w, "MHDR", new byte[64]);
        for (int i = 0; i < options.ChunkCount; i++)
        {
            int x = i % 16, y = i / 16;
            using var body = new MemoryStream();
            using var f = new BinaryWriter(body);
            byte[] header = new byte[128];
            BitConverter.GetBytes(options.ChunkFlags).CopyTo(header, 0);
            BitConverter.GetBytes(x).CopyTo(header, 4);
            BitConverter.GetBytes(y).CopyTo(header, 8);
            BitConverter.GetBytes(77u).CopyTo(header, 52);
            if (options.HighHoles is ulong holes)
            {
                BitConverter.GetBytes(holes).CopyTo(header, 20);
            }

            BitConverter.GetBytes(100f + y * 16 + x).CopyTo(header, 112);
            f.Write(header);
            byte[] heights = new byte[580];
            for (int s = 0; s < 145; s++)
            {
                BitConverter.GetBytes((float)s).CopyTo(heights, s * 4);
            }

            f.Write(Sub("MCVT", heights));
            f.Write(Sub("MCNR", new byte[448]));
            f.Write(Sub("MCSE", []));
            Chunk(w, "MCNK", body.ToArray());
        }

        return m.ToArray();
    }

    /// <summary>Texture file: base layer plus one blended layer (when the options give an alpha map).</summary>
    public static byte[] TextureFile(Options? options = null)
    {
        options ??= new Options();
        using var m = new MemoryStream();
        using var w = new BinaryWriter(m);
        Chunk(w, "MVER", BitConverter.GetBytes(options.Version));
        Chunk(w, "MAMP", [0, 0, 0, 0]);
        if (!options.SkipMdid)
        {
            byte[] ids = new byte[options.Textures.Length * 4];
            for (int i = 0; i < options.Textures.Length; i++)
            {
                BitConverter.GetBytes(options.Textures[i]).CopyTo(ids, i * 4);
            }

            Chunk(w, "MDID", ids);
        }

        for (int i = 0; i < options.ChunkCount; i++)
        {
            using var body = new MemoryStream();
            using var f = new BinaryWriter(body);
            int layerCount = options.NoLayers ? 0 : options.LayerAlpha == null ? 1 : 2 + options.ExtraLayers;
            byte[] layers = new byte[layerCount * 16];
            for (int layer = 1; layer < layerCount; layer++)
            {
                BitConverter.GetBytes(options.LayerTexture).CopyTo(layers, layer * 16);
                BitConverter.GetBytes(options.LayerFlags).CopyTo(layers, layer * 16 + 4);
            }

            f.Write(Sub("MCLY", layers));
            if (options.LayerAlpha != null && layerCount > 0)
            {
                f.Write(Sub("MCAL", options.LayerAlpha));
            }

            Chunk(w, "MCNK", body.ToArray());
        }

        return m.ToArray();
    }

    /// <summary>WDT with MAIN flags for the given tiles and a MAID row (root, obj0, obj1, tex0, lod) for each.</summary>
    public static byte[] Wdt(uint mphdFlags, params (int X, int Y, uint Root, uint Textures, uint Objects)[] tiles)
    {
        using var m = new MemoryStream();
        using var w = new BinaryWriter(m);
        Chunk(w, "MVER", BitConverter.GetBytes(18));
        byte[] mphd = new byte[32];
        BitConverter.GetBytes(mphdFlags).CopyTo(mphd, 0);
        Chunk(w, "MPHD", mphd);
        byte[] main = new byte[64 * 64 * 8];
        byte[] maid = new byte[64 * 64 * 32];
        foreach (var t in tiles)
        {
            int index = t.Y * 64 + t.X;
            BitConverter.GetBytes(1u).CopyTo(main, index * 8);
            BitConverter.GetBytes(t.Root).CopyTo(maid, index * 32);
            BitConverter.GetBytes(t.Objects).CopyTo(maid, index * 32 + 4);
            BitConverter.GetBytes(t.Textures).CopyTo(maid, index * 32 + 12);
        }

        Chunk(w, "MAIN", main);
        Chunk(w, "MAID", maid);
        return m.ToArray();
    }
}
