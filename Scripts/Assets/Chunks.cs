using System.Text;

namespace ACWorldEdit.Assets;
public readonly record struct ChunkHeader(string Tag, int Start, int Data, int Size);
public static class Chunks
{
    public static IEnumerable<ChunkHeader> Read(byte[] b, int start = 0, int? end = null)
    {
        int limit = end ?? b.Length;
        for (int p = start; p < limit;)
        {
            if (p + 8 > limit)
                throw new InvalidDataException("Truncated chunk header");
            string tag = Encoding.ASCII.GetString(b, p, 4);
            tag = new string (tag.Reverse().ToArray());
            int size = checked((int)MpqArchive.U(b, p + 4));
            if (size > limit - p - 8)
                throw new InvalidDataException("Invalid " + tag + " chunk size");
            yield return new(tag, p, p + 8, size);
            p += size + 8;
        }
    }

    public static float F(byte[] b, int p) => BitConverter.ToSingle(b, p);
}
