using Godot;
using ACWorldEdit.Assets;

namespace ACWorldEdit;

/// <summary>
/// Main-thread terrain/liquid GPU owner. Base textures and liquid atlases are reference-counted
/// separately; decoding finishes before this class creates resources.
/// </summary>
public sealed class TerrainRenderer
{
    readonly Shader shader = ClientShaderGlobals.Load("res://Shaders/terrain/terrain.gdshader");
    readonly Dictionary<string, TextureEntry> textures = new(StringComparer.OrdinalIgnoreCase);
    readonly ImageTexture fallback;
    readonly HashSet<ShaderMaterial> materials = [];
    bool grid;
    sealed class LiquidEntry(ImageTexture texture, ShaderMaterial material)
    {
        public ImageTexture Texture = texture;
        public ShaderMaterial Material = material;
        public int References;
    }

    readonly Dictionary<string, LiquidEntry> liquids = new();
    sealed class TextureEntry(ImageTexture texture)
    {
        public ImageTexture Texture = texture;
        public int References;
    }

    // Splat atlas per tile node, shared by that tile's group materials.
    readonly Dictionary<Node3D, ImageTexture> splats = [];
    public int LiquidSurfaceCount => liquids.Values.Sum(e => e.References);
    /// <summary>Terrain chunks uploaded across loaded and building tiles.</summary>
    public int CellCount { get; private set; }
    public int TextureCount => textures.Count;

    public TerrainRenderer()
    {
        byte[] pixels = new byte[16];
        for (int i = 0; i < 16; i += 4)
        {
            pixels[i] = 255;
            pixels[i + 2] = 255;
            pixels[i + 3] = 255;
        }

        fallback = Texture(new BlpTexture(2, 2, pixels));
    }

    /// <summary>
    /// Create terrain RGBA resources and mip levels on the Godot thread.
    /// </summary>
    static ImageTexture Texture(BlpTexture b)
    {
        using var image = Image.CreateFromData(b.Width, b.Height, false, Image.Format.Rgba8, b.Rgba);
        image.GenerateMipmaps();
        return ImageTexture.CreateFromImage(image);
    }

    /// <summary>
    /// Create the tile root at its viewer origin; terrain and liquid geometry stays tile-local.
    /// </summary>
    public Node3D BeginTile(TileKey key) => new()
    {
        Name = $"Tile_{key.X}_{key.Y}",
        Position = new Vector3((key.X - 32) * TerrainTile.Size, 0, (key.Y - 32) * TerrainTile.Size)
    };
    /// <summary>
    /// Everything about a chunk that needs no GPU object: shaded vertex arrays plus the packed blend
    /// and shadow masks. Pure CPU work, so it runs on the tile worker.
    /// </summary>
    sealed record PreparedChunk(Vector3[] Vertices, Vector3[] Normals, Vector2[] Uv, Color[] Colors, int[] Indices, byte[] Alpha, byte[] Shadow);

    /// <summary>
    /// The chunks of one tile that share an identical layer list, concatenated into one surface.
    /// UV is chunk-local (layers, grid); UV2 holds the chunk's cell in the tile's splat atlas.
    /// </summary>
    public sealed record PreparedGroup(string[] Textures, float[] Scales, string[] Heights, float[] HeightScales, float[] HeightOffsets, int Chunks, Vector3[] Vertices, Vector3[] Normals, Vector2[] Uv, Vector2[] Uv2, Color[] Colors, int[] Indices);

    /// <summary>
    /// A tile ready for upload: one group per distinct layer list and the 1024x1024 RGBA splat atlas
    /// (64x64 per chunk; RGB layer opacity, A baked shadow).
    /// </summary>
    public sealed record PreparedTerrain(PreparedGroup[] Groups, byte[] Splat);

    const int SplatSize = 16 * 64;

