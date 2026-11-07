using ACWorldEdit.Assets;
using Xunit;

namespace ACWorldEdit.Tests.Assets;

/// <summary>
/// The low-LOD terrain bake: layer blending matches the full terrain shader's three modes,
/// normals come from authored MCNR data or heightfield central differences, and missing
/// structure falls back to the full path (null) instead of drawing a half bake.
/// </summary>
public class TerrainLowBakeTests
{
    static BlpTexture Solid(int w, int h, byte r, byte g, byte b, byte a = 255)
    {
        var rgba = new byte[w * h * 4];
        for (int i = 0; i < rgba.Length; i += 4) { rgba[i] = r; rgba[i + 1] = g; rgba[i + 2] = b; rgba[i + 3] = a; }
        return new BlpTexture(w, h, rgba);
    }

    static TerrainLayer Layer(string texture, byte fill = 0, string height = "", float heightScale = 0, float heightOffset = 1)
    {
        var alpha = new byte[4096];
        Array.Fill(alpha, fill);
        return new TerrainLayer(texture, alpha, 1, height, heightScale, heightOffset);
    }

    static List<TerrainChunk> Tile(params TerrainLayer[] layers)
    {
        var chunks = new List<TerrainChunk>();
        for (int y = 0; y < 16; y++)
            for (int x = 0; x < 16; x++)
                chunks.Add(new() { X = x, Y = y, Layers = [.. layers], Heights = new float[145] });
        return chunks;
    }

