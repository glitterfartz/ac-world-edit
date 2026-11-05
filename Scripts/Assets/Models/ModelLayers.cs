using System.Numerics;

namespace ACWorldEdit.Assets;

/// <summary>
/// Where a texture unit takes its coordinates from: the first UV set (with the batch's texture
/// transform), the second UV set, or a sphere map of the view-space reflection (an environment map).
/// </summary>
public enum LayerCoord : byte
{
    T1,
    T2,
    /// <summary>Sphere map of the view-space reflection (M2 environment mapping).</summary>
    Env,
    /// <summary>A WMO's fixed-direction reflection of the surface normal (not view dependent).</summary>
    WmoEnv,
    /// <summary>The ground-plane projection WMO composite shaders use for their second layer.</summary>
    Planar,
}

/// <summary>
/// The pixel combiner of a multi-texture batch. Values are the case numbers of the combiner switch
/// in <c>Shaders/model/model.gdshader</c>; <see cref="None"/> is a plain single-texture batch. The M2 names
/// are the client's <c>Combiners_*</c> pixel shaders (https://wowdev.wiki/M2/.skin), the Wmo names its
/// MapObj pixel shaders (https://wowdev.wiki/WMO).
/// </summary>
public enum LayerCombiner : byte
{
    None = 0,
    Opaque = 1,
    Mod = 2,
    OpaqueMod = 3,
    OpaqueMod2x = 4,
    OpaqueMod2xNA = 5,
    OpaqueOpaque = 6,
    ModMod = 7,
    ModMod2x = 8,
    ModAdd = 9,
    ModMod2xNA = 10,
    ModAddNA = 11,
    ModOpaque = 12,
    OpaqueMod2xNAAlpha = 13,
    OpaqueAddAlpha = 14,
    OpaqueAddAlphaAlpha = 15,
    OpaqueMod2xNAAlphaAdd = 16,
    ModAddAlpha = 17,
    ModAddAlphaAlpha = 18,
    OpaqueAlphaAlpha = 19,
    OpaqueMod2xNAAlpha3s = 20,
    OpaqueAddAlphaWgt = 21,
    ModAddWithAlpha = 22,
    OpaqueModNAAlpha = 23,
    ModAddAlphaWgt = 24,
    OpaqueModAddWgt = 25,
    OpaqueMod2xNAAlphaUnshAlpha = 26,
    ModDualCrossfade = 27,
    OpaqueMod2xNAAlphaAlpha = 28,
    ModMaskedDualCrossfade = 29,
    OpaqueAlpha = 30,
    ModDepth = 31,
    Illum = 32,
    ModModModConst = 33,
    ModModDepth = 34,
    WmoEnv = 40,
    WmoEnvMetal = 41,
    WmoTwoLayerDiffuse = 42,
    WmoTwoLayerTerrain = 43,
    WmoDiffuseEmissive = 44,
    WmoMaskedEnvMetal = 45,
    WmoEnvMetalEmissive = 46,
    WmoTwoLayerDiffuseOpaque = 47,
    WmoTwoLayerDiffuseEmissive = 48,
    WmoAdditiveMaskedEnvMetal = 49,
    WmoTwoLayerDiffuseMod2x = 50,
    WmoTwoLayerDiffuseMod2xNA = 51,
    WmoTwoLayerDiffuseAlpha = 52,
    WmoTwoLayerEnvMetal = 53,
}

/// <summary>
/// The extra texture units of a multi-texture M2 batch and how to combine them with the base
/// texture. <see cref="Textures"/> are units 2 and up (the base texture is the surface's own),
/// <see cref="Coords"/> has one entry per unit including the first. <see cref="Uv2"/> is the vertex
/// second UV set, present only when a unit reads it; <see cref="Alpha2"/> is a WMO's second vertex-color
/// alpha, which its composite shaders use as a layer mask (the first color's alpha is the surface's own).
/// </summary>
public sealed record SurfaceLayers(LayerCombiner Combiner, string[] Textures, LayerCoord[] Coords, Vector2[]? Uv2 = null, bool EdgeFade = false, float[]? Alpha2 = null)
{
    /// <summary>Equal layers draw identically; used to decide which batches may share a material or merge.</summary>
    public string Signature => $"{(int)Combiner}|{string.Join(',', Textures.Select(t => t.ToLowerInvariant()))}|{string.Join(',', Coords)}|{EdgeFade}";
}

