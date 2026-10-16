using ACWorldEdit.Assets;
using System.Numerics;
using ACWorldEdit.Tests.Support;
using Xunit;
using static Test;

/// <summary>
/// Native texture/ADT corpus checks plus explicit binary fixtures for MH2O/MCLQ/WMO masks and
/// malformed records, independent of Godot rendering.
/// </summary>
[Trait("Category", "Corpus")]
public class LiquidCorpusTests
{
    [ClientDataFact]
    public void Passes() => Core(ClientData.Store);

    /// <summary>
    /// Validate categories, animated/shared atlases and representative grids. Report unsupported
    /// procedural entries rather than claiming they render.
    /// </summary>
    static void Core(AssetStore store)
    {
        var grid = LiquidData.Grid(2, 2, 1, (x, y) => new(x, 7, y), (x, y) => x == 0 && y == 1);
        Check(grid.Indices.Length == 6, "Liquid visibility mask ignored.");
        Check(grid.Positions.All(p => p.Y == 7), "Liquid height changed.");
        var water = new LiquidTexturesFixture();
        water.Run();
        var types = new DbcTable(store.Read("DBFilesClient/LiquidType.dbc"));
        var textures = LiquidTextures.For(store);
        foreach (uint id in types.Ids)
        {
            Console.Write($"Liquid {id} kind={textures.Kind(id)} pattern={textures.Pattern(id)} ");
            try
            {
                var atlas = textures.Decode(id);
                Console.WriteLine($"atlas={atlas.Width}x{atlas.Height} rgb={atlas.Rgba.Where((b, i) => i % 4 == 0).Average(b => (double)b):F1}/{atlas.Rgba.Where((b, i) => i % 4 == 1).Average(b => (double)b):F1}/{atlas.Rgba.Where((b, i) => i % 4 == 2).Average(b => (double)b):F1} colors={types.U(id, 21):X8}/{types.U(id, 22):X8}");
            }
            catch (InvalidDataException e) when (id >= 100)
            {
                Console.WriteLine("unsupported procedural entry: " + e.Message);
            }
        }

        var lake = textures.Decode(1);
        Check(lake.Width / lake.Height == 30, "Unexpected native water frame count.");
        Check(!lake.Rgba.AsSpan(0, lake.Height * 4).SequenceEqual(lake.Rgba.AsSpan(lake.Height * 4 * 5, lake.Height * 4)), "Native ripple frames do not vary.");
        Check(ReferenceEquals(lake, textures.Decode(13)), "Identical liquid sequences are not shared.");
        int layers = 0, cells = 0;
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
            foreach (var key in world.Tiles.OrderBy(k => Math.Abs(k.X - 32) + Math.Abs(k.Y - 48)).Take(12).Concat(name == "Azeroth" ? new[] { new TileKey(34, 49) } : Array.Empty<TileKey>()))
            {
                var tile = new TerrainTile(key, store.Read(world.TilePath(key)), world.BigAlpha);
                var surfaces = LiquidData.Terrain(store.Read(world.TilePath(key))).ToArray();
                layers += surfaces.Length;
                cells += surfaces.Sum(s => s.Indices.Length / 6);
                Check(surfaces.All(s => s.Positions.All(p => float.IsFinite(p.Y))), "Nonfinite liquid height.");
                foreach (var s in surfaces)
                {
                    _ = textures.Decode(s.LiquidType!.Value);
                }

                if (surfaces.Length > 0)
                {
                    Console.WriteLine($"{name} {key}: {surfaces.Length} layers, {surfaces.Sum(s => s.Indices.Length / 6)} visible cells; first={surfaces[0].Positions[0]} alpha={surfaces.SelectMany(s => s.Colors!).Max(c => c.W)}");
                }
            }
        }

