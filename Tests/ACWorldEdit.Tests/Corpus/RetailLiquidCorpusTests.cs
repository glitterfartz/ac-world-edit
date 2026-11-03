using ACWorldEdit.Assets;
using ACWorldEdit.Tests.Support;
using Xunit;

/// <summary>
/// Retail liquids against a real install: MH2O decodes on Azeroth tiles, every liquid type found has a
/// texture sequence reachable through the bundled listfile, and WMO liquid groups resolve.
/// </summary>
[Trait("Category", "Corpus")]
[Collection(RetailData.Collection)]
public class RetailLiquidCorpusTests
{
    static RetailLiquidCorpusTests()
    {
        if (ListfileEnvironment.Skip() != null)
        {
            return;
        }

        var files = RetailData.Files;
        files.PathResolver ??= PathListfile.Load(ListfileEnvironment.Path()!).Resolve;
        RetailLiquids.Register(files, RetailLiquids.Load(RetailData.Tables));
    }

    static WorldMap Azeroth()
    {
        var azeroth = RetailData.Tables.Maps().Single(m => m.Directory.Equals("azeroth", StringComparison.OrdinalIgnoreCase));
        return new WorldMap(azeroth.Directory, RetailData.Files.Read(AssetRef.FromFileId(azeroth.WdtFileId)));
    }

    [RetailListfileFact]
    public void ReportedSwampTilesResolveThreeDepthTextures()
    {
        var map = Azeroth();
        var textures = LiquidTextures.For(RetailData.Files);
        foreach (var key in new[] { new TileKey(46, 22), new TileKey(46, 23), new TileKey(47, 23), new TileKey(45, 23) })
        {
            var swamp = map.ReadTile(RetailData.Files, key).Liquids.First(s => s.LiquidType == 1225);
            var style = textures.Style(swamp.LiquidType!.Value);
            Assert.NotNull(style.Swamp);
            Assert.NotEmpty(textures.Decode(1225).Rgba);
            Assert.NotEmpty(style.Swamp.Middle.Rgba);
            Assert.NotEmpty(style.Swamp.Deep.Rgba);
            Assert.NotNull(style.Swamp.Shore);
            Assert.NotNull(style.Swamp.Normals);
            Assert.Equal(30, style.Swamp.Normals.Width / style.Swamp.Normals.Height);
            Assert.Equal(0.04f, style.Swamp.Settings.Depths.Y);
            Assert.Equal(style.Swamp.Settings.Depths.Y, style.Swamp.Settings.Depths.Z);
        }
    }

    [RetailListfileFact]
    public void AzerothTilesDecodeLiquidsWithKnownTypes()
    {
        var map = Azeroth();
        var liquids = RetailLiquids.For(RetailData.Files)!;
        var types = new HashSet<uint>();
        int tiles = 0;
        foreach (var key in map.Tiles.OrderBy(k => Math.Abs(k.X - 32) + Math.Abs(k.Y - 48)).Take(40))
        {
            var tile = map.ReadTile(RetailData.Files, key);
            tiles++;
            foreach (var surface in tile.Liquids)
            {
                Assert.True(liquids.HasType(surface.LiquidType!.Value), $"unknown liquid type {surface.LiquidType} on {key}");
                Assert.All(surface.Positions, p => Assert.True(float.IsFinite(p.Y)));
                types.Add(surface.LiquidType!.Value);
            }
        }

        Assert.Equal(40, tiles);
        Assert.NotEmpty(types);
    }

    [RetailListfileFact]
    public void EveryContinentSampleDecodesAndEveryFoundTypeHasATextureSequence()
    {
        var failures = new List<string>();
        var textures = LiquidTextures.For(RetailData.Files);
        var liquids = RetailLiquids.For(RetailData.Files)!;
        var types = new HashSet<uint>();
        int tiles = 0;
        foreach (string directory in new[] { "azeroth", "kalimdor", "expansion01", "northrend" })
        {
            var info = RetailData.Tables.Maps().FirstOrDefault(m => m.Directory.Equals(directory, StringComparison.OrdinalIgnoreCase));
            if (info == null)
            {
                continue;
            }

            var map = new WorldMap(info.Directory, RetailData.Files.Read(AssetRef.FromFileId(info.WdtFileId)));
            foreach (var key in map.Tiles.Where((_, i) => i % 7 == 0))
            {
                try
                {
                    tiles++;
                    foreach (var surface in map.ReadTile(RetailData.Files, key).Liquids)
                    {
                        types.Add(surface.LiquidType!.Value);
                    }
                }
                catch (Exception e)
                {
                    failures.Add($"{directory} {key}: {e.Message}");
                }
            }
        }

        foreach (uint type in types)
        {
            try
            {
                Assert.True(liquids.HasType(type), "unknown type");
                textures.Decode(type);
            }
            catch (Exception e)
            {
                failures.Add($"type {type}: {e.Message}");
            }
        }

        Assert.True(tiles > 100, "sampled " + tiles);
        Assert.True(failures.Count == 0, string.Join("\n", failures.Take(15)));
    }

    [RetailListfileFact]
    public void WmoLiquidGroupsResolveToKnownTypesWithTextures()
    {
        var map = Azeroth();
        var liquids = RetailLiquids.For(RetailData.Files)!;
        var textures = LiquidTextures.For(RetailData.Files);
        var paths = map.Tiles.OrderBy(k => Math.Abs(k.X - 32) + Math.Abs(k.Y - 48)).Take(12)
            .SelectMany(k => map.ReadTile(RetailData.Files, k).Objects).Select(o => o.Path)
            .Where(p => p.EndsWith(".wmo", StringComparison.OrdinalIgnoreCase)).Distinct().Take(80).ToList();
        var failures = new List<string>();
        int surfaces = 0;
        foreach (string path in paths)
        {
            try
            {
                foreach (var surface in WmoModel.Read(RetailData.Files, path).Surfaces.Where(s => s.LiquidType != null))
                {
                    surfaces++;
                    Assert.True(liquids.HasType(surface.LiquidType!.Value), $"unknown liquid type {surface.LiquidType}");
                    textures.Decode(surface.LiquidType!.Value);
                }
            }
            catch (Exception e)
            {
                failures.Add($"{path}: {e.Message}");
            }
        }

        Assert.True(failures.Count == 0, string.Join("\n", failures.Take(10)));
        Assert.True(paths.Count > 0, "no WMO placements sampled");
        Assert.True(surfaces > 0, "no WMO liquid surfaces found in " + paths.Count + " WMOs");
    }

    [RetailListfileFact]
    public void EveryLiquidTypeInTheSampleHasATextureSequence()
    {
        var map = Azeroth();
        var textures = LiquidTextures.For(RetailData.Files);
        var failures = new List<string>();
        foreach (uint type in map.Tiles.OrderBy(k => Math.Abs(k.X - 32) + Math.Abs(k.Y - 48)).Take(40)
            .SelectMany(k => map.ReadTile(RetailData.Files, k).Liquids).Select(s => s.LiquidType!.Value).Distinct())
        {
            try
            {
                var atlas = textures.Decode(type);
                Assert.True(atlas.Width >= atlas.Height);
            }
            catch (Exception e)
            {
                failures.Add($"{type}: {e.Message}");
            }
        }

        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }
}
