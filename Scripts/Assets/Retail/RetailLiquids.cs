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
    readonly Dictionary<uint, (float U, float V)> scrolls = [];
    readonly Dictionary<uint, SwampSettings> swamps = [];
    readonly Dictionary<uint, FelSettings> fels = [];
    readonly Dictionary<uint, LeySettings> leys = [];
    readonly Dictionary<uint, FogSettings> fogs = [];
    readonly Dictionary<uint, MagmaSettings> magmas = [];
    readonly Dictionary<uint, ProceduralWaterSettings> waters = [];

    public sealed record ProceduralWaterSettings(LiquidGradient Gradient, int DepthTable, float DepthScale,
        float WaveScale, float NormalStrength, float Refraction, float SpecularPower);
    public ProceduralWaterSettings? ProceduralWater(uint type) => waters.GetValueOrDefault(type);

    public sealed record MagmaSettings(string Middle, string Top, System.Numerics.Vector3 Scales,
        System.Numerics.Vector3 BaseColor, float ScrollU, float ScrollV, float NoiseSpeed);
    public MagmaSettings? Magma(uint type) => magmas.GetValueOrDefault(type);
    static string BlpPath(string path) => string.IsNullOrWhiteSpace(path) || Path.HasExtension(path) ? path : path + ".blp";

    // liquidfog_0 CB1[18].rgb and CB1[17].y; DB2 bindings remain inferred.
    public sealed record FogSettings(System.Numerics.Vector3 Color, float OpacityScale);
    public FogSettings? Fog(uint type) => fogs.GetValueOrDefault(type);

    public sealed record LeySettings(string Detail, System.Numerics.Vector3 FirstUv,
        System.Numerics.Vector3 SecondUv, System.Numerics.Vector3 Color);
    public LeySettings? Ley(uint type) => leys.GetValueOrDefault(type);

    // Material 13: two scale/angle/scroll triples; depth mapping remains inferred.
    public sealed record FelSettings(string Shallow, System.Numerics.Vector3 DeepUv, System.Numerics.Vector3 ShallowUv, float DepthCoefficient);
    public FelSettings? Fel(uint type) => fels.GetValueOrDefault(type);

    /// <summary>
    /// Material 14 depth layers, shore texture, ripple sequence and surface constants. The
    /// DB2-to-buffer mapping is inferred; UV motion uses an estimate because CPU texture matrices
    /// are stripped. Float assignments and the unassigned fields are recorded in viewer-runtime.md.
    /// </summary>
    public sealed record SwampSettings(string Middle, string Deep, System.Numerics.Vector4 Depths,
        System.Numerics.Vector3 Scales, float Opacity, string NormalPattern = "",
        float NormalStrength = 0.15f, float SpecularPower = 300, float Refraction = 0.075f,
        float WaveScale = 1, float Reflection = 0.6f, float FlowSpeed = 0.3f,
        float FramesPerSecond = 60, float SpecularStrength = 0.1f, string Shore = "", float ShoreWidth = 0.3f);

    public static void Register(IClientFiles files, RetailLiquids liquids) => registry.AddOrUpdate(files, liquids);

    public static RetailLiquids? For(IClientFiles files) => registry.TryGetValue(files, out var liquids) ? liquids : null;

    public static RetailLiquids Load(Db2Tables tables)
    {
        var result = new RetailLiquids();
        foreach (var row in tables.Table("LiquidType").Values)
        {
            string[] textures = row.Field<string[]>("Texture") ?? [];
            result.types[(uint)row.ID] = (textures.Length > 0 ? textures[0] ?? "" : "", row.FieldAs<uint>("SoundBank"), row.FieldAs<uint>("MaterialID"));
            if (row.Field<float[]>("Float") is { Length: > 1 } floats)
            {
                result.scrolls[(uint)row.ID] = (floats[0], floats[1]);
                if (row.FieldAs<uint>("MaterialID") == 3 && floats.Length >= 12 && textures.Length >= 2)
                {
                    var ints = row.Field<int[]>("Int") ?? [];
                    result.waters[(uint)row.ID] = new(LiquidStyle.GradientFor(textures[1]),
                        ints.Length > 0 && ints[0] == 1 ? 1 : 0, floats[2], floats[0], floats[4], floats[3],
                        Math.Max(floats[11], 1));
                }
                if (row.FieldAs<uint>("MaterialID") == 4 && floats.Length >= 10 && textures.Length >= 3)
                {
                    var ints = row.Field<int[]>("Int") ?? [];
                    uint color = ints.Length > 2 ? unchecked((uint)ints[2]) : 0;
                    result.magmas[(uint)row.ID] = new(BlpPath(textures[1]), BlpPath(textures[2]),
                        new(floats[2], floats[3], floats[4]),
                        new((color >> 16 & 255) / 255f, (color >> 8 & 255) / 255f, (color & 255) / 255f),
                        floats[0], floats[1], floats[9]);
                }
                if (row.FieldAs<uint>("MaterialID") == 10)
                {
                    var colors = row.Field<int[]>("Color") ?? [];
                    uint color = colors.Length > 0 ? unchecked((uint)colors[0]) : 0;
                    result.fogs[(uint)row.ID] = new(
                        new((color >> 16 & 255) / 255f, (color >> 8 & 255) / 255f, (color & 255) / 255f), floats[1]);
                }
                if (row.FieldAs<uint>("MaterialID") == 12 && floats.Length >= 6 && textures.Length >= 2)
                {
                    var colors = row.Field<int[]>("Color") ?? [];
                    uint color = colors.Length > 0 ? unchecked((uint)colors[0]) : 0;
                    result.leys[(uint)row.ID] = new(textures[1], new(floats[0], floats[1], floats[2]),
                        new(floats[3], floats[4], floats[5]),
                        new((color >> 16 & 255) / 255f, (color >> 8 & 255) / 255f, (color & 255) / 255f));
                }
                if (row.FieldAs<uint>("MaterialID") == 13 && floats.Length >= 15 && textures.Length >= 2)
                {
                    result.fels[(uint)row.ID] = new(textures[1], new(floats[0], floats[1], floats[2]),
                        new(floats[3], floats[4], floats[5]), floats[14]);
                }
                if (row.FieldAs<uint>("MaterialID") == 14 && floats.Length >= 18 && textures.Length >= 3)
                {
                    result.swamps[(uint)row.ID] = new(textures[1], textures[2],
                        new(floats[0], floats[1], floats[2], floats[3]),
                        new(floats[13], floats[14], floats[15]), floats[12],
                        textures.Length > 5 ? textures[5] : "", floats[4], floats[5], floats[6],
                        floats[11], floats[8], 0.3f /* estimated UV scroll speed */, floats[10], floats[17],
                        textures.Length > 3 ? textures[3] : "", floats[9]);
                }
            }

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
    public static RetailLiquids From(IEnumerable<(uint Type, string Pattern, uint Kind, uint Material)> types, IEnumerable<(uint Object, uint Type)> objects, IEnumerable<(uint Material, int Format)> materials, IEnumerable<(uint Type, string Bump)>? bumps = null, IEnumerable<(uint Type, float U, float V)>? scrolls = null, IEnumerable<(uint Type, SwampSettings Settings)>? swamps = null, IEnumerable<(uint Type, FogSettings Settings)>? fogs = null, IEnumerable<(uint Type, MagmaSettings Settings)>? magmas = null, IEnumerable<(uint Type, ProceduralWaterSettings Settings)>? waters = null)
    {
        var result = new RetailLiquids();
        foreach (var water in waters ?? [])
        {
            result.waters[water.Type] = water.Settings;
        }
        foreach (var magma in magmas ?? [])
        {
            result.magmas[magma.Type] = magma.Settings;
        }
        foreach (var fog in fogs ?? [])
        {
            result.fogs[fog.Type] = fog.Settings;
        }
        foreach (var swamp in swamps ?? [])
        {
            result.swamps[swamp.Type] = swamp.Settings;
        }
        foreach (var sc in scrolls ?? [])
        {
            result.scrolls[sc.Type] = (sc.U, sc.V);
        }

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

    public string Pattern(uint type) => types.TryGetValue(type, out var t) ? (t.Material == 4 ? BlpPath(t.Pattern) : t.Pattern) : throw new InvalidDataException($"Unknown liquid type {type}.");

    /// <summary>The ripple normal map a type names, or null when it has none (magma, slime).</summary>
    public string? Bump(uint type) => bumpMaps.GetValueOrDefault(type);

    /// <summary>The scroll speeds (cycles per second along U and V) of a magma or slime type: its first two floats.</summary>
    public (float U, float V) Scroll(uint type) => scrolls.GetValueOrDefault(type);

    public uint Material(uint type) => types.TryGetValue(type, out var t) ? t.Material : 0;

    public uint Kind(uint type) => types.TryGetValue(type, out var t) ? t.Kind : 0;

    public SwampSettings? Swamp(uint type) => swamps.GetValueOrDefault(type);

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