/// <summary>
/// Resolves an M2 batch's <c>shader_id</c> and texture-unit count to a pixel combiner and the
/// coordinate source of each unit, following the client's selection logic. Returns null for a shader
/// this viewer does not implement (so the model keeps its fidelity warning).
/// </summary>
public static class M2Combiners
{
    // shader_id & 0x7FFF indexes this table when bit 0x8000 is set: (pixel combiner, vertex shader).
    static readonly (LayerCombiner Pixel, string Vertex)[] Table =
    [
        (LayerCombiner.OpaqueMod2xNAAlpha, "Diffuse_T1_Env"),
        (LayerCombiner.OpaqueAddAlpha, "Diffuse_T1_Env"),
        (LayerCombiner.OpaqueAddAlphaAlpha, "Diffuse_T1_Env"),
        (LayerCombiner.OpaqueMod2xNAAlphaAdd, "Diffuse_T1_Env_T1"),
        (LayerCombiner.ModAddAlpha, "Diffuse_T1_Env"),
        (LayerCombiner.OpaqueAddAlpha, "Diffuse_T1_T1"),
        (LayerCombiner.ModAddAlpha, "Diffuse_T1_T1"),
        (LayerCombiner.ModAddAlphaAlpha, "Diffuse_T1_Env"),
        (LayerCombiner.OpaqueAlphaAlpha, "Diffuse_T1_Env"),
        (LayerCombiner.OpaqueMod2xNAAlpha3s, "Diffuse_T1_Env_T1"),
        (LayerCombiner.OpaqueAddAlphaWgt, "Diffuse_T1_T1"),
        (LayerCombiner.ModAddWithAlpha, "Diffuse_T1_Env"),
        (LayerCombiner.OpaqueModNAAlpha, "Diffuse_T1_Env"),
        (LayerCombiner.ModAddAlphaWgt, "Diffuse_T1_Env"),
        (LayerCombiner.ModAddAlphaWgt, "Diffuse_T1_T1"),
        (LayerCombiner.OpaqueAddAlphaWgt, "Diffuse_T1_T2"),
        (LayerCombiner.OpaqueModAddWgt, "Diffuse_T1_Env"),
        (LayerCombiner.OpaqueMod2xNAAlphaUnshAlpha, "Diffuse_T1_Env_T1"),
        (LayerCombiner.ModDualCrossfade, "Diffuse_T1"),
        (LayerCombiner.ModDepth, "Diffuse_EdgeFade_T1"),
        (LayerCombiner.OpaqueMod2xNAAlphaAlpha, "Diffuse_T1_Env_T2"),
        (LayerCombiner.ModMod, "Diffuse_EdgeFade_T1_T2"),
        (LayerCombiner.ModMaskedDualCrossfade, "Diffuse_T1_T2"),
        (LayerCombiner.OpaqueAlpha, "Diffuse_T1_T1"),
        (LayerCombiner.OpaqueMod2xNAAlphaUnshAlpha, "Diffuse_T1_Env_T2"),
        (LayerCombiner.ModDepth, "Diffuse_EdgeFade_Env"),
        // 26 to 28 are the guild tabard shaders, which need per-guild colors: unmapped.
        (LayerCombiner.None, ""),
        (LayerCombiner.None, ""),
        (LayerCombiner.None, ""),
        (LayerCombiner.Illum, "Diffuse_T1_T1"),
        (LayerCombiner.ModModModConst, "Diffuse_T1_T2_T3"),
        (LayerCombiner.ModModModConst, "Color_T1_T2_T3"),
        (LayerCombiner.Opaque, "Diffuse_T1"),
        (LayerCombiner.ModMod2x, "Diffuse_EdgeFade_T1_T2"),
        (LayerCombiner.Mod, "Diffuse_EdgeFade_T1"),
        (LayerCombiner.ModModDepth, "Diffuse_EdgeFade_T1_T2"),
    ];

    /// <summary>
    /// The combiner and per-unit coordinate sources for a batch, or null when unimplemented.
    /// <paramref name="textureCount"/> is the batch's texture-unit count (at least 2 here).
    /// </summary>
    public static (LayerCombiner Combiner, LayerCoord[] Coords, bool EdgeFade)? Resolve(int shaderId, int textureCount)
    {
        // Wrath files store 0 and the client picks the combiner at run time from the batch's material, which this
        // viewer does not derive: guessing one would draw a wrong blend with no warning, so the batch keeps its base texture.
        if (textureCount < 2 || textureCount > 4 || shaderId == 0)
        {
            return null;
        }

        LayerCombiner pixel;
        string vertex;
        if ((shaderId & 0x8000) != 0)
        {
            int index = shaderId & 0x7FFF;
            if (index >= Table.Length || Table[index].Pixel == LayerCombiner.None)
            {
                return null;
            }

            (pixel, vertex) = Table[index];
        }
        else
        {
            pixel = Legacy(shaderId);
            vertex = (shaderId & 0x80) != 0
                ? (shaderId & 0x8) != 0 ? "Diffuse_Env_Env" : "Diffuse_Env_T1"
                : (shaderId & 0x8) != 0 ? "Diffuse_T1_Env" : (shaderId & 0x4000) != 0 ? "Diffuse_T1_T2" : "Diffuse_T1_T1";
        }

        bool edgeFade = vertex.Contains("EdgeFade", StringComparison.Ordinal);
        var coords = vertex.Split('_').Skip(1).Where(p => p != "EdgeFade").Select(p => p switch
        {
            "Env" => LayerCoord.Env,
            "T2" => LayerCoord.T2,
            _ => LayerCoord.T1,
        }).ToList();
        while (coords.Count < textureCount)
        {
            coords.Add(LayerCoord.T1);
        }

        return (pixel, coords.Take(textureCount).ToArray(), edgeFade);
    }

