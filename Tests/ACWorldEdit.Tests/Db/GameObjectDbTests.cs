using ACWorldEdit.Assets;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;
using ACWorldEdit.Tests.Support;
using Xunit;
using static Test;

/// <summary>Native models and material/animation data reached through real database GO displays.</summary>
[Trait("Category", "Corpus")]
[Trait("Category", "Db")]
public class GameObjectDbTests
{
    [DbFact(NeedsClientData = true)]
    public async Task Passes() => await Core(ClientData.Store);

    static async Task Core(AssetStore store)
    {
        var config = Config;
        var result = await new GameObjectSource(config).ReadAsync(new(0, 31, 33, 48, 50), new(), new(-9492.72, 75.6383, 56.1841), "", default);
        var catalog = new GameObjectCatalog(store);
        var table = new DbcTable(store.Read("DBFilesClient/GameObjectDisplayInfo.dbc"));
        var paths = result.Spawns.Select(s => catalog.Resolve(s.DisplayId)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        // Exercise the WMO display branch as well as native M2 props from this neighborhood.
        paths.AddRange(table.Ids.Where(id => table.Str(id, 1).EndsWith(".wmo", StringComparison.OrdinalIgnoreCase)).Take(3).Select(catalog.Resolve));
        int m2 = 0, wmo = 0, materials = 0, animated = 0;
        foreach (string path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            bool isWmo = path.EndsWith(".wmo", StringComparison.OrdinalIgnoreCase);
            var model = isWmo ? WmoModel.Read(store, path, 0) : M2Model.Read(store, path);
            Check(model.Surfaces.Length > 0, "GameObject has no model surfaces");
            foreach (var surface in model.Surfaces.Where(s => s.Texture.Length > 0).DistinctBy(s => s.Texture))
            {
                var decoded = surface.LiquidType is uint liquid ? LiquidTextures.For(store).Decode(liquid) : BlpTexture.Decode(store.Read(surface.Texture));
                Check(decoded.Rgba.Length == decoded.Width * decoded.Height * 4, "Invalid GameObject material texture");
                materials++;
            }

            var palette = AnimationPalette.Bake(model, default);
            if (model.Duration > 0)
            {
                Check(palette.Height > 1, "Animated GameObject has no sampled frames");
                animated++;
            }

            if (isWmo)
            {
                wmo++;
            }
            else
            {
                m2++;
            }
        }

        Check(m2 > 0 && wmo > 0, "Missing GameObject format coverage");
        Console.WriteLine($"GAMEOBJECT_CHECKS_PASSED rows={result.Spawns.Count} M2={m2} WMO={wmo} materials={materials} animated={animated} native-display-paths");
    }
}
