using System.Numerics;

namespace ACWorldEdit.Assets;

/// <summary>
/// CPU bake of one tile's terrain layers into a single low-LOD albedo + normal pair (the
/// viewer's stand-in for the client's terrainblendbake + terrainlow pair, whose inputs are
/// identical: the four layer textures blended by the splat weights). Runs on the streaming
/// worker; the result is uploaded like the splat atlas and never touches Godot objects.
/// fell back to the full shader whenever an input is missing so a half bake never draws.
/// </summary>
public static class TerrainLowBake
{
    /// <summary>Bake resolution: 16 texels per chunk, 256 per tile side.</summary>
    public const int TexelsPerChunk = 16;
    public const int TileTexels = 16 * TexelsPerChunk;

    // The renderer's missing-texture fallback (magenta, transparent), so the bake blends
    // exactly what the full path samples when a layer texture did not resolve.
    static readonly BlpTexture Fallback = new(2, 2, [255, 0, 255, 0, 255, 0, 255, 0, 255, 0, 255, 0, 255, 0, 255, 0]);

    /// <summary>
    /// Blend one tile's layers into a 256x256 albedo and a matching normal map. Missing layer
    /// textures blend the same magenta fallback the full path samples, so the bake always
    /// matches what the full shader would draw; a null return means the tile has no drawable
    /// layers at all and it keeps the full path. Normals prefer the authored MCNR vectors and
    /// fall back to central differences of the heightfield. All arithmetic mirrors
    /// Shaders/terrain/terrain.gdshader.
    /// </summary>
    public static (BlpTexture Albedo, BlpTexture Normal)? Bake(IReadOnlyList<TerrainChunk> chunks, IReadOnlyDictionary<string, BlpTexture> textures, bool bigAlpha)
    {
        var byCell = new Dictionary<(int X, int Y), TerrainChunk>();
        foreach (var c in chunks)
        {
            byCell[(c.X, c.Y)] = c;
        }

        byte[] albedo = new byte[TileTexels * TileTexels * 4];
        byte[] normal = new byte[TileTexels * TileTexels * 4];
        for (int ty = 0; ty < TileTexels; ty++)
        {
            for (int tx = 0; tx < TileTexels; tx++)
            {
                int cx = tx / TexelsPerChunk, cy = ty / TexelsPerChunk;
                if (!byCell.TryGetValue((cx, cy), out var chunk) || chunk.Layers.Count == 0)
                {
                    return null;
                }

                float lx = (tx % TexelsPerChunk + 0.5f) / TexelsPerChunk;
                float ly = (ty % TexelsPerChunk + 0.5f) / TexelsPerChunk;
                var rgb = TexelColor(chunk, textures, bigAlpha, lx, ly);
                var n = NormalAt(chunk, lx, ly);

                int o = (ty * TileTexels + tx) * 4;
                albedo[o] = rgb.X;
                albedo[o + 1] = rgb.Y;
                albedo[o + 2] = rgb.Z;
                albedo[o + 3] = 255;
                normal[o] = (byte)Math.Clamp((int)((n.X * 0.5f + 0.5f) * 255), 0, 255);
                normal[o + 1] = (byte)Math.Clamp((int)((n.Y * 0.5f + 0.5f) * 255), 0, 255);
                normal[o + 2] = (byte)Math.Clamp((int)((n.Z * 0.5f + 0.5f) * 255), 0, 255);
                normal[o + 3] = 255;
            }
        }

        return (new BlpTexture(TileTexels, TileTexels, albedo), new BlpTexture(TileTexels, TileTexels, normal));
    }

    static (byte X, byte Y, byte Z) TexelColor(TerrainChunk chunk, IReadOnlyDictionary<string, BlpTexture> textures, bool bigAlpha, float u, float v)
    {
        int layers = Math.Min(4, chunk.Layers.Count);
        var color = new Vector4[4];
        var alpha = new float[4];
        for (int i = 0; i < 4; i++)
        {
            if (i >= layers)
            {
                color[i] = new Vector4(1, 1, 1, 1);
                continue;
            }

            var layer = chunk.Layers[i];
            float scale = layer.Scale == 0 ? 1 : layer.Scale;
            color[i] = SampleWrapped(Lookup(textures, layer.Texture), u * (8 / scale), v * (8 / scale));
            if (i > 0)
            {
                alpha[i] = layer.Alpha.Length >= 4096
                    ? layer.Alpha[Math.Min(63, (int)(v * 64)) * 64 + Math.Min(63, (int)(u * 64))] / 255f
                    : 0;
            }
        }

        Vector4 blended;
        if (bigAlpha && chunk.Layers.Any(l => l.HeightTexture.Length > 0 && textures.ContainsKey(l.HeightTexture) && (l.HeightScale != 0 || l.HeightOffset != 1)))
        {
            var h = new Vector4(1, 1, 1, 1);
            for (int i = 0; i < 4; i++)
            {
                if (i < layers)
                {
                    var layer = chunk.Layers[i];
                    if (layer.HeightTexture.Length > 0 && textures.TryGetValue(layer.HeightTexture, out var height))
                    {
                        float scale = layer.Scale == 0 ? 1 : layer.Scale;
                        h[i] = SampleWrapped(height, u * (8 / scale), v * (8 / scale)).W * layer.HeightScale + layer.HeightOffset;
                    }
                }
            }

            var pct = new Vector4(
                Math.Max(0, 1 - alpha[1] - alpha[2] - alpha[3]) * h.X,
                alpha[1] * h.Y, alpha[2] * h.Z, alpha[3] * h.W);
            float tallest = Math.Max(Math.Max(pct.X, pct.Y), Math.Max(pct.Z, pct.W));
            pct *= Vector4.One - new Vector4(Math.Clamp(tallest - pct.X, 0, 1), Math.Clamp(tallest - pct.Y, 0, 1), Math.Clamp(tallest - pct.Z, 0, 1), Math.Clamp(tallest - pct.W, 0, 1));
            float sum = pct.X + pct.Y + pct.Z + pct.W;
            blended = sum <= 0 ? Vector4.One : (color[0] * pct.X + color[1] * pct.Y + color[2] * pct.Z + color[3] * pct.W) / sum;
        }
        else if (bigAlpha)
        {
            float w0 = Math.Max(0, 1 - alpha[1] - alpha[2] - alpha[3]);
            float sum = Math.Max(w0 + alpha[1] + alpha[2] + alpha[3], 0.001f);
            blended = (color[0] * w0 + color[1] * alpha[1] + color[2] * alpha[2] + color[3] * alpha[3]) / sum;
        }
        else
        {
            blended = Vector4.Lerp(Vector4.Lerp(Vector4.Lerp(color[0], color[1], alpha[1]), color[2], alpha[2]), color[3], alpha[3]);
        }

        return ((byte)Math.Clamp((int)(blended.X * 255), 0, 255), (byte)Math.Clamp((int)(blended.Y * 255), 0, 255), (byte)Math.Clamp((int)(blended.Z * 255), 0, 255));
    }

