using DBCD;

namespace ACWorldEdit.Assets;

/// <summary>
/// What the viewer needs to draw a creature display: the catalog of one client family. Wrath reads
/// DBCs, retail reads DB2 tables; the overlays never ask which.
/// </summary>
public interface ICreatureCatalog
{
    /// <summary>Animation names by ID for the preview menu; empty where the client has none loaded.</summary>
    IReadOnlyDictionary<ushort, string> AnimationNames { get; }

    /// <summary>Appearance of one display ID. A display the client does not describe throws.</summary>
    CreatureAppearance Resolve(uint displayId);
}

/// <summary>Model reference of a GameObject display ID, or an exception when the client has none.</summary>
public interface IGameObjectCatalog
{
    string Resolve(uint displayId);
}

/// <summary>
/// One display row as the server's hotfixes describe it. A hotfix row replaces the client row with
/// the same ID, the way the live client applies it; zero file IDs mean "no model" / "no texture".
/// </summary>
public readonly record struct CreatureDisplayOverride(uint ModelFileId, uint[] TextureFileIds, float Scale);

/// <summary>A row of the server's hotfixes.creature_display_info: which model data, scale and skins a display uses.</summary>
public readonly record struct CreatureDisplayHotfix(uint Id, uint ModelId, float Scale, uint[] TextureFileIds);

/// <summary>
/// Retail creature displays: CreatureDisplayInfo → CreatureModelData → model FileDataID, with the
/// three replaceable skins named by FileDataID. A humanoid display's CreatureDisplayInfoExtra names
/// its pre-baked skin through TextureFileData, and its customization choices
/// (CreatureDisplayInfoOption → ChrCustomizationElement) pick the geoset variants and the hair and eye
/// textures. Equipment is not composed.
/// </summary>
public sealed class RetailCreatureCatalog : ICreatureCatalog
{
    // Geoset groups whose variant 1 is an empty stub on character models: ears (7) and the head (32).
    // A display without a choice for them shows variant 2, the ears and head the client draws.
    static readonly IReadOnlyDictionary<int, int> DefaultVariants = new Dictionary<int, int> { [7] = 2, [32] = 2 };

    // Loaded up front: the catalog is built on the open worker, and Db2Tables is not thread-safe.
    readonly IDBCDStorage displays, models, extras, geosets, materials;
    // Indexes built on the first humanoid lookup: TextureFileData MaterialResourcesID → FileDataID,
    // display extra → chosen customization choices, choice → elements, texture target → M2 texture type.
    readonly Lazy<Dictionary<int, uint>> materialFiles;
    readonly Lazy<ILookup<int, int>> choicesByExtra;
    readonly Lazy<ILookup<int, DBCDRow>> elementsByChoice;
    readonly Lazy<Dictionary<int, int>> targetTypes;
    readonly Dictionary<uint, CreatureDisplayOverride> overrides = [];

    public RetailCreatureCatalog(Db2Tables tables)
    {
        displays = tables.Table("CreatureDisplayInfo");
        models = tables.Table("CreatureModelData");
        extras = tables.Table("CreatureDisplayInfoExtra");
        geosets = tables.Table("ChrCustomizationGeoset");
        materials = tables.Table("ChrCustomizationMaterial");
        var textureFiles = tables.Table("TextureFileData");
        var options = tables.Table("CreatureDisplayInfoOption");
        var elements = tables.Table("ChrCustomizationElement");
        var layers = tables.Table("ChrModelTextureLayer");
        materialFiles = new(() =>
        {
            Dictionary<int, uint> files = [];
            // Several rows can share a material; the base texture (usage 0) wins.
            foreach (var row in textureFiles.Values.OrderBy(r => r.FieldAs<int>("UsageType") == 0 ? 0 : 1))
            {
                files.TryAdd(row.FieldAs<int>("MaterialResourcesID"), row.FieldAs<uint>("FileDataID"));
            }

            return files;
        });
        choicesByExtra = new(() => options.Values.ToLookup(o => o.FieldAs<int>("CreatureDisplayInfoExtraID"), o => o.FieldAs<int>("ChrCustomizationChoiceID")));
        elementsByChoice = new(() => elements.Values.ToLookup(e => e.FieldAs<int>("ChrCustomizationChoiceID")));
        targetTypes = new(() =>
        {
            Dictionary<int, int> types = [];
            foreach (var layer in layers.Values)
            {
                foreach (int target in layer.FieldAs<int[]>("ChrModelTextureTargetID").Where(t => t != 0))
                {
                    types.TryAdd(target, layer.FieldAs<int>("TextureType"));
                }
            }

            return types;
        });
    }

    public IReadOnlyDictionary<ushort, string> AnimationNames { get; } = new Dictionary<ushort, string>();

    /// <summary>Replace client display rows with hotfix rows.</summary>
    public void Override(uint displayId, CreatureDisplayOverride row) => overrides[displayId] = row;

    /// <summary>
    /// Apply the server's display hotfixes. A hotfix names its model by CreatureModelData ID; the file
    /// comes from the hotfix's own model data row when it has one (<paramref name="modelFiles"/>), else
    /// from the client's. A model the client does not know leaves that display without a model.
    /// </summary>
    public void Apply(IEnumerable<CreatureDisplayHotfix> rows, IReadOnlyDictionary<uint, uint> modelFiles)
    {
        foreach (var row in rows)
        {
            uint file = modelFiles.TryGetValue(row.ModelId, out uint hotfixed) ? hotfixed
                : models.TryGetValue((int)row.ModelId, out var data) ? data.FieldAs<uint>("FileDataID") : 0;
            Override(row.Id, new(file, row.TextureFileIds, row.Scale));
        }
    }

