using Godot;
using ACWorldEdit.Assets;

namespace ACWorldEdit;

/// <summary>
/// Main-thread terrain/liquid GPU owner. Base textures and liquid atlases are reference-counted
/// separately; decoding finishes before this class creates resources.
/// </summary>
public sealed class TerrainRenderer
{
    readonly Shader shader = GD.Load<Shader>("res://Shaders/terrain.gdshader");
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

    public int LiquidSurfaceCount => liquids.Values.Sum(e => e.References);
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
    /// Upload native topology/normals/tint and four layers with packed opacity and shadow masks.
    /// Chunk materials share reference-counted base textures.
    /// </summary>
    public void AddChunk(Node3D parent, TerrainChunk c, Dictionary<string, BlpTexture> decoded, bool bigAlpha)
    {
        var geometry = TerrainGeometry.Build(c);
        var vertices = geometry.Vertices.Select(v => new Vector3(v.X, v.Y, v.Z)).ToArray();
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
            return;
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

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.TexUV] = uv;
        arrays[(int)Mesh.ArrayType.Color] = colors;
        arrays[(int)Mesh.ArrayType.Index] = indices;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        var material = new ShaderMaterial
        {
            Shader = shader
        };
        for (int i = 0; i < 4; i++)
        {
            ImageTexture texture = fallback;
            if (i < c.Layers.Count)
            {
                string name = c.Layers[i].Texture;
                if (!textures.TryGetValue(name, out var entry))
                {
                    entry = new TextureEntry(decoded.TryGetValue(name, out var blp) ? Texture(blp) : fallback);
                    textures.Add(name, entry);
                }

                entry.References++;
                texture = entry.Texture;
            }

            material.SetShaderParameter("layer" + i, texture);
        }

        byte[] alpha = new byte[4096 * 3];
        for (int layer = 1; layer < c.Layers.Count; layer++)
        {
            for (int i = 0; i < 4096; i++)
            {
                alpha[i * 3 + layer - 1] = c.Layers[layer].Alpha[i];
            }
        }

        using var alphaImage = Image.CreateFromData(64, 64, false, Image.Format.Rgb8, alpha);
        material.SetShaderParameter("blend_map", ImageTexture.CreateFromImage(alphaImage));
        byte[] shadow = new byte[4096];
        if (c.Shadow != null)
        {
            for (int i = 0; i < shadow.Length; i++)
            {
                shadow[i] = (byte)(((c.Shadow[i / 8] >> (i & 7)) & 1) * 255);
            }
        }

        using var shadowImage = Image.CreateFromData(64, 64, false, Image.Format.R8, shadow);
        material.SetShaderParameter("shadow_map", ImageTexture.CreateFromImage(shadowImage));
        material.SetShaderParameter("big_alpha", bigAlpha);
        material.SetShaderParameter("grid", grid);
        materials.Add(material);
        mesh.SurfaceSetMaterial(0, material);
        var instance = new MeshInstance3D
        {
            Name = $"Chunk_{c.X}_{c.Y}",
            Mesh = mesh,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.On
        };
        instance.SetMeta("textures", c.Layers.Select(l => l.Texture).ToArray());
        parent.AddChild(instance);
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

        if (!liquids.TryGetValue(surface.Texture, out var entry))
        {
            using var image = Image.CreateFromData(pixels.Width, pixels.Height, false, Image.Format.Rgba8, pixels.Rgba);
            var texture = ImageTexture.CreateFromImage(image);
            var mat = new ShaderMaterial
            {
                Shader = LiquidRendering.Shader
            };
            LiquidRendering.Configure(mat, texture, surface.LiquidKind);
            entry = new(texture, mat);
            liquids.Add(surface.Texture, entry);
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
        node.SetMeta("liquid", surface.Texture);
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
            }

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
