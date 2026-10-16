using ACWorldEdit.Assets;
using Xunit;

namespace ACWorldEdit.Tests.Assets;

/// <summary>Malformed-input rejection and the synthetic golden ADT (ported from the legacy Program.cs; needs no client data).</summary>
public class MalformedInputTests
{
    static void Reject(Action action, string message)
    {
        try
        {
            action();
        }
        catch (InvalidDataException)
        {
            return;
        }
        catch (NotSupportedException)
        {
            return;
        }

        throw new Exception(message);
    }

    [Fact]
    public void TruncatedChunkRejected() => Reject(() => Chunks.Read(new byte[7]).ToList(), "Truncated chunk accepted");

    [Fact]
    public void OversizedChunkRejected() => Reject(() => Chunks.Read(new byte[] { 82, 69, 86, 77, 255, 0, 0, 0 }).ToList(), "Oversized chunk accepted");

    [Fact]
    public void UnsupportedAdtRejected() => Reject(() => new TerrainTile(new(0, 0), new byte[] { 82, 69, 86, 77, 4, 0, 0, 0, 17, 0, 0, 0 }, false), "Unsupported ADT accepted");

    [Fact]
    public void ZeroAlphaRunRejected() => Reject(() => TerrainTile.DecodeAlpha(new byte[] { 128 }, 0, 0x300, true, false), "Zero alpha run accepted");

    [Fact]
    public void ShortAlphaFillRejected() => Reject(() => TerrainTile.DecodeAlpha(new byte[] { 255 }, 0, 0x300, true, false), "Short alpha fill accepted");

    [Fact]
    public void BadWdtMainRejected() => Reject(() => new WorldMap("fixture", new byte[] { 78, 73, 65, 77, 4, 0, 0, 0, 0, 0, 0, 0 }), "Bad WDT MAIN accepted");

    [Fact]
    public void BadBlpMagicRejected() => Reject(() => BlpTexture.Decode(new byte[148]), "Bad BLP magic accepted");

    [Fact]
    public void SyntheticGoldenAdtParses()
    {
        var tile = new TerrainTile(new(32, 32), SyntheticTerrain.Tile(), false);
        Assert.True(tile.Chunks.Count == 256 && tile.Chunks[255].X == 15 && tile.Chunks[255].Y == 15 && tile.Chunks.All(c => c.Height == 10), "Synthetic golden ADT parse");
    }

    [Fact]
    public void DuplicateChunkIndexRejected() => Reject(() => new TerrainTile(new(32, 32), SyntheticTerrain.Tile(true), false), "Duplicate chunk index accepted");

    [Fact]
    public void AlphaNibbleOrderAndRle()
    {
        byte[] packed = Enumerable.Repeat((byte)0xA5, 2048).ToArray();
        var alpha = TerrainTile.DecodeAlpha(packed, 0, 0x100, false, false);
        Assert.True(alpha[0] == 85 && alpha[1] == 170, "Nibble alpha order");
        byte[] rle = Enumerable.Range(0, 32).SelectMany(_ => new byte[] { 255, 123 }).Concat(new byte[] { 160, 123 }).ToArray();
        Assert.True(TerrainTile.DecodeAlpha(rle, 0, 0x300, true, false).All(v => v == 123), "Alpha RLE");
    }
}
