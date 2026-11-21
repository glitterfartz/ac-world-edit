using Godot;

namespace ACWorldEdit;
/// <summary>Scene-node teardown helpers. <c>QueueFree</c> is deferred, so the node is hidden first to vanish this frame.</summary>
public static class NodeExtensions
{
    public static void Discard(this Node3D node)
    {
        if (!GodotObject.IsInstanceValid(node))
        {
            return;
        }

        node.Visible = false;
        node.QueueFree();
    }

    /// <summary>Discards the node of every value, then empties the dictionary.</summary>
    public static void FreeAll<TKey, TValue>(this Dictionary<TKey, TValue> items, Func<TValue, Node3D> node) where TKey : notnull
    {
        foreach (var value in items.Values)
        {
            node(value).Discard();
        }

        items.Clear();
    }

    /// <summary>Discards and removes one entry. Returns false when the key is absent.</summary>
    public static bool Free<TKey, TValue>(this Dictionary<TKey, TValue> items, TKey key, Func<TValue, Node3D> node) where TKey : notnull
    {
        if (!items.Remove(key, out var value))
        {
            return false;
        }

        node(value).Discard();
        return true;
    }
}
