using System.Numerics;

namespace ACWorldEdit.Assets;

/// <summary>
/// Client ADT/WDT placement in viewer coordinates, including rotations and optional MODF bounds.
/// These are client scenery, not database GameObject spawns.
/// </summary>
public sealed record WorldObject(uint Id, string Path, Vector3 Position, Vector3 Rotation, float Scale, ushort DoodadSet, bool Global = false, Vector3? BoundsMinimum = null, Vector3? BoundsMaximum = null)
{
    public bool IsWmo => Path.EndsWith(".wmo", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Read MDDF/MODF placements and name tables. Static rotations include a facing bias separate
/// from creature heading conversion.
/// </summary>
public static class WorldObjects
{
    /// <summary>
    /// Resolve names, map raw ADT positions around the tile-32 origin, retain doodad-set IDs and
    /// keep MODF bounds for validation.
    /// </summary>
    public static List<WorldObject> Read(byte[] bytes, bool global = false)
    {
        var b = new BinaryAsset(bytes);
        var chunks = Chunks.Read(bytes).ToList();
        List<WorldObject> result = [];
        void Placements(string names, string offsets, string placements, int stride)
        {
            var defs = chunks.FirstOrDefault(c => c.Tag == placements);
            if (defs.Size == 0)
            {
                return;
            }

            var table = chunks.FirstOrDefault(c => c.Tag == names);
            var ids = chunks.FirstOrDefault(c => c.Tag == offsets);
            if (defs.Size % stride != 0 || table.Size == 0)
            {
                throw new InvalidDataException("Invalid world placement chunks.");
            }

            for (int i = 0; i < defs.Size / stride; i++)
            {
                int p = defs.Data + i * stride;
                int name = b.I(p), offset;
                if (global)
                {
                    offset = 0;
                }
                else
                {
                    if (name >= ids.Size / 4)
                    {
                        throw new InvalidDataException("Invalid placement name index.");
                    }

                    offset = b.I(ids.Data + name * 4);
                }

                if (offset < 0 || offset >= table.Size)
                {
                    throw new InvalidDataException("Invalid placement name offset.");
                }

                string path = b.Str(table.Data + offset);
                if (stride == 36)
                {
                    path = BinaryAsset.ModelPath(path);
                }

                var pos = b.V(p + 8);
                float origin = TerrainTile.Size * 32;
                // Global WDT zero placements are at the map origin, not the northwest ADT corner.
                if (global && pos.X == 0 && pos.Z == 0)
                {
                    pos = new(origin, pos.Y, origin);
                }

                pos -= new Vector3(origin, 0, origin);
                result.Add(new(b.U(p + 4), path, pos, b.V(p + 20), stride == 36 ? b.H(p + 32) / 1024f : 1, stride == 64 ? b.H(p + 58) : (ushort)0, global, stride == 64 ? b.V(p + 32) - new Vector3(origin, 0, origin) : null, stride == 64 ? b.V(p + 44) - new Vector3(origin, 0, origin) : null));
            }
        }

        Placements("MMDX", "MMID", "MDDF", 36);
        Placements("MWMO", "MWID", "MODF", 64);
        return result;
    }

    /// <summary>
    /// CPU row-vector equivalent of ModelLibrary.Placement. Do not reuse the static half-turn facing
    /// correction for creature models.
    /// </summary>
    public static Matrix4x4 Transform(WorldObject item)
    {
        float radians = MathF.PI / 180;
        return Matrix4x4.CreateScale(item.Scale) * Matrix4x4.CreateRotationY(MathF.PI) * Matrix4x4.CreateRotationZ(item.Rotation.Z * radians) * Matrix4x4.CreateRotationX(item.Rotation.X * radians) * Matrix4x4.CreateRotationY(item.Rotation.Y * radians) * Matrix4x4.CreateTranslation(item.Position);
    }
}
