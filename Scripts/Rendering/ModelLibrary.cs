using Godot;
using ACWorldEdit.Assets;

namespace ACWorldEdit;
/// <summary>
/// Shared native variant identity: replacement skins, baked texture, WMO doodad set and
/// requested animation ID. Equal keys reuse decoded and GPU resources. Skybox variants get
/// unlit, fog-free, first-drawn materials and unculled, shadowless instances (see SkyLighting).
/// </summary>
public sealed record ModelKey(string Path, string Texture1 = "", string Texture2 = "", string Texture3 = "", string Baked = "", ushort DoodadSet = 0, ushort Animation = 0, bool Skybox = false, string Hair = "", string Fur = "", int HairGeoset = 1);
/// <summary>
/// Main-thread resource owner with one decode worker. Upload surfaces incrementally, batch
/// placements and animate on the GPU; active resources can exceed the dormant-cache target.
/// </summary>
public sealed class ModelLibrary
{
    internal sealed class Entry(ModelData data, AnimationPalette palette, MaterialPalette materials, Dictionary<string, BlpTexture> textures)
    {
        public ModelData Data = data;
        public AnimationPalette Palette = palette;
        public MaterialPalette Materials = materials;
        public readonly (System.Numerics.Vector3 Min, System.Numerics.Vector3 Max) Bounds = FindBounds(data);
        public Dictionary<string, BlpTexture> Decoded = textures;
        public readonly List<ArrayMesh> Meshes = [];
        public ImageTexture? AnimationTexture;
        public ImageTexture? MaterialTexture;
        public int Surface;
        public long LastUsed;
        public readonly long Bytes = data.EstimatedBytes + palette.Pixels.Length + materials.Pixels.Length + textures.Values.Sum(t => (long)t.Rgba.Length);

        public readonly HashSet<string> TexturePaths = new(StringComparer.OrdinalIgnoreCase);
        /// <summary>
        /// Compute reference-pose bounds from distinct source arrays. Selection uses bounding boxes, not
        /// animated triangles or terrain occlusion.
        /// </summary>
        static (System.Numerics.Vector3, System.Numerics.Vector3) FindBounds(ModelData data)
        {
            var min = new System.Numerics.Vector3(float.MaxValue);
            var max = new System.Numerics.Vector3(float.MinValue);
            bool any = false;
            foreach (var positions in data.Surfaces.Select(s => s.Positions).Distinct())
            {
                foreach (var point in positions)
                {
                    min = System.Numerics.Vector3.Min(min, point);
                    max = System.Numerics.Vector3.Max(max, point);
                    any = true;
                }
            }

            return any ? (min, max) : (System.Numerics.Vector3.Zero, System.Numerics.Vector3.Zero);
        }
    }

    sealed class TextureEntry(ImageTexture texture)
    {
        public ImageTexture Texture = texture;
        public int References;
    }

