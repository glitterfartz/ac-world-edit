using Godot;
using ACWorldEdit.Assets;

namespace ACWorldEdit;

/// <summary>
/// The terrain part of the F12 dump: the chunk under a point with its layers, alpha statistics and
/// texture decode status, plus a tile-wide count of suspicious chunks. The tile is re-read from the
/// client because the viewer keeps only the built meshes.
/// </summary>
public partial class TerrainViewer
{
    object TextureStatus(string path, Dictionary<string, object> cache)
    {
        if (path.Length == 0)
        {
            return new { path, ok = false, error = "empty (drawn with the fallback texture)" };
        }

        if (cache.TryGetValue(path, out var known))
        {
            return known;
        }

        object result;
        try
        {
            var texture = BlpTexture.Decode(store!.Read(path), 16);
            int pixels = texture.Width * texture.Height;
            long r = 0, g = 0, b = 0, a = 0;
            for (int i = 0; i < pixels; i++)
            {
                r += texture.Rgba[i * 4];
                g += texture.Rgba[i * 4 + 1];
                b += texture.Rgba[i * 4 + 2];
                a += texture.Rgba[i * 4 + 3];
            }

            result = new { path, ok = true, averageRgba = new[] { r / pixels, g / pixels, b / pixels, a / pixels } };
        }
        catch (Exception e)
        {
            result = new { path, ok = false, error = e.Message };
        }

        cache[path] = result;
        return result;
    }

    static object AlphaStats(byte[] alpha, int cell)
    {
        if (alpha.Length == 0)
        {
            return new { present = false };
        }

        int min = 255, max = 0;
        long sum = 0;
        foreach (byte value in alpha)
        {
            min = Math.Min(min, value);
            max = Math.Max(max, value);
            sum += value;
        }

        return new { present = true, min, max, mean = Math.Round((double)sum / alpha.Length, 1), atPoint = cell < alpha.Length ? (int?)alpha[cell] : null };
    }

    /// <summary>The terrain chunk under a view-space point, or why it could not be read.</summary>
    object TerrainReport(Vector3 point)
    {
        if (world == null || store == null)
        {
            return new { error = "no map open" };
        }

        try
        {
            var key = new TileKey((int)Math.Floor(point.X / TerrainTile.Size + 32), (int)Math.Floor(point.Z / TerrainTile.Size + 32));
            if (!world.Tiles.Contains(key))
            {
                return new { tile = new { x = key.X, y = key.Y }, error = "tile is not part of the map" };
            }

            var tile = world.ReadTile(store, key);
            float localX = point.X - (key.X - 32) * TerrainTile.Size, localZ = point.Z - (key.Y - 32) * TerrainTile.Size;
            int cx = Math.Clamp((int)(localX / TerrainTile.ChunkSize), 0, 15), cy = Math.Clamp((int)(localZ / TerrainTile.ChunkSize), 0, 15);
            int ax = Math.Clamp((int)((localX - cx * TerrainTile.ChunkSize) / TerrainTile.ChunkSize * 64), 0, 63), ay = Math.Clamp((int)((localZ - cy * TerrainTile.ChunkSize) / TerrainTile.ChunkSize * 64), 0, 63);
            int cell = ay * 64 + ax;
            var textures = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            object Describe(TerrainChunk c)
            {
                int stride = c.Colors != null && c.Colors.Length % 4 == 0 && c.Colors.Length > 0 ? 4 : 0;
                long[] colorSum = new long[3];
                for (int i = 0; stride > 0 && i < c.Colors!.Length; i += stride)
                {
                    for (int k = 0; k < 3; k++)
                    {
                        colorSum[k] += c.Colors[i + k];
                    }
                }

                int count = stride > 0 ? c.Colors!.Length / stride : 1;
                return new
                {
                    chunk = new { x = c.X, y = c.Y },
                    flags = $"0x{c.Flags:X}",
                    areaId = c.AreaId,
                    holeCells = System.Numerics.BitOperations.PopCount(c.CellHoles),
                    heightRange = new[] { MathF.Round(c.Heights.Min() + c.Height, 2), MathF.Round(c.Heights.Max() + c.Height, 2) },
                    hasNormals = c.Normals != null,
                    hasShadow = c.Shadow != null,
                    vertexColorMeanRgb = stride > 0 ? colorSum.Select(s => s / count).ToArray() : null,
                    droppedLayers = c.DroppedLayers,
                    layers = c.Layers.Select((l, i) => new
                    {
                        index = i,
                        texture = TextureStatus(l.Texture, textures),
                        heightTexture = l.HeightTexture.Length > 0 ? TextureStatus(l.HeightTexture, textures) : null,
                        scale = l.Scale,
                        heightScale = l.HeightScale,
                        heightOffset = l.HeightOffset,
                        alpha = AlphaStats(l.Alpha, cell),
                    }).ToList(),
                };
            }

            var here = tile.Chunks.FirstOrDefault(c => c.X == cx && c.Y == cy);
            var noLayers = tile.Chunks.Where(c => c.Layers.Count == 0).Select(c => new { c.X, c.Y }).ToList();
            var emptyBase = tile.Chunks.Where(c => c.Layers.Count > 0 && c.Layers[0].Texture.Length == 0).Select(c => new { c.X, c.Y }).ToList();
            return new
            {
                tile = new { x = key.X, y = key.Y },
                alphaCell = new { x = ax, y = ay },
                chunkUnderAim = here == null ? null : Describe(here),
                // Chunks that cannot draw a normal texture, for finding a black patch that is not under the aim.
                tileSummary = new
                {
                    chunks = tile.Chunks.Count,
                    chunksWithoutLayers = noLayers,
                    chunksWithEmptyBaseTexture = emptyBase,
                    chunksWithDroppedLayers = tile.Chunks.Count(c => c.DroppedLayers > 0),
                    chunksWithHoles = tile.Chunks.Count(c => c.CellHoles != 0),
                    texturesThatFailToDecode = tile.Chunks.SelectMany(c => c.Layers).SelectMany(l => new[] { l.Texture, l.HeightTexture }).Where(t => t.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)
                        .Select(t => TextureStatus(t, textures)).Where(s => s.GetType().GetProperty("ok")!.GetValue(s) is false).ToList(),
                },
            };
        }
        catch (Exception e)
        {
            return new { error = e.Message };
        }
    }
}