    static Dictionary<string, BlpTexture> Decoded(params (string Name, BlpTexture Texture)[] textures) =>
        textures.ToDictionary(t => t.Name, t => t.Texture, StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void UniformWhiteLayerBakesWhiteWithUpNormals()
    {
        var baked = TerrainLowBake.Bake(Tile(Layer("white.blp", 0)),
            Decoded(("white.blp", Solid(4, 4, 255, 255, 255))), bigAlpha: false);
        Assert.NotNull(baked);
        Assert.Equal(256, baked.Value.Albedo.Width);
        var rgba = baked.Value.Albedo.Rgba;
        Assert.Equal(255, rgba[(128 * 256 + 128) * 4]);
        Assert.Equal(255, rgba[(128 * 256 + 128) * 4 + 1]);
        var n = baked.Value.Normal.Rgba;
        Assert.Equal(255, n[(128 * 256 + 128) * 4 + 1]);
        Assert.InRange(n[(128 * 256 + 128) * 4], 126, 129);
        Assert.InRange(n[(128 * 256 + 128) * 4 + 2], 126, 129);
    }

    [Fact]
    public void LegacyBlendAveragesTwoHalfWeightLayers()
    {
        var baked = TerrainLowBake.Bake(Tile(Layer("white.blp", 0), Layer("black.blp", 128)),
            Decoded(("white.blp", Solid(4, 4, 255, 255, 255)), ("black.blp", Solid(4, 4, 0, 0, 0))), bigAlpha: false);
        Assert.NotNull(baked);
        var rgba = baked.Value.Albedo.Rgba;
        Assert.InRange(rgba[(64 * 256 + 64) * 4], 120, 135);
    }

    [Fact]
    public void BigAlphaWeightsMatchTheLiveIndependentMode()
    {
        var baked = TerrainLowBake.Bake(Tile(Layer("white.blp", 0), Layer("black.blp", 128)),
            Decoded(("white.blp", Solid(4, 4, 255, 255, 255)), ("black.blp", Solid(4, 4, 0, 0, 0))), bigAlpha: true);
        Assert.NotNull(baked);
        // w0 = 1 - 0.5 = 0.5, w1 = 0.5: same average as the legacy mix here.
        var rgba = baked.Value.Albedo.Rgba;
        Assert.InRange(rgba[0], 120, 135);
    }

    [Fact]
    public void HeightTextureSuppressesTheLowLayer()
    {
        // Layer 1 has a black height map with a unit scale: its height term is 0, so the
        // tallest-layer rule drops it and the white base shows through almost fully.
        var baked = TerrainLowBake.Bake(Tile(Layer("white.blp", 0), Layer("black.blp", 255, "h.blp", 1, 0)),
            Decoded(("white.blp", Solid(4, 4, 255, 255, 255)), ("black.blp", Solid(4, 4, 0, 0, 0)), ("h.blp", Solid(4, 4, 0, 0, 0, 0))), bigAlpha: true);
        Assert.NotNull(baked);
        Assert.InRange(baked.Value.Albedo.Rgba[0], 240, 255);
    }

    [Fact]
    public void SlopedHeightsTiltTheNormal()
    {
        var chunks = Tile(Layer("white.blp", 0));
        foreach (var chunk in chunks)
            for (int r = 0; r < 9; r++)
                for (int c = 0; c < 9; c++)
                    chunk.Heights[r * 17 + c] = c * 2f;
        var baked = TerrainLowBake.Bake(chunks, Decoded(("white.blp", Solid(4, 4, 255, 255, 255))), bigAlpha: false);
        Assert.NotNull(baked);
        var n = baked.Value.Normal.Rgba;
        float nx = n[4] / 255f * 2 - 1, ny = n[5] / 255f * 2 - 1, nz = n[6] / 255f * 2 - 1;
        Assert.True(nx < -0.1f, $"tilted east-down, got {nx}");
        Assert.True(ny > 0.9f, $"mostly up, got {ny}");
        Assert.True(Math.Abs(nz) < 0.1f, $"no north tilt, got {nz}");
    }

    [Fact]
    public void McnrNormalsDecodeLikeTheMeshPath()
    {
        var chunks = Tile(Layer("white.blp", 0));
        foreach (var chunk in chunks)
        {
            chunk.Normals = new sbyte[145 * 3];
            for (int i = 0; i < 145; i++) { chunk.Normals[i * 3] = 0; chunk.Normals[i * 3 + 1] = 0; chunk.Normals[i * 3 + 2] = 127; }
        }
        var baked = TerrainLowBake.Bake(chunks, Decoded(("white.blp", Solid(4, 4, 255, 255, 255))), bigAlpha: false);
        Assert.NotNull(baked);
        var n = baked.Value.Normal.Rgba;
        Assert.Equal(255, n[5]);
        Assert.InRange(n[4], 126, 129);
    }

    [Fact]
    public void MissingChunkOrLayersKeepTheFullPath()
    {
        Assert.Null(TerrainLowBake.Bake([], Decoded(), bigAlpha: false));
        Assert.Null(TerrainLowBake.Bake([new TerrainChunk { X = 0, Y = 0 }], Decoded(), bigAlpha: false));
    }

    [Fact]
    public void SamplerWrapsAndBilinearlyInterpolates()
    {
        var tex = new BlpTexture(2, 1, [255, 0, 0, 255, 0, 0, 255, 255]);
        var mid = TerrainLowBake.SampleWrapped(tex, 0.5f, 0.5f);
        Assert.Equal(0.5f, mid.X, 2);
        Assert.Equal(0.5f, mid.Z, 2);
        var wrapped = TerrainLowBake.SampleWrapped(tex, 2.5f, 0.5f);
        Assert.Equal(mid.X, wrapped.X, 5);
    }

    [Theory]
    [InlineData(32, 48, 32, 48, false)]
    [InlineData(33, 48, 32, 48, false)]
    [InlineData(31, 47, 32, 48, false)]
    [InlineData(34, 48, 32, 48, true)]
    [InlineData(32, 50, 32, 48, true)]
    [InlineData(30, 46, 32, 48, true)]
    [InlineData(35, 51, 32, 48, true)]
    public void FarRingSelectsLowTiles(int x, int y, int cx, int cy, bool low)
    {
        Assert.Equal(low, TerrainLowBake.IsFarTile(new TileKey(x, y), new TileKey(cx, cy), TerrainLowBake.LowLodRing));
        Assert.Equal(2, TerrainLowBake.LowLodRing);
    }
}
