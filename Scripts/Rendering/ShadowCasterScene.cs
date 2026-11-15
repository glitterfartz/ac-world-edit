using Godot;
using ACWorldEdit.Assets;

namespace ACWorldEdit;

/// <summary>
/// Owns the shadow-caster proxies and the one caster material. A proxy is a child of the geometry it
/// duplicates, so it inherits the source's transform and visibility; it shares the source's mesh (or
/// MultiMesh) and is drawn only on the cascade camera whose cull mask includes its category layer.
/// The main camera clears those layers, so a proxy never appears in the normal view.
/// <para>
/// Registrations happen when geometry is created and are remembered even while shadows are off, so
/// enabling the pass later does not need a scene walk. A <see cref="Version"/> bump tells
/// <see cref="SunShadows"/> that the registered set changed.
/// </para>
/// </summary>
public sealed class ShadowCasterScene
{
    /// <summary>User layers 17 to 20, one per caster category. 1 is layer 1.</summary>
    public const int TerrainLayer = 17;
    public const int WmoShellLayer = 18;
    public const int WmoDoodadLayer = 19;
    public const int AdtDoodadLayer = 20;

    public const uint ShadowLayerMask =
        (1u << (TerrainLayer - 1)) | (1u << (WmoShellLayer - 1)) | (1u << (WmoDoodadLayer - 1)) | (1u << (AdtDoodadLayer - 1));

    /// <summary>Every layer except the four shadow-proxy layers.</summary>
    public const uint MainCameraMask = 0xFFFFFu & ~ShadowLayerMask;

    sealed class Caster(GeometryInstance3D source, ShadowCasterCategory category)
    {
        public readonly GeometryInstance3D Source = source;
        public readonly ShadowCasterCategory Category = category;
        public GeometryInstance3D? Proxy;
    }

    readonly Dictionary<GeometryInstance3D, Caster> casters = [];
    readonly ShaderMaterial material;
    bool enabled;

    /// <summary>
    /// Bumped whenever the registered set or the enabled state changes; the cascade pass refreshes
    /// only when this differs from the value it last rendered with.
    /// </summary>
    public int Version { get; private set; }

    /// <summary>Proxies currently in the scene tree (zero while shadows are off).</summary>
    public int ProxyCount => casters.Values.Count(c => c.Proxy != null);

    public ShadowCasterScene()
    {
        material = new ShaderMaterial
        {
            Shader = ClientShaderGlobals.Load("res://Shaders/shadow/shadow_caster.gdshader")
        };
    }

    /// <summary>The render layer a category's proxies draw on.</summary>
    public static uint LayerFor(ShadowCasterCategory category) => category switch
    {
        ShadowCasterCategory.Terrain => 1u << (TerrainLayer - 1),
        ShadowCasterCategory.WmoShell => 1u << (WmoShellLayer - 1),
        ShadowCasterCategory.WmoDoodad => 1u << (WmoDoodadLayer - 1),
        ShadowCasterCategory.AdtDoodad => 1u << (AdtDoodadLayer - 1),
        _ => 0u,
    };

    /// <summary>
    /// Remember a geometry node as a caster and, when the pass is on, create its proxy now. The
    /// caller must call <see cref="Unregister"/> before disposing the source's mesh.
    /// </summary>
    public void Register(GeometryInstance3D source, ShadowCasterCategory category)
    {
        if (category == ShadowCasterCategory.None || !GodotObject.IsInstanceValid(source) || casters.ContainsKey(source))
        {
            return;
        }

        var caster = new Caster(source, category);
        casters[source] = caster;
        // The signal fires while the source is still valid, so the proxy can be freed before the
        // source's mesh (owned by the model cache or TerrainRenderer) is disposed.
        source.TreeExiting += () => Unregister(source);
        if (enabled)
        {
            CreateProxy(caster);
        }

        Version++;
    }

    /// <summary>
    /// Free a source's proxy immediately and forget the registration. Call before disposing the
    /// source's mesh; a later <see cref="Unregister"/> from the tree signal is a no-op.
    /// </summary>
    public void Unregister(Node source)
    {
        if (source is not GeometryInstance3D geometry || !casters.Remove(geometry, out var caster))
        {
            return;
        }

        FreeProxy(caster);
        Version++;
    }

    /// <summary>Unregister every proxy at or below one node, before a scene reset frees the meshes.</summary>
    public void UnregisterTree(Node root)
    {
        foreach (var source in casters.Keys
            .Where(s => GodotObject.IsInstanceValid(s) && (s == root || root.IsAncestorOf(s)))
            .ToArray())
        {
            Unregister(source);
        }
    }

    /// <summary>Free every proxy and drop every registration (map or client change).</summary>
    public void Clear()
    {
        foreach (var caster in casters.Values)
        {
            FreeProxy(caster);
        }

        casters.Clear();
        Version++;
    }

    /// <summary>
    /// Draw or skip the proxies. Turning the pass on creates all proxies; turning it off frees them,
    /// which leaves the sources untouched.
    /// </summary>
    public void SetEnabled(bool value)
    {
        if (enabled == value)
        {
            return;
        }

        enabled = value;
        foreach (var caster in casters.Values)
        {
            if (value)
            {
                CreateProxy(caster);
            }
            else
            {
                FreeProxy(caster);
            }
        }

        Version++;
    }

    void CreateProxy(Caster caster)
    {
        if (caster.Proxy != null || !GodotObject.IsInstanceValid(caster.Source))
        {
            return;
        }

        GeometryInstance3D? proxy = caster.Source switch
        {
            MultiMeshInstance3D multi => new MultiMeshInstance3D { Multimesh = multi.Multimesh },
            MeshInstance3D mesh => new MeshInstance3D { Mesh = mesh.Mesh },
            _ => null,
        };
        if (proxy == null)
        {
            return;
        }

        proxy.Name = "ShadowProxy";
        proxy.MaterialOverride = material;
        proxy.Layers = LayerFor(caster.Category);
        proxy.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        // Diagnostics and the perf breakdown skip proxies by this marker.
        proxy.SetMeta("shadow_proxy", true);
        caster.Source.AddChild(proxy);
        caster.Proxy = proxy;
    }

    static void FreeProxy(Caster caster)
    {
        if (caster.Proxy is { } proxy)
        {
            caster.Proxy = null;
            // Immediate, not QueueFree: the source's mesh may be disposed before the next frame.
            if (GodotObject.IsInstanceValid(proxy))
            {
                proxy.Free();
            }
        }
    }
}
