using ACWorldEdit.Assets;
using System.Numerics;
public static class ModelChecks
{
    public static void Run(AssetStore store)
    {
        void Check(bool condition,string message) { if (!condition) throw new Exception(message); }
        var catalog = new CreatureCatalog(store);
        var animal = catalog.Resolve(1141); // Stock rat; real display->model/skin lookup.
        var candidates = new List<(string Path,ushort Set)> { (animal.Path,0) };
        foreach (var key in new[] { new TileKey(34,49),new TileKey(32,48) })
        {
            var tile = new TerrainTile(key,store.Read($"World/Maps/Azeroth/Azeroth_{key.X}_{key.Y}.adt"),false);
            Check(tile.Objects.Count > 0,"ADT has no parsed placements");
            candidates.AddRange(tile.Objects.Select(o => (o.Path,o.DoodadSet)));
            Console.WriteLine($"Objects {key}: M2={tile.Objects.Count(o => !o.IsWmo)}, WMO={tile.Objects.Count(o => o.IsWmo)}");
        }
        int interiors = 0;
        foreach (var info in store.Maps())
        {
            WorldMap world;
            try { world = new(info.Directory,store.Read($"World/Maps/{info.Directory}/{info.Directory}.wdt")); } catch (NotSupportedException) { continue; }
            if (world.Tiles.Count != 0 || world.GlobalObjects.Count == 0) continue;
            interiors++; candidates.AddRange(world.GlobalObjects.Select(o => (o.Path,o.DoodadSet))); Console.WriteLine($"Interior {info.Directory}: {world.GlobalObjects[0].Path} position={world.GlobalObjects[0].Position}");
        }
        int m2 = 0,wmo = 0,animated = 0,textureCount = 0;
        foreach (var item in candidates.Distinct())
        {
            Console.WriteLine("Reading " + item.Path);
            bool isWmo = item.Path.EndsWith(".wmo",StringComparison.OrdinalIgnoreCase);
            var model = isWmo ? WmoModel.Read(store,item.Path,item.Set) : M2Model.Read(store,item.Path,item.Path == animal.Path ? animal.Textures : null);
            Check(model.Surfaces.Length > 0,"Empty geometry: " + item.Path);
            foreach (var surface in model.Surfaces)
            {
                Check(surface.Indices.Length % 3 == 0 && surface.Indices.All(i => i >= 0 && i < surface.Positions.Length),"Invalid model topology");
                foreach (string path in new[] { surface.Texture }.Where(p => p.Length > 0).Distinct()) { var texture = BlpTexture.Decode(store.Read(path)); Check(texture.Rgba.Length == texture.Width*texture.Height*4,"Bad model texture"); textureCount++; }
            }
            var palette = IdlePalette.Bake(model,default);
            var floats = new float[palette.Pixels.Length/4]; Buffer.BlockCopy(palette.Pixels,0,floats,0,palette.Pixels.Length);
            Check(floats.All(float.IsFinite),"Nonfinite native animation palette");
            if (model.Duration > 0 && model.Bones.Any(b => b.Rotation.Values.Length > 1 || b.Translation.Values.Length > 1)) animated++;
            if (isWmo) wmo++; else m2++;
            Console.WriteLine($"Model {item.Path}: surfaces={model.Surfaces.Length}, bones={model.Bones.Length}, idle={model.Duration}ms, doodads={model.Doodads.Length}, warnings={model.Warnings.Length}");
        }
        Check(m2 > 0 && wmo > 0 && interiors > 0 && animated > 0,"Incomplete native model corpus");
        Console.WriteLine($"MODEL_CHECKS_PASSED M2={m2} WMO={wmo} animated={animated} textures={textureCount} interiorMaps={interiors}");
    }
}
