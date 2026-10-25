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
/// three replaceable skins named by FileDataID. Humanoid customization (ChrCustomization) is not
/// composed, so a player-race display draws with its default skin.
/// </summary>
public sealed class RetailCreatureCatalog(Db2Tables tables) : ICreatureCatalog
{
    // Loaded up front: the catalog is built on the open worker, and Db2Tables is not thread-safe.
    readonly IDBCDStorage displays = tables.Table("CreatureDisplayInfo");
    readonly IDBCDStorage models = tables.Table("CreatureModelData");
    readonly Dictionary<uint, CreatureDisplayOverride> overrides = [];

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

        return Appearance(displayId, data.FieldAs<uint>("FileDataID"), display.FieldAs<uint[]>("TextureVariationFileDataID"), display.FieldAs<float>("CreatureModelScale"));
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
