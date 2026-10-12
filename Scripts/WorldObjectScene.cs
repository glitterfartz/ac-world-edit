using Godot;
using ACWorldEdit.Assets;

namespace ACWorldEdit;
/// <summary>
/// Deduplicate ADT references by native type/ID and batch static placements per tile/variant.
/// Global WDT objects have a separate owner key.
/// </summary>
public sealed class WorldObjectScene(Node3D root, ModelLibrary library)
{
    readonly Dictionary<TileKey, List<WorldObject>> tiles = [];
    readonly Dictionary<(TileKey Tile, ModelKey Model), (ModelInstance Node, WorldObject[] Items)> batches = [];
    WorldObject[] globals = [];
    public int ObjectCount { get; private set; }
    public int DoodadCount { get; private set; }
    public int WmoCount { get; private set; }

    bool showDoodads = true, showWmos = true;
    public void SetLayers(bool doodads, bool wmos)
    {
        showDoodads = doodads;
        showWmos = wmos;
        library.SetDoodadsVisible(doodads);
        Reconcile();
    }

    /// <summary>
    /// Publish a completed tile list; partial/stale uploads never attach scenery.
    /// </summary>
    public void SetTile(TileKey key, List<WorldObject> items)
    {
        tiles[key] = items;
        Reconcile();
    }

    /// <summary>
    /// Drop a tile owner and rebuild only batches whose deduplicated memberships change.
    /// </summary>
    public void RemoveTile(TileKey key)
    {
        if (tiles.Remove(key))
        {
            Reconcile();
        }
    }

    /// <summary>
    /// Publish global WMOs for the map, including interiors without terrain owners.
    /// </summary>
    public void SetGlobals(List<WorldObject> items)
    {
        globals = items.ToArray();
        Reconcile();
    }

    /// <summary>
    /// Hide/free batches and clear ownership on map/client changes. ModelLibrary still owns the
    /// shared cache.
    /// </summary>
    public void Clear()
    {
        foreach (var b in batches.Values)
        {
            if (GodotObject.IsInstanceValid(b.Node))
            {
                b.Node.Visible = false;
                b.Node.QueueFree();
            }
        }

        batches.Clear();
        tiles.Clear();
        globals = [];
        ObjectCount = 0;
        DoodadCount = WmoCount = 0;
    }

    /// <summary>
    /// Pick deterministic owners for duplicate IDs and retain unchanged batches. ModelLibrary
    /// attaches embedded doodads after each WMO becomes ready.
    /// </summary>
    void Reconcile()
    {
        var seen = new HashSet<(bool, uint)>();
        var desired = new Dictionary<(TileKey Tile, ModelKey Model), List<WorldObject>>();
        void Add(TileKey tile, IEnumerable<WorldObject> items)
        {
            foreach (var item in items)
            {
                if (item.IsWmo ? !showWmos : !showDoodads)
                {
                    continue;
                }

                if (!seen.Add((item.IsWmo, item.Id)))
                {
                    continue;
                }

                var key = (tile, new ModelKey(item.Path, DoodadSet: item.DoodadSet));
                if (!desired.TryGetValue(key, out var list))
                {
                    desired[key] = list = [];
                }

                list.Add(item);
            }
        }

        Add(new(-1, -1), globals);
        foreach (var tile in tiles.OrderBy(p => p.Key.X).ThenBy(p => p.Key.Y))
        {
            Add(tile.Key, tile.Value);
        }

        foreach (var pair in batches.ToArray())
        {
            if (desired.TryGetValue(pair.Key, out var items) && items.SequenceEqual(pair.Value.Items))
            {
                continue;
            }

            pair.Value.Node.Visible = false;
            pair.Value.Node.QueueFree();
            batches.Remove(pair.Key);
        }

        foreach (var pair in desired)
        {
            if (!batches.ContainsKey(pair.Key))
            {
                batches[pair.Key] = (library.Attach(root, pair.Key.Model, pair.Value.Select(ModelLibrary.Placement).ToArray()), pair.Value.ToArray());
            }
        }

        ObjectCount = seen.Count;
        DoodadCount = seen.Count(id => !id.Item1);
        WmoCount = seen.Count(id => id.Item1);
    }
}
