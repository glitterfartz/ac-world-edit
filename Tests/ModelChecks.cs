using ACWorldEdit.Assets;
using System.Numerics;
using static Test;

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

        int m2 = 0, wmo = 0, animated = 0, textureCount = 0, materialModels = 0;
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
            var materials = MaterialPalette.Bake(model, default);
            var materialFloats = new float[materials.Pixels.Length / 4];
            Buffer.BlockCopy(materials.Pixels, 0, materialFloats, 0, materials.Pixels.Length);
            Check(materials.Surfaces.Length == model.Surfaces.Length && materialFloats.All(float.IsFinite) && materials.Surfaces.All(m => float.IsFinite(m.Color.W) && float.IsFinite(m.UvRow0.Z)), "Nonfinite material palette: " + item.Path);
            if (materials.Surfaces.Any(m => m.Column >= 0 || m.Hidden || m.Color != Vector4.One))
            {
                materialModels++;
            }
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
        // Ground-probe cost: one vertical line through each placement's bounds center. Report the
        // slowest building, which bounds the cost of one on-demand Drop to ground.
        var slowest = (Path: "", Triangles: 0, Surfaces: 0, Milliseconds: 0.0, Warm: 0.0, Hits: 0);
        foreach (var placement in placements)
        {
            var model = parsed[(placement.Path, placement.DoodadSet)];
            var min = placement.BoundsMinimum!.Value;
            var max = placement.BoundsMaximum!.Value;
            var started = System.Diagnostics.Stopwatch.GetTimestamp();
            var hits = GroundProbe.WmoHeights(placement, model, (min + max) / 2);
            double elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            // A second probe elsewhere reuses cached surface bounds, like repeated drops in the viewer.
            started = System.Diagnostics.Stopwatch.GetTimestamp();
            GroundProbe.WmoHeights(placement, model, min + (max - min) * 0.3f);
            double warm = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            if (elapsed > slowest.Milliseconds)
            {
                slowest = (placement.Path, model.Surfaces.Sum(surface => surface.Indices.Length / 3), model.Surfaces.Length, elapsed, warm, hits.Count);
            }
        }

        Console.WriteLine($"WMO_GROUND_PROBE_COST slowest={slowest.Path} triangles={slowest.Triangles} surfaces={slowest.Surfaces} firstMs={slowest.Milliseconds:F3} warmMs={slowest.Warm:F3} hits={slowest.Hits}");
        MaterialAnimation(store, Check);
        Console.WriteLine($"MODEL_CHECKS_PASSED M2={m2} WMO={wmo} animated={animated} materialAnimated={materialModels} textures={textureCount} interiorMaps={interiors}");
    }

    /// <summary>
    /// Ruby Sanctum effect doors and instance portals carry visibility, tint and scrolling only in
    /// batch color/weight/UV tracks. Checks the parsed data and baked rows, not on-screen output.
    /// </summary>
    static void MaterialAnimation(AssetStore store, Action<bool, string> check)
    {
        const string ring = "world/expansion02/doodads/rubysanctum/rubysanctum_door_03.m2";
        MaterialPalette Bake(string path, ushort clip) => MaterialPalette.Bake(M2Model.Read(store, path, animationId: clip), default);
        // Halion Flame Ring: state 1 (Closed) shows scrolling orange flame, state 0 (Opened) fades out.
        var closed = Bake(ring, 147);
        check(closed.Surfaces.All(m => !m.Hidden && m.UvFrames > 1 && m.Color.X > 0.9f && m.Color.Y is > 0.4f and < 0.5f), "Closed flame ring lacks scrolling orange batches");
        var rows = new float[closed.Pixels.Length / 4];
        Buffer.BlockCopy(closed.Pixels, 0, rows, 0, closed.Pixels.Length);
        int u = (closed.Surfaces[0].Column + 1) * 4 + 2;
        float first = rows[u], middle = rows[closed.Width * 4 * (closed.Surfaces[0].UvFrames / 2) + u];
        check(Math.Abs(first - middle) > 0.25f, "Flame ring UV translation does not advance");
        check(Bake(ring, 149).Surfaces.All(m => m.Hidden), "Opened flame ring still drawn");
        // Missing state clips fall back to Stand: this egg only authors Stand.
        var egg = M2Model.Read(store, "World/Kalimdor/DragonCave/PassiveDoodads/BlackDragonEggs/BlackDragonEgg01.m2", animationId: 147);
        check(egg.Warnings.Length == 0 || !egg.Warnings.Any(w => w.Contains("unavailable")), "Missing door clip did not fall back to Stand");
        // Burnt tree: two weight-zero variant batches are hidden in Stand.
        var tree = Bake("world/expansion02/doodads/rubysanctum/reddragonshrine_tree04_burned_anim.m2", 0);
        check(tree.Surfaces.Count(m => m.Hidden) == 2, "Burnt tree variant batches not hidden");
        // Heroic portal: static weights below one, scrolling swirl and keyed flicker.
        var portal = Bake("SPELLS/INSTANCEPORTAL_GREEN_10MAN_HEROIC.m2", 0);
        check(portal.Surfaces.Any(m => m.UvFrames > 1) && portal.Surfaces.Any(m => m.ColorFrames > 1) && portal.Surfaces.Any(m => m.Color.W is > 0.1f and < 0.9f), "Portal material animation missing");
        Console.WriteLine($"MATERIAL_ANIMATION_PASSED ringUv={first:F3}->{middle:F3} ringFrames={closed.Surfaces[0].UvFrames} portalAnimated={portal.Surfaces.Count(m => m.Column >= 0)}/{portal.Surfaces.Length} treeHidden=2");
    }
}
