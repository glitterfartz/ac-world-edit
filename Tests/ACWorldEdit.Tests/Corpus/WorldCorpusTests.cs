using System.Security.Cryptography;
using ACWorldEdit.Assets;
using ACWorldEdit.Foundation;
using ACWorldEdit.Tests.Support;
using Xunit;
using Xunit.Abstractions;

namespace ACWorldEdit.Tests.Corpus;

/// <summary>
/// Real WDT/ADT/BLP corpus (ported from the legacy Program.cs). When STORMLIB_DLL points at the
/// pinned StormLib x64 DLL (see Tests/Run-StormLibChecks.py) every asset read is also compared
/// byte for byte against it.
/// </summary>
[Trait("Category", "Corpus")]
public class WorldCorpusTests(ITestOutputHelper output) : IDisposable
{
    readonly StormLibOracle? oracle = OpenOracle();
    int differentialCount;

    static StormLibOracle? OpenOracle()
    {
        string? dll = Environment.GetEnvironmentVariable("STORMLIB_DLL");
        string? data = Environments.DataDirectory();
        return string.IsNullOrWhiteSpace(dll) || data == null ? null : new StormLibOracle(dll, data);
    }

    public void Dispose() => oracle?.Dispose();

    void Compare(string asset, byte[] bytes)
    {
        if (oracle == null)
        {
            return;
        }

        Assert.True(bytes.AsSpan().SequenceEqual(oracle.Read(asset)), "StormLib byte mismatch: " + asset);
        differentialCount++;
    }

    [ClientDataFact]
    public void MapsAndWdtCorpus()
    {
        var store = ClientData.Store;
        Compare("DBFilesClient/Map.dbc", store.Read("DBFilesClient/Map.dbc"));
        var maps = store.Maps();
        Assert.True(maps.Any(m => m.Directory == "Azeroth"), "Azeroth map missing");
        output.WriteLine($"Opened {store.ArchiveCount} archives; discovered {maps.Count} maps.");
        output.WriteLine($"Map.dbc source: {store.Source("DBFilesClient/Map.dbc")}");
        int terrainMaps = 0, unsupportedMaps = 0;
        foreach (var map in maps)
        {
            string asset = $"World/Maps/{map.Directory}/{map.Directory}.wdt";
            byte[] bytes = store.Read(asset);
            Compare(asset, bytes);
            try
            {
                _ = new WorldMap(map.Directory, bytes);
                terrainMaps++;
            }
            catch (NotSupportedException e)
            {
                unsupportedMaps++;
                output.WriteLine($"Unsupported {asset}: {e.Message}");
            }

            output.WriteLine($"Corpus {asset} SHA256={Convert.ToHexString(SHA256.HashData(bytes))} source={store.Source(asset)}");
        }

        output.WriteLine($"WDT corpus: {terrainMaps} terrain maps, {unsupportedMaps} explicitly unsupported maps.");
        output.WriteLine(oracle == null ? "StormLib differential checks pending (STORMLIB_DLL not set)." : $"StormLib differential byte comparisons passed: {differentialCount} assets.");
    }

    [ClientDataTheory]
    [InlineData("Azeroth")]
    [InlineData("Kalimdor")]
    [InlineData("Expansion01")]
    [InlineData("Northrend")]
    public void ContinentTileAndTextures(string name)
    {
        var store = ClientData.Store;
        var world = new WorldMap(name, store.Read($"World/Maps/{name}/{name}.wdt"));
        var key = name == "Azeroth" && world.Tiles.Contains(new(32, 48)) ? new TileKey(32, 48) : world.Tiles.OrderBy(t => Math.Abs(t.X - 32) + Math.Abs(t.Y - 32)).First();
        var bytes = store.Read(world.TilePath(key));
        Compare(world.TilePath(key), bytes);
        output.WriteLine($"Corpus {world.TilePath(key)} SHA256={Convert.ToHexString(SHA256.HashData(bytes))} source={store.Source(world.TilePath(key))}");
        var tile = new TerrainTile(key, bytes, world.BigAlpha);
        Assert.True(tile.Chunks.Count == 256, "Tile incomplete");
        Assert.True(tile.Chunks.All(c => c.Heights.All(float.IsFinite)), "Non-finite terrain");
        double maximumAnchorError = 0;
        foreach (var chunk in tile.Chunks)
        {
            var projected = WorldCoordinates.ToView(new(chunk.ServerX, chunk.ServerY, chunk.Height));
            double expectedX = (key.X - 32) * WorldCoordinates.TileSize + chunk.X * WorldCoordinates.TileSize / 16;
            double expectedZ = (key.Y - 32) * WorldCoordinates.TileSize + chunk.Y * WorldCoordinates.TileSize / 16;
            maximumAnchorError = Math.Max(maximumAnchorError, Math.Max(Math.Abs(projected.X - expectedX), Math.Abs(projected.Z - expectedZ)));
        }

        Assert.True(maximumAnchorError <= 0.05, "MCNK source anchors disagree with projection basis");
        output.WriteLine($"Coordinate source proof {name}: 256 MCNK anchors, max horizontal error={maximumAnchorError:R}; not client landmark parity.");
        var textures = tile.Chunks.SelectMany(c => c.Layers).Select(l => l.Texture).Distinct().ToList();
        foreach (string texture in textures)
        {
            byte[] textureBytes = store.Read(texture);
            Compare(texture, textureBytes);
            var blp = BlpTexture.Decode(textureBytes);
            Assert.True(blp.Rgba.Length == blp.Width * blp.Height * 4, "Texture decode length");
        }

        output.WriteLine($"{name}: {world.Tiles.Count} tiles; {key}; 256 chunks; {textures.Count} decoded textures; big alpha={world.BigAlpha}");
    }

    [ClientDataFact]
    public void BackupArchivesNotMounted() => Assert.False(ClientData.Store.Source("DBFilesClient/Map.dbc").Contains("backup-", StringComparison.OrdinalIgnoreCase), "Backup mounted");
}