    /// <summary>
    /// Worker: prepare every chunk, group chunks by layer list (texture names compared without case)
    /// and pack the splat atlas. A tile draws as one surface per group instead of 256 chunk meshes,
    /// while base textures stay shared between tiles (a per-tile texture array would copy them).
    /// </summary>
    public static PreparedTerrain PrepareTile(TerrainTile tile, ParallelOptions options)
    {
        var chunks = new PreparedChunk?[tile.Chunks.Count];
        Parallel.For(0, chunks.Length, options, i => chunks[i] = PrepareChunk(tile.Chunks[i]));
        byte[] splat = new byte[SplatSize * SplatSize * 4];
        Dictionary<string, List<int>> groups = [];
        for (int i = 0; i < chunks.Length; i++)
        {
            var c = tile.Chunks[i];
            if (chunks[i] is not { } prepared)
            {
                continue;
            }

            for (int y = 0; y < 64; y++)
            {
                for (int x = 0; x < 64; x++)
                {
                    int source = y * 64 + x, target = ((c.Y * 64 + y) * SplatSize + c.X * 64 + x) * 4;
                    splat[target] = prepared.Alpha[source * 3];
                    splat[target + 1] = prepared.Alpha[source * 3 + 1];
                    splat[target + 2] = prepared.Alpha[source * 3 + 2];
                    splat[target + 3] = prepared.Shadow[source];
                }
            }

            string key = string.Join("\n", c.Layers.Select(l => l.Texture.ToLowerInvariant() + "@" + l.Scale + "|" + l.HeightTexture.ToLowerInvariant() + "@" + l.HeightScale + "@" + l.HeightOffset));
            if (!groups.TryGetValue(key, out var members))
            {
                groups[key] = members = [];
            }

            members.Add(i);
        }

        var result = groups.Values.Select(members =>
        {
            int vertexCount = members.Sum(i => chunks[i]!.Vertices.Length);
            var vertices = new Vector3[vertexCount];
            var normals = new Vector3[vertexCount];
            var uv = new Vector2[vertexCount];
            var uv2 = new Vector2[vertexCount];
            var colors = new Color[vertexCount];
            var indices = new int[members.Sum(i => chunks[i]!.Indices.Length)];
            int v = 0, n = 0;
            foreach (int i in members)
            {
                var p = chunks[i]!;
                p.Vertices.CopyTo(vertices, v);
                p.Normals.CopyTo(normals, v);
                p.Uv.CopyTo(uv, v);
                p.Colors.CopyTo(colors, v);
                Array.Fill(uv2, new Vector2(tile.Chunks[i].X, tile.Chunks[i].Y), v, p.Vertices.Length);
                foreach (int index in p.Indices)
                {
                    indices[n++] = index + v;
                }

                v += p.Vertices.Length;
            }

            return new PreparedGroup(tile.Chunks[members[0]].Layers.Select(l => l.Texture).ToArray(), tile.Chunks[members[0]].Layers.Select(l => l.Scale).ToArray(), tile.Chunks[members[0]].Layers.Select(l => l.HeightTexture).ToArray(), tile.Chunks[members[0]].Layers.Select(l => l.HeightScale).ToArray(), tile.Chunks[members[0]].Layers.Select(l => l.HeightOffset).ToArray(), members.Count, vertices, normals, uv, uv2, colors, indices);
        }).ToArray();
        return new(result, splat);
    }

    /// <summary>Build the render arrays for one chunk; null when the chunk is entirely holes.</summary>
    static PreparedChunk? PrepareChunk(TerrainChunk c)
    {
        var geometry = TerrainGeometry.Build(c);
        var vertices = ModelLibrary.Convert(geometry.Vertices, v => new Vector3(v.X, v.Y, v.Z));
        var uv = new Vector2[145];
        var colors = new Color[145];
        int p = 0;
        // The file interleaves 9 outer and 8 inner vertices per row (17 rows).
        for (int row = 0; row < 17; row++)
        {
            int width = (row & 1) == 0 ? 9 : 8;
            for (int col = 0; col < width; col++, p++)
            {
                float x = col + ((row & 1) == 1 ? 0.5f : 0), y = row * 0.5f;
                uv[p] = new Vector2(x / 8, y / 8);
                colors[p] = c.Colors == null ? Colors.White : new Color(c.Colors[p * 4 + 2] / 127f, c.Colors[p * 4 + 1] / 127f, c.Colors[p * 4] / 127f);
            }
        }

        var indices = geometry.Indices;
        if (indices.Length == 0)
        {
            return null;
        }

        var normals = new Vector3[145];
        for (int i = 0; i < indices.Length; i += 3)
        {
            int a = indices[i], b = indices[i + 1], d = indices[i + 2];
            var n = (vertices[d] - vertices[a]).Cross(vertices[b] - vertices[a]);
            normals[a] += n;
            normals[b] += n;
            normals[d] += n;
        }

        for (int i = 0; i < 145; i++)
        {
            // MCNR components are stored along WoW X (north), Y (west), Z (up).
            normals[i] = c.Normals == null ? normals[i].Normalized() : new Vector3(-c.Normals[i * 3 + 1], c.Normals[i * 3 + 2], -c.Normals[i * 3]).Normalized();
            if (normals[i].Y < 0)
            {
                normals[i] = -normals[i];
            }
        }

        byte[] alpha = new byte[4096 * 3];
        for (int layer = 1; layer < c.Layers.Count; layer++)
        {
            for (int i = 0; i < 4096; i++)
            {
                alpha[i * 3 + layer - 1] = c.Layers[layer].Alpha[i];
            }
        }

        byte[] shadow = new byte[4096];
        if (c.Shadow != null)
        {
            for (int i = 0; i < shadow.Length; i++)
            {
                shadow[i] = (byte)(((c.Shadow[i / 8] >> (i & 7)) & 1) * 255);
            }
        }

        return new(vertices, normals, uv, colors, indices, alpha, shadow);
    }