    // The two-texture pixel shader of a legacy shader_id (no 0x8000 bit): the 0x70 bits pick a Mod
    // or Opaque first operation, the low three bits the second.
    static LayerCombiner Legacy(int shaderId) => (shaderId & 0x70) != 0
        ? (shaderId & 7) switch
        {
            3 => LayerCombiner.ModAdd,
            4 => LayerCombiner.ModMod2x,
            6 => LayerCombiner.ModMod2xNA,
            7 => LayerCombiner.ModAddNA,
            _ => LayerCombiner.ModMod,
        }
        : (shaderId & 7) switch
        {
            0 => LayerCombiner.OpaqueOpaque,
            3 or 7 => LayerCombiner.OpaqueAddAlpha,
            4 => LayerCombiner.OpaqueMod2x,
            6 => LayerCombiner.OpaqueMod2xNA,
            _ => LayerCombiner.OpaqueMod,
        };
}

/// <summary>
/// What a WMO material's <c>shader</c> field needs beyond its base texture. <see cref="Combiner"/> is
/// <see cref="LayerCombiner.None"/> for the shaders that draw the base texture alone;
/// <see cref="Units"/> counts the texture units including the first; <see cref="Opaque"/> forces the
/// batch opaque (the client's Opaque shader ignores texture alpha whatever the blend mode says).
/// </summary>
public readonly record struct WmoShaderInfo(LayerCombiner Combiner, int Units, LayerCoord[] Coords, bool Opaque = false);

/// <summary>
/// Maps a WMO material's MOMT shader (https://wowdev.wiki/WMO#MOMT) to its pixel combiner. Returns null
/// for shaders this viewer does not implement (so the model keeps its fidelity warning).
/// </summary>
public static class WmoCombiners
{
    static readonly WmoShaderInfo Plain = new(LayerCombiner.None, 1, [LayerCoord.T1]);

    public static WmoShaderInfo? Resolve(int shader) => shader switch
    {
        // Diffuse, Specular, Metal, Parallax, the DF shader (23) and the terrain-blended and level-of-detail variants draw the base texture.
        0 or 1 or 2 or 16 or 21 or 22 or 23 => Plain,
        // The water and submarine window shaders have no pixel shader of their own: they draw the base texture.
        10 or 14 => Plain,
        4 => Plain with { Opaque = true },
        3 => new(LayerCombiner.WmoEnv, 2, [LayerCoord.T1, LayerCoord.WmoEnv], Opaque: true),
        5 => new(LayerCombiner.WmoEnvMetal, 2, [LayerCoord.T1, LayerCoord.WmoEnv], Opaque: true),
        6 => new(LayerCombiner.WmoTwoLayerDiffuse, 2, [LayerCoord.T1, LayerCoord.T2]),
        7 => new(LayerCombiner.WmoTwoLayerEnvMetal, 3, [LayerCoord.T1, LayerCoord.T2, LayerCoord.WmoEnv]),
        8 => new(LayerCombiner.WmoTwoLayerTerrain, 2, [LayerCoord.T1, LayerCoord.Planar]),
        9 => new(LayerCombiner.WmoDiffuseEmissive, 2, [LayerCoord.T1, LayerCoord.T2]),
        11 => new(LayerCombiner.WmoMaskedEnvMetal, 3, [LayerCoord.T1, LayerCoord.WmoEnv, LayerCoord.T2]),
        12 => new(LayerCombiner.WmoEnvMetalEmissive, 3, [LayerCoord.T1, LayerCoord.WmoEnv, LayerCoord.T2]),
        13 => new(LayerCombiner.WmoTwoLayerDiffuseOpaque, 2, [LayerCoord.T1, LayerCoord.T2], Opaque: true),
        15 => new(LayerCombiner.WmoTwoLayerDiffuseEmissive, 2, [LayerCoord.T1, LayerCoord.T2]),
        17 => new(LayerCombiner.WmoAdditiveMaskedEnvMetal, 3, [LayerCoord.T1, LayerCoord.WmoEnv, LayerCoord.T2]),
        18 => new(LayerCombiner.WmoTwoLayerDiffuseMod2x, 3, [LayerCoord.T1, LayerCoord.Planar, LayerCoord.T2]),
        19 => new(LayerCombiner.WmoTwoLayerDiffuseMod2xNA, 2, [LayerCoord.T1, LayerCoord.T2]),
        20 => new(LayerCombiner.WmoTwoLayerDiffuseAlpha, 3, [LayerCoord.T1, LayerCoord.Planar, LayerCoord.T2]),
        _ => null,
    };
}
