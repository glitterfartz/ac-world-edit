using Godot;
using ACWorldEdit.Assets;
namespace ACWorldEdit;

public sealed record ModelKey(string Path, string Texture1 = "", string Texture2 = "", string Texture3 = "", string Baked = "", ushort DoodadSet = 0, ushort Animation = 0);
public partial class ModelInstance : Node3D
{
    internal ModelKey Key = null!;
    internal Transform3D[]? Transforms;
    internal bool Built;
    public Aabb Bounds { get; internal set; }
    public ushort[] AvailableAnimations { get; internal set; } = [];
    public bool ModelReady => Built;
    public void SetPlacement(int index, Transform3D transform)
    {
        if (Transforms == null || index >= Transforms.Length) return; Transforms[index] = transform;
        foreach (var child in GetChildren().OfType<MultiMeshInstance3D>()) child.Multimesh.SetInstanceTransform(index,transform);
    }
}

/// <summary>Single decode worker, incremental GPU uploads, shared resources and GPU idle skinning.</summary>
public sealed class ModelLibrary
{
    sealed class Entry(ModelData data, IdlePalette palette, Dictionary<string, BlpTexture> textures)
    {
        public ModelData Data = data; public IdlePalette Palette = palette;
        public readonly (System.Numerics.Vector3 Min,System.Numerics.Vector3 Max) Bounds = FindBounds(data);
        public Dictionary<string, BlpTexture> Decoded = textures;
        public readonly List<ArrayMesh> Meshes = [];
        public ImageTexture? Idle;
        public int Surface; public long LastUsed;
        public long Bytes => Data.EstimatedBytes + Palette.Pixels.Length + Decoded.Values.Sum(t => (long)t.Rgba.Length);
        public readonly HashSet<string> TexturePaths = new(StringComparer.OrdinalIgnoreCase);
        static (System.Numerics.Vector3,System.Numerics.Vector3) FindBounds(ModelData data)
        {
            var min = new System.Numerics.Vector3(float.MaxValue); var max = new System.Numerics.Vector3(float.MinValue); bool any = false;
            foreach (var positions in data.Surfaces.Select(s => s.Positions).Distinct()) foreach (var point in positions)
            { min = System.Numerics.Vector3.Min(min,point); max = System.Numerics.Vector3.Max(max,point); any = true; }
            return any ? (min,max) : (System.Numerics.Vector3.Zero,System.Numerics.Vector3.Zero);
        }
    }
    sealed class TextureEntry(ImageTexture texture) { public ImageTexture Texture = texture; public int References; }
    readonly Dictionary<ModelKey, Entry> cache = [];
    readonly Dictionary<string, TextureEntry> textures = new(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<ModelKey> failed = [];
    readonly List<ModelInstance> instances = [];
    readonly Shader baseShader = GD.Load<Shader>("res://Shaders/model.gdshader");
    readonly Dictionary<(int Blend,bool TwoSided,bool Unlit), Shader> shaders = [];
    readonly ImageTexture fallback;
    AssetStore? store;
    CancellationTokenSource cancellation = new();
    Task<Entry>? pending;
    ModelKey? pendingKey, buildingKey;
    Entry? building;
    long tick;
    public int ReadyModels => cache.Count;
    public int TextureCount => textures.Count;
    public int FailedModels => failed.Count;
    public int ActiveFailedModels => instances.Count(i => failed.Contains(i.Key));
    public long CachedBytes => cache.Values.Sum(e => e.Bytes);
    public int InstanceCount => instances.Count;
    public double UploadMilliseconds { get; private set; }
    public bool Busy => pending != null || building != null || instances.Any(i => GodotObject.IsInstanceValid(i) && !i.Built && !failed.Contains(i.Key));
    public CreatureCatalog? Catalog { get; private set; }
    public ModelLibrary()
    {
        byte[] pink = [255,0,255,255]; using var image = Image.CreateFromData(1,1,false,Image.Format.Rgba8,pink); fallback = ImageTexture.CreateFromImage(image);
    }
    public async Task SetStore(AssetStore? next)
    {
        cancellation.Cancel(); if (pending != null) { try { await pending; } catch { } }
        pending = null; building = null; pendingKey = buildingKey = null;
        cancellation.Dispose(); cancellation = new();
        foreach (var i in instances) if (GodotObject.IsInstanceValid(i)) i.QueueFree(); instances.Clear();
        foreach (var e in cache.Values) DisposeEntry(e); cache.Clear(); textures.Clear(); failed.Clear();
        store = next; Catalog = next == null ? null : await Task.Run(() => new CreatureCatalog(next));
    }
    public ModelInstance Attach(Node3D parent, ModelKey key, Transform3D[]? transforms = null)
    {
        var node = new ModelInstance { Key = key, Transforms = transforms }; parent.AddChild(node); instances.Add(node); return node;
    }
    public static Transform3D Placement(WorldObject item)
    {
        // Client rotations are Z/Y/X in its Z-up model space. Convert that basis to Y-up.
        float rad = Mathf.Pi / 180;
        var basis = new Basis(Vector3.Up, item.Rotation.Y * rad) * new Basis(Vector3.Right, item.Rotation.X * rad) * new Basis(Vector3.Back, item.Rotation.Z * rad) * new Basis(Vector3.Up,Mathf.Pi);
        return new(basis.Scaled(Vector3.One * item.Scale), new(item.Position.X,item.Position.Y,item.Position.Z));
    }
    public void Update(Camera3D camera)
    {
        var start = System.Diagnostics.Stopwatch.GetTimestamp(); tick++;
        instances.RemoveAll(i => !GodotObject.IsInstanceValid(i) || i.IsQueuedForDeletion());
        if (pending is { IsCompleted: true })
        {
            try { building = pending.GetAwaiter().GetResult(); buildingKey = pendingKey; }
            catch (OperationCanceledException) { }
            catch (Exception e) { if (pendingKey != null && failed.Add(pendingKey)) GD.PushWarning($"Model unavailable: {pendingKey.Path}: {e.Message}"); }
            pending = null; pendingKey = null;
        }
        if (building != null && buildingKey != null)
        {
            if (!instances.Any(i => i.Key == buildingKey)) { DisposeEntry(building); building = null; buildingKey = null; }
            else
            {
                if (building.Idle == null) { using var image = Image.CreateFromData(building.Palette.Width,building.Palette.Height,false,Image.Format.Rgbaf,building.Palette.Pixels); building.Idle = ImageTexture.CreateFromImage(image); }
                // One indivisible surface upload per frame; time is measured, never a hard guarantee.
                if (building.Surface < building.Data.Surfaces.Length) AddSurface(building, building.Data.Surfaces[building.Surface++]);
                else { cache[buildingKey] = building; building.LastUsed = tick; foreach (string warning in building.Data.Warnings) GD.PushWarning(buildingKey.Path + ": " + warning); building = null; buildingKey = null; }
            }
        }
        int attached = 0;
        foreach (var i in instances.ToArray())
        {
            if (cache.TryGetValue(i.Key, out var e))
            {
                e.LastUsed = tick;
                if (!i.Built && attached < 8 && System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds < 2)
                { BuildInstance(i,e); attached++; }
            }
        }
        if (store != null && pending == null && building == null)
        {
            var next = instances.Where(i => !i.Built && !cache.ContainsKey(i.Key) && !failed.Contains(i.Key))
                .OrderBy(i => i.GlobalPosition.DistanceSquaredTo(camera.GlobalPosition)).FirstOrDefault();
            if (next != null)
            {
                var key = next.Key; var source = store; var token = cancellation.Token; pendingKey = key;
                pending = Task.Run(() =>
                {
                    token.ThrowIfCancellationRequested(); var data = key.Path.EndsWith(".wmo",StringComparison.OrdinalIgnoreCase)
                        ? WmoModel.Read(source,key.Path,key.DoodadSet,token) : M2Model.Read(source,key.Path,[key.Texture1,key.Texture2,key.Texture3],key.Baked.Length == 0 ? null : key.Baked,token,key.Animation);
                    Dictionary<string,BlpTexture> decoded = new(StringComparer.OrdinalIgnoreCase);
                    foreach (string path in data.Surfaces.Select(s => s.Texture).Where(s => s.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase))
                    { token.ThrowIfCancellationRequested(); try { decoded[path] = BlpTexture.Decode(source.Read(path),0); } catch (Exception e) { GD.PushWarning($"Model texture unavailable: {path}: {e.Message}"); } }
                    return new Entry(data,IdlePalette.Bake(data,token),decoded);
                },token);
            }
        }
        // Bound retained cache to 256 MiB; in-use resources remain alive, dormant resources evict first.
        long bytes = CachedBytes;
        foreach (var pair in cache.OrderBy(p => p.Value.LastUsed).ToArray())
        {
            if (bytes <= 256L*1024*1024) break;
            if (instances.Any(i => i.Key == pair.Key)) continue;
            bytes -= pair.Value.Bytes; cache.Remove(pair.Key); DisposeEntry(pair.Value);
        }
        UploadMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }
    void BuildInstance(ModelInstance node, Entry entry)
    {
        var min = entry.Bounds.Min; var max = entry.Bounds.Max;
        node.Bounds = new(new(min.X,min.Y,min.Z),new(max.X-min.X,max.Y-min.Y,max.Z-min.Z));
        node.AvailableAnimations = entry.Data.AnimationIds ?? [];
        foreach (var mesh in entry.Meshes)
        {
            if (node.Transforms != null)
            {
                var multi = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = mesh, InstanceCount = node.Transforms.Length };
                for (int n = 0; n < node.Transforms.Length; n++) multi.SetInstanceTransform(n,node.Transforms[n]);
                node.AddChild(new MultiMeshInstance3D { Multimesh = multi, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, VisibilityRangeEnd = 1600 });
            }
            else node.AddChild(new MeshInstance3D { Mesh = mesh, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, VisibilityRangeEnd = 400 });
        }
        foreach (var group in entry.Data.Doodads.GroupBy(d => d.Path,StringComparer.OrdinalIgnoreCase))
        {
            var local = group.Select(d => new Transform3D(new Basis(new Quaternion(d.Rotation.X,d.Rotation.Y,d.Rotation.Z,d.Rotation.W)).Scaled(Vector3.One*d.Scale), new(d.Position.X,d.Position.Y,d.Position.Z))).ToArray();
            var transforms = node.Transforms == null ? local : node.Transforms.SelectMany(parent => local.Select(t => parent*t)).ToArray();
            Attach(node,new(group.Key),transforms);
        }
        node.Built = true;
    }
    void AddSurface(Entry e, ModelSurface surface)
    {
        if (surface.Indices.Length == 0) return;
        var arrays = new Godot.Collections.Array(); arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = surface.Positions.Select(v => new Vector3(v.X,v.Y,v.Z)).ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = surface.Normals.Select(v => new Vector3(v.X,v.Y,v.Z)).ToArray();
        arrays[(int)Mesh.ArrayType.TexUV] = surface.Uv.Select(v => new Vector2(v.X,v.Y)).ToArray(); arrays[(int)Mesh.ArrayType.Index] = surface.Indices;
        if (surface.Colors != null) arrays[(int)Mesh.ArrayType.Color] = surface.Colors.Select(v => new Color(v.X,v.Y,v.Z,v.W)).ToArray();
        Mesh.ArrayFormat flags = 0;
        if (surface.Bones != null && surface.Weights != null)
        {
            arrays[(int)Mesh.ArrayType.Custom0] = surface.Bones.Select(b => (float)b).ToArray(); arrays[(int)Mesh.ArrayType.Custom1] = surface.Weights;
            flags = (Mesh.ArrayFormat)((ulong)Mesh.ArrayCustomFormat.RgbaFloat << (int)Mesh.ArrayFormat.FormatCustom0Shift | (ulong)Mesh.ArrayCustomFormat.RgbaFloat << (int)Mesh.ArrayFormat.FormatCustom1Shift);
        }
        if (e.Meshes.Count == 0 || e.Meshes[^1].GetSurfaceCount() >= 64) e.Meshes.Add(new ArrayMesh()); var mesh = e.Meshes[^1];
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles,arrays,flags:flags);
        var shaderKey = (surface.Blend,surface.TwoSided,surface.Unlit);
        if (!shaders.TryGetValue(shaderKey,out var shader))
        {
            string code = baseShader.Code;
            if (surface.Blend >= 2)
            {
                code = code.Replace("render_mode cull_disabled;",surface.Blend is 3 or 4 ? "render_mode cull_disabled, blend_add, depth_draw_never;" : surface.Blend is 5 or 6 ? "render_mode cull_disabled, blend_mul, depth_draw_never;" : "render_mode cull_disabled, blend_mix, depth_prepass_alpha;");
                code = code.Replace("ROUGHNESS = 1.0;", "ALPHA = tex.a; ROUGHNESS = 1.0;");
            }
            if (!surface.TwoSided) code = code.Replace("cull_disabled","cull_back");
            if (surface.Unlit) code = code.Replace("render_mode ","render_mode unshaded, ");
            shader = new Shader { Code = code }; shaders[shaderKey] = shader;
        }
        var material = new ShaderMaterial { Shader = shader };
        ImageTexture texture = fallback;
        if (surface.Texture.Length > 0 && e.Decoded.TryGetValue(surface.Texture,out var blp))
        {
            if (!textures.TryGetValue(surface.Texture,out var t)) { using var image = Image.CreateFromData(blp.Width,blp.Height,false,Image.Format.Rgba8,blp.Rgba); image.GenerateMipmaps(); t = new(ImageTexture.CreateFromImage(image)); textures[surface.Texture] = t; }
            if (e.TexturePaths.Add(surface.Texture)) t.References++; texture = t.Texture;
        }
        material.SetShaderParameter("albedo_tex",texture); material.SetShaderParameter("idle_palette",e.Idle!);
        material.SetShaderParameter("skinned",surface.Bones != null); material.SetShaderParameter("idle_frames",e.Palette.Height); material.SetShaderParameter("idle_duration",e.Palette.Duration);
        material.SetShaderParameter("blend_mode",surface.Blend); material.SetShaderParameter("vertex_color",surface.Colors != null);
        mesh.SurfaceSetMaterial(mesh.GetSurfaceCount()-1,material);
        // Include sampled animated bounds, rather than culling to only the unanimated pose.
        mesh.CustomAabb = mesh.GetAabb().Grow(5);
    }
    void DisposeEntry(Entry e)
    {
        foreach (var path in e.TexturePaths) if (textures.TryGetValue(path,out var texture) && --texture.References == 0) { textures.Remove(path); texture.Texture.Dispose(); }
        foreach (var mesh in e.Meshes) mesh.Dispose(); e.Idle?.Dispose();
    }
    public async Task Shutdown() => await SetStore(null);
    public Task Stop()
    { cancellation.Cancel(); return pending ?? Task.CompletedTask; }
}
