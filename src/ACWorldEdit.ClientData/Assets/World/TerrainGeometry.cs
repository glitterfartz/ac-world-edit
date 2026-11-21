using System.Numerics;

namespace ACWorldEdit.Assets;

/// <summary>
/// Engine-independent topology shared by rendering and CPU picking checks. Preserve the
/// staggered 145-vertex MCNK grid and omit native hole cells.
/// </summary>
public sealed record TerrainGeometry(Vector3[] Vertices, int[] Indices)
{
    /// <summary>
    /// Emit four triangles around each inner vertex and skip native hole cells (a coarse Wrath hole
    /// covers a 2-by-2 block of cells, retail can hole single cells). Heights are relative to the
    /// chunk elevation.
    /// </summary>
    public static TerrainGeometry Build(TerrainChunk chunk)
    {
        var vertices = new Vector3[145];
        int index = 0;
        for (int row = 0; row < 17; row++)
        {
            for (int column = 0; column < ((row & 1) == 0 ? 9 : 8); column++, index++)
            {
                float x = column + ((row & 1) == 1 ? 0.5f : 0);
                vertices[index] = new((chunk.X + x / 8) * TerrainTile.ChunkSize, chunk.Height + chunk.Heights[index], (chunk.Y + row * 0.5f / 8) * TerrainTile.ChunkSize);
            }
        }

        List<int> indices = [];
        ulong holes = chunk.CellHoles;
        for (int y = 0; y < 8; y++)
        {
            for (int x = 0; x < 8; x++)
            {
                if ((holes & (1UL << (y * 8 + x))) != 0)
                {
                    continue;
                }

                int tl = y * 17 + x, tr = tl + 1, bl = tl + 17, br = bl + 1, center = tl + 9;
                indices.AddRange([tl, tr, center, tr, br, center, br, bl, center, bl, tl, center]);
            }
        }

        return new(vertices, indices.ToArray());
    }
}
