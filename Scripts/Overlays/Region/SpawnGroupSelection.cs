using Godot;

namespace ACWorldEdit;

/// <summary>
/// The spawns selected along with the primary one: Ctrl-click adds or removes a spawn, a Shift-drag box adds
/// everything inside it. Each member gets a ring on the ground so the group can be seen; the primary keeps
/// the overlay's own highlight and its gizmo. Delete, Duplicate and a move act on the primary and the members together.
/// </summary>
public partial class SpawnGroupSelection : Node3D
{
    readonly List<ulong> members = [];
    readonly Dictionary<ulong, MeshInstance3D> rings = [];
    readonly StandardMaterial3D material = new()
    {
        AlbedoColor = new(1f, 0.81f, 0.41f),
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        NoDepthTest = true
    };

    readonly TorusMesh ring = new() { InnerRadius = 0.75f, OuterRadius = 0.95f, Rings = 24, RingSegments = 6 };

    /// <summary>The members, in the order they were added (the primary is not among them).</summary>
    public IReadOnlyList<ulong> Members => members;

    public bool Contains(ulong guid) => members.Contains(guid);

    /// <summary>Add a spawn, or remove it when it is already a member. Returns true when it is now a member.</summary>
    public bool Toggle(ulong guid)
    {
        if (members.Remove(guid))
        {
            Free(guid);
            return false;
        }

        Add(guid);
        return true;
    }

    public void Add(ulong guid)
    {
        if (members.Contains(guid))
        {
            return;
        }

        members.Add(guid);
        var node = new MeshInstance3D { Mesh = ring, MaterialOverride = material, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        AddChild(node);
        rings[guid] = node;
    }

    public void Remove(ulong guid)
    {
        if (members.Remove(guid))
        {
            Free(guid);
        }
    }

    public void Clear()
    {
        foreach (ulong guid in members.ToArray())
        {
            Free(guid);
        }

        members.Clear();
    }

    /// <summary>
    /// Put each ring under its spawn. A member the overlay no longer shows (deleted, undone, out of range) leaves
    /// the group. <paramref name="positionOf"/> returns the spawn's base position, or null when it is gone.
    /// </summary>
    public void Sync(Func<ulong, Vector3?> positionOf)
    {
        foreach (ulong guid in members.ToArray())
        {
            if (positionOf(guid) is Vector3 position)
            {
                rings[guid].Position = position + Vector3.Up * 0.05f;
            }
            else
            {
                Remove(guid);
            }
        }
    }

    void Free(ulong guid)
    {
        if (rings.Remove(guid, out var node))
        {
            node.QueueFree();
        }
    }
}
