using System.Numerics;

namespace ACWorldEdit.Assets;

/// <summary>
/// One placed fog volume of a retail map: an oriented box of haze (a VFOG entry of the map's <c>_fogs.wdt</c>, with the
/// optional VFEX data that shares its <see cref="Id"/>). The fields are laid out as on https://wowdev.wiki/WDT#VFOG;
/// what the client's volume-fog compute shader does with each is documented on <see cref="ThicknessPerYard"/>,
/// <see cref="Height"/> and the other derived members, and is partly inferred.
/// </summary>
/// <param name="Color">Fog color, 0 to 1 per channel.</param>
/// <param name="Intensity">Three floats: thickness, an edge softness and a height in yards.</param>
/// <param name="Unknown18">A float the wiki cannot name (0 to 5000).</param>
/// <param name="Position">Center, in server axes.</param>
/// <param name="Rotation">Orientation as a quaternion (x, y, z, w) in server axes.</param>
/// <param name="Radius">Three floats: the box's size along its x and y axes in yards and an edge falloff power.</param>
/// <param name="AnimationPeriods">Periods (milliseconds) of the volume's noise animation.</param>
/// <param name="Flags">Flags the client keeps; unused here.</param>
/// <param name="ModelFileDataId">The M2 the client uses as the volume's proxy shape (0 means the error cube).</param>
/// <param name="FogLevel">0 to 2: the client only uses a volume whose level is at most its volume fog quality setting.</param>
/// <param name="Id">Unique across the map's files; VFEX entries refer to it.</param>
/// <param name="Extra">The first three floats of the VFEX entry with this id (they alone hold real values), if any.</param>
public sealed record MapFogVolume(Vector3 Color, float[] Intensity, float Unknown18, Vector3 Position, Quaternion Rotation, float[] Radius, int[] AnimationPeriods, uint Flags, uint ModelFileDataId, uint FogLevel, uint Id, float[]? Extra)
{
    /// <summary>Extinction per yard along a ray through the volume's middle (the thickness, scaled to read as haze rather than a wall).</summary>
    public float ThicknessPerYard => Intensity[0] * ThicknessScale;

    /// <summary>Yards the box measures along its local x, y and z axes (the shader's box size).</summary>
    public Vector3 Size => new(Radius[0], Radius[1], Intensity[2]);

    /// <summary>The power that shapes how density falls from the box's faces toward its middle.</summary>
    public float EdgePower => Math.Max(Radius[2], 0.1f);

    /// <summary>
    /// Scale from the stored thickness to extinction per yard. The client's constant buffer is not in the shipped bytecode,
    /// so this is chosen to give the stock volumes (0.007 to 0.17) a haze of a few tenths of opacity across a few
    /// hundred yards.
    /// </summary>
    public const float ThicknessScale = 0.01f;
}

/// <summary>
/// Reader for a retail map's <c>_fogs.wdt</c>: chunks of tag (reversed in the file) and size, holding VFOG entries of
/// 104 bytes and the VFEX entries of 96 bytes that extend them.
/// </summary>
public static class FogVolumes
{
    const int VfogEntrySize = 104;
    const int VfexEntrySize = 96;
    // VFEX: a default-1 uint, sixteen floats, then the id of the VFOG entry it extends.
    const int VfexFloats = 4;
    const int VfexId = 0x44;

    /// <summary>The path a map's fog volumes are stored at, for a map directory name.</summary>
    public static string PathFor(string directory) => $"World/Maps/{directory}/{directory}_fogs.wdt";

    /// <summary>
    /// A map's fog volumes, or an empty list when the client has no <c>_fogs.wdt</c> for it (classic continents, MPQ clients).
    /// </summary>
    public static IReadOnlyList<MapFogVolume> Load(IClientFiles files, string directory)
    {
        string path = PathFor(directory);
        return files.Exists(path) ? Read(files.Read(path)) : [];
    }

    /// <summary>Read every VFOG entry, joining each to its VFEX entry. Anything that is not a whole entry is ignored.</summary>
    public static IReadOnlyList<MapFogVolume> Read(byte[] data)
    {
        var entries = new List<byte[]>();
        Dictionary<uint, float[]> extras = [];
        int offset = 0;
        while (offset + 8 <= data.Length)
        {
            string tag = new([(char)data[offset + 3], (char)data[offset + 2], (char)data[offset + 1], (char)data[offset]]);
            uint size = BitConverter.ToUInt32(data, offset + 4);
            int body = offset + 8;
            if (size > data.Length - body)
            {
                break;
            }

            if (tag == "VFOG")
            {
                for (int at = 0; at + VfogEntrySize <= size; at += VfogEntrySize)
                {
                    entries.Add(data.AsSpan(body + at, VfogEntrySize).ToArray());
                }
            }
            else if (tag == "VFEX")
            {
                for (int at = 0; at + VfexEntrySize <= size; at += VfexEntrySize)
                {
                    uint id = BitConverter.ToUInt32(data, body + at + VfexId);
                    extras[id] = [BitConverter.ToSingle(data, body + at + VfexFloats), BitConverter.ToSingle(data, body + at + VfexFloats + 4), BitConverter.ToSingle(data, body + at + VfexFloats + 8)];
                }
            }

            offset = body + (int)size;
        }

        return [.. entries.Select(Entry).Select(e => extras.TryGetValue(e.Id, out var extra) ? e with { Extra = extra } : e)];
    }

    static MapFogVolume Entry(byte[] e)
    {
        float F(int at) => BitConverter.ToSingle(e, at);
        uint U(int at) => BitConverter.ToUInt32(e, at);
        return new(
            new(F(0x00), F(0x04), F(0x08)),
            [F(0x0C), F(0x10), F(0x14)],
            F(0x18),
            new(F(0x1C), F(0x20), F(0x24)),
            new(F(0x2C), F(0x30), F(0x34), F(0x38)),
            [F(0x3C), F(0x40), F(0x44)],
            [(int)U(0x48), (int)U(0x4C), (int)U(0x50), (int)U(0x54)],
            U(0x58), U(0x5C), U(0x60), U(0x64), null);
    }
}
