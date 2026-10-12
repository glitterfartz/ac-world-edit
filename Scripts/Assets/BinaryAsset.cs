using System.Numerics;
using System.Text;

namespace ACWorldEdit.Assets;
public sealed class BinaryAsset(byte[] bytes)
{
    public byte[] Bytes => bytes;
    public int Length => bytes.Length;

    public void Range(int p, int count, int stride = 1)
    {
        if (p < 0 || count < 0 || stride < 0 || (long)p + (long)count * stride > bytes.Length)
            throw new InvalidDataException("Model array outside asset bounds.");
    }

    public uint U(int p)
    {
        Range(p, 4);
        return BitConverter.ToUInt32(bytes, p);
    }

    public int I(int p) => checked((int)U(p));
    public ushort H(int p)
    {
        Range(p, 2);
        return BitConverter.ToUInt16(bytes, p);
    }

    public short S(int p) => unchecked((short)H(p));
    public float F(int p)
    {
        Range(p, 4);
        float v = BitConverter.ToSingle(bytes, p);
        if (!float.IsFinite(v))
            throw new InvalidDataException("Non-finite model value.");
        return v;
    }

    public Vector3 V(int p) => new(F(p), F(p + 4), F(p + 8));
    public string Str(int p)
    {
        Range(p, 1);
        int end = System.Array.IndexOf(bytes, (byte)0, p);
        if (end < 0)
            throw new InvalidDataException("Unterminated asset string.");
        return Encoding.UTF8.GetString(bytes, p, end - p);
    }

    public (int Count, int Offset) Array(int p, int stride)
    {
        int count = I(p), offset = I(p + 4);
        Range(offset, count, stride);
        return (count, offset);
    }

    public static Vector3 View(Vector3 v) => new(-v.Y, v.Z, -v.X);
    public static Quaternion View(Quaternion q) => Quaternion.Normalize(new(-q.Y, q.Z, -q.X, q.W));
    public static string ModelPath(string path) => System.IO.Path.ChangeExtension(path.Replace('\\', '/'), ".m2");
}
