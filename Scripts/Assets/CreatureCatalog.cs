using System.Numerics;
using System.Text;

namespace ACWorldEdit.Assets;
public sealed record CreatureAppearance(string Path, string[] Textures, float Scale, string? BakedTexture);
public sealed class CreatureCatalog
{
    readonly DbcTable displays, models, extras;
    public IReadOnlyDictionary<ushort, string> AnimationNames { get; }

    public CreatureCatalog(AssetStore store)
    {
        displays = new(store.Read("DBFilesClient/CreatureDisplayInfo.dbc"));
        models = new(store.Read("DBFilesClient/CreatureModelData.dbc"));
        extras = new(store.Read("DBFilesClient/CreatureDisplayInfoExtra.dbc"));
        var animations = new DbcTable(store.Read("DBFilesClient/AnimationData.dbc"));
        AnimationNames = animations.Ids.Where(id => id <= ushort.MaxValue).ToDictionary(id => (ushort)id, id => animations.Str(id, 1));
    }

    public CreatureAppearance Resolve(uint id)
    {
        uint model = displays.U(id, 1), extra = displays.U(id, 3);
        string path = BinaryAsset.ModelPath(models.Str(model, 2));
        string dir = path[..(path.LastIndexOf('/') + 1)];
        string[] textures = Enumerable.Range(6, 3).Select(f => displays.Str(id, f)).Select(s => s.Length == 0 ? "" : dir + s + ".blp").ToArray();
        string? baked = extra != 0 && extras.Contains(extra) ? extras.Str(extra, 20) : null;
        return new(path, textures, displays.F(id, 4), string.IsNullOrEmpty(baked) ? null : "Textures/BakedNpcTextures/" + baked);
    }
}
