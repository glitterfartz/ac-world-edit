using System.Numerics;
using System.Text;

namespace ACWorldEdit.Assets;

/// <summary>
/// Bounds-checked little-endian reader shared by M2, WMO and DBC parsers. It rejects non-finite
/// float geometry and resolves byte offsets explicitly; it allocates no engine resources.
/// </summary>
public sealed class BinaryAsset(byte[] bytes)
{
    public byte[] Bytes => bytes;
    public int Length => bytes.Length;

    /// <summary>
    /// Validate range/count/stride with wide arithmetic before allocation or indexing, including
    /// nested binary array descriptors.
    /// </summary>
    public void Range(int p, int count, int stride = 1)
    {
        if (p < 0 || count < 0 || stride < 0 || (long)p + (long)count * stride > bytes.Length)
        {
            throw new InvalidDataException("Model array outside asset bounds.");
        }
    }

    /// <summary>
    /// Read an unsigned 32-bit integer (uint32) at byte offset <paramref name="p"/>.
    /// </summary>
    public uint U(int p)
    {
        Range(p, 4);
        return BitConverter.ToUInt32(bytes, p);
    }

    /// <summary>
    /// Read a signed 32-bit integer (int32). Values above <see cref="int.MaxValue"/> throw
    /// <see cref="OverflowException"/> so corrupt counts/offsets fail instead of turning negative.
    /// </summary>
    public int I(int p) => checked((int)U(p));
    /// <summary>
    /// Read an unsigned 16-bit integer (uint16, "half" word).
    /// </summary>
    public ushort H(int p)
    {
        Range(p, 2);
        return BitConverter.ToUInt16(bytes, p);
    }

    /// <summary>
    /// Read a signed 16-bit integer (int16, "short").
    /// </summary>
    public short S(int p) => unchecked((short)H(p));
    /// <summary>
    /// Read a 32-bit float; NaN and infinity are rejected as malformed geometry.
    /// </summary>
    public float F(int p)
    {
        Range(p, 4);
        float v = BitConverter.ToSingle(bytes, p);
        if (!float.IsFinite(v))
        {
            throw new InvalidDataException("Non-finite model value.");
        }

        return v;
    }

    /// <summary>
    /// Read three consecutive floats (12 bytes) as a vector in native file axes; use
    /// <see cref="View(Vector3)"/> to convert to the viewer basis.
    /// </summary>
    public Vector3 V(int p) => new(F(p), F(p + 4), F(p + 8));
    /// <summary>
    /// Read a NUL-terminated UTF-8 asset string; unterminated or out-of-range strings are malformed
    /// data.
    /// </summary>
    public string Str(int p)
    {
        Range(p, 1);
        int end = System.Array.IndexOf(bytes, (byte)0, p);
        if (end < 0)
        {
            throw new InvalidDataException("Unterminated asset string.");
        }

        return Encoding.UTF8.GetString(bytes, p, end - p);
    }

    /// <summary>
    /// Read a native count/offset pair and validate its full byte extent using the caller-provided
    /// record stride.
    /// </summary>
    public (int Count, int Offset) Array(int p, int stride)
    {
        int count = I(p), offset = I(p + 4);
        Range(offset, count, stride);
        return (count, offset);
    }

    /// <summary>
    /// Convert model vectors or quaternion axes into the Y-up viewer basis. Quaternion normalization
    /// keeps rotations usable by CPU animation and Godot.
    /// </summary>
    public static Vector3 View(Vector3 v) => new(-v.Y, v.Z, -v.X);
    /// <summary>
    /// Convert model vectors or quaternion axes into the Y-up viewer basis. Quaternion normalization
    /// keeps rotations usable by CPU animation and Godot.
    /// </summary>
    public static Quaternion View(Quaternion q) => Quaternion.Normalize(new(-q.Y, q.Z, -q.X, q.W));
    /// <summary>
    /// Normalize client separators and legacy .mdx names to the .m2 asset path used by the Wrath
    /// reader.
    /// </summary>
    public static string ModelPath(string path) => AssetRef.IsFileReference(path) ? path : System.IO.Path.ChangeExtension(path.Replace('\\', '/'), ".m2");
}
