using Godot;
using ACWorldEdit.Assets;

namespace ACWorldEdit;

/// <summary>
/// Scene binding to a shared model variant. Optional transforms identify a MultiMesh batch; this
/// handle owns one plain scene node while ModelLibrary owns the shared geometry, textures and
/// palettes. It is a plain class (not a Node subclass) so the render library can live outside the
/// Godot project without registering script types.
/// </summary>
public sealed class ModelHandle
{
    internal const string ModelMeta = "acw_model";
    internal const string WmoMeta = "acw_model_wmo";
    internal const string PathMeta = "acw_model_path";

    internal ModelHandle(Node3D node, ModelKey key, Transform3D[]? transforms, ShadowCasterCategory shadowCategory)
    {
        Node = node;
        Key = key;
        Transforms = transforms;
        ShadowCategory = shadowCategory;
        node.SetMeta(ModelMeta, true);
        node.SetMeta(WmoMeta, AssetRef.IsWmo(key.Path));
        node.SetMeta(PathMeta, key.Path);
    }

    /// <summary>The scene node this handle drives; it is parented by <see cref="ModelLibrary.Attach"/>.</summary>
    public Node3D Node { get; }

    internal ModelKey Key;
    public Transform3D[]? Transforms { get; }
    internal bool Built;
    internal ModelLibrary.Entry? Resolved;
    internal bool DoodadsAttached;
    /// <summary>What this instance is for shadow casting, set by its call site.</summary>
    internal ShadowCasterCategory ShadowCategory;
    /// <summary>The caster registry, when this instance registered any geometry; null otherwise.</summary>
    internal ShadowCasterScene? Shadows;
    /// <summary>The handle whose node holds this one, for embedded WMO doodads.</summary>
    internal ModelHandle? Parent;
    internal bool IsDoodad;
    public Aabb Bounds { get; internal set; }
    public ushort[] AvailableAnimations { get; internal set; } = [];
    public bool ModelReady => Built;

    /// <summary>Whether a scene node is a model root, for diagnostics that walk the tree.</summary>
    public static bool IsModelNode(Node node) => node.HasMeta(ModelMeta);

    /// <summary>Whether a model node draws a WMO (a building) rather than an M2.</summary>
    public static bool IsWmoNode(Node node) => node.GetMeta(WmoMeta, false).AsBool();

    /// <summary>The model path a model node was attached with.</summary>
    public static string ModelPath(Node node) => node.GetMeta(PathMeta, "").AsString();

    /// <summary>
    /// Update one transform across all mesh batches. Geometry and appearance resources remain shared
    /// during gizmo movement.
    /// </summary>
    public void SetPlacement(int index, Transform3D transform)
    {
        if (Transforms == null || index >= Transforms.Length)
        {
            return;
        }

        // A rider's seat follow writes the same transform every frame; an exact no-op must not ask
        // for a redraw.
        if (Transforms[index] == transform)
        {
            return;
        }

        Transforms[index] = transform;
        foreach (var child in Node.GetChildren().OfType<MultiMeshInstance3D>())
        {
            child.Multimesh.SetInstanceTransform(index, transform);
        }

        // The proxies share the source MultiMesh, so moving it moves them too. Touch asks for a
        // throttled animated redraw, never an immediate one.
        Shadows?.Touch();
    }
}
