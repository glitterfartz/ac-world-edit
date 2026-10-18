using System.Numerics;
using ACWorldEdit.Assets;
using Xunit;

namespace ACWorldEdit.Tests.Placement;

public class PlacementOwnershipTests
{
    static WorldObject Doodad(uint id, string path = "a.m2", ushort set = 0) => new(id, path, Vector3.Zero, Vector3.Zero, 1, set);

    static WorldObject Wmo(uint id) => new(id, "b.wmo", Vector3.Zero, Vector3.Zero, 1, 0);

    /// <summary>The previous full-rebuild algorithm, kept as the oracle for the incremental one.</summary>
    sealed class Oracle
    {
        public readonly Dictionary<TileKey, List<WorldObject>> Tiles = [];
        public WorldObject[] Globals = [];
        public bool ShowDoodads = true, ShowWmos = true;
        readonly Dictionary<(bool IsWmo, uint Id), TileKey> owner = [];
        public int Objects, Doodads, Wmos;

        public Dictionary<PlacementBatch, List<WorldObject>> Reconcile()
        {
            var candidates = new Dictionary<(bool IsWmo, uint Id), List<(TileKey Tile, WorldObject Item)>>();
            void Collect(TileKey tile, IEnumerable<WorldObject> items)
            {
                foreach (var item in items)
                {
                    if (item.IsWmo ? !ShowWmos : !ShowDoodads)
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

            Collect(new(-1, -1), Globals);
            foreach (var tile in Tiles)
            {
                Collect(tile.Key, tile.Value);
            }

            var desired = new Dictionary<PlacementBatch, List<WorldObject>>();
            foreach (var (id, options) in candidates)
            {
                var chosen = owner.TryGetValue(id, out var prior) ? options.FirstOrDefault(o => o.Tile == prior) : default;
                if (chosen.Item == null)
                {
                    chosen = options.OrderBy(o => o.Tile.X).ThenBy(o => o.Tile.Y).First();
                }

                owner[id] = chosen.Tile;
                var key = new PlacementBatch(chosen.Tile, chosen.Item.Path, chosen.Item.DoodadSet);
                if (!desired.TryGetValue(key, out var set))
                {
                    desired[key] = set = [];
                }

                set.Add(chosen.Item);
            }

            foreach (var id in owner.Keys.Except(candidates.Keys).ToArray())
            {
                owner.Remove(id);
            }

            Objects = candidates.Count;
            Doodads = candidates.Keys.Count(id => !id.IsWmo);
            Wmos = candidates.Keys.Count(id => id.IsWmo);
            return desired;
        }
    }

    static void AssertMatches(PlacementOwnership scene, Oracle oracle, Dictionary<PlacementBatch, WorldObject[]> live)
    {
        var expected = oracle.Reconcile();
        Assert.Equal(oracle.Objects, scene.ObjectCount);
        Assert.Equal(oracle.Doodads, scene.DoodadCount);
        Assert.Equal(oracle.Wmos, scene.WmoCount);
        // `live` is what a caller holding only the reported changes believes is on screen.
        Assert.Equal(expected.Keys.OrderBy(k => (k.Tile.X, k.Tile.Y, k.Path, k.DoodadSet)), live.Keys.OrderBy(k => (k.Tile.X, k.Tile.Y, k.Path, k.DoodadSet)));
        foreach (var (key, items) in expected)
        {
            // A republished tile with equal contents keeps its batch, so compare by value, not reference.
            Assert.Equal(items.OrderBy(i => (i.Id, i.Path, i.DoodadSet)), live[key].OrderBy(i => (i.Id, i.Path, i.DoodadSet)));
            Assert.Equal(live[key], scene.Items(key));
        }
    }

    [Fact]
    public void MatchesFullRebuildOverRandomTileStreaming()
    {
        var random = new Random(42);
        var scene = new PlacementOwnership();
        var oracle = new Oracle();
        var live = new Dictionary<PlacementBatch, WorldObject[]>();

        void Apply(IReadOnlyList<PlacementBatch> changed)
        {
            foreach (var batch in changed)
            {
                var items = scene.Items(batch);
                if (items.Length == 0)
                {
                    live.Remove(batch);
                }
                else
                {
                    live[batch] = items;
                }
            }
        }

        // Ids drawn from a small pool so tiles overlap heavily, as border-straddling objects do.
        List<WorldObject> Items() => Enumerable.Range(0, random.Next(0, 12)).Select(_ =>
        {
            uint id = (uint)random.Next(0, 25);
            return random.Next(3) == 0 ? Wmo(id) : Doodad(id, random.Next(2) == 0 ? "a.m2" : "c.m2", (ushort)random.Next(2));
        }).ToList();

        for (int step = 0; step < 600; step++)
        {
            var key = new TileKey(random.Next(0, 4), random.Next(0, 3));
            switch (random.Next(10))
            {
                case < 5:
                    var items = Items();
                    oracle.Tiles[key] = items;
                    Apply(scene.SetTile(key, items));
                    break;
                case < 8:
                    oracle.Tiles.Remove(key);
                    Apply(scene.RemoveTile(key));
                    break;
                case 8:
                    var globals = Items().Where(i => i.IsWmo).ToArray();
                    oracle.Globals = globals;
                    Apply(scene.SetGlobals(globals));
                    break;
                default:
                    oracle.ShowDoodads = random.Next(2) == 0;
                    oracle.ShowWmos = random.Next(2) == 0;
                    Apply(scene.SetLayers(oracle.ShowDoodads, oracle.ShowWmos));
                    break;
            }

            AssertMatches(scene, oracle, live);
        }
    }

    [Fact]
    public void ObjectStaysWithItsOwnerWhileThatTileIsLoaded()
    {
        var scene = new PlacementOwnership();
        var shared = Doodad(1);
        scene.SetTile(new(5, 5), [shared]);
        scene.SetTile(new(4, 5), [shared]);
        // (5,5) loaded first and still loaded, so (4,5) - lower X - must not take the object.
        Assert.Single(scene.Items(new PlacementBatch(new(5, 5), "a.m2", 0)));
        Assert.Empty(scene.Items(new PlacementBatch(new(4, 5), "a.m2", 0)));

        // Once the owner unloads the object moves to the surviving tile.
        var changed = scene.RemoveTile(new(5, 5));
        Assert.Equal(2, changed.Count);
        Assert.Single(scene.Items(new PlacementBatch(new(4, 5), "a.m2", 0)));
    }

    [Fact]
    public void RepublishingIdenticalTileReportsNoChange()
    {
        var scene = new PlacementOwnership();
        scene.SetTile(new(1, 1), [Doodad(1), Doodad(2)]);
        Assert.Empty(scene.SetTile(new(1, 1), [Doodad(1), Doodad(2)]));
    }

    [Fact]
    public void UnrelatedTileDoesNotDisturbExistingBatches()
    {
        var scene = new PlacementOwnership();
        scene.SetTile(new(1, 1), [Doodad(1), Wmo(2)]);
        var changed = scene.SetTile(new(9, 9), [Doodad(50)]);
        Assert.Equal([new PlacementBatch(new(9, 9), "a.m2", 0)], changed);
    }
}
