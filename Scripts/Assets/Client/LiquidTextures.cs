using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace ACWorldEdit.Assets;

/// <summary>
/// Which procedural depth gradient a liquid type names in its second texture slot.
/// </summary>
public enum LiquidGradient
{
    River = 0,
    Ocean = 1,
    Wmo = 2
}

/// <summary>
/// How the client's water material draws one LiquidType (Liquid::CSettingsBank::UpdateRecord):
/// the depth gradient and the table mapping the stored depth byte onto it, the animation period, and
/// the texture scale, rotation (radians) and depth scale.
/// </summary>
public sealed record LiquidStyle(LiquidGradient Gradient, int DepthTable, int PeriodMs, float Scale, float Rotation, float DepthScale, BlpTexture? Bump = null, float ScrollU = 0, float ScrollV = 0, SwampLiquidStyle? Swamp = null, FelLiquidStyle? Fel = null, LeyLiquidStyle? Ley = null, bool Mercury = false, RetailLiquids.FogSettings? Fog = null, MagmaLiquidStyle? Magma = null, RetailLiquids.ProceduralWaterSettings? ProceduralWater = null, AzeriteLiquidStyle? Azerite = null, BlpTexture? FogMask = null)
{
    /// <summary>
    /// What distinguishes one style's material from another's, so surfaces that share a texture but not a
    /// style (magma that scrolls and WMO magma that scrolls faster) do not share a material.
    /// </summary>
    public string CacheKey => $"{Gradient}|{DepthTable}|{PeriodMs}|{Scale}|{Rotation}|{DepthScale}|{ScrollU}|{ScrollV}|{(Bump == null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(Bump))}|{Swamp?.CacheKey}|{Fel?.CacheKey}|{Ley?.CacheKey}|{Mercury}|{Fog}|{Magma?.CacheKey}|{ProceduralWater}|{Azerite?.CacheKey}|{(FogMask == null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(FogMask))}";

    /// <summary>
    /// What the stock river and lake types use.
    /// </summary>
    public static LiquidStyle River { get; } = new(LiquidGradient.River, 0, 1250, 1, 0, 1);
    /// <summary>
    /// What the stock ocean types use.
    /// </summary>
    public static LiquidStyle Ocean { get; } = new(LiquidGradient.Ocean, 1, 1250, 1, 0, 1);

    /// <summary>
    /// Pick the gradient from the procedural texture name in a type's second slot ("proceduralOceanDepthTex").
    /// </summary>
    public static LiquidGradient GradientFor(string textureName) =>
        textureName.Contains("Ocean", StringComparison.OrdinalIgnoreCase) ? LiquidGradient.Ocean
        : textureName.Contains("Wmo", StringComparison.OrdinalIgnoreCase) ? LiquidGradient.Wmo
        : LiquidGradient.River;
}

/// <summary>Decoded material 4 layers and inferred constants; no engine resources.</summary>
public sealed record MagmaLiquidStyle(BlpTexture Middle, BlpTexture Top, RetailLiquids.MagmaSettings Settings)
{
    public string CacheKey => $"{Settings}|{RuntimeHelpers.GetHashCode(Middle)}|{RuntimeHelpers.GetHashCode(Top)}";
}

/// <summary>Decoded material 12 detail atlas, ripple normal map and shore texture; no engine resources.</summary>
public sealed record LeyLiquidStyle(BlpTexture Detail, RetailLiquids.LeySettings Settings,
    BlpTexture? Ripple = null, BlpTexture? Shore = null)
{
    public string CacheKey => $"{Settings}|{RuntimeHelpers.GetHashCode(Detail)}|{(Ripple == null ? 0 : RuntimeHelpers.GetHashCode(Ripple))}|{(Shore == null ? 0 : RuntimeHelpers.GetHashCode(Shore))}";
}

/// <summary>Decoded material 13 shallow layer, emissive overlay, animated normal sequence, specular and shore; no engine resources.</summary>
public sealed record FelLiquidStyle(BlpTexture Shallow, RetailLiquids.FelSettings Settings,
    BlpTexture? Overlay = null, BlpTexture? Normals = null, BlpTexture? Specular = null, BlpTexture? Shore = null)
{
    public string CacheKey => $"{Settings}|{RuntimeHelpers.GetHashCode(Shallow)}|{(Overlay == null ? 0 : RuntimeHelpers.GetHashCode(Overlay))}|{(Normals == null ? 0 : RuntimeHelpers.GetHashCode(Normals))}|{(Specular == null ? 0 : RuntimeHelpers.GetHashCode(Specular))}|{(Shore == null ? 0 : RuntimeHelpers.GetHashCode(Shore))}";
}

