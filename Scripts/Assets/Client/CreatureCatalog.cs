using System.Numerics;
using System.Text;

namespace ACWorldEdit.Assets;

/// <summary>
/// Model path, three replaceable skins, display scale and optional baked body texture. Humanoid
/// displays with an extra row also carry the hair texture (type 6), fur/extra skin texture (type 8)
/// and hair geoset chosen by their race, sex, style and colors. Equipment is not composed.
/// </summary>
public sealed record CreatureAppearance(string Path, string[] Textures, float Scale, string? BakedTexture, string? HairTexture = null, string? FurTexture = null, int HairGeoset = 1);
/// <summary>
/// Joins client display/model/extra DBCs and exposes native animation names. Available assets
/// are independent of live server model/equipment choices.
/// </summary>
public sealed class CreatureCatalog : ICreatureCatalog
{
    readonly IClientFiles store;
    readonly DbcTable displays, models, extras, sections;
    // Key: race, sex, CharSections type, variation, color. Value: CharSections row id.
    readonly Dictionary<(uint, uint, uint, uint, uint), uint> sectionRows = [];
    // Key: race, sex, hair style. Value: model geoset from CharHairGeosets.
    readonly Dictionary<(uint, uint, uint), int> hairGeosets = [];
    public IReadOnlyDictionary<ushort, string> AnimationNames { get; }

    /// <summary>
    /// Load client lookup tables once per store on a decode worker; animation IDs/names populate the
    /// selected preview menu.
    /// </summary>
    public CreatureCatalog(IClientFiles store)
    {
        this.store = store;
        displays = new(store.Read("DBFilesClient/CreatureDisplayInfo.dbc"));
        models = new(store.Read("DBFilesClient/CreatureModelData.dbc"));
        extras = new(store.Read("DBFilesClient/CreatureDisplayInfoExtra.dbc"));
        // CharSections: race 1, sex 2, type 3, textures 4-6, flags 7, variation 8, color 9.
        sections = new(store.Read("DBFilesClient/CharSections.dbc"));
        foreach (uint row in sections.Ids)
        {
            sectionRows.TryAdd((sections.U(row, 1), sections.U(row, 2), sections.U(row, 3), sections.U(row, 8), sections.U(row, 9)), row);
        }

        // CharHairGeosets: race 1, sex 2, style 3, geoset 4.
        var hair = new DbcTable(store.Read("DBFilesClient/CharHairGeosets.dbc"));
        foreach (uint row in hair.Ids)
        {
            hairGeosets.TryAdd((hair.U(row, 1), hair.U(row, 2), hair.U(row, 3)), (int)hair.U(row, 4));
        }

        var animations = new DbcTable(store.Read("DBFilesClient/AnimationData.dbc"));
        AnimationNames = animations.Ids.Where(id => id <= ushort.MaxValue).ToDictionary(id => (ushort)id, id => animations.Str(id, 1));
    }

    /// <summary>
    /// Follow display/model/extra links, resolve skins beside the model and select a baked NPC body
    /// texture. Missing rows fail for a labeled-marker fallback.
    /// </summary>
    public CreatureAppearance Resolve(uint id)
    {
        uint model = displays.U(id, 1), extra = displays.U(id, 3);
        string path = BinaryAsset.ModelPath(models.Str(model, 2));
        string dir = path[..(path.LastIndexOf('/') + 1)];
        string[] textures = Enumerable.Range(6, 3).Select(f => displays.Str(id, f)).Select(s => s.Length == 0 ? "" : dir + s + ".blp").ToArray();
        string? baked = extra != 0 && extras.Contains(extra) ? extras.Str(extra, 20) : null;
        string? hairTexture = null, furTexture = null;
        int hairGeoset = 1;
        if (extra != 0 && extras.Contains(extra))
        {
            uint race = extras.U(extra, 1), sex = extras.U(extra, 2), skin = extras.U(extra, 3), style = extras.U(extra, 5), color = extras.U(extra, 6);
            hairTexture = SectionTexture(race, sex, 3, style, color, 4, dir);
            furTexture = SectionTexture(race, sex, 0, 0, skin, 5, dir);
            if (hairGeosets.TryGetValue((race, sex, style), out int geoset))
            {
                hairGeoset = geoset;
            }
        }

        return new(path, textures, displays.F(id, 4), string.IsNullOrEmpty(baked) ? null : "Textures/BakedNpcTextures/" + baked, hairTexture, furTexture, hairGeoset);
    }

    string? SectionTexture(uint race, uint sex, uint type, uint variation, uint color, int field, string modelDirectory)
    {
        if (!sectionRows.TryGetValue((race, sex, type, variation, color), out uint row))
        {
            return null;
        }

        string texture = sections.Str(row, field).Replace('\\', '/');
        if (texture.Length == 0)
        {
            return null;
        }

        // Some rows (Tauren hair) name a file that only exists beside the model; the client resolves
        // those relative to the model folder. Unresolvable hair is hidden rather than drawn as the
        // missing-texture placeholder.
        if (store.Exists(texture))
        {
            return texture;
        }

        string local = modelDirectory + texture[(texture.LastIndexOf('/') + 1)..];
        return store.Exists(local) ? local : null;
    }
}
