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
    public void ProceduralMagmaTypesResolveAllThreeNativeLayers()
    {
        var textures = LiquidTextures.For(RetailData.Files);
        var terrain = textures.Style(302);
        var wmo = textures.Style(303);
        Assert.NotNull(terrain.Magma);
        Assert.NotNull(wmo.Magma);
        Assert.EndsWith("Magma0.blp", textures.Pattern(302));
        Assert.NotEmpty(textures.Decode(302).Rgba);
        Assert.NotEmpty(terrain.Magma.Middle.Rgba);
        Assert.NotEmpty(terrain.Magma.Top.Rgba);
        Assert.Same(terrain.Magma.Middle, wmo.Magma.Middle);
        Assert.Same(terrain.Magma.Top, wmo.Magma.Top);
        Assert.Equal(.025f, terrain.Magma.Settings.ScrollU);
        Assert.Equal(.1f, wmo.Magma.Settings.ScrollU);
        Assert.NotEqual(terrain.CacheKey, wmo.CacheKey);
        Assert.Null(textures.Style(3).Magma);
    }

    [RetailListfileFact]
    public void FogLiquidsShareDensityPixelsButKeepTheirAuthoredTint()
    {
        var textures = LiquidTextures.For(RetailData.Files);
        var forest = textures.Style(750);
        var witch = textures.Style(751);
        Assert.NotNull(forest.Fog);
        Assert.NotNull(witch.Fog);
        Assert.Null(forest.Bump);
        Assert.Equal(0.5f, forest.Fog.OpacityScale);
        Assert.NotEqual(forest.Fog.Color, witch.Fog.Color);
        Assert.NotEqual(forest.CacheKey, witch.CacheKey);
        var density = textures.Decode(750);
        Assert.NotEmpty(density.Rgba);
        Assert.Same(density, textures.Decode(751));
    }

    [RetailListfileFact]
    public void MercuryTypesResolveTheSharedNativeRippleMap()
    {
        var textures = LiquidTextures.For(RetailData.Files);
        var exterior = textures.Style(350);
        var interior = textures.Style(412);
        Assert.True(exterior.Mercury);
        Assert.True(interior.Mercury);
        Assert.NotNull(exterior.Bump);
        Assert.NotEmpty(exterior.Bump.Rgba);
        Assert.Same(exterior.Bump, interior.Bump);
        Assert.False(textures.Style(2).Mercury);
        Assert.NotEqual(exterior.CacheKey, textures.Style(2).CacheKey);
    }

    [RetailListfileFact]
    public void LeyAndIlluminatedWaterResolveIndependentDetailLayers()
    {
        var textures = LiquidTextures.For(RetailData.Files);
        var ley = textures.Style(868);
        var mana = textures.Style(890);
        Assert.NotNull(ley.Ley);
        Assert.NotNull(mana.Ley);
        Assert.Null(ley.Bump);
        Assert.Null(ley.Fel);
        Assert.NotEmpty(textures.Decode(868).Rgba);
        Assert.NotEmpty(ley.Ley.Detail.Rgba);
        Assert.NotEmpty(mana.Ley.Detail.Rgba);
        Assert.Same(ley.Ley.Detail, textures.Style(868).Ley!.Detail);
        Assert.NotSame(ley.Ley.Detail, mana.Ley.Detail);
        Assert.NotEqual(ley.CacheKey, mana.CacheKey);
        Assert.Equal(90, ley.Ley.Settings.FirstUv.Y);
        Assert.Equal(-0.02f, ley.Ley.Settings.FirstUv.Z);
        Assert.True(ley.Ley.Settings.Color.LengthSquared() > 0);
    }

    [RetailListfileFact]
    public void VenomTileResolvesDistinctDepthLayersAndUvSettings()
    {
        var tile = Azeroth().ReadTile(RetailData.Files, new TileKey(52, 22));
        var textures = LiquidTextures.For(RetailData.Files);
        foreach (uint type in new uint[] { 1233, 1234 })
        {
            Assert.Contains(tile.Liquids, surface => surface.LiquidType == type);
            var style = textures.Style(type);
            var settings = RetailLiquids.For(RetailData.Files)!.Fel(type);
            Assert.NotNull(settings);
            // These newly named venom base textures are absent from current community listfiles.
            // Until they resolve, the material must retain the existing generic fallback.
            if (RetailData.Files.Exists(settings.Shallow))
                Assert.NotNull(style.Fel);
            else
                Assert.Null(style.Fel);
            Assert.Null(style.Swamp);
            Assert.Null(style.Bump);
            Assert.NotEmpty(textures.Decode(type).Rgba);
            Assert.Equal(0.45f, settings.DeepUv.X);
            Assert.Equal(90, settings.ShallowUv.Y);
            Assert.Equal(15, settings.DepthCoefficient);
        }
        var felStyle = textures.Style(898);
        Assert.NotNull(felStyle.Fel);
        Assert.NotEmpty(felStyle.Fel.Shallow.Rgba);
        Assert.NotEqual(felStyle.CacheKey, LiquidStyle.River.CacheKey);
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
