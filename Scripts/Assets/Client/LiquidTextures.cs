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
public sealed record LiquidStyle(LiquidGradient Gradient, int DepthTable, int PeriodMs, float Scale, float Rotation, float DepthScale, BlpTexture? Bump = null, float ScrollU = 0, float ScrollV = 0, SwampLiquidStyle? Swamp = null, FelLiquidStyle? Fel = null)
{
    /// <summary>
    /// What distinguishes one style's material from another's, so surfaces that share a texture but not a
    /// style (magma that scrolls and WMO magma that scrolls faster) do not share a material.
    /// </summary>
    public string CacheKey => $"{Gradient}|{DepthTable}|{PeriodMs}|{Scale}|{Rotation}|{DepthScale}|{ScrollU}|{ScrollV}|{(Bump == null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(Bump))}|{Swamp?.CacheKey}|{Fel?.CacheKey}";

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

/// <summary>Decoded material 13 shallow layer and inferred settings; no engine resources.</summary>
public sealed record FelLiquidStyle(BlpTexture Shallow, RetailLiquids.FelSettings Settings)
{
    public string CacheKey => $"{Settings}|{RuntimeHelpers.GetHashCode(Shallow)}";
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
            if (retail!.Fel(type) is { } fel && Extra(fel.Shallow) is { } shallow)
            {
                return stock with { Fel = new(shallow, fel) };
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

    BlpTexture? Extra(string path)
    {
        path = path.Replace('\\', '/');

        lock (extraTextures)
        {
            if (!extraTextures.TryGetValue(path, out var texture))
            {
                try
                {
                    texture = store.Exists(path) ? BlpTexture.Decode(store.Read(path), 256) : null;
                }
                catch (Exception e) when (e is InvalidDataException or FileNotFoundException)
                {
                    texture = null;
                }

                extraTextures[path] = texture;
            }

            return texture;
        }
    }

    /// <summary>
    /// Resolve the first native texture slot and normalize separators, retaining printf-style frame
    /// placeholders.
    /// </summary>
    public string Pattern(uint type) => (retail?.Pattern(type) ?? types!.Str(type, 15)).Replace('\\', '/');

    BlpTexture? OptionalSequence(string pattern)
    {
        lock (atlases)
        {
            try { return Sequence(pattern.Replace('\\', '/')); }
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
            if (sequences.TryGetValue(pattern, out cached))
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
    BlpTexture? Sequence(string pattern)
    {
        if (sequences.TryGetValue(pattern, out var cached)) return cached;
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

                frames.Add(BlpTexture.Decode(store.Read(path), 128));
            }
        }
        else if (pattern.Length > 0 && store.Exists(pattern))
        {
            frames.Add(BlpTexture.Decode(store.Read(pattern), 128));
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

        return sequences[pattern] = new(w * frames.Count, h, rgba);
    }
}