/// <summary>Decoded material 18 body layers, masks and animated normal sequence; no engine resources.</summary>
public sealed record AzeriteLiquidStyle(BlpTexture Deep, BlpTexture Center, RetailLiquids.AzeriteSettings Settings,
    BlpTexture? Blend = null, BlpTexture? Transcolor = null, BlpTexture? Normals = null)
{
    public string CacheKey => $"{Settings}|{RuntimeHelpers.GetHashCode(Deep)}|{RuntimeHelpers.GetHashCode(Center)}|{(Blend == null ? 0 : RuntimeHelpers.GetHashCode(Blend))}|{(Transcolor == null ? 0 : RuntimeHelpers.GetHashCode(Transcolor))}|{(Normals == null ? 0 : RuntimeHelpers.GetHashCode(Normals))}";
}

/// <summary>Decoded material 14 depth layers; shared CPU textures, with no engine resources.</summary>
public sealed record SwampLiquidStyle(BlpTexture Middle, BlpTexture Deep, RetailLiquids.SwampSettings Settings,
    BlpTexture? Shore = null, BlpTexture? Normals = null)
{
    public string CacheKey => $"{Settings}|{RuntimeHelpers.GetHashCode(Middle)}|{RuntimeHelpers.GetHashCode(Deep)}|{(Shore == null ? 0 : RuntimeHelpers.GetHashCode(Shore))}|{(Normals == null ? 0 : RuntimeHelpers.GetHashCode(Normals))}";
}

/// <summary>
/// Per-store CPU liquid catalog/atlas cache. Identical patterns share decoded storage;
/// ConditionalWeakTable ties lifetime to the mounted client rather than a global singleton.
/// </summary>
public sealed class LiquidTextures
{
    static readonly ConditionalWeakTable<IClientFiles, LiquidTextures> stores = new();
    static readonly BlpTexture proceduralPlaceholder = new(1, 1, [255, 255, 255, 255]);
    readonly IClientFiles store;
    readonly DbcTable? types;
    readonly RetailLiquids? retail;
    readonly Dictionary<uint, BlpTexture> atlases = [];
    readonly Dictionary<string, BlpTexture> sequences = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, BlpTexture?> extraTextures = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<uint, SwampLiquidStyle?> swamps = [];
    LiquidTextures(IClientFiles source)
    {
        store = source;
        retail = source.Kind == ClientKind.Retail
            ? RetailLiquids.For(source) ?? throw new InvalidDataException("The retail liquid tables were not loaded for this client.")
            : null;
        types = retail == null ? new(source.Read("DBFilesClient/LiquidType.dbc")) : null;
    }

