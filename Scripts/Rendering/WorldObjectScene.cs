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
    // A border-straddling object can be referenced by more than one neighboring tile. Sticky
    // ownership keeps it on whichever tile already owns it (while that tile stays loaded) so an
    // unrelated tile streaming in/out elsewhere cannot reassign it and force a visible rebuild.
    readonly Dictionary<(bool IsWmo, uint Id), TileKey> owner = [];
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
            if (!model.Path.EndsWith(".wmo", StringComparison.OrdinalIgnoreCase))
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
        .Where(pair => pair.Key.Model.Path.EndsWith(".wmo", StringComparison.OrdinalIgnoreCase) && library.ReadyData(pair.Key.Model) != null)
        .SelectMany(pair => pair.Value.Items);

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
        owner.Clear();
        globals = [];
        ObjectCount = 0;
        DoodadCount = WmoCount = 0;
    }

    /// <summary>
    /// Pick sticky owners for duplicate/border-shared IDs and retain unchanged batches. An object
    /// keeps its current owner tile as long as that tile stays loaded, so one tile streaming in or
    /// out elsewhere cannot reassign a shared object and force an unrelated batch to rebuild.
    /// ModelLibrary attaches embedded doodads after each WMO becomes ready.
    /// </summary>
    void Reconcile()
    {
        var candidates = new Dictionary<(bool IsWmo, uint Id), List<(TileKey Tile, WorldObject Item)>>();
        void Collect(TileKey tile, IEnumerable<WorldObject> items)
        {
            foreach (var item in items)
            {
                if (item.IsWmo ? !showWmos : !showDoodads)
                {
                    continue;
                }

                var id = (item.IsWmo, item.Id);
                if (!candidates.TryGetValue(id, out var list))
                {
                    candidates[id] = list = [];
                }

                list.Add((tile, item));
            }
        }

        Collect(new(-1, -1), globals);
        foreach (var tile in tiles)
        {
            Collect(tile.Key, tile.Value);
        }

        var desired = new Dictionary<(TileKey Tile, ModelKey Model), List<WorldObject>>();
        foreach (var (id, options) in candidates)
        {
            var chosen = owner.TryGetValue(id, out var prior) ? options.FirstOrDefault(o => o.Tile == prior) : default;
            if (chosen.Item == null)
            {
                chosen = options.OrderBy(o => o.Tile.X).ThenBy(o => o.Tile.Y).First();
            }

            owner[id] = chosen.Tile;
            var key = (chosen.Tile, new ModelKey(chosen.Item.Path, DoodadSet: chosen.Item.DoodadSet));
            if (!desired.TryGetValue(key, out var list))
            {
                desired[key] = list = [];
            }

            list.Add(chosen.Item);
        }

        foreach (var id in owner.Keys.Except(candidates.Keys).ToArray())
        {
            owner.Remove(id);
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

        ObjectCount = candidates.Count;
        DoodadCount = candidates.Keys.Count(id => !id.IsWmo);
        WmoCount = candidates.Keys.Count(id => id.IsWmo);
    }
}
