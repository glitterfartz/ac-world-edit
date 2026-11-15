namespace ACWorldEdit.Assets;

/// <summary>
/// What a piece of world geometry is, for shadow casting. Each category also selects the render layer
/// its proxy is drawn on, so a cascade can include far layers and skip near detail ones.
/// </summary>
public enum ShadowCasterCategory
{
    /// <summary>Never casts (creatures, GameObjects, skyboxes, overlays, liquids, markers).</summary>
    None = 0,
    Terrain,
    /// <summary>A WMO building's own (non-doodad) surfaces.</summary>
    WmoShell,
    /// <summary>A doodad embedded in a WMO's doodad set.</summary>
    WmoDoodad,
    /// <summary>A doodad placed directly by an ADT.</summary>
    AdtDoodad,
}

/// <summary>
/// Which geometry casts into the cascade maps, and how much of it must be opaque for that to be safe.
/// <para>
/// The v1 rules skip anything that needs an alpha test or a second lighting model: a doodad casts only
/// when every surface is opaque, so leaf cards and fences do not throw solid rectangles. A WMO shell
/// casts when any surface is opaque, because its transparent windows sit in a solid building. Liquids,
/// unlit batches, skinned creatures and GameObjects never cast.
/// </para>
/// </summary>
public static class ShadowCasterPolicy
{
    /// <summary>
    /// Whether a model with these surface traits may cast. <paramref name="anyOpaque"/> is true when at
    /// least one surface is opaque; <paramref name="allOpaque"/> is true when every surface is.
    /// </summary>
    public static bool Allows(ShadowCasterCategory category, bool anyOpaque, bool allOpaque) => category switch
    {
        ShadowCasterCategory.Terrain => true,
        ShadowCasterCategory.WmoShell => anyOpaque,
        ShadowCasterCategory.WmoDoodad => allOpaque,
        ShadowCasterCategory.AdtDoodad => allOpaque,
        _ => false,
    };

    /// <summary>
    /// A surface the caster pass can draw as a solid depth: opaque, not unlit, not a liquid and not
    /// skinned. v1 has no alpha test and no second lighting model, so everything else is skipped.
    /// </summary>
    public static bool IsOpaque(ModelSurface surface) =>
        surface.Blend == 0 && !surface.Unlit && surface.LiquidType == null && surface.Bones == null;
}
