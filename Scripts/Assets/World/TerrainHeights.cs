using ACWorldEdit.Foundation;

namespace ACWorldEdit.Assets;

/// <summary>
/// Compact CPU copy of one ADT tile's height samples for on-demand ground probes. Keeps only
/// absolute chunk elevation, the 145 relative samples and the hole mask per chunk (about 150 KB per
/// tile), not textures or layers. Coordinates are tile-local view space: X/Z horizontal, Y up.
/// </summary>
public sealed class TerrainHeights
{
    const int Samples = 145;
    readonly float[] heights = new float[256 * Samples];
    readonly float[] elevation = new float[256];
    readonly uint[] holes = new uint[256];
    readonly bool[] present = new bool[256];

    public TerrainHeights(TerrainTile tile)
    {
        foreach (var chunk in tile.Chunks)
        {
            int index = chunk.Y * 16 + chunk.X;
            Array.Copy(chunk.Heights, 0, heights, index * Samples, Samples);
            elevation[index] = chunk.Height;
            holes[index] = chunk.Holes;
            present[index] = true;
        }
    }

    public long Bytes => heights.Length * sizeof(float) + elevation.Length * sizeof(float) + holes.Length * sizeof(uint) + present.Length;

    /// <summary>
    /// Return the terrain height at a tile-local X/Z point, or null outside the tile, in a missing
    /// chunk or inside a native hole. Uses the same staggered triangles as rendering so the probe
    /// matches the visible surface.
    /// </summary>
    public float? Probe(float localX, float localZ)
    {
        if (!float.IsFinite(localX) || !float.IsFinite(localZ) || localX < 0 || localZ < 0 || localX > TerrainTile.Size || localZ > TerrainTile.Size)
        {
            return null;
        }

        int cx = Math.Min(15, (int)(localX / TerrainTile.ChunkSize));
        int cy = Math.Min(15, (int)(localZ / TerrainTile.ChunkSize));
        int index = cy * 16 + cx;
        if (!present[index])
        {
            return null;
        }

        var chunk = new TerrainChunk
        {
            X = cx,
            Y = cy,
            Height = elevation[index],
            Holes = holes[index]
        };
        Array.Copy(heights, index * Samples, chunk.Heights, 0, Samples);
        var geometry = TerrainGeometry.Build(chunk);
        // Cast straight down from far above; terrain is single-valued so the first hit is the surface.
        var origin = new Point3(localX, 100000, localZ);
        var down = new Point3(0, -1, 0);
        for (int i = 0; i < geometry.Indices.Length; i += 3)
        {
            var a = geometry.Vertices[geometry.Indices[i]];
            var b = geometry.Vertices[geometry.Indices[i + 1]];
            var c = geometry.Vertices[geometry.Indices[i + 2]];
            double? hit = WorldCoordinates.Hit(origin, down, new(a.X, a.Y, a.Z), new(b.X, b.Y, b.Z), new(c.X, c.Y, c.Z));
            if (hit.HasValue)
            {
                return (float)(origin.Y - hit.Value);
            }
        }

        return null;
    }
}