    readonly Dictionary<ModelKey, Entry> cache = [];
    long cachedBytes;
    readonly Dictionary<string, TextureEntry> textures = new(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<ModelKey> failed = [];
    readonly List<ModelInstance> instances = [];
    // Instances not yet bound to a cache entry, so the per-frame scans skip the (usually far larger)
    // built population. Attach adds to it; Update rebuilds it while pruning freed nodes.
    readonly List<ModelInstance> unbuilt = [];
    // Live instance count per key, replacing a scan of every instance when asking "is this key wanted".
    readonly Dictionary<ModelKey, int> liveKeys = [];
    readonly Shader baseShader = GD.Load<Shader>("res://Shaders/model.gdshader");
    readonly Dictionary<(int Blend, bool TwoSided, bool Unlit, bool Skybox), Shader> shaders = [];
    readonly ImageTexture fallback;
    IClientFiles? store;
    CancellationTokenSource cancellation = new();
    // A variant progresses through exactly one decode slot, one upload slot, then
    // the shared ready cache. Partial GPU resources are never bound to scene instances.
    Task<Entry>? pending;
    ModelKey? pendingKey, buildingKey;
    Entry? building;
    long tick;
    /// <summary>
    /// Expose cached parsed WMO liquid surfaces for opt-in smoke placement without archive reads or
    /// GPU readback.
    /// </summary>
    public ModelSurface[] LiquidSurfaces(ModelKey key) => cache.TryGetValue(key, out var e) ? e.Data.Surfaces.Where(s => s.LiquidType != null).ToArray() : [];
    /// <summary>
    /// Expose a ready variant's decoded CPU data for on-demand ground probes. Null means the
    /// variant is still decoding/uploading (or failed), not that it has no geometry.
    /// </summary>
    public ModelData? ReadyData(ModelKey key) => cache.TryGetValue(key, out var e) ? e.Data : null;
    public int ReadyModels => cache.Count;
    /// <summary>
    /// Diagnostic hold for --reload-smoke: at most one surface uploads per frame and nothing is
    /// published while set. Fast machines otherwise finish a model inside one frame, leaving no
    /// partial upload to interrupt. Never set during normal viewing.
    /// </summary>
    public bool HoldPartialUploads { get; set; }
    /// <summary>
    /// An entry owns uploaded GPU surfaces but has not reached the ready cache.
    /// </summary>
    public bool HasPartialUpload => building != null && building.Surface > 0;
    public int TextureCount => textures.Count;
    public int ActiveFailedModels => unbuilt.Count(i => failed.Contains(i.Key));
    public long CachedBytes => cachedBytes;
    public double UploadMilliseconds { get; private set; }
    public bool Busy => pending != null || building != null || unbuilt.Any(i => GodotObject.IsInstanceValid(i) && !i.Built && !failed.Contains(i.Key));
    public CreatureCatalog? Catalog { get; private set; }
    public GameObjectCatalog? GameObjects { get; private set; }

    bool embeddedDoodadsVisible = true;
    public ModelLibrary()
    {
        byte[] pink = [255, 0, 255, 255];
        using var image = Image.CreateFromData(1, 1, false, Image.Format.Rgba8, pink);
        fallback = ImageTexture.CreateFromImage(image);
    }

    /// <summary>
    /// Cancel/await the old worker before releasing client resources and loading the next catalog.
    /// The caller may dispose old MPQs only after this completes.
    /// </summary>
    public async Task SetStore(IClientFiles? next)
    {
        cancellation.Cancel();
        if (pending != null)
        {
            try
            {
                await pending;
            }
            catch
            {
            }
        }

        pending = null;
        // A reload can interrupt GPU upload before an entry reaches the ready cache.
        // It still owns meshes, the palette and shared texture references at this point.
        if (building != null)
        {
            DisposeEntry(building);
        }

        building = null;
        pendingKey = buildingKey = null;
        cancellation.Dispose();
        cancellation = new();
        foreach (var i in instances)
        {
            if (GodotObject.IsInstanceValid(i))
            {
                i.QueueFree();
            }
        }

        instances.Clear();
        unbuilt.Clear();
        liveKeys.Clear();
        foreach (var e in cache.Values)
        {
            DisposeEntry(e);
        }

        cache.Clear();
        cachedBytes = 0;
        textures.Clear();
        failed.Clear();
        store = next;
        // The creature and GameObject catalogs read 3.3.5 DBC tables; other clients have no
        // database-backed spawns to display, so they run without catalogs.
        bool wrath = next is { Kind: ClientKind.Wrath335 };
        Catalog = wrath ? await Task.Run(() => new CreatureCatalog(next!)) : null;
        GameObjects = wrath ? await Task.Run(() => new GameObjectCatalog(next!)) : null;
    }

    /// <summary>
    /// Register a scene binding immediately; Update resolves shared resources asynchronously. Null
    /// transforms select a single mesh rather than a placement batch.
    /// </summary>
    public ModelInstance Attach(Node3D parent, ModelKey key, Transform3D[]? transforms = null)
    {
        var node = new ModelInstance
        {
            Key = key,
            Transforms = transforms
        };
        parent.AddChild(node);
        instances.Add(node);
        unbuilt.Add(node);
        liveKeys[key] = liveKeys.GetValueOrDefault(key) + 1;
        return node;
    }

    /// <summary>
    /// Compose Godot column-vector static rotations/scaling. Order and the extra half-turn match the
    /// CPU transform and source MODF bounds checks.
    /// </summary>
    /// <summary>
    /// Vertex, normal, UV, index and (when present) color arrays of one parsed surface, converted from
    /// the engine-free numerics types. Shared with the terrain liquid meshes.
    /// </summary>
    public static Godot.Collections.Array SurfaceArrays(ModelSurface surface)
    {
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = Convert(surface.Positions, v => new Vector3(v.X, v.Y, v.Z));
        arrays[(int)Mesh.ArrayType.Normal] = Convert(surface.Normals, v => new Vector3(v.X, v.Y, v.Z));
        arrays[(int)Mesh.ArrayType.TexUV] = Convert(surface.Uv, v => new Vector2(v.X, v.Y));
        arrays[(int)Mesh.ArrayType.Index] = surface.Indices;
        if (surface.Colors != null)
        {
            arrays[(int)Mesh.ArrayType.Color] = Convert(surface.Colors, v => new Color(v.X, v.Y, v.Z, v.W));
        }

        return arrays;
    }

    /// <summary>
    /// Convert a numerics vertex array to its Godot counterpart. In single-precision builds the two
    /// have identical layouts (X, Y, Z[, W] floats), so the array is block-copied; a double-precision
    /// engine build falls back to converting element by element.
    /// </summary>
    public static TTo[] Convert<TFrom, TTo>(TFrom[] source, Func<TFrom, TTo> convert) where TFrom : unmanaged where TTo : unmanaged
    {
        if (System.Runtime.CompilerServices.Unsafe.SizeOf<TFrom>() == System.Runtime.CompilerServices.Unsafe.SizeOf<TTo>())
        {
            return System.Runtime.InteropServices.MemoryMarshal.Cast<TFrom, TTo>(source).ToArray();
        }

        var result = new TTo[source.Length];
        for (int i = 0; i < result.Length; i++)
        {
            result[i] = convert(source[i]);
        }

        return result;
    }

    public static Transform3D Placement(WorldObject item)
    {
        // Client rotations are Z/Y/X in its Z-up model space. Convert that basis to Y-up.
        float rad = Mathf.Pi / 180;
        var basis = new Basis(Vector3.Up, item.Rotation.Y * rad) * new Basis(Vector3.Right, item.Rotation.X * rad) * new Basis(Vector3.Back, item.Rotation.Z * rad) * new Basis(Vector3.Up, Mathf.Pi);
        return new(basis.Scaled(Vector3.One * item.Scale), new(item.Position.X, item.Position.Y, item.Position.Z));
    }

    /// <summary>
    /// Consume worker results, upload one surface, bind bounded instances, schedule the nearest
    /// missing variant and evict dormant entries. Soft budgets cannot preempt a GPU upload.
    /// </summary>
    public void Update(Camera3D camera)
    {
        var start = System.Diagnostics.Stopwatch.GetTimestamp();
        tick++;
        PruneInstances();
        ConsumeDecodedModel();
        UploadPendingModel();
        BindReadyInstances(start);
        ScheduleModelDecode(camera);
        EvictDormantModels();
        UploadMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }

    /// <summary>
    /// One pass over every instance: drop freed nodes (keeping the per-key counts in step), refresh the
    /// recency of built instances through their cached entry, and collect the ones still waiting.
    /// </summary>
    void PruneInstances()
    {
        unbuilt.Clear();
        int kept = 0;
        for (int n = 0; n < instances.Count; n++)
        {
            var i = instances[n];
            if (!GodotObject.IsInstanceValid(i) || i.IsQueuedForDeletion())
            {
                if (liveKeys.TryGetValue(i.Key, out int count))
                {
                    if (count <= 1)
                    {
                        liveKeys.Remove(i.Key);
                    }
                    else
                    {
                        liveKeys[i.Key] = count - 1;
                    }
                }

                continue;
            }

            if (i.Built)
            {
                if (i.Resolved != null)
                {
                    i.Resolved.LastUsed = tick;
                }
            }
            else
            {
                unbuilt.Add(i);
            }

            instances[kept++] = i;
        }

        instances.RemoveRange(kept, instances.Count - kept);
    }

    /// <summary>
    /// Bind shared meshes, publish selection bounds/clip IDs and batch embedded WMO doodads using
    /// parent-composed transforms.
    /// </summary>
    void BuildInstance(ModelInstance node, Entry entry)
    {
        var min = entry.Bounds.Min;
        var max = entry.Bounds.Max;
        node.Bounds = new(new(min.X, min.Y, min.Z), new(max.X - min.X, max.Y - min.Y, max.Z - min.Z));
        node.AvailableAnimations = entry.Data.AnimationIds ?? [];
        foreach (var mesh in entry.Meshes)
        {
            if (node.Transforms != null)
            {
                var multi = new MultiMesh
                {
                    TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                    Mesh = mesh,
                    InstanceCount = node.Transforms.Length
                };
                for (int n = 0; n < node.Transforms.Length; n++)
                {
                    multi.SetInstanceTransform(n, node.Transforms[n]);
                }

                // A margin with the default (disabled) fade mode acts as hysteresis around the cutoff,
                // not a visual fade: it stops small back-and-forth drift at the boundary from popping
                // geometry in and out every frame while flying.
                node.AddChild(new MultiMeshInstance3D { Multimesh = multi, CastShadow = GeometryInstance3D.ShadowCastingSetting.On, VisibilityRangeEnd = 1600, VisibilityRangeEndMargin = 100 });
            }
            else if (node.Key.Skybox)
            {
                // The sky shell is scaled far out around the camera: it must never be range-culled
                // or cast a shadow over the whole scene.
                node.AddChild(new MeshInstance3D { Mesh = mesh, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
            }
            else
            {
                node.AddChild(new MeshInstance3D { Mesh = mesh, CastShadow = GeometryInstance3D.ShadowCastingSetting.On, VisibilityRangeEnd = 400, VisibilityRangeEndMargin = 30 });
            }
        }

        AttachDoodads(node, entry);
        node.Resolved = entry;
        node.Built = true;
    }

    /// <summary>Scenery toggles remove embedded requests as well as hiding their geometry.</summary>
    public void SetDoodadsVisible(bool visible)
    {
        if (embeddedDoodadsVisible == visible)
        {
            return;
        }

        embeddedDoodadsVisible = visible;
        foreach (var node in instances.ToArray().Where(i => GodotObject.IsInstanceValid(i) && i.Built))
        {
            if (!cache.TryGetValue(node.Key, out var entry) || entry.Data.Doodads.Length == 0)
            {
                continue;
            }

            if (visible)
            {
                AttachDoodads(node, entry);
            }
            else
            {
                foreach (var child in node.GetChildren().OfType<ModelInstance>())
                {
                    child.Visible = false;
                    child.QueueFree();
                }

                node.DoodadsAttached = false;
            }
        }
    }

    void AttachDoodads(ModelInstance node, Entry entry)
    {
        if (!embeddedDoodadsVisible || node.DoodadsAttached)
        {
            return;
        }

        foreach (var group in entry.Data.Doodads.GroupBy(d => d.Path, StringComparer.OrdinalIgnoreCase))
        {
            var local = group.Select(d => new Transform3D(new Basis(new Quaternion(d.Rotation.X, d.Rotation.Y, d.Rotation.Z, d.Rotation.W)).Scaled(Vector3.One * d.Scale), new(d.Position.X, d.Position.Y, d.Position.Z))).ToArray();
            var transforms = node.Transforms == null ? local : node.Transforms.SelectMany(parent => local.Select(t => parent * t)).ToArray();
            Attach(node, new(group.Key), transforms);
        }

        node.DoodadsAttached = true;
    }

    /// <summary>
    /// The client draws LightSkybox models first around the eye, with the world always on top.
    /// Native skyboxes are nested layers 100-600 yd across (inner rocks/clouds included), so plain
    /// geometry hid any world farther than a layer. This variant projects every vertex along its
    /// own view direction onto a sphere just inside the camera far plane (screen position unchanged,
    /// independent of the renderer's depth convention), draws in the translucent pass after the
    /// background sky without writing depth, and is unlit and fog-free. World pixels drawn earlier
    /// always win the depth test; opaque sky batches become alpha 1 to join that pass.
    /// </summary>
    static string SkyboxShader(string code, ModelSurface surface)
    {
        string blend = surface.Blend is 3 or 4 ? "blend_add" : surface.Blend is 5 or 6 ? "blend_mul" : "blend_mix";
        string cull = surface.TwoSided ? "cull_disabled" : "cull_back";
        code = code.Replace("render_mode cull_disabled;", $"render_mode {cull}, {blend}, depth_draw_never, unshaded, fog_disabled;");
        if (surface.Blend >= 2)
        {
            code = code.Replace("ROUGHNESS = 1.0;", "ALPHA = tex.a * batch_color.a; ROUGHNESS = 1.0;");
        }
        else
        {
            code = code.Replace("ROUGHNESS = 1.0;", "ALPHA = batch_color.a; ROUGHNESS = 1.0;");
        }

        string distance = SkyLighting.ShellDistance.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
        return code.Replace(
            "    // SKYBOX_VERTEX: ModelLibrary replaces this line for LightSkybox variants.",
            "    vec3 sky_eye = (MODELVIEW_MATRIX * vec4(VERTEX, 1.0)).xyz;\n    POSITION = PROJECTION_MATRIX * vec4(normalize(sky_eye) * " + distance + ", 1.0);");
    }

    /// <summary>
    /// Upload a batch including optional GPU skin channels. Respect blend/culling metadata; skip
    /// liquid-atlas mipmaps to prevent cross-frame bleeding.
    /// </summary>
    void AddSurface(Entry e, int index, bool skybox)
    {
        var surface = e.Data.Surfaces[index];
        var appearance = e.Materials.Surfaces[index];
        // Batches whose color/weight alpha is constantly zero (closed-door effects, unused burnt
        // tree variants) are invisible in the client; never upload them.
        if (surface.Indices.Length == 0 || appearance.Hidden || (surface.LiquidType != null && !e.Decoded.ContainsKey(surface.Texture)))
        {
            return;
        }

        var arrays = SurfaceArrays(surface);
        Mesh.ArrayFormat flags = 0;
        if (surface.Bones != null && surface.Weights != null)
        {
            arrays[(int)Mesh.ArrayType.Custom0] = surface.Bones.Select(b => (float)b).ToArray();
            arrays[(int)Mesh.ArrayType.Custom1] = surface.Weights;
            flags = (Mesh.ArrayFormat)((ulong)Mesh.ArrayCustomFormat.RgbaFloat << (int)Mesh.ArrayFormat.FormatCustom0Shift | (ulong)Mesh.ArrayCustomFormat.RgbaFloat << (int)Mesh.ArrayFormat.FormatCustom1Shift);
        }

        if (e.Meshes.Count == 0 || e.Meshes[^1].GetSurfaceCount() >= 64)
        {
            e.Meshes.Add(new ArrayMesh());
        }

        var mesh = e.Meshes[^1];
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays, flags: flags);
        var shaderKey = (surface.Blend, surface.TwoSided, surface.Unlit, skybox);
        if (!shaders.TryGetValue(shaderKey, out var shader))
        {
            string code = baseShader.Code;
            if (skybox)
            {
                code = SkyboxShader(code, surface);
            }
            else if (surface.Blend >= 2)
            {
                code = code.Replace("render_mode cull_disabled;", surface.Blend is 3 or 4 ? "render_mode cull_disabled, blend_add, depth_draw_never, fog_disabled;" : surface.Blend is 5 or 6 ? "render_mode cull_disabled, blend_mul, depth_draw_never, fog_disabled;" : "render_mode cull_disabled, blend_mix, depth_prepass_alpha;");
                code = code.Replace("ROUGHNESS = 1.0;", "ALPHA = tex.a * batch_color.a; ROUGHNESS = 1.0;");
            }

            if (!surface.TwoSided)
            {
                code = code.Replace("cull_disabled", "cull_back");
            }

            // Skybox variants are already unshaded.
            if (!skybox && surface.Unlit)
            {
                code = code.Replace("render_mode ", "render_mode unshaded, ");
            }

            shader = new Shader
            {
                Code = code
            };
            shaders[shaderKey] = shader;
        }

        var material = new ShaderMaterial
        {
            Shader = surface.LiquidType != null ? LiquidRendering.Shader : shader,
            // Sky layers keep native batch order and draw before translucent world surfaces such
            // as water.
            RenderPriority = skybox ? Math.Min((int)Material.RenderPriorityMin + e.Meshes.Sum(m => m.GetSurfaceCount()), -1) : 0
        };
        ImageTexture texture = fallback;
        if (surface.Texture.Length > 0 && e.Decoded.TryGetValue(surface.Texture, out var blp))
        {
            if (!textures.TryGetValue(surface.Texture, out var t))
            {
                using var image = Image.CreateFromData(blp.Width, blp.Height, false, Image.Format.Rgba8, blp.Rgba);
                if (surface.LiquidType == null)
                {
                    image.GenerateMipmaps();
                }

                t = new(ImageTexture.CreateFromImage(image));
                textures[surface.Texture] = t;
            }

            if (e.TexturePaths.Add(surface.Texture))
            {
                t.References++;
            }

            texture = t.Texture;
        }

        if (surface.LiquidType != null)
        {
            LiquidRendering.Configure(material, texture, surface.LiquidKind);
            mesh.SurfaceSetMaterial(mesh.GetSurfaceCount() - 1, material);
            return;
        }

        material.SetShaderParameter("albedo_tex", texture);
        material.SetShaderParameter("animation_palette", e.AnimationTexture!);
        material.SetShaderParameter("skinned", surface.Bones != null);
        material.SetShaderParameter("animation_frames", e.Palette.Height);
        material.SetShaderParameter("animation_duration", e.Palette.Duration);
        material.SetShaderParameter("blend_mode", surface.Blend);
        material.SetShaderParameter("vertex_color", (int)surface.ColorMode);
        material.SetShaderParameter("material_palette", e.MaterialTexture!);
        material.SetShaderParameter("material_column", appearance.Column);
        material.SetShaderParameter("color_frames", appearance.ColorFrames);
        material.SetShaderParameter("color_period", appearance.ColorPeriod);
        material.SetShaderParameter("uv_frames", appearance.UvFrames);
        material.SetShaderParameter("uv_period", appearance.UvPeriod);
        material.SetShaderParameter("surface_color", new Vector4(appearance.Color.X, appearance.Color.Y, appearance.Color.Z, appearance.Color.W));
        material.SetShaderParameter("uv_row0", new Vector3(appearance.UvRow0.X, appearance.UvRow0.Y, appearance.UvRow0.Z));
        material.SetShaderParameter("uv_row1", new Vector3(appearance.UvRow1.X, appearance.UvRow1.Y, appearance.UvRow1.Z));
        mesh.SurfaceSetMaterial(mesh.GetSurfaceCount() - 1, material);
        // Include sampled animated bounds, rather than culling to only the unanimated pose.
        mesh.CustomAabb = mesh.GetAabb().Grow(5);
    }

    /// <summary>
    /// Release entry meshes/palette and decrement shared texture references. Dormant eviction calls
    /// this only for entries without live scene bindings.
    /// </summary>
    void DisposeEntry(Entry e)
    {
        foreach (var path in e.TexturePaths)
        {
            if (textures.TryGetValue(path, out var texture) && --texture.References == 0)
            {
                textures.Remove(path);
                texture.Texture.Dispose();
            }
        }

        foreach (var mesh in e.Meshes)
        {
            mesh.Dispose();
        }

        e.AnimationTexture?.Dispose();
        e.MaterialTexture?.Dispose();
    }

    /// <summary>
    /// Cancel scheduling and return the active task so shutdown can await archive reads before
    /// closing the store.
    /// </summary>
    public Task Stop()
    {
        cancellation.Cancel();
        return pending ?? Task.CompletedTask;
    }

    /// <summary>
    /// Promote a completed CPU decode into the upload slot. Canceled/failed requests are consumed
    /// once and missing variants are remembered to prevent per-frame retries.
    /// </summary>
    void ConsumeDecodedModel()
    {
        if (pending is { IsCompleted: true })
        {
            try
            {
                building = pending.GetAwaiter().GetResult();
                buildingKey = pendingKey;
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception e)
            {
                if (pendingKey != null && failed.Add(pendingKey))
                {
                    GD.PushWarning($"Model unavailable: {pendingKey.Path}: {e.Message}");
                }
            }

            pending = null;
            pendingKey = null;
        }
    }

    /// <summary>
    /// Upload within a soft per-frame time budget rather than exactly one surface, and publish the
    /// cache entry only after all surfaces are ready. Drop work whose requesting instances have
    /// disappeared. A multi-surface model (complex creatures, WMOs) that trickled in at one surface
    /// per frame held the decode worker idle for several extra frames per model, which visibly
    /// prolonged the fallback-marker period while flying through new areas.
    /// </summary>
    void UploadPendingModel()
    {
        // HoldPartialUploads (diagnostics only) narrows uploads to one surface per frame and keeps
        // the entry unpublished, so a reload can be timed against a guaranteed partial upload.
        if (building != null && buildingKey != null)
        {
            if (!liveKeys.ContainsKey(buildingKey))
            {
                DisposeEntry(building);
                building = null;
                buildingKey = null;
            }
            else
            {
                if (building.AnimationTexture == null)
                {
                    using var image = Image.CreateFromData(building.Palette.Width, building.Palette.Height, false, Image.Format.Rgbaf, building.Palette.Pixels);
                    building.AnimationTexture = ImageTexture.CreateFromImage(image);
                }

                if (building.MaterialTexture == null)
                {
                    using var image = Image.CreateFromData(building.Materials.Width, building.Materials.Height, false, Image.Format.Rgbaf, building.Materials.Pixels);
                    building.MaterialTexture = ImageTexture.CreateFromImage(image);
                }

                // Leave headroom within the soft 2 ms upload target; a single indivisible surface
                // upload can still exceed the deadline.
                var started = System.Diagnostics.Stopwatch.GetTimestamp();
                int maxSurfaces = HoldPartialUploads ? 1 : 8;
                for (int budget = 0; budget < maxSurfaces && building.Surface < building.Data.Surfaces.Length && (budget == 0 || System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds < 1.5); budget++)
                {
                    AddSurface(building, building.Surface++, buildingKey.Skybox);
                }

                if (building.Surface == building.Data.Surfaces.Length && !HoldPartialUploads)
                {
                    cache[buildingKey] = building;
                    cachedBytes += building.Bytes;
                    building.LastUsed = tick;
                    foreach (string warning in building.Data.Warnings)
                    {
                        GD.PushWarning(buildingKey.Path + ": " + warning);
                    }

                    building = null;
                    buildingKey = null;
                }
            }
        }
    }

    /// <summary>
    /// Attach at most eight ready bindings within the remaining soft two-millisecond budget. Access
    /// refreshes cache recency even for already-built instances.
    /// </summary>
    void BindReadyInstances(long start)
    {
        // WMO attachment can enqueue embedded doodads. Iterate a snapshot so those
        // new requests enter the next frame rather than mutating this enumeration.
        int attached = 0;
        // Only unbuilt instances can need binding (built ones were refreshed in PruneInstances). Embedded
        // doodads attached while binding land in `unbuilt` too, so cap the walk at the count on entry.
        for (int n = 0, count = unbuilt.Count; n < count; n++)
        {
            var i = unbuilt[n];
            if (!GodotObject.IsInstanceValid(i) || i.Built)
            {
                continue;
            }

            if (cache.TryGetValue(i.Key, out var e))
            {
                e.LastUsed = tick;
                if (!i.Built && attached < 8 && System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds < 2)
                {
                    BuildInstance(i, e);
                    attached++;
                }
            }
        }
    }

    /// <summary>
    /// Choose the nearest unbuilt variant and capture its store/cancellation token before starting
    /// the sole decode worker. Never overlap decoding and incremental upload slots.
    /// </summary>
    void ScheduleModelDecode(Camera3D camera)
    {
        if (store != null && pending == null && building == null)
        {
            ModelInstance? next = null;
            float nearest = float.MaxValue;
            var eye = camera.GlobalPosition;
            foreach (var i in unbuilt)
            {
                if (i.Built || !GodotObject.IsInstanceValid(i) || cache.ContainsKey(i.Key) || failed.Contains(i.Key))
                {
                    continue;
                }

                float distance = i.GlobalPosition.DistanceSquaredTo(eye);
                if (next == null || distance < nearest)
                {
                    next = i;
                    nearest = distance;
                }
            }

            if (next != null)
            {
                var key = next.Key;
                var source = store;
                var token = cancellation.Token;
                pendingKey = key;
                pending = Task.Run(() => DecodeModel(key, source, token), token);
            }
        }
    }

    /// <summary>
    /// Reclaim least-recently-used entries until the soft 256 MiB estimate is met. Never evict
    /// resources referenced by a current instance; active scenes may exceed this target.
    /// </summary>
    void EvictDormantModels()
    {
        // Bound retained cache to 256 MiB; in-use resources remain alive, dormant resources evict first.
        long bytes = CachedBytes;
        if (bytes <= 256L * 1024 * 1024)
        {
            return;
        }

        foreach (var pair in cache.OrderBy(p => p.Value.LastUsed).ToArray())
        {
            if (bytes <= 256L * 1024 * 1024)
            {
                break;
            }

            if (liveKeys.ContainsKey(pair.Key))
            {
                continue;
            }

            bytes -= pair.Value.Bytes;
            cachedBytes -= pair.Value.Bytes;
            cache.Remove(pair.Key);
            DisposeEntry(pair.Value);
        }
    }

    /// <summary>
    /// Worker-only native parse, texture/atlas decode and animation sampling. Texture failures
    /// retain warnings/fallbacks; all Godot resource creation is deferred to Update.
    /// </summary>
    Entry DecodeModel(ModelKey key, IClientFiles source, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var data = AssetRef.IsWmo(key.Path)
            ? WmoModel.Read(source, key.Path, key.DoodadSet, token)
            : M2Model.Read(source, key.Path, [key.Texture1, key.Texture2, key.Texture3],
                key.Baked.Length == 0 ? null : key.Baked, token, key.Animation, key.Hair.Length == 0 ? null : key.Hair, key.Fur.Length == 0 ? null : key.Fur, key.HairGeoset);
        Dictionary<string, BlpTexture> decoded = new(StringComparer.OrdinalIgnoreCase);
        foreach (string path in data.Surfaces.Select(s => s.Texture).Where(s => s.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            token.ThrowIfCancellationRequested();
            try
            {
                decoded[path] = path.StartsWith("@liquid/") ? LiquidTextures.For(source).Decode(uint.Parse(path[8..])) : BlpTexture.Decode(source.Read(path), 0);
            }
            catch (Exception e)
            {
                GD.PushWarning($"Model texture unavailable: {path}: {e.Message}");
            }
        }

        return new Entry(data, AnimationPalette.Bake(data, token), MaterialPalette.Bake(data, token), decoded);
    }
}