        Check(layers > 0 && cells > 0, "No native liquid geometry tested.");
        Console.WriteLine($"PASS liquid textures and 49 ADTs: {layers} layers, {cells} visible cells.");
    }

    /// <summary>
    /// Small binary liquid records make height/mask/offset interpretation testable independently of
    /// the real client corpus.
    /// </summary>
    sealed class LiquidTexturesFixture
    {
        /// <summary>
        /// Check visible/hidden cells, legacy/mixed grids, flat oceans, WMO masks and invalid
        /// dimensions/offsets.
        /// </summary>
        public void Run()
        {
            byte[] data = new byte[3072 + 24 + 1 + 20];
            void U(int p, uint v) => BitConverter.GetBytes(v).CopyTo(data, p);
            void F(int p, float v) => BitConverter.GetBytes(v).CopyTo(data, p);
            U(0, 3072);
            U(4, 1);
            BitConverter.GetBytes((ushort)1).CopyTo(data, 3072);
            F(3076, 9);
            F(3080, 9);
            data[3086] = data[3087] = 1;
            U(3088, 3096);
            U(3092, 3097);
            data[3096] = 1;
            for (int i = 0; i < 4; i++)
            {
                F(3097 + i * 4, 9 + i);
                data[3113 + i] = 255;
            }

            byte[] file = new byte[data.Length + 8];
            System.Text.Encoding.ASCII.GetBytes("O2HM").CopyTo(file, 0);
            BitConverter.GetBytes(data.Length).CopyTo(file, 4);
            data.CopyTo(file, 8);
            var surface = LiquidData.Terrain(file).Single();
            Check(surface.Indices.Length == 6 && surface.Positions[3].Y == 12, "MH2O native vertex decoding failed.");
            data[3096] = 0;
            data.CopyTo(file, 8);
            Check(LiquidData.Terrain(file).Count == 0, "MH2O absent cell became water.");
            // Legacy MCLQ retains its own cell mask and elevations.
            byte[] old = new byte[8 + 128 + 8 + 720];
            System.Text.Encoding.ASCII.GetBytes("KNCM").CopyTo(old, 0);
            void OU(int p, uint v) => BitConverter.GetBytes(v).CopyTo(old, p);
            OU(4, 856);
            OU(12, 1);
            OU(104, 136);
            OU(108, 728);
            System.Text.Encoding.ASCII.GetBytes("QLCM").CopyTo(old, 136);
            OU(140, 720);
            for (int i = 0; i < 81; i++)
            {
                BitConverter.GetBytes(20f).CopyTo(old, 156 + i * 8);
            }

            Array.Fill(old, (byte)15, 800, 64);
            old[800] = 0;
            var legacy = LiquidData.Terrain(old).Single();
            Check(legacy.Indices.Length == 6 && legacy.Positions.All(p => p.Y == 20), "MCLQ liquid grid failed.");
            // The empty new chunk must not suppress legacy water elsewhere in the tile.
            byte[] mixed = new byte[file.Length + old.Length];
            file.CopyTo(mixed, 0);
            old.CopyTo(mixed, file.Length);
            Check(LiquidData.Terrain(mixed).Count == 1, "Empty MH2O suppressed legacy liquid.");
            // A depth-only ocean grid uses its flat native level, not depth bytes as heights.
            data[3096] = 1;
            BitConverter.GetBytes((ushort)2).CopyTo(data, 3074);
            data.CopyTo(file, 8);
            Check(LiquidData.Terrain(file).Single().Positions.All(p => p.Y == 9), "Ocean flat height ignored.");
            byte[] wmo = new byte[30 + 4 * 8 + 1];
            void WI(int p, int v) => BitConverter.GetBytes(v).CopyTo(wmo, p);
            WI(0, 2);
            WI(4, 2);
            WI(8, 1);
            WI(12, 1);
            for (int i = 0; i < 4; i++)
            {
                BitConverter.GetBytes(12f).CopyTo(wmo, 34 + i * 8);
            }

            var interior = LiquidData.Wmo(wmo, 13);
            Check(interior.Indices.Length == 6 && interior.Positions.All(p => p.Y == 12), "WMO liquid grid failed.");
            wmo[^1] = 15;
            Check(LiquidData.Wmo(wmo, 13).Indices.Length == 0, "WMO hidden cell became water.");
            WI(0, 3);
            try
            {
                LiquidData.Wmo(wmo, 13);
                throw new Exception("Invalid WMO dimensions accepted.");
            }
            catch (InvalidDataException)
            {
            }

            U(3092, uint.MaxValue);
            data.CopyTo(file, 8);
            try
            {
                LiquidData.Terrain(file);
                throw new Exception("Invalid liquid offset accepted.");
            }
            catch (OverflowException)
            {
            }
            catch (System.IO.InvalidDataException)
            {
            }
        }
    }
}
