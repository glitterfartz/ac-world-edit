using ACWorldEdit.Assets;
using System.Security.Cryptography;
static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
static void Reject(Action action, string message)
{
    try { action(); } catch (InvalidDataException) { return; } catch (NotSupportedException) { return; }
    throw new Exception(message);
}
string path = args.Length > 0 ? args[0] : "wow-data";
using var store = new AssetStore(path);
var maps = store.Maps(); Check(maps.Any(m => m.Directory == "Azeroth"), "Azeroth map missing");
Console.WriteLine($"Opened {store.ArchiveCount} archives; discovered {maps.Count} maps.");
Console.WriteLine($"Map.dbc source: {store.Source("DBFilesClient/Map.dbc")}");
int terrainMaps = 0, unsupportedMaps = 0;
foreach (var map in maps)
{
    string asset = $"World/Maps/{map.Directory}/{map.Directory}.wdt";
    byte[] bytes = store.Read(asset);
    try { _ = new WorldMap(map.Directory, bytes); terrainMaps++; }
    catch (NotSupportedException e) { unsupportedMaps++; Console.WriteLine($"Unsupported {asset}: {e.Message}"); }
    Console.WriteLine($"Corpus {asset} SHA256={Convert.ToHexString(SHA256.HashData(bytes))} source={store.Source(asset)}");
}
Console.WriteLine($"WDT corpus: {terrainMaps} terrain maps, {unsupportedMaps} explicitly unsupported maps.");
foreach (string name in new[] { "Azeroth", "Kalimdor", "Expansion01", "Northrend" })
{
    var world = new WorldMap(name, store.Read($"World/Maps/{name}/{name}.wdt"));
    var key = name == "Azeroth" && world.Tiles.Contains(new(32, 48)) ? new TileKey(32, 48) : world.Tiles.OrderBy(t => Math.Abs(t.X - 32) + Math.Abs(t.Y - 32)).First();
    var bytes = store.Read(world.TilePath(key));
    Console.WriteLine($"Corpus {world.TilePath(key)} SHA256={Convert.ToHexString(SHA256.HashData(bytes))} source={store.Source(world.TilePath(key))}");
    var tile = new TerrainTile(key, bytes, world.BigAlpha);
    Check(tile.Chunks.Count == 256, "Tile incomplete");
    Check(tile.Chunks.All(c => c.Heights.All(float.IsFinite)), "Non-finite terrain");
    var textures = tile.Chunks.SelectMany(c => c.Layers).Select(l => l.Texture).Distinct().ToList();
    foreach (string texture in textures) { var blp = BlpTexture.Decode(store.Read(texture)); Check(blp.Rgba.Length == blp.Width * blp.Height * 4, "Texture decode length"); }
    Console.WriteLine($"{name}: {world.Tiles.Count} tiles; {key}; 256 chunks; {textures.Count} decoded textures; big alpha={world.BigAlpha}");
}
byte[] packed = Enumerable.Repeat((byte)0xA5, 2048).ToArray();
var alpha = TerrainTile.DecodeAlpha(packed, 0, 0x100, false, false);
Check(alpha[0] == 85 && alpha[1] == 170, "Nibble alpha order");
byte[] rle = Enumerable.Range(0, 32).SelectMany(_ => new byte[] {255, 123}).Concat(new byte[] {160, 123}).ToArray();
Check(TerrainTile.DecodeAlpha(rle, 0, 0x300, true, false).All(v => v == 123), "Alpha RLE");
Console.WriteLine("All asset checks passed.");
Reject(() => Chunks.Read(new byte[7]).ToList(), "Truncated chunk accepted");
Reject(() => Chunks.Read(new byte[] { 82, 69, 86, 77, 255, 0, 0, 0 }).ToList(), "Oversized chunk accepted");
Reject(() => new TerrainTile(new(0, 0), new byte[] { 82, 69, 86, 77, 4, 0, 0, 0, 17, 0, 0, 0 }, false), "Unsupported ADT accepted");
Reject(() => TerrainTile.DecodeAlpha(new byte[] { 128 }, 0, 0x300, true, false), "Zero alpha run accepted");
Reject(() => TerrainTile.DecodeAlpha(new byte[] { 255 }, 0, 0x300, true, false), "Short alpha fill accepted");
Check(!store.Source("DBFilesClient/Map.dbc").Contains("backup-", StringComparison.OrdinalIgnoreCase), "Backup mounted");
Console.WriteLine("Malformed-input and explicit-mount checks passed.");
