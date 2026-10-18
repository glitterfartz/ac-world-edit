using ACWorldEdit.Assets;
using ACWorldEdit.Tests.Support;
using Xunit;

/// <summary>
/// The downloaded community listfile against a real install: liquid texture frames and path-named
/// garrison interiors resolve to readable files.
/// </summary>
[Trait("Category", "Corpus")]
public class RetailListfileCorpusTests
{
    static readonly Lazy<PathListfile> list = new(() => PathListfile.Load(ListfileEnvironment.Path()!));

    [RetailListfileFact]
    public void CoversTheStandardLiquidFrames()
    {
        Assert.NotNull(list.Value.Resolve("XTextures\\river\\lake_a.1.blp"));
        Assert.NotNull(list.Value.Resolve("XTextures\\ocean\\ocean_h.30.blp"));
        Assert.NotNull(list.Value.Resolve("XTextures\\lava\\lava.1.blp"));
        Assert.NotNull(list.Value.Resolve("XTextures\\slime\\slime.1.blp"));
    }

    [RetailListfileFact]
    public void GarrisonInteriorsNameReadableWmos()
    {
        var files = RetailData.Files;
        files.PathResolver = list.Value.Resolve;
        var garrisons = RetailData.Tables.Maps().Where(m => m.Name.StartsWith("Garrison - ", StringComparison.Ordinal) && m.WdtFileId != 0).ToList();
        Assert.NotEmpty(garrisons);
        var missing = new List<string>();
        foreach (var map in garrisons)
        {
            var world = new WorldMap(map.Directory, files.Read(AssetRef.FromFileId(map.WdtFileId)));
            foreach (var placement in world.GlobalObjects.Where(o => !files.Exists(o.Path)))
            {
                missing.Add($"{map.Name}: {placement.Path}");
            }
        }

        Assert.True(missing.Count == 0, string.Join("\n", missing));
    }
}
