using System.Runtime.CompilerServices;

namespace ACWorldEdit.Assets;

/// <summary>
/// The retail liquid tables reduced to plain dictionaries so terrain and WMO readers need no DB2
/// access on worker threads: LiquidType (texture pattern, sound bank used as the material kind),
/// LiquidObject (object to type) and LiquidMaterial (vertex format). Built once while the client
/// opens and registered against its file source.
/// </summary>
public sealed class RetailLiquids
{
    /// <summary>MH2O stores a LiquidObject ID instead of a vertex format from this value up.</summary>
    public const int FirstObjectId = 42;

    /// <summary>LiquidType texture slot holding the water's ripple normal map ("newoceanbump.blp" on the stock types).</summary>
    public const int BumpSlot = 2;

    static readonly ConditionalWeakTable<IClientFiles, RetailLiquids> registry = new();

    readonly Dictionary<uint, (string Pattern, uint Kind, uint Material)> types = [];
    readonly Dictionary<uint, uint> objectTypes = [];
    readonly Dictionary<uint, int> materialFormats = [];
    readonly Dictionary<uint, string> bumpMaps = [];

    public static void Register(IClientFiles files, RetailLiquids liquids) => registry.AddOrUpdate(files, liquids);

    public static RetailLiquids? For(IClientFiles files) => registry.TryGetValue(files, out var liquids) ? liquids : null;

    public static RetailLiquids Load(Db2Tables tables)
    {
        var result = new RetailLiquids();
        foreach (var row in tables.Table("LiquidType").Values)
        {
            string[] textures = row.Field<string[]>("Texture") ?? [];
            result.types[(uint)row.ID] = (textures.Length > 0 ? textures[0] ?? "" : "", row.FieldAs<uint>("SoundBank"), row.FieldAs<uint>("MaterialID"));
            if (textures.Length > BumpSlot && !string.IsNullOrWhiteSpace(textures[BumpSlot]))
            {
                result.bumpMaps[(uint)row.ID] = textures[BumpSlot]!.Replace('\\', '/');
            }
        }

        foreach (var row in tables.Table("LiquidObject").Values)
        {
            result.objectTypes[(uint)row.ID] = row.FieldAs<uint>("LiquidTypeID");
        }

        foreach (var row in tables.Table("LiquidMaterial").Values)
        {
            result.materialFormats[(uint)row.ID] = row.FieldAs<int>("LVF");
        }

        return result;
    }

    /// <summary>
    /// Test seam: build the catalog from plain values (type to pattern/kind/material, object to type,
    /// material to vertex format).
    /// </summary>
    public static RetailLiquids From(IEnumerable<(uint Type, string Pattern, uint Kind, uint Material)> types, IEnumerable<(uint Object, uint Type)> objects, IEnumerable<(uint Material, int Format)> materials, IEnumerable<(uint Type, string Bump)>? bumps = null)
    {
        var result = new RetailLiquids();
        foreach (var b in bumps ?? [])
        {
            result.bumpMaps[b.Type] = b.Bump.Replace('\\', '/');
        }

        foreach (var t in types)
        {
            result.types[t.Type] = (t.Pattern, t.Kind, t.Material);
        }

        foreach (var o in objects)
        {
            result.objectTypes[o.Object] = o.Type;
        }

        foreach (var m in materials)
        {
            result.materialFormats[m.Material] = m.Format;
        }

        return result;
    }

    public bool HasType(uint type) => types.ContainsKey(type);

    public string Pattern(uint type) => types.TryGetValue(type, out var t) ? t.Pattern : throw new InvalidDataException($"Unknown liquid type {type}.");

    /// <summary>The ripple normal map a type names, or null when it has none (magma, slime).</summary>
    public string? Bump(uint type) => bumpMaps.GetValueOrDefault(type);

    public uint Kind(uint type) => types.TryGetValue(type, out var t) ? t.Kind : 0;

    /// <summary>
    /// Resolve an MH2O layer's type and vertex format. Values below <see cref="FirstObjectId"/> are the
    /// vertex format itself; larger ones name a LiquidObject whose type's material decides it. An
    /// object the table does not have (the ocean layers store 42, which has no row) keeps the layer's
    /// type and is depth-only (format 2), which is what those layers' vertex data measures as on 12.1.5.
    /// </summary>
    public (uint Type, int Format) Layer(uint type, uint formatOrObject)
    {
        if (formatOrObject < FirstObjectId)
        {
            return (type, (int)formatOrObject);
        }

        if (!objectTypes.TryGetValue(formatOrObject, out uint resolved))
        {
            return (type, 2);
        }

        int format = types.TryGetValue(resolved, out var t) && materialFormats.TryGetValue(t.Material, out int lvf) ? lvf : 0;
        return (resolved, format);
    }
}
