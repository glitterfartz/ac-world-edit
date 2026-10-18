namespace ACWorldEdit.Assets;

/// <summary>One render batch: the placements of a single model variant that a tile owns.</summary>
public readonly record struct PlacementBatch(TileKey Tile, string Path, ushort DoodadSet);

/// <summary>
/// Deduplicates ADT placements by native type and ID across loaded tiles and groups the survivors
/// into per-tile, per-model batches. A border-straddling object appears in several neighbouring
/// tiles; it keeps its current owner tile for as long as that tile stays loaded (so a tile
/// streaming in or out elsewhere never reassigns it), otherwise the lowest tile X, then Y, takes it.
/// Every change reports only the batches whose contents actually changed, so the caller rebuilds
/// those instead of the whole scene. Items within a batch keep the order of their owner tile's list.
/// </summary>
public sealed class PlacementOwnership
{
    static readonly TileKey GlobalTile = new(-1, -1);
    readonly Dictionary<TileKey, WorldObject[]> tiles = [];
    WorldObject[] globals = [];
    readonly Dictionary<(bool IsWmo, uint Id), List<(TileKey Tile, int Index, WorldObject Item)>> candidates = [];
    readonly Dictionary<(bool IsWmo, uint Id), TileKey> owner = [];
    readonly Dictionary<(bool IsWmo, uint Id), (PlacementBatch Batch, int Index, WorldObject Item)> placed = [];
    readonly Dictionary<PlacementBatch, List<(int Index, WorldObject Item)>> batches = [];
    // Contents of each batch the current operation touched, captured before its first edit.
    Dictionary<PlacementBatch, WorldObject[]> before = [];
    bool showDoodads = true, showWmos = true;
    int doodads, wmos;

    public int ObjectCount => candidates.Count;
    public int DoodadCount => doodads;
    public int WmoCount => wmos;

    /// <summary>Show or hide each placement kind; hidden kinds contribute no candidates.</summary>
    public IReadOnlyList<PlacementBatch> SetLayers(bool showDoodads, bool showWmos)
    {
        if (this.showDoodads == showDoodads && this.showWmos == showWmos)
        {
            return [];
        }

        this.showDoodads = showDoodads;
        this.showWmos = showWmos;
        before = batches.ToDictionary(b => b.Key, b => b.Value.Select(e => e.Item).ToArray());
        candidates.Clear();
        placed.Clear();
        batches.Clear();
        doodads = wmos = 0;
        var touched = new HashSet<(bool IsWmo, uint Id)>();
        Add(GlobalTile, globals, touched);
        foreach (var (tile, items) in tiles)
        {
            Add(tile, items, touched);
        }

        Resolve(touched);
        foreach (var id in owner.Keys.Where(id => !candidates.ContainsKey(id)).ToArray())
        {
            owner.Remove(id);
        }

        return Finish(before.Keys.Concat(batches.Keys));
    }

    /// <summary>Publish a tile's completed placement list, replacing any earlier one.</summary>
    public IReadOnlyList<PlacementBatch> SetTile(TileKey key, IEnumerable<WorldObject> items)
    {
        before = [];
        var touched = new HashSet<(bool IsWmo, uint Id)>();
        if (tiles.TryGetValue(key, out var old))
        {
            Remove(key, old, touched);
        }

        var added = items.ToArray();
        tiles[key] = added;
        Add(key, added, touched);
        Resolve(touched);
        return Finish(before.Keys);
    }

    public IReadOnlyList<PlacementBatch> RemoveTile(TileKey key)
    {
        if (!tiles.Remove(key, out var old))
        {
            return [];
        }

        before = [];
        var touched = new HashSet<(bool IsWmo, uint Id)>();
        Remove(key, old, touched);
        Resolve(touched);
        return Finish(before.Keys);
    }

    /// <summary>Publish the map's global WMOs, which have no terrain tile of their own.</summary>
    public IReadOnlyList<PlacementBatch> SetGlobals(IEnumerable<WorldObject> items)
    {
        before = [];
        var touched = new HashSet<(bool IsWmo, uint Id)>();
        Remove(GlobalTile, globals, touched);
        globals = items.ToArray();
        Add(GlobalTile, globals, touched);
        Resolve(touched);
        return Finish(before.Keys);
    }

    public void Clear()
    {
        tiles.Clear();
        globals = [];
        candidates.Clear();
        owner.Clear();
        placed.Clear();
        batches.Clear();
        before = [];
        doodads = wmos = 0;
    }

