namespace ACWorldEdit.Assets;

/// <summary>
/// What a piece of world geometry is, for shadow casting. Each category also selects the render layer
/// its proxy is drawn on, so a cascade can include far layers and skip near detail ones.
/// </summary>
public enum ShadowCasterCategory
{
    /// <summary>Never casts (skyboxes, overlays, liquids, markers, fallback capsules).</summary>
    None = 0,
    Terrain,
    /// <summary>A WMO building's own (non-doodad) surfaces.</summary>
    WmoShell,
    /// <summary>A doodad embedded in a WMO's doodad set.</summary>
    WmoDoodad,
    /// <summary>A doodad placed directly by an ADT.</summary>
    AdtDoodad,
    /// <summary>A database creature (NPC, mount or rider) drawn in its animated pose.</summary>
    Creature,
    /// <summary>A database GameObject whose model is an M2.</summary>
    GameObject,
}

/// <summary>
/// Which geometry casts into the cascade maps, and how much of it must be opaque for that to be safe.
/// <para>
/// Static scenery keeps the conservative v1 rules: a WMO doodad or ADT doodad casts only when every
/// surface is opaque, so leaf cards and fences do not throw solid rectangles; a WMO shell casts when
/// any surface is opaque, because its transparent windows sit in a solid building. Creatures and M2
/// GameObjects are database spawns drawn as animated multimeshes, so they cast when any surface is
/// castable — skinned surfaces are allowed and alpha-blended surfaces are alpha-tested in the depth
/// pass. Liquids, unlit batches and fallback markers never cast.
/// </para>
/// </summary>
public static class ShadowCasterPolicy
{
    /// <summary>
    /// Whether a model with these surface traits may cast. <paramref name="anyOpaque"/> is true when at
    /// least one surface is opaque; <paramref name="allOpaque"/> is true when every surface is;
    /// <paramref name="anyCasting"/> is true when at least one surface passes
    /// <see cref="IsCastingSurface"/>. Static categories ignore <paramref name="anyCasting"/> so their
    /// rules do not move.
    /// </summary>
    public static bool Allows(ShadowCasterCategory category, bool anyOpaque, bool allOpaque, bool anyCasting = false) => category switch
    {
        ShadowCasterCategory.Terrain => true,
        ShadowCasterCategory.WmoShell => anyOpaque,
        ShadowCasterCategory.WmoDoodad => allOpaque,
        ShadowCasterCategory.AdtDoodad => allOpaque,
        ShadowCasterCategory.Creature => anyCasting,
        ShadowCasterCategory.GameObject => anyCasting,
        _ => false,
    };

    /// <summary>
    /// A static-scenery surface the caster pass draws as a solid depth: opaque, not unlit, not a liquid
    /// and not skinned. Kept unchanged so the static categories do not regress.
    /// </summary>
    public static bool IsOpaque(ModelSurface surface) =>
        surface.Blend == 0 && !surface.Unlit && surface.LiquidType == null && surface.Bones == null;

    /// <summary>Whether a surface is GPU-skinned: it has both a bone palette and per-vertex weights.</summary>
    public static bool IsSkinned(ModelSurface surface) => surface.Bones != null && surface.Weights != null;

    /// <summary>
    /// A surface the caster pass draws as solid depth for a creature or GameObject: opaque, not unlit and
    /// not a liquid. Unlike <see cref="IsOpaque"/>, skinning is allowed.
    /// </summary>
    public static bool IsCastingOpaque(ModelSurface surface) =>
        surface.Blend == 0 && !surface.Unlit && surface.LiquidType == null;

    /// <summary>
    /// An alpha-blended surface the caster pass draws with an alpha test (hair, capes): blend mode 1,
    /// not unlit and not a liquid.
    /// </summary>
    public static bool IsAlphaTestedCaster(ModelSurface surface) =>
        surface.Blend == 1 && !surface.Unlit && surface.LiquidType == null;

    /// <summary>Whether the caster pass can draw this surface at all: opaque or alpha-tested.</summary>
    public static bool IsCastingSurface(ModelSurface surface) =>
        IsCastingOpaque(surface) || IsAlphaTestedCaster(surface);
}