    /// <summary>
    /// Get the catalog for exactly this client store. Catalogs hold decoded bytes and allocate no
    /// Godot resources.
    /// </summary>
    public static LiquidTextures For(IClientFiles store) => stores.GetValue(store, s => new(s));
    /// <summary>
    /// Read the client SoundBank category used for water/ocean versus lava/slime preview blending.
    /// </summary>
    public uint Kind(uint type) => retail?.Kind(type) ?? types!.U(type, 3);
    /// <summary>
    /// How the water material draws a type. Wrath reads the LiquidType.dbc columns the client reads
    /// (texture slot 1, floats 0 to 2, ints 0 and 1); retail types use the stock river or ocean style by
    /// their sound bank and add the ripple normal map or material 14's depth layers.
    /// </summary>
    public LiquidStyle Style(uint type)
    {
        if (types is not { } table)
        {
            var stock = Kind(type) == 1 ? LiquidStyle.Ocean : LiquidStyle.River;
            if (retail!.ProceduralWater(type) is { } procedural)
            {
                return stock with { ProceduralWater = procedural, Bump = Bump(type), Gradient = procedural.Gradient,
                    DepthTable = procedural.DepthTable, DepthScale = procedural.DepthScale };
            }
            if (retail!.Magma(type) is { } magma && Extra(magma.Middle) is { } magmaMiddle && Extra(magma.Top) is { } magmaTop)
            {
                return stock with { Magma = new(magmaMiddle, magmaTop, magma) };
            }
            if (retail!.Fog(type) is { } fog)
            {
                BlpTexture? mask = string.IsNullOrEmpty(fog.Mask) ? null : Extra(fog.Mask);
                return stock with { Fog = fog, FogMask = mask };
            }
            if (retail!.Material(type) == 5)
            {
                return stock with { Mercury = true, Bump = Bump(type) };
            }
            if (retail!.Ley(type) is { } ley && OptionalSequence(ley.Detail) is { } detail)
            {
                // Ripple and shore are optional: without the ripple map the shader keeps the flat
                // leyline_0 path, and without a shore texture the foam term is omitted (void rows).
                return stock with { Ley = new(detail, ley, OptionalExtra(ley.Ripple), OptionalExtra(ley.Shore)) };
            }
            if (retail!.Fel(type) is { } fel && Extra(fel.Shallow) is { } shallow)
            {
                // The overlay, normal sequence, specular and shore are optional: a type that
                // names only the body keeps the flat fel_0 path instead of drawing a half material.
                return stock with { Fel = new(shallow, fel, OptionalExtra(fel.Overlay), OptionalSequence(fel.NormalPattern, 256), OptionalExtra(fel.Specular), OptionalExtra(fel.Shore)) };
            }
            if (retail!.Swamp(type) is { } settings)
            {
                lock (swamps)
                {
                    if (!swamps.TryGetValue(type, out var swamp))
                    {
                        // Missing extra layers retain the existing base-texture fallback.
                        var middle = Extra(settings.Middle);
                        var deep = Extra(settings.Deep);
                        swamps[type] = swamp = middle != null && deep != null
                            ? new(middle, deep, settings, Extra(settings.Shore), OptionalSequence(settings.NormalPattern)) : null;
                    }

                    return stock with { Swamp = swamp };
                }
            }

            // Azerite (material 18) needs both body layers; one missing leaves the lava/slime fallback.
            // The mask and animation slots are optional so a type that names only the body still draws.
            if (retail!.Azerite(type) is { } azerite && Extra(azerite.Deep, 512) is { } azeriteDeep && Extra(azerite.Center) is { } azeriteCenter)
            {
                var blend = OptionalExtra(azerite.Blend, 512);
                var transcolor = OptionalExtra(azerite.Transcolor);
                var normals = OptionalSequence(azerite.NormalPattern, 256);
                return stock with { Azerite = new(azeriteDeep, azeriteCenter, azerite, blend, transcolor, normals) };
            }

            if (Kind(type) >= 2)
            {
                // Magma and slime scroll their frames at the type's first two floats.
                var (u, v) = retail!.Scroll(type);
                return stock with { ScrollU = u, ScrollV = v };
            }

            return Bump(type) is { } bump ? stock with { Bump = bump } : stock;
        }

        // Only the water material (1) reads the floats as scale, rotation and depth scale; magma uses the first two as scroll speeds.
        bool water = table.U(type, 14) == 1;
        int period = (int)table.U(type, 42);
        return new(LiquidStyle.GradientFor(table.Str(type, 16)), (int)table.U(type, 41) == 1 ? 1 : 0, period > 0 ? period : 1250,
            water ? table.F(type, 23) : 1, water ? table.F(type, 24) : 0, water ? table.F(type, 25) : 1, null,
            water ? 0 : table.F(type, 23), water ? 0 : table.F(type, 24));
    }
    /// <summary>
    /// The retail ripple normal map for a water type, decoded once and shared by every type that names
    /// it. Null when the type has none or the client cannot read it, which leaves the flat water look.
    /// </summary>
    BlpTexture? Bump(uint type)
    {
        if (retail?.Bump(type) is not { } path)
        {
            return null;
        }

        return Extra(path);
    }

    BlpTexture? Extra(string path, int maximumDimension = 256)
    {
        path = path.Replace('\\', '/');
        string key = $"{maximumDimension}|{path}";

        lock (extraTextures)
        {
            if (!extraTextures.TryGetValue(key, out var texture))
            {
                try
                {
                    texture = store.Exists(path) ? BlpTexture.Decode(store.Read(path), maximumDimension) : null;
                }
                catch (Exception e) when (e is InvalidDataException or FileNotFoundException)
                {
                    texture = null;
                }

                extraTextures[key] = texture;
            }

            return texture;
        }
    }

