using ACWorldEdit.Assets;
using System.Numerics;

/// <summary>
/// Read-only native geometry/texture/animation corpus and static placement bounds checks. CPU
/// checks do not establish full client visual parity.
/// </summary>
public static class ModelChecks
{
    /// <summary>
    /// Validate native clips, outdoor placements and every interior-only map, with texture/atlas
    /// decodes and sampled animation variation.
    /// </summary>
    public static void Run(AssetStore store)
    {
        void Check(bool condition, string message)
        {
            if (!condition)
            {
                throw new Exception(message);
            }
        }

        var catalog = new CreatureCatalog(store);
        var animal = catalog.Resolve(1141); // Stock rat; real display->model/skin lookup.
        var wolf = catalog.Resolve(11415);
        foreach (ushort id in new ushort[]
        {
            0,
            4,
            5
        }

        )
        {
            var clip = M2Model.Read(store, wolf.Path, wolf.Textures, animationId: id);
            Check(clip.Duration > 0 && clip.AnimationIds!.Contains(id), "Missing native wolf clip " + id);
            var clipPalette = AnimationPalette.Bake(clip, default);
            Check(clipPalette.Height > 1, "Static wolf clip " + id);
            Console.WriteLine($"NATIVE_CLIP_PASSED {catalog.AnimationNames[id]} ({id}) duration={clip.Duration}ms frames={clipPalette.Height}");
        }

        var candidates = new List<(string Path, ushort Set)>
        {
            (animal.Path, 0)
        };
        List<WorldObject> placements = [];
        var parsed = new Dictionary<(string, ushort), ModelData>();
        foreach (var key in new[]
        {
            new TileKey(34, 49),
            new TileKey(32, 48)
        }

        )
        {
            var tile = new TerrainTile(key, store.Read($"World/Maps/Azeroth/Azeroth_{key.X}_{key.Y}.adt"), false);
            Check(tile.Objects.Count > 0, "ADT has no parsed placements");
            candidates.AddRange(tile.Objects.Select(o => (o.Path, o.DoodadSet)));
            placements.AddRange(tile.Objects.Where(o => o.IsWmo));
            Console.WriteLine($"Objects {key}: M2={tile.Objects.Count(o => !o.IsWmo)}, WMO={tile.Objects.Count(o => o.IsWmo)}");
        }

        int interiors = 0;
        foreach (var info in store.Maps())
        {
            WorldMap world;
            try
            {
                world = new(info.Directory, store.Read($"World/Maps/{info.Directory}/{info.Directory}.wdt"));
            }
            catch (NotSupportedException)
            {
                continue;
            }

            if (world.Tiles.Count != 0 || world.GlobalObjects.Count == 0)
            {
                continue;
            }

            interiors++;
            candidates.AddRange(world.GlobalObjects.Select(o => (o.Path, o.DoodadSet)));
            Console.WriteLine($"Interior {info.Directory}: {world.GlobalObjects[0].Path} position={world.GlobalObjects[0].Position}");
        }

        int m2 = 0, wmo = 0, animated = 0, textureCount = 0;
        foreach (var item in candidates.Distinct())
        {
            Console.WriteLine("Reading " + item.Path);
            bool isWmo = item.Path.EndsWith(".wmo", StringComparison.OrdinalIgnoreCase);
            var model = isWmo ? WmoModel.Read(store, item.Path, item.Set) : M2Model.Read(store, item.Path, item.Path == animal.Path ? animal.Textures : null);
            parsed[(item.Path, item.Set)] = model;
            if (model.Surfaces.Any(s => s.LiquidType != null))
            {
                Console.WriteLine($"WMO_LIQUID {item.Path}: surfaces={model.Surfaces.Count(s => s.LiquidType != null)} types={string.Join(',', model.Surfaces.Where(s => s.LiquidType != null).Select(s => s.LiquidType).Distinct())}");
            }

            Check(model.Surfaces.Length > 0, "Empty geometry: " + item.Path);
            foreach (var surface in model.Surfaces)
            {
                Check(surface.Indices.Length % 3 == 0 && surface.Indices.All(i => i >= 0 && i < surface.Positions.Length), "Invalid model topology");
                foreach (string path in new[]
                {
                    surface.Texture
                }.Where(p => p.Length > 0).Distinct())
                {
                    var texture = surface.LiquidType is uint liquid ? LiquidTextures.For(store).Decode(liquid) : BlpTexture.Decode(store.Read(path));
                    Check(texture.Rgba.Length == texture.Width * texture.Height * 4, "Bad model texture");
                    textureCount++;
                }
            }

            var palette = AnimationPalette.Bake(model, default);
            var floats = new float[palette.Pixels.Length / 4];
            Buffer.BlockCopy(palette.Pixels, 0, floats, 0, palette.Pixels.Length);
            Check(floats.All(float.IsFinite), "Nonfinite native animation palette");
            if (model.Duration > 0 && model.Bones.Any(b => b.Rotation.Values.Length > 1 || b.Translation.Values.Length > 1))
            {
                Check(palette.Height > 1 && !palette.Pixels.AsSpan(0, palette.Width * 16).SequenceEqual(palette.Pixels.AsSpan(palette.Width * 16 * (palette.Height / 2), palette.Width * 16)), "Native idle frames do not vary");
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

            Console.WriteLine($"Model {item.Path}: surfaces={model.Surfaces.Length}, bones={model.Bones.Length}, idle={model.Duration}ms, doodads={model.Doodads.Length}, warnings={model.Warnings.Length}");
        }

        Check(m2 > 0 && wmo > 0 && interiors > 0 && animated > 0, "Incomplete native model corpus");
        double maximumBoundsError = 0;
        foreach (var placement in placements)
        {
            var model = parsed[(placement.Path, placement.DoodadSet)];
            var transform = WorldObjects.Transform(placement);
            var min = placement.BoundsMinimum!.Value;
            var max = placement.BoundsMaximum!.Value;
            foreach (var surface in model.Surfaces)
            {
                foreach (var v in surface.Positions)
                {
                    var world = Vector3.Transform(v, transform);
                    maximumBoundsError = Math.Max(maximumBoundsError, Math.Max(Math.Max(min.X - world.X, world.X - max.X), Math.Max(min.Z - world.Z, world.Z - max.Z)));
                }
            }
        }

        Check(maximumBoundsError < 2, "WMO geometry disagrees with ADT placement bounds: " + maximumBoundsError);
        Console.WriteLine($"PLACEMENT_BOUNDS_PASSED outdoorWmos={placements.Count} maxHorizontalError={maximumBoundsError:F4}");
        Console.WriteLine($"MODEL_CHECKS_PASSED M2={m2} WMO={wmo} animated={animated} textures={textureCount} interiorMaps={interiors}");
    }
}
