using ACWorldEdit.Assets;
using Xunit;

namespace ACWorldEdit.Tests.Assets;

/// <summary>Which world geometry may cast into the cascade maps.</summary>
public class ShadowCasterPolicyTests
{
    [Theory]
    [InlineData(ShadowCasterCategory.Terrain, false, false, true)]
    [InlineData(ShadowCasterCategory.Terrain, true, true, true)]
    [InlineData(ShadowCasterCategory.WmoShell, true, false, true)]
    [InlineData(ShadowCasterCategory.WmoShell, false, false, false)]
    [InlineData(ShadowCasterCategory.WmoDoodad, true, true, true)]
    [InlineData(ShadowCasterCategory.WmoDoodad, true, false, false)]
    [InlineData(ShadowCasterCategory.WmoDoodad, false, false, false)]
    [InlineData(ShadowCasterCategory.AdtDoodad, true, true, true)]
    [InlineData(ShadowCasterCategory.AdtDoodad, true, false, false)]
    [InlineData(ShadowCasterCategory.None, true, true, false)]
    public void CastersFollowTheOpaqueRules(ShadowCasterCategory category, bool anyOpaque, bool allOpaque, bool expected) =>
        Assert.Equal(expected, ShadowCasterPolicy.Allows(category, anyOpaque, allOpaque));

    static ModelSurface Surface(int blend = 0, bool unlit = false, uint? liquid = null, bool skinned = false) =>
        new([], [], [], [], "", blend, false, skinned ? new[] { 0 } : null, Unlit: unlit, LiquidType: liquid);

    [Theory]
    [InlineData(0, false, false, false, true)]
    [InlineData(1, false, false, false, false)]
    [InlineData(2, false, false, false, false)]
    [InlineData(0, true, false, false, false)]
    [InlineData(0, false, true, false, false)]
    [InlineData(0, false, false, true, false)]
    public void OnlySolidBatchesAreOpaque(int blend, bool unlit, bool liquid, bool skinned, bool expected) =>
        Assert.Equal(expected, ShadowCasterPolicy.IsOpaque(Surface(blend, unlit, liquid ? 1u : null, skinned)));
}