    /// <summary>The batch's placements in owner-tile order; empty when the batch no longer exists.</summary>
    public WorldObject[] Items(PlacementBatch batch) => batches.TryGetValue(batch, out var list) ? list.Select(e => e.Item).ToArray() : [];

    bool Visible(WorldObject item) => item.IsWmo ? showWmos : showDoodads;

    void Add(TileKey tile, WorldObject[] items, HashSet<(bool IsWmo, uint Id)> touched)
    {
        for (int i = 0; i < items.Length; i++)
        {
            var item = items[i];
            if (!Visible(item))
            {
                continue;
            }

            var id = (item.IsWmo, item.Id);
            if (!candidates.TryGetValue(id, out var list))
            {
                candidates[id] = list = [];
                if (item.IsWmo)
                {
                    wmos++;
                }
                else
                {
                    doodads++;
                }
            }

            list.Add((tile, i, item));
            touched.Add(id);
        }
    }

    void Remove(TileKey tile, WorldObject[] items, HashSet<(bool IsWmo, uint Id)> touched)
    {
        foreach (var item in items)
        {
            var id = (item.IsWmo, item.Id);
            if (!Visible(item))
            {
                continue;
            }

            touched.Add(id);
            if (candidates.TryGetValue(id, out var list))
            {
                list.RemoveAll(e => e.Tile == tile);
                if (list.Count == 0)
                {
                    candidates.Remove(id);
                    if (item.IsWmo)
                    {
                        wmos--;
                    }
                    else
                    {
                        doodads--;
                    }
                }
            }
        }
    }

    void Resolve(HashSet<(bool IsWmo, uint Id)> touched)
    {
        foreach (var id in touched)
        {
            if (!candidates.TryGetValue(id, out var options))
            {
                owner.Remove(id);
                Unplace(id);
                continue;
            }

            // Keep the current owner while it is still a candidate; otherwise lowest X, then Y (first
            // listed wins a same-tile duplicate).
            (TileKey Tile, int Index, WorldObject Item) chosen = default;
            bool found = false;
            if (owner.TryGetValue(id, out var prior))
            {
                foreach (var option in options)
                {
                    if (option.Tile == prior)
                    {
                        chosen = option;
                        found = true;
                        break;
                    }
                }
            }

            if (!found)
            {
                chosen = options[0];
                foreach (var option in options)
                {
                    if (option.Tile.X < chosen.Tile.X || option.Tile.X == chosen.Tile.X && option.Tile.Y < chosen.Tile.Y)
                    {
                        chosen = option;
                    }
                }
            }

            owner[id] = chosen.Tile;
            var batch = new PlacementBatch(chosen.Tile, chosen.Item.Path, chosen.Item.DoodadSet);
            if (placed.TryGetValue(id, out var current))
            {
                if (current.Batch == batch && current.Index == chosen.Index && ReferenceEquals(current.Item, chosen.Item))
                {
                    continue;
                }

                Unplace(id);
            }

            Touch(batch);
            if (!batches.TryGetValue(batch, out var members))
            {
                batches[batch] = members = [];
            }

            members.Add((chosen.Index, chosen.Item));
            placed[id] = (batch, chosen.Index, chosen.Item);
        }
    }

    void Unplace(in (bool IsWmo, uint Id) id)
    {
        if (!placed.Remove(id, out var current))
        {
            return;
        }

        Touch(current.Batch);
        var members = batches[current.Batch];
        members.RemoveAll(e => ReferenceEquals(e.Item, current.Item) && e.Index == current.Index);
        if (members.Count == 0)
        {
            batches.Remove(current.Batch);
        }
    }

    void Touch(PlacementBatch batch)
    {
        if (!before.ContainsKey(batch))
        {
            before[batch] = batches.TryGetValue(batch, out var members) ? members.Select(e => e.Item).ToArray() : [];
        }
    }

    /// <summary>Order the touched batches and keep only those whose contents differ from before.</summary>
    List<PlacementBatch> Finish(IEnumerable<PlacementBatch> touched)
    {
        List<PlacementBatch> changed = [];
        foreach (var batch in touched.Distinct())
        {
            WorldObject[] now = [];
            if (batches.TryGetValue(batch, out var members))
            {
                members.Sort((a, b) => a.Index.CompareTo(b.Index));
                now = members.Select(e => e.Item).ToArray();
            }

            if (!before.TryGetValue(batch, out var was) || !was.SequenceEqual(now))
            {
                changed.Add(batch);
            }
        }

        before = [];
        return changed;
    }
}
