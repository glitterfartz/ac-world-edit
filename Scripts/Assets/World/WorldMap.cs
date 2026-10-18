using System.Text;

namespace ACWorldEdit.Assets;

/// <summary>
/// FileDataIDs of one retail tile (MAID): the root ADT (heights, normals, liquids), the texture file
/// (layers and alpha) and the object file (placements; 0 when the tile has none).
/// </summary>
public readonly record struct TileFiles(uint Root, uint Textures, uint Objects);

/// <summary>
/// Read-only WDT terrain availability, alpha-map convention and global WMO placements. No ADTs
/// is valid when a map contains a global interior WMO.
/// </summary>
public sealed class WorldMap
{
    public string Name { get; }
    public bool BigAlpha { get; }
    public HashSet<TileKey> Tiles { get; } = [];

    /// <summary>
    /// Per-tile FileDataIDs for split (retail) maps; empty for Wrath, whose tiles are named by path.
    /// </summary>
    public Dictionary<TileKey, TileFiles> Files { get; } = [];

    /// <summary>
    /// Tiles the WDT marks as present but whose MAID entry lacks a root or texture file. They are left
    /// out of <see cref="Tiles"/> instead of failing the whole map.
    /// </summary>
    public int IncompleteTiles { get; }

    public bool IsSplit => Files.Count > 0;
    public List<WorldObject> GlobalObjects { get; }

    /// <summary>
    /// Validate MAIN availability and retain global placements; reject maps only when both terrain
    /// and global objects are absent.
    /// </summary>
    public WorldMap(string name, byte[] data)
    {
        Name = name;
        var chunks = Chunks.Read(data).ToList();
        uint flags = 0;
        foreach (var c in chunks)
        {
            if (c.Tag == "MPHD")
            {
                flags = MpqArchive.U(data, c.Data);
                BigAlpha = (flags & 4) != 0;
            }

            if (c.Tag == "MAIN")
            {
                if (c.Size != 64 * 64 * 8)
                {
                    throw new InvalidDataException("Unexpected WDT MAIN size");
                }

                for (int y = 0; y < 64; y++)
                {
                    for (int x = 0; x < 64; x++)
                    {
                        if ((MpqArchive.U(data, c.Data + (y * 64 + x) * 8) & 1) != 0)
                        {
                            Tiles.Add(new(x, y));
                        }
                    }
                }
            }
        }

        var maid = chunks.FirstOrDefault(c => c.Tag == "MAID");
        if (maid.Size > 0)
        {
            // Retail: MAID holds eight FileDataIDs per tile in MAIN order. Uncompressed alpha is always
            // 4096 bytes there (0x80 is the height-texturing flag that ships with it; observed on
            // 12.1.5, where the 0x4 flag is clear).
            if (maid.Size != 64 * 64 * 32)
            {
                throw new InvalidDataException("Unexpected WDT MAID size");
            }

            BigAlpha |= (flags & 0x80) != 0;
            foreach (var key in Tiles.ToList())
            {
                int p = maid.Data + (key.Y * 64 + key.X) * 32;
                var files = new TileFiles(MpqArchive.U(data, p), MpqArchive.U(data, p + 12), MpqArchive.U(data, p + 4));
                if (files.Root == 0 || files.Textures == 0)
                {
                    Tiles.Remove(key);
                    IncompleteTiles++;
                    continue;
                }

                Files[key] = files;
            }
        }

        GlobalObjects = WorldObjects.Read(data, true);
        if (Tiles.Count == 0 && GlobalObjects.Count == 0)
        {
            throw new NotSupportedException("This map contains neither terrain nor a global WMO.");
        }
    }

    /// <summary>
    /// Read and decode one tile with the format this map uses: split retail files addressed by
    /// FileDataID, or the single Wrath ADT. Worker-safe; creates no engine resources.
    /// </summary>
    public TerrainTile ReadTile(IClientFiles files, TileKey key)
    {
        if (Files.TryGetValue(key, out var split))
        {
            return RetailTerrain.Read(key, files.Read(AssetRef.FromFileId(split.Root)), files.Read(AssetRef.FromFileId(split.Textures)), BigAlpha, split.Objects == 0 ? null : files.Read(AssetRef.FromFileId(split.Objects)), RetailLiquids.For(files));
        }

        return new TerrainTile(key, files.Read(TilePath(key)), BigAlpha);
    }

    /// <summary>
    /// Construct the legacy unsplit ADT path for this map directory and tile.
    /// </summary>
    public string TilePath(TileKey key) => $"World/Maps/{Name}/{Name}_{key.X}_{key.Y}.adt";
}
