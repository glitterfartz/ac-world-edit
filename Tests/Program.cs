using ACWorldEdit.Assets;
using ACWorldEdit.Foundation;
using System.Security.Cryptography;

static void Check(bool condition, string message)
{
    if (!condition)
    {
        throw new Exception(message);
    }
}

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

string path = args.Length > 0 ? args[0] : "wow-data";
if (args.Contains("--draft"))
{
    CreatureDraftChecks.Run();
    GraveyardDraftChecks.Run();
    InteractionDraftChecks.Run();
    return;
}

if (args.Contains("--placement"))
{
    PlacementChecks.Run();
    return;
}

if (args.Contains("--spawns"))
{
    await SpawnFilterChecks.Run();
    return;
}

using var store = new AssetStore(path);
if (args.Contains("--interactions"))
{
    await InteractionChecks.Run(store);
    return;
}

if (args.Contains("--graveyards"))
{
    await GraveyardChecks.Run(store);
    return;
}

if (args.Contains("--gameobjects"))
{
    await GameObjectChecks.Run(store);
    return;
}
if (args.Contains("--liquids"))
{
    LiquidChecks.Run(store);
    return;
}

if (args.Contains("--models"))
{
    ModelChecks.Run(store);
    return;
}

using var oracle = args.Length > 1 ? new StormLibOracle(args[1], path) : null;
int differentialCount = 0;
void Compare(string asset, byte[] bytes)
{
    if (oracle == null)
    {
        return;
    }

    Check(bytes.AsSpan().SequenceEqual(oracle.Read(asset)), "StormLib byte mismatch: " + asset);
    differentialCount++;
}

Compare("DBFilesClient/Map.dbc", store.Read("DBFilesClient/Map.dbc"));
var maps = store.Maps();
Check(maps.Any(m => m.Directory == "Azeroth"), "Azeroth map missing");
Console.WriteLine($"Opened {store.ArchiveCount} archives; discovered {maps.Count} maps.");
Console.WriteLine($"Map.dbc source: {store.Source("DBFilesClient/Map.dbc")}");
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
        Console.WriteLine($"Unsupported {asset}: {e.Message}");
    }

    Console.WriteLine($"Corpus {asset} SHA256={Convert.ToHexString(SHA256.HashData(bytes))} source={store.Source(asset)}");
}

Console.WriteLine($"WDT corpus: {terrainMaps} terrain maps, {unsupportedMaps} explicitly unsupported maps.");
foreach (string name in new[]
{
    "Azeroth",
    "Kalimdor",
    "Expansion01",
    "Northrend"
}

)
{
    var world = new WorldMap(name, store.Read($"World/Maps/{name}/{name}.wdt"));
    var key = name == "Azeroth" && world.Tiles.Contains(new(32, 48)) ? new TileKey(32, 48) : world.Tiles.OrderBy(t => Math.Abs(t.X - 32) + Math.Abs(t.Y - 32)).First();
    var bytes = store.Read(world.TilePath(key));
    Compare(world.TilePath(key), bytes);
    Console.WriteLine($"Corpus {world.TilePath(key)} SHA256={Convert.ToHexString(SHA256.HashData(bytes))} source={store.Source(world.TilePath(key))}");
    var tile = new TerrainTile(key, bytes, world.BigAlpha);
    Check(tile.Chunks.Count == 256, "Tile incomplete");
    Check(tile.Chunks.All(c => c.Heights.All(float.IsFinite)), "Non-finite terrain");
    double maximumAnchorError = 0;
    foreach (var chunk in tile.Chunks)
    {
        var projected = WorldCoordinates.ToView(new(chunk.ServerX, chunk.ServerY, chunk.Height));
        double expectedX = (key.X - 32) * WorldCoordinates.TileSize + chunk.X * WorldCoordinates.TileSize / 16;
        double expectedZ = (key.Y - 32) * WorldCoordinates.TileSize + chunk.Y * WorldCoordinates.TileSize / 16;
        maximumAnchorError = Math.Max(maximumAnchorError, Math.Max(Math.Abs(projected.X - expectedX), Math.Abs(projected.Z - expectedZ)));
    }

    Check(maximumAnchorError <= 0.05, "MCNK source anchors disagree with projection basis");
    Console.WriteLine($"Coordinate source proof {name}: 256 MCNK anchors, max horizontal error={maximumAnchorError:R}; not client landmark parity.");
    var textures = tile.Chunks.SelectMany(c => c.Layers).Select(l => l.Texture).Distinct().ToList();
    foreach (string texture in textures)
    {
        byte[] textureBytes = store.Read(texture);
        Compare(texture, textureBytes);
        var blp = BlpTexture.Decode(textureBytes);
        Check(blp.Rgba.Length == blp.Width * blp.Height * 4, "Texture decode length");
    }

    Console.WriteLine($"{name}: {world.Tiles.Count} tiles; {key}; 256 chunks; {textures.Count} decoded textures; big alpha={world.BigAlpha}");
}