    /// <summary>
    /// Upload the tile's splat atlas; call once after BeginTile and before AddGroup.
    /// </summary>
    public void BeginTerrain(Node3D tile, PreparedTerrain terrain)
    {
        using var image = Image.CreateFromData(SplatSize, SplatSize, false, Image.Format.Rgba8, terrain.Splat);
        splats[tile] = ImageTexture.CreateFromImage(image);
    }

    /// <summary>
    /// Upload one layer group with its own material over shared, reference-counted base textures
    /// and the tile's splat atlas. A node per group keeps per-group frustum culling.
    /// </summary>
    public void AddGroup(Node3D tile, PreparedGroup group, Dictionary<string, BlpTexture> decoded, bool bigAlpha)
    {
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = group.Vertices;
        arrays[(int)Mesh.ArrayType.Normal] = group.Normals;
        arrays[(int)Mesh.ArrayType.TexUV] = group.Uv;
        arrays[(int)Mesh.ArrayType.TexUV2] = group.Uv2;
        arrays[(int)Mesh.ArrayType.Color] = group.Colors;
        arrays[(int)Mesh.ArrayType.Index] = group.Indices;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        var material = new ShaderMaterial
        {
            Shader = shader
        };
        for (int i = 0; i < 4; i++)
        {
            ImageTexture texture = fallback;
            if (i < group.Textures.Length)
            {
                string name = group.Textures[i];
                if (!textures.TryGetValue(name, out var entry))
                {
                    entry = new TextureEntry(decoded.TryGetValue(name, out var blp) ? Texture(blp) : fallback);
                    textures.Add(name, entry);
                }

                entry.References++;
                texture = entry.Texture;
            }

            material.SetShaderParameter("layer" + i, texture);
            // A layer without a height map keeps the neutral fallback; its scale 0 / offset 1 leaves the weight untouched.
            ImageTexture heightTexture = fallback;
            if (i < group.Heights.Length && group.Heights[i].Length > 0 && decoded.ContainsKey(group.Heights[i]))
            {
                string name = group.Heights[i];
                if (!textures.TryGetValue(name, out var entry))
                {
                    entry = new TextureEntry(Texture(decoded[name]));
                    textures.Add(name, entry);
                }

                entry.References++;
                heightTexture = entry.Texture;
            }

            material.SetShaderParameter("height" + i, heightTexture);
        }

        float[] heightScales = new float[4], heightOffsets = [1f, 1f, 1f, 1f];
        for (int i = 0; i < Math.Min(4, group.Heights.Length); i++)
        {
            if (group.Heights[i].Length > 0 && decoded.ContainsKey(group.Heights[i]))
            {
                heightScales[i] = group.HeightScales[i];
                heightOffsets[i] = group.HeightOffsets[i];
            }
        }

        material.SetShaderParameter("height_blend", heightScales.Any(s => s != 0f) || heightOffsets.Any(o => o != 1f));
        material.SetShaderParameter("height_scales", new Vector4(heightScales[0], heightScales[1], heightScales[2], heightScales[3]));
        material.SetShaderParameter("height_offsets", new Vector4(heightOffsets[0], heightOffsets[1], heightOffsets[2], heightOffsets[3]));

        material.SetShaderParameter("layer_scales", new Vector4(
            group.Scales.Length > 0 ? group.Scales[0] : 1f, group.Scales.Length > 1 ? group.Scales[1] : 1f,
            group.Scales.Length > 2 ? group.Scales[2] : 1f, group.Scales.Length > 3 ? group.Scales[3] : 1f));
        material.SetShaderParameter("splat_map", splats[tile]);
        material.SetShaderParameter("big_alpha", bigAlpha);
        material.SetShaderParameter("grid", grid);
        materials.Add(material);
        mesh.SurfaceSetMaterial(0, material);
        var instance = new MeshInstance3D
        {
            Mesh = mesh,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.On
        };
        instance.SetMeta("textures", group.Textures.Concat(group.Heights.Take(4).Where(h => h.Length > 0 && decoded.ContainsKey(h))).ToArray());
        instance.SetMeta("chunks", group.Chunks);
        CellCount += group.Chunks;
        tile.AddChild(instance);
    }

