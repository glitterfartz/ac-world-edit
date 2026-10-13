using System.Text;

namespace ACWorldEdit.Assets;

/// <summary>
/// Read-only WDT terrain availability, alpha-map convention and global WMO placements. No ADTs
/// is valid when a map contains a global interior WMO.
/// </summary>
public sealed class WorldMap
{
    public string Name { get; }
    public bool BigAlpha { get; }
    public HashSet<TileKey> Tiles { get; } = [];
    public List<WorldObject> GlobalObjects { get; }

    /// <summary>
    /// Validate MAIN availability and retain global placements; reject maps only when both terrain
    /// and global objects are absent.
    /// </summary>
    public WorldMap(string name, byte[] data)
    {
        Name = name;
        foreach (var c in Chunks.Read(data))
        {
            if (c.Tag == "MPHD")
            {
                BigAlpha = (MpqArchive.U(data, c.Data) & 4) != 0;
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

        GlobalObjects = WorldObjects.Read(data, true);
        if (Tiles.Count == 0 && GlobalObjects.Count == 0)
        {
            throw new NotSupportedException("This map contains neither terrain nor a global WMO.");
        }
    }

    /// <summary>
    /// Construct the legacy unsplit ADT path for this map directory and tile.
    /// </summary>
    public string TilePath(TileKey key) => $"World/Maps/{Name}/{Name}_{key.X}_{key.Y}.adt";
}