    static BlpTexture Lookup(IReadOnlyDictionary<string, BlpTexture> textures, string name) =>
        name.Length == 0 || !textures.TryGetValue(name, out var tex) ? Fallback : tex;

    /// <summary>Chebyshev ring at and beyond which loaded tiles use the baked low-LOD material.</summary>
    public const int LowLodRing = 2;

    /// <summary>Whether a tile is far enough from the camera tile to use the baked material.</summary>
    public static bool IsFarTile(TileKey tile, TileKey center, int ring = LowLodRing) =>
        Math.Max(Math.Abs(tile.X - center.X), Math.Abs(tile.Y - center.Y)) >= ring;

    /// <summary>Bilinear sample with repeat wrapping, matching the layer samplers' repeat_enable.</summary>
    public static Vector4 SampleWrapped(BlpTexture texture, float u, float v)
    {
        u -= MathF.Floor(u);
        v -= MathF.Floor(v);
        float x = u * texture.Width - 0.5f, y = v * texture.Height - 0.5f;
        int x0 = Mod((int)MathF.Floor(x), texture.Width), y0 = Mod((int)MathF.Floor(y), texture.Height);
        int x1 = (x0 + 1) % texture.Width, y1 = (y0 + 1) % texture.Height;
        float fx = x - MathF.Floor(x), fy = y - MathF.Floor(y);
        var a = Texel(texture, x0, y0);
        var b = Texel(texture, x1, y0);
        var c = Texel(texture, x0, y1);
        var d = Texel(texture, x1, y1);
        return Vector4.Lerp(Vector4.Lerp(a, b, fx), Vector4.Lerp(c, d, fx), fy);
    }

    static int Mod(int a, int n) => ((a % n) + n) % n;

    static Vector4 Texel(BlpTexture texture, int x, int y)
    {
        int o = (y * texture.Width + x) * 4;
        return new Vector4(texture.Rgba[o] / 255f, texture.Rgba[o + 1] / 255f, texture.Rgba[o + 2] / 255f, texture.Rgba[o + 3] / 255f);
    }

    /// <summary>
    /// Baked normal at a chunk-local UV: bilinear interpolation of the authored MCNR vectors
    /// (same transform as the mesh path) or, without them, central differences of the heightfield.
    /// </summary>
    public static Vector3 NormalAt(TerrainChunk chunk, float u, float v)
    {
        float gx = u * 8, gy = v * 8;
        if (chunk.Normals is { Length: >= 435 })
        {
            return Bilinear(
                (x, y) => Mcnr(chunk.Normals, x, y),
                gx, gy);
        }

        return Bilinear(
            (x, y) => HeightNormal(chunk.Heights, x, y),
            gx, gy);
    }

    static Vector3 Mcnr(sbyte[] normals, int x, int y)
    {
        x = Math.Clamp(x, 0, 8);
        y = Math.Clamp(y, 0, 8);
        int i = (y * 17 + x) * 3;
        var n = new Vector3(-normals[i + 1], normals[i + 2], -normals[i]);
        return n.Y < 0 ? -n : n;
    }

    static Vector3 HeightNormal(float[] heights, int x, int y)
    {
        if (heights.Length < 145)
        {
            return Vector3.UnitY;
        }

        float H(int r, int c) => heights[Math.Clamp(r, 0, 8) * 17 + Math.Clamp(c, 0, 8)];
        float s = TerrainTile.ChunkSize / 8;
        float dx = (H(y, x + 1) - H(y, x - 1)) / (2 * s);
        float dz = (H(y + 1, x) - H(y - 1, x)) / (2 * s);
        return Vector3.Normalize(new Vector3(-dx, 1, -dz));
    }

    static Vector3 Bilinear(Func<int, int, Vector3> grid, float gx, float gy)
    {
        int x0 = Math.Clamp((int)MathF.Floor(gx), 0, 7), y0 = Math.Clamp((int)MathF.Floor(gy), 0, 7);
        float fx = Math.Clamp(gx - x0, 0, 1), fy = Math.Clamp(gy - y0, 0, 1);
        var a = grid(x0, y0);
        var b = grid(x0 + 1, y0);
        var c = grid(x0, y0 + 1);
        var d = grid(x0 + 1, y0 + 1);
        var n = Vector3.Lerp(Vector3.Lerp(a, b, fx), Vector3.Lerp(c, d, fx), fy);
        return n == Vector3.Zero ? Vector3.UnitY : Vector3.Normalize(n);
    }
}