    public CreatureAppearance Resolve(uint displayId)
    {
        if (overrides.TryGetValue(displayId, out var hotfix))
        {
            return Appearance(displayId, hotfix.ModelFileId, hotfix.TextureFileIds, hotfix.Scale);
        }

        if (!displays.TryGetValue((int)displayId, out var display))
        {
            throw new InvalidDataException($"The client has no creature display {displayId}.");
        }

        uint model = display.FieldAs<uint>("ModelID");
        if (!models.TryGetValue((int)model, out var data))
        {
            throw new InvalidDataException($"Creature display {displayId} names model data {model}, which the client does not have.");
        }

        return Humanoid(Appearance(displayId, data.FieldAs<uint>("FileDataID"), display.FieldAs<uint[]>("TextureVariationFileDataID"), display.FieldAs<float>("CreatureModelScale")), display.FieldAs<int>("ExtendedDisplayInfoID"));
    }

    /// <summary>Add a humanoid's baked skin and customization; displays without a baked extra are returned as they are.</summary>
    CreatureAppearance Humanoid(CreatureAppearance appearance, int extraId)
    {
        string? baked = BakedSkin(extraId);
        return baked == null ? appearance : Customize(appearance with { BakedTexture = baked }, extraId);
    }

    /// <summary>
    /// Apply a humanoid's customization choices. An element applies when it names no related choice or
    /// its related choice is also chosen (a hair colour's texture depends on the skin colour, say).
    /// Geoset elements set their group's variant; material elements whose texture target is the hair
    /// (type 6) or eyes (type 19) name those textures. Skin targets are already in the baked skin.
    /// </summary>
    CreatureAppearance Customize(CreatureAppearance appearance, int extraId)
    {
        var chosen = choicesByExtra.Value[extraId].ToHashSet();
        var variants = new Dictionary<int, int>(DefaultVariants);
        string? hair = null, eyes = null;
        foreach (var element in chosen.SelectMany(c => elementsByChoice.Value[c]))
        {
            int related = element.FieldAs<int>("RelatedChrCustomizationChoiceID");
            if (related != 0 && !chosen.Contains(related))
            {
                continue;
            }

            if (geosets.TryGetValue(element.FieldAs<int>("ChrCustomizationGeosetID"), out var geoset))
            {
                variants[geoset.FieldAs<int>("GeosetType")] = geoset.FieldAs<int>("GeosetID");
            }

            if (materials.TryGetValue(element.FieldAs<int>("ChrCustomizationMaterialID"), out var material)
                && targetTypes.Value.TryGetValue(material.FieldAs<int>("ChrModelTextureTargetID"), out int type)
                && materialFiles.Value.TryGetValue(material.FieldAs<int>("MaterialResourcesID"), out uint file) && file != 0)
            {
                if (type == 6)
                {
                    hair = AssetRef.FromFileId(file);
                }
                else if (type == 19)
                {
                    eyes = AssetRef.FromFileId(file);
                }
            }
        }

        return appearance with { HairTexture = hair, EyeTexture = eyes, Geosets = GeosetVariants.From(variants) };
    }

    /// <summary>The pre-baked skin of a humanoid display (the HD bake when there is one), or null.</summary>
    string? BakedSkin(int extraId)
    {
        if (extraId == 0 || !extras.TryGetValue(extraId, out var extra))
        {
            return null;
        }

        foreach (string column in new[] { "HDBakeMaterialResourcesID", "BakeMaterialResourcesID" })
        {
            int material = extra.FieldAs<int>(column);
            if (material != 0 && materialFiles.Value.TryGetValue(material, out uint file) && file != 0)
            {
                return AssetRef.FromFileId(file);
            }
        }

        return null;
    }

    static CreatureAppearance Appearance(uint displayId, uint modelFileId, uint[] textureFileIds, float scale)
    {
        if (modelFileId == 0)
        {
            throw new InvalidDataException($"Creature display {displayId} has no client model.");
        }

        string[] textures = new string[3];
        for (int i = 0; i < textures.Length; i++)
        {
            textures[i] = i < textureFileIds.Length && textureFileIds[i] != 0 ? AssetRef.FromFileId(textureFileIds[i]) : "";
        }

        return new(AssetRef.FromFileId(modelFileId), textures, scale > 0 ? scale : 1f, null);
    }
}

/// <summary>Retail GameObject displays: GameObjectDisplayInfo.FileDataID, with hotfix rows replacing client rows.</summary>
public sealed class RetailGameObjectCatalog(Db2Tables tables) : IGameObjectCatalog
{
    readonly IDBCDStorage displays = tables.Table("GameObjectDisplayInfo");
    readonly Dictionary<uint, uint> overrides = [];

    /// <summary>Replace a client display row with the hotfix row's model FileDataID.</summary>
    public void Override(uint displayId, uint modelFileId) => overrides[displayId] = modelFileId;

    public string Resolve(uint displayId)
    {
        uint file;
        if (overrides.TryGetValue(displayId, out uint hotfix))
        {
            file = hotfix;
        }
        else if (displays.TryGetValue((int)displayId, out var row))
        {
            file = row.FieldAs<uint>("FileDataID");
        }
        else
        {
            throw new InvalidDataException($"The client has no GameObject display {displayId}.");
        }

        return file == 0
            ? throw new InvalidDataException("This GameObject display has no client model.")
            : AssetRef.FromFileId(file);
    }
}
