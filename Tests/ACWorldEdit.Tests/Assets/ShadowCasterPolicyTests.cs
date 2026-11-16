using ACWorldEdit.Assets;
using Xunit;

namespace ACWorldEdit.Tests.Assets;

/// <summary>Which world geometry may cast into the cascade maps.</summary>
public class ShadowCasterPolicyTests
{
    [Theory]
    // Static categories keep their opaque rules and ignore anyCasting.
    [InlineData(ShadowCasterCategory.Terrain, false, false, false, true)]
    [InlineData(ShadowCasterCategory.Terrain, false, false, true, true)]
    [InlineData(ShadowCasterCategory.Terrain, true, true, false, true)]
    [InlineData(ShadowCasterCategory.WmoShell, true, false, false, true)]
    [InlineData(ShadowCasterCategory.WmoShell, true, false, true, true)]
    [InlineData(ShadowCasterCategory.WmoShell, false, false, false, false)]
    [InlineData(ShadowCasterCategory.WmoDoodad, true, true, false, true)]
    [InlineData(ShadowCasterCategory.WmoDoodad, true, false, false, false)]
    [InlineData(ShadowCasterCategory.WmoDoodad, false, false, true, false)]
    [InlineData(ShadowCasterCategory.AdtDoodad, true, true, false, true)]
    [InlineData(ShadowCasterCategory.AdtDoodad, true, false, true, false)]
    // Database creatures and M2 GameObjects cast whenever a surface is castable.
    [InlineData(ShadowCasterCategory.Creature, false, false, false, false)]
    [InlineData(ShadowCasterCategory.Creature, false, false, true, true)]
    [InlineData(ShadowCasterCategory.GameObject, false, false, false, false)]
    [InlineData(ShadowCasterCategory.GameObject, false, false, true, true)]
    [InlineData(ShadowCasterCategory.None, true, true, true, false)]
    public void CastersFollowTheCategoryRules(ShadowCasterCategory category, bool anyOpaque, bool allOpaque, bool anyCasting, bool expected) =>
        Assert.Equal(expected, ShadowCasterPolicy.Allows(category, anyOpaque, allOpaque, anyCasting));

    static ModelSurface Surface(int blend = 0, bool unlit = false, uint? liquid = null, bool skinned = false) =>
        new([], [], [], [], "", blend, false,
            skinned ? new[] { 0 } : null,
            skinned ? new[] { 1f } : null,
            Unlit: unlit, LiquidType: liquid);

    [Theory]
    [InlineData(0, false, false, false, true)]
    [InlineData(1, false, false, false, false)]
    [InlineData(2, false, false, false, false)]
    [InlineData(0, true, false, false, false)]
    [InlineData(0, false, true, false, false)]
    [InlineData(0, false, false, true, false)]
    public void OnlySolidBatchesAreOpaque(int blend, bool unlit, bool liquid, bool skinned, bool expected) =>
        Assert.Equal(expected, ShadowCasterPolicy.IsOpaque(Surface(blend, unlit, liquid ? 1u : null, skinned)));

    [Theory]
    [InlineData(0, false, false, true, true)]
    [InlineData(1, false, false, true, true)]
    [InlineData(1, false, false, false, true)]
    [InlineData(2, false, false, true, false)]
    [InlineData(0, true, false, true, false)]
    [InlineData(0, false, true, true, false)]
    public void CastingSurfacesAllowSkinningAndAlphaTest(int blend, bool unlit, bool liquid, bool skinned, bool expected) =>
        Assert.Equal(expected, ShadowCasterPolicy.IsCastingSurface(Surface(blend, unlit, liquid ? 1u : null, skinned)));

    [Fact]
    public void SkinnedOpaqueSurfacesCast()
    {
        var surface = Surface(skinned: true);
        Assert.True(ShadowCasterPolicy.IsSkinned(surface));
        Assert.True(ShadowCasterPolicy.IsCastingOpaque(surface));
    }

    [Fact]
    public void SkinnedAlphaTestedSurfacesAlphaTestAndCast()
    {
        var surface = Surface(blend: 1, skinned: true);
        Assert.True(ShadowCasterPolicy.IsSkinned(surface));
        Assert.True(ShadowCasterPolicy.IsAlphaTestedCaster(surface));
        Assert.False(ShadowCasterPolicy.IsCastingOpaque(surface));
        Assert.True(ShadowCasterPolicy.IsCastingSurface(surface));
    }
}
