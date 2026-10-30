using Godot;
using ACWorldEdit.Assets;

namespace ACWorldEdit;
/// <summary>
/// Deduplicate ADT references by native type/ID and batch static placements per tile/variant.
/// Global WDT objects have a separate owner key.
/// </summary>
public sealed class WorldObjectScene(Node3D root, ModelLibrary library)
{
    // Which tile owns each deduplicated object, and what each batch holds. A border-straddling object
    // can be referenced by several neighbouring tiles; see PlacementOwnership for the sticky rule.
    readonly PlacementOwnership ownership = new();
    readonly Dictionary<(TileKey Tile, ModelKey Model), (ModelInstance Node, WorldObject[] Items)> batches = [];
    public int ObjectCount => ownership.ObjectCount;
    public int DoodadCount => ownership.DoodadCount;
    public int WmoCount => ownership.WmoCount;

    public void SetLayers(bool doodads, bool wmos)
    {
        library.SetDoodadsVisible(doodads);
        Apply(ownership.SetLayers(doodads, wmos));
    }

    /// <summary>
    /// Publish a completed tile list; partial/stale uploads never attach scenery.
    /// </summary>
    public void SetTile(TileKey key, List<WorldObject> items)
    {
        Apply(ownership.SetTile(key, items));
    }

    /// <summary>
    /// Drop a tile owner and rebuild only batches whose deduplicated memberships change.
    /// </summary>
    public void RemoveTile(TileKey key)
    {
        Apply(ownership.RemoveTile(key));
    }

    /// <summary>
    /// Publish global WMOs for the map, including interiors without terrain owners.
    /// </summary>
    public void SetGlobals(List<WorldObject> items)
    {
        Apply(ownership.SetGlobals(items));
    }

    /// <summary>
    /// Collect WMO floor heights under a viewer-space point from visible, ready buildings. Pending
    /// counts covering buildings whose geometry is not decoded yet, so callers can refuse to guess.
    /// Hidden WMO layers contribute nothing.
    /// </summary>
    public (List<float> Heights, int Pending) FloorHeights(Vector3 point)
    {
        List<float> heights = [];
        int pending = 0;
        var probe = new System.Numerics.Vector3(point.X, point.Y, point.Z);
        foreach (var ((_, model), (_, items)) in batches)
        {
            if (!AssetRef.IsWmo(model.Path))
            {
                continue;
            }

            var covering = items.Where(item => GroundProbe.Covers(item, probe)).ToArray();
            if (covering.Length == 0)
            {
                continue;
            }

            var data = library.ReadyData(model);
            if (data == null)
            {
                pending += covering.Length;
                continue;
            }

            foreach (var item in covering)
            {
                heights.AddRange(GroundProbe.WmoHeights(item, data, probe));
            }
        }

        return (heights, pending);
    }

    /// <summary>
    /// Bound WMO placements whose geometry is decoded, for opt-in ground-probe smoke checks.
    /// </summary>
    public IEnumerable<WorldObject> ReadyWmoPlacements() => batches
        .Where(pair => AssetRef.IsWmo(pair.Key.Model.Path) && library.ReadyData(pair.Key.Model) != null)
        .SelectMany(pair => pair.Value.Items);

    /// <summary>One attached placement for the diagnostic dump: its model, view-space transform and local bounds (empty until the model is ready).</summary>
    public readonly record struct Placed(WorldObject Item, ModelKey Model, Transform3D Transform, Aabb Bounds, bool Ready);

    public IEnumerable<Placed> Placements() => batches.SelectMany(pair => pair.Value.Items.Select((item, i) =>
        new Placed(item, pair.Key.Model, pair.Value.Node.Transforms is { } t && i < t.Length ? t[i] : ModelLibrary.Placement(item), pair.Value.Node.Bounds, pair.Value.Node.ModelReady)));

    /// <summary>
    /// Hide/free batches and clear ownership on map/client changes. ModelLibrary still owns the
    /// shared cache.
    /// </summary>
    public void Clear()
    {
        batches.FreeAll(b => b.Node);
        ownership.Clear();
    }

    /// <summary>
    /// Rebuild exactly the batches whose membership changed; every other batch node stays as is.
    /// ModelLibrary attaches embedded doodads after each WMO becomes ready.
    /// </summary>
    void Apply(IReadOnlyList<PlacementBatch> changed)
    {
        foreach (var batch in changed)
        {
            var key = (batch.Tile, new ModelKey(batch.Path, DoodadSet: batch.DoodadSet));
            if (batches.ContainsKey(key))
            {
                batches.Free(key, b => b.Node);
            }

            var items = ownership.Items(batch);
            if (items.Length > 0)
            {
                batches[key] = (library.Attach(root, key.Item2, items.Select(ModelLibrary.Placement).ToArray()), items);
            }
        }
    }
}
