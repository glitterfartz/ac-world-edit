using System.Buffers.Binary;

namespace ACWorldEdit.Assets;

/// <summary>
/// Little-endian primitive reads from raw client bytes. Out-of-range offsets throw
/// <see cref="ArgumentOutOfRangeException"/>; callers own the payload bounds.
/// </summary>
public static class LittleEndian
{
    public static uint U32(byte[] data, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, 4));
    public static ushort U16(byte[] data, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset, 2));
    public static uint U32(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4));
    public static ushort U16(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset, 2));
    /// <summary>Raw 32-bit float; unlike <see cref="BinaryAsset.F"/> it does not reject non-finite values.</summary>
    public static float F32(byte[] data, int offset) => BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(offset, 4));
}