    /// <summary>
    /// Bind one batched liquid type beneath a separate container, keeping terrain height scans and
    /// texture-release accounting separate.
    /// </summary>
    public void AddLiquid(Node3D parent, ModelSurface surface, Dictionary<string, BlpTexture> decoded)
    {
        if (!decoded.TryGetValue(surface.Texture, out var pixels))
        {
            return;
        }

        string key = surface.Texture + "|" + (surface.LiquidStyle?.CacheKey ?? "");
        if (!liquids.TryGetValue(key, out var entry))
        {
            using var image = Image.CreateFromData(pixels.Width, pixels.Height, false, Image.Format.Rgba8, pixels.Rgba);
            var texture = ImageTexture.CreateFromImage(image);
            var mat = new ShaderMaterial
            {
                Shader = LiquidRendering.Shader
            };
            LiquidRendering.Configure(mat, texture, surface.LiquidKind, surface.LiquidStyle);
            entry = new(texture, mat);
            liquids.Add(key, entry);
        }

        entry.References++;
        var container = parent.GetNodeOrNull<Node3D>("Liquids");
        if (container == null)
        {
            container = new Node3D
            {
                Name = "Liquids"
            };
            parent.AddChild(container);
        }

        var node = new MeshInstance3D
        {
            Mesh = LiquidRendering.Mesh(surface, entry.Material),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
        };
        node.SetMeta("liquid", key);
        container.AddChild(node);
    }

    /// <summary>
    /// Unregister materials, release liquid meshes/last-use atlases and queue the tile for deletion.
    /// Call once per loaded or partially built tile.
    /// </summary>
    public void Release(Node3D tile)
    {
        foreach (var child in tile.GetChildren().OfType<MeshInstance3D>())
        {
            if (child.Mesh.SurfaceGetMaterial(0) is ShaderMaterial material)
            {
                materials.Remove(material);
                material.Dispose();
            }

            CellCount -= child.GetMeta("chunks").AsInt32();
            child.Mesh.Dispose();

            foreach (string name in child.GetMeta("textures").AsStringArray())
            {
                if (textures.TryGetValue(name, out var e) && --e.References == 0)
                {
                    textures.Remove(name);
                }
            }
        }

        if (tile.GetNodeOrNull<Node3D>("Liquids") is { } container)
        {
            foreach (var node in container.GetChildren().OfType<MeshInstance3D>())
            {
                string key = node.GetMeta("liquid").AsString();
                if (liquids.TryGetValue(key, out var entry) && --entry.References == 0)
                {
                    liquids.Remove(key);
                    entry.Material.Dispose();
                    entry.Texture.Dispose();
                }

                node.Mesh.Dispose();
            }
        }

        if (splats.Remove(tile, out var splat))
        {
            splat.Dispose();
        }

        tile.QueueFree();
    }

    /// <summary>
    /// Update editor-only borders on live materials without changing geometry or client files.
    /// </summary>
    public void SetGrid(bool value)
    {
        grid = value;
        foreach (var material in materials)
        {
            material.SetShaderParameter("grid", value);
        }
    }
}