    /// <summary>An optional extra texture: an empty slot or an unreadable file leaves the caller's fallback.</summary>
    BlpTexture? OptionalExtra(string path, int maximumDimension = 256) => string.IsNullOrWhiteSpace(path) ? null : Extra(path, maximumDimension);

    /// <summary>
    /// Resolve the first native texture slot and normalize separators, retaining printf-style frame
    /// placeholders.
    /// </summary>
    public string Pattern(uint type) => (retail?.Pattern(type) ?? types!.Str(type, 15)).Replace('\\', '/');

    BlpTexture? OptionalSequence(string pattern, int frameSize = 128)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return null;
        }

        lock (atlases)
        {
            try { return Sequence(pattern.Replace('\\', '/'), frameSize); }
            catch (Exception e) when (e is InvalidDataException or FileNotFoundException or NotSupportedException) { return null; }
        }
    }
    /// <summary>
    /// Decode bounded sequences at up to 128 pixels per frame into a horizontal atlas. A lock
    /// prevents duplicate worker decodes; missing procedural assets fail explicitly.
    /// </summary>
    public BlpTexture Decode(uint type)
    {
        lock (atlases)
        {
            if (atlases.TryGetValue(type, out var cached))
            {
                return cached;
            }

            string pattern = Pattern(type);
            // Material 3 needs no frame texture. An empty authored slot is intentional,
            // not a reason to decode ordinary ocean frames as a fallback.
            if (string.IsNullOrWhiteSpace(pattern) && retail?.Material(type) == 3)
            {
                return atlases[type] = proceduralPlaceholder;
            }
            if (sequences.TryGetValue($"128|{pattern}", out cached))
            {
                return atlases[type] = cached;
            }

            if (Sequence(pattern) is not { } sequence)
            {
                // Some retail types name textures the listfile does not know; they draw with the base
                // liquid of their kind (1 water, 2 ocean, 3 magma, 4 slime) instead of vanishing.
                uint baseType = Kind(type) + 1;
                if (retail != null && baseType != type && retail.HasType(baseType))
                {
                    return atlases[type] = Decode(baseType);
                }

                throw new InvalidDataException($"Liquid {type} has no supported native texture sequence: {pattern}");
            }

            return atlases[type] = sequence;
        }
    }

    // Called under the atlas lock; base colors and animated normal maps share decoded sequences.
    // The frame size is part of the cache key because the body atlases want small frames and the
    // azerite normal sequence wants enough detail to warp a whole pool.
    BlpTexture? Sequence(string pattern, int frameSize = 128)
    {
        string key = $"{frameSize}|{pattern}";
        if (sequences.TryGetValue(key, out var cached)) return cached;
        List<BlpTexture> frames = [];
        if (pattern.Contains('%'))
        {
            for (int i = 1; i <= 60; i++)
            {
                string path = Regex.Replace(pattern, @"%0?(\d*)d", m => m.Groups[1].Value.Length > 0 ? i.ToString("D" + m.Groups[1].Value) : i.ToString());
                if (!store.Exists(path))
                {
                    break;
                }

                frames.Add(BlpTexture.Decode(store.Read(path), frameSize));
            }
        }
        else if (pattern.Length > 0 && store.Exists(pattern))
        {
            frames.Add(BlpTexture.Decode(store.Read(pattern), frameSize));
        }

        if (frames.Count == 0) return null;
        int w = frames[0].Width, h = frames[0].Height;
        if (frames.Any(f => f.Width != w || f.Height != h))
        {
            throw new InvalidDataException("Mismatched liquid texture frames.");
        }

        byte[] rgba = new byte[checked(w * h * frames.Count * 4)];
        for (int f = 0; f < frames.Count; f++)
        {
            for (int y = 0; y < h; y++)
            {
                frames[f].Rgba.AsSpan(y * w * 4, w * 4).CopyTo(rgba.AsSpan((y * w * frames.Count + f * w) * 4));
            }
        }

        return sequences[key] = new(w * frames.Count, h, rgba);
    }
}
