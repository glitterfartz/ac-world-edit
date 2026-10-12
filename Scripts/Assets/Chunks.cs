using System.Text;

namespace ACWorldEdit.Assets;

/// <summary>
/// Validated readable FourCC, absolute header/payload offsets and payload size. Native FourCC
/// bytes are reversed into the readable tag.
/// </summary>
public readonly record struct ChunkHeader(string Tag, int Start, int Data, int Size);
/// <summary>
/// Bounded iterator over ADT/WDT/WMO chunk streams. Subchunk callers supply explicit enclosing
/// bounds; unknown tags remain available for deliberate omission.
/// </summary>
public static class Chunks
{
    /// <summary>
    /// Read reversed FourCC headers and require each payload to fit its enclosing stream before
    /// advancing.
    /// </summary>
    public static IEnumerable<ChunkHeader> Read(byte[] b, int start = 0, int? end = null)
    {
        int limit = end ?? b.Length;
        for (int p = start; p < limit;)
        {
            if (p + 8 > limit)
            {
                throw new InvalidDataException("Truncated chunk header");
            }

            string tag = Encoding.ASCII.GetString(b, p, 4);
            tag = new string(tag.Reverse().ToArray());
            int size = checked((int)MpqArchive.U(b, p + 4));
            if (size > limit - p - 8)
            {
                throw new InvalidDataException("Invalid " + tag + " chunk size");
            }

            yield return new(tag, p, p + 8, size);
            p += size + 8;
        }
    }

    public static float F(byte[] b, int p) => BitConverter.ToSingle(b, p);
}
