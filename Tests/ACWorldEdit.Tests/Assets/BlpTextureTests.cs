using System.Security.Cryptography;
using ACWorldEdit.Assets;
using Xunit;

namespace ACWorldEdit.Tests.Assets;

public class BlpTextureTests
{
    // 13 by 10 is not a multiple of the 4-pixel DXT block, so edge clipping is covered.
    const int W = 13, H = 10;

    static byte[] Blp(int encoding, int depth, int alphaEncoding, int dataLength, int seed)
    {
        var b = new byte[1172 + dataLength];
        "BLP2"u8.CopyTo(b);
        b[8] = (byte)encoding;
        b[9] = (byte)depth;
        b[10] = (byte)alphaEncoding;
        BitConverter.GetBytes(W).CopyTo(b, 12);
        BitConverter.GetBytes(H).CopyTo(b, 16);
        BitConverter.GetBytes(1172).CopyTo(b, 20);
        BitConverter.GetBytes(dataLength).CopyTo(b, 84);
        new Random(seed).NextBytes(b.AsSpan(148));
        return b;
    }

    static string Hash(byte[] rgba) => Convert.ToHexString(SHA256.HashData(rgba))[..16];

    // Hashes captured from the per-block-allocating decoder before it was rewritten to use spans.
    [Theory]
    [InlineData(1, 0, 0, W * H + 0, "B611A8B02E495D78")]
    [InlineData(1, 1, 0, W * H + (W * H + 7) / 8, "3566129BD175B786")]
    [InlineData(1, 4, 0, W * H + (W * H * 4 + 7) / 8, "A752420AA7E5E72E")]
    [InlineData(1, 8, 0, W * H * 2, "AAB5B1AB0F18A25E")]
    [InlineData(3, 8, 0, W * H * 4, "BD97596E34E52DC5")]
    [InlineData(2, 0, 0, 4 * 3 * 8, "3B07D4FCDE3E6936")]
    [InlineData(2, 1, 0, 4 * 3 * 8, "60AC7C51A5E04058")]
    [InlineData(2, 8, 1, 4 * 3 * 16, "57A06E41BF3DDD42")]
    [InlineData(2, 8, 7, 4 * 3 * 16, "837FDFC221C1EF26")]
    public void DecodesToKnownPixels(int encoding, int depth, int alphaEncoding, int length, string expected)
    {
        var texture = BlpTexture.Decode(Blp(encoding, depth, alphaEncoding, length, seed: 1234 + encoding * 10 + depth + alphaEncoding), 0);
        Assert.Equal((W, H), (texture.Width, texture.Height));
        Assert.Equal(expected, Hash(texture.Rgba));
    }
}