byte[] packed = Enumerable.Repeat((byte)0xA5, 2048).ToArray();
var alpha = TerrainTile.DecodeAlpha(packed, 0, 0x100, false, false);
Check(alpha[0] == 85 && alpha[1] == 170, "Nibble alpha order");
byte[] rle = Enumerable.Range(0, 32).SelectMany(_ => new byte[] { 255, 123 }).Concat(new byte[] { 160, 123 }).ToArray();
Check(TerrainTile.DecodeAlpha(rle, 0, 0x300, true, false).All(v => v == 123), "Alpha RLE");
Console.WriteLine("All asset checks passed.");
Reject(() => Chunks.Read(new byte[7]).ToList(), "Truncated chunk accepted");
Reject(() => Chunks.Read(new byte[] { 82, 69, 86, 77, 255, 0, 0, 0 }).ToList(), "Oversized chunk accepted");
Reject(() => new TerrainTile(new(0, 0), new byte[] { 82, 69, 86, 77, 4, 0, 0, 0, 17, 0, 0, 0 }, false), "Unsupported ADT accepted");
Reject(() => TerrainTile.DecodeAlpha(new byte[] { 128 }, 0, 0x300, true, false), "Zero alpha run accepted");
Reject(() => TerrainTile.DecodeAlpha(new byte[] { 255 }, 0, 0x300, true, false), "Short alpha fill accepted");
Reject(() => new WorldMap("fixture", new byte[] { 78, 73, 65, 77, 4, 0, 0, 0, 0, 0, 0, 0 }), "Bad WDT MAIN accepted");
Reject(() => BlpTexture.Decode(new byte[148]), "Bad BLP magic accepted");
var syntheticTile = new TerrainTile(new(32, 32), SyntheticTerrain.Tile(), false);
Check(syntheticTile.Chunks.Count == 256 && syntheticTile.Chunks[255].X == 15 && syntheticTile.Chunks[255].Y == 15 && syntheticTile.Chunks.All(c => c.Height == 10), "Synthetic golden ADT parse");
Reject(() => new TerrainTile(new(32, 32), SyntheticTerrain.Tile(true), false), "Duplicate chunk index accepted");
Check(!store.Source("DBFilesClient/Map.dbc").Contains("backup-", StringComparison.OrdinalIgnoreCase), "Backup mounted");
Console.WriteLine("Malformed-input and explicit-mount checks passed.");
var flat = new TerrainChunk
{
    Height = 10
};
static double? Pick(TerrainGeometry geometry, Point3 origin)
{
    Point3 P(int index)
    {
        var v = geometry.Vertices[index];
        return new(v.X, v.Y, v.Z);
    }

    double? nearest = null;
    for (int i = 0; i < geometry.Indices.Length; i += 3)
    {
        var hit = WorldCoordinates.Hit(origin, new(0, -1, 0), P(geometry.Indices[i]), P(geometry.Indices[i + 1]), P(geometry.Indices[i + 2]));
        if (hit.HasValue && (!nearest.HasValue || hit < nearest))
        {
            nearest = hit;
        }
    }

    return nearest;
}

var complete = TerrainGeometry.Build(flat);
Check(complete.Indices.Length == 768 && complete.Vertices.Length == 145, "Shared geometry topology");
Check(Pick(complete, new(1, 20, 1)) == 10, "Flat terrain elevation hit");
flat.Holes = 1;
var holed = TerrainGeometry.Build(flat);
Check(holed.Indices.Length == 720, "Hole mask topology");
Check(Pick(holed, new(1, 20, 1)) == null, "Picked terrain through hole");
double holeEdge = TerrainTile.ChunkSize / 4;
Check(Pick(holed, new(holeEdge - 0.001, 20, 1)) == null && Pick(holed, new(holeEdge + 0.001, 20, 1)) == 10, "Hole boundary picking");
flat.Holes = 0xFFFF;
Check(TerrainGeometry.Build(flat).Indices.Length == 0, "Fully holed terrain emitted triangles");
Console.WriteLine("Shared renderer/picker geometry: hole interior, both boundary sides and fully holed chunk pass.");
Console.WriteLine(oracle == null ? "StormLib differential checks pending (no DLL supplied)." : $"StormLib differential byte comparisons passed: {differentialCount} assets.");
