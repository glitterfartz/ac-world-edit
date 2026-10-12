using Godot;
using ACWorldEdit.Assets;

namespace ACWorldEdit;

/// <summary>
/// Scene binding to a shared model variant. Optional transforms identify a MultiMesh batch; this
/// node owns instances while ModelLibrary owns shared resources.
/// </summary>
public partial class ModelInstance : Node3D
{
    internal ModelKey Key = null!;
    internal Transform3D[]? Transforms;
    internal bool Built;
    public Aabb Bounds { get; internal set; }
    public ushort[] AvailableAnimations { get; internal set; } = [];
    public bool ModelReady => Built;

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

        Transforms[index] = transform;
        foreach (var child in GetChildren().OfType<MultiMeshInstance3D>())
        {
            child.Multimesh.SetInstanceTransform(index, transform);
        }
    }
}
