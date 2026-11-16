using System.Numerics;
using ACWorldEdit.Assets;
using Xunit;

namespace ACWorldEdit.Tests.Assets;

/// <summary>
/// The retail cascade frame and mask math. The shader in client_shadow.gdshaderinc mirrors these
/// functions, so the boundaries here are the ones the receiver relies on.
/// </summary>
public class ShadowCascadesTests
{
    static readonly Quaternion CameraRotation = Quaternion.Normalize(
        Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.55f) * Quaternion.CreateFromAxisAngle(Vector3.UnitX, -0.3f));
    static readonly Vector3 Sun = Vector3.Normalize(new Vector3(0.42f, -0.85f, 0.32f));
    static readonly Vector3 Eye = new(120f, 40f, -75f);
    const float Fov = 70f * MathF.PI / 180f;
    const float Aspect = 16f / 9f;

    static ShadowCascadeFit Fit(float near = 0.2f, float farStart = 0.2f, float farEnd = 100f, int resolution = 1024, float padding = 25f, int index = 0) =>
        ShadowCascades.FitCascade(Sun, Eye, CameraRotation, Fov, Aspect, near, farStart, farEnd, resolution, padding, index: index);

    [Fact]
    public void SplitDepthsStartAtNearEndAtFarAndIncrease()
    {
        var splits = ShadowCascades.SplitDepths(0.2f, 180f, 3);

        Assert.Equal(4, splits.Length);
        Assert.Equal(0.2f, splits[0], 4);
        Assert.Equal(180f, splits[^1], 4);
        for (int i = 1; i < splits.Length; i++)
        {
            Assert.True(splits[i] > splits[i - 1], $"split {i} ({splits[i]}) must exceed {splits[i - 1]}");
        }
    }

    [Fact]
    public void OneCascadeSpansTheWholeRange()
    {
        var splits = ShadowCascades.SplitDepths(0.5f, 100f, 1);

        Assert.Equal([0.5f, 100f], splits);
    }

    [Fact]
    public void NoCascadesHaveNoBoundaries()
    {
        Assert.Empty(ShadowCascades.SplitDepths(0.2f, 100f, 0));
    }

    [Fact]
    public void FrustumCornersSitOnTheNearAndFarPlanes()
    {
        var corners = ShadowCascades.FrustumCorners(Eye, CameraRotation, Fov, Aspect, 1f, 50f);

        Assert.Equal(8, corners.Length);
        // The camera looks along local -Z: four corners one yard out and four fifty yards out.
        Vector3 forward = Vector3.Transform(-Vector3.UnitZ, CameraRotation);
        Assert.Equal(4, corners.Count(c => MathF.Abs(Vector3.Dot(c - Eye, forward) - 1f) < 0.001f));
        Assert.Equal(4, corners.Count(c => MathF.Abs(Vector3.Dot(c - Eye, forward) - 50f) < 0.001f));
    }

    [Fact]
    public void EveryFrustumCornerMapsInsideTheFittedSquare()
    {
        var fit = Fit();
        var corners = ShadowCascades.FrustumCorners(Eye, CameraRotation, Fov, Aspect, 0.2f, 100f);

        foreach (var corner in corners)
        {
            var uv = ShadowCascades.WorldToUv(fit, corner);
            Assert.InRange(uv.X, -0.0001f, 1.0001f);
            Assert.InRange(uv.Y, -0.0001f, 1.0001f);
        }
    }

    [Fact]
    public void TheFittedCenterMapsToTheMapCenter()
    {
        var fit = Fit();
        Vector3 center = fit.Eye + fit.LightForward * fit.Radius;

        var uv = ShadowCascades.WorldToUv(fit, center);

        Assert.Equal(0.5f, uv.X, 4);
        Assert.Equal(0.5f, uv.Y, 4);
    }

    [Fact]
    public void TheVerticalUvAxisIsFlippedForGodotsTopLeftTextures()
    {
        var fit = Fit();
        Vector3 center = fit.Eye + fit.LightForward * fit.Radius;

        var up = ShadowCascades.WorldToUv(fit, center + fit.LightUp * (fit.Radius * 0.5f));
        var down = ShadowCascades.WorldToUv(fit, center - fit.LightUp * (fit.Radius * 0.5f));
        var right = ShadowCascades.WorldToUv(fit, center + fit.LightRight * (fit.Radius * 0.5f));

        // v = 0 is the top of the texture, so the light's up axis maps to smaller v.
        Assert.Equal(0.25f, up.Y, 3);
        Assert.Equal(0.75f, down.Y, 3);
        Assert.Equal(0.75f, right.X, 3);
    }

    [Fact]
    public void LightBasisIsOrthonormalAndRightHanded()
    {
        var (forward, right, up) = ShadowCascades.LightBasis(Sun);

        Assert.Equal(1f, forward.Length(), 4);
        Assert.Equal(1f, right.Length(), 4);
        Assert.Equal(1f, up.Length(), 4);
        Assert.Equal(0f, Vector3.Dot(forward, right), 4);
        Assert.Equal(0f, Vector3.Dot(forward, up), 4);
        Assert.Equal(0f, Vector3.Dot(right, up), 4);
        // cross(right, up) = -forward, so (right, up, -forward) is the camera's right-handed basis.
        Assert.Equal(-1f, Vector3.Dot(Vector3.Cross(right, up), forward), 4);
    }

    [Fact]
    public void AVerticalSunStillHasAStableBasis()
    {
        var (forward, right, up) = ShadowCascades.LightBasis(Vector3.UnitY);

        Assert.Equal(Vector3.UnitY, forward);
        Assert.Equal(1f, right.Length(), 4);
        Assert.Equal(1f, up.Length(), 4);
        Assert.Equal(0f, Vector3.Dot(right, up), 4);
    }

    [Fact]
    public void SnapCenterIsStableUnderSubTexelMotion()
    {
        var snapped = ShadowCascades.SnapCenter(Fit(), 1024);
        // A drift of less than half a texel from an already snapped center cannot change the grid point.
        var shifted = snapped with { Eye = snapped.Eye + snapped.LightRight * (0.4f * snapped.TexelWorldSize) };

        var again = ShadowCascades.SnapCenter(shifted, 1024);
        float firstDot = Vector3.Dot(snapped.Eye + snapped.LightForward * snapped.Radius, snapped.LightRight);
        float secondDot = Vector3.Dot(again.Eye + again.LightForward * again.Radius, again.LightRight);

        Assert.Equal(firstDot, secondDot, 3);
        // The snapped center is a whole number of texels from the origin along the light's right axis.
        Assert.Equal(0f, MathF.Abs(firstDot / snapped.TexelWorldSize - MathF.Round(firstDot / snapped.TexelWorldSize)), 3);
    }

    [Fact]
    public void FitCarriesTheSplitTexelAndPadding()
    {
        var fit = Fit(farStart: 10f, farEnd: 120f, resolution: 2048, padding: 250f, index: 2);

        Assert.Equal(2f * fit.Radius, fit.Far, 3);
        Assert.Equal(2f * fit.Radius / 2048f, fit.TexelWorldSize, 4);
        Assert.Equal(120f, fit.SplitDistance, 4);
        Assert.Equal(new Vector4(120f, fit.TexelWorldSize, 2048f, 2f), fit.SplitAndTexel);
        Assert.True(fit.Radius >= 250f);
        float corpus = (fit.Radius - 250f) / ShadowCascades.RadiusQuantum;
        Assert.Equal(0f, MathF.Abs(corpus - MathF.Round(corpus)), 3);
        Assert.Equal(new Vector4(0.05f, 0.05f, 0.95f, 0.95f), fit.FadeBounds);
    }

    [Fact]
    public void SelectCascadePicksTheFirstCoveringSplit()
    {
        float[] distances = [30f, 90f, 180f];

        Assert.Equal(0, ShadowCascades.SelectCascade(10f, distances));
        Assert.Equal(0, ShadowCascades.SelectCascade(30f, distances));
        Assert.Equal(1, ShadowCascades.SelectCascade(30.1f, distances));
        Assert.Equal(1, ShadowCascades.SelectCascade(90f, distances));
        Assert.Equal(2, ShadowCascades.SelectCascade(179.9f, distances));
        Assert.Equal(2, ShadowCascades.SelectCascade(5000f, distances));
        Assert.Equal(0, ShadowCascades.SelectCascade(10f, []));
    }

    [Fact]
    public void BlendWeightCoversTheLastFivePercent()
    {
        Assert.Equal(0f, ShadowCascades.BlendWeight(90f, 100f));
        Assert.Equal(0f, ShadowCascades.BlendWeight(95f, 100f));
        Assert.Equal(0.5f, ShadowCascades.BlendWeight(97.5f, 100f), 3);
        Assert.Equal(1f, ShadowCascades.BlendWeight(100f, 100f));
        Assert.Equal(1f, ShadowCascades.BlendWeight(120f, 100f));
        Assert.Equal(0f, ShadowCascades.BlendWeight(5f, 0f));
    }

    [Fact]
    public void EdgeFadeIsLitInTheMiddleAndDarkAtTheBorder()
    {
        var bounds = new Vector4(0.05f, 0.05f, 0.95f, 0.95f);

        Assert.Equal(1f, ShadowCascades.EdgeFade(new Vector2(0.5f, 0.5f), bounds));
        Assert.Equal(0f, ShadowCascades.EdgeFade(new Vector2(0.05f, 0.5f), bounds));
        Assert.Equal(0f, ShadowCascades.EdgeFade(new Vector2(0f, 0.5f), bounds));
        Assert.Equal(0f, ShadowCascades.EdgeFade(new Vector2(1f, 0.5f), bounds));
        Assert.Equal(0f, ShadowCascades.EdgeFade(new Vector2(1.2f, 0.5f), bounds));
        Assert.Equal(0.5f, ShadowCascades.EdgeFade(new Vector2(0.1f, 0.5f), bounds), 3);
    }

    [Fact]
    public void TheSlopeBiasGrowsAsTheSunGrazesASlope()
    {
        // Facing the sun: only the fixed term.
        Assert.Equal(0.05f, ShadowCascades.DepthBias(1f, 0.8f, 0.05f, 0.5f, 1f), 3);
        // Grazing: tan(acos(0.2)) * texel plus the normal-offset term.
        Assert.Equal(4.369f, ShadowCascades.DepthBias(0.2f, 0.8f, 0.05f, 0.5f, 1f), 2);
        // The scale can turn the term off for tight contacts.
        Assert.Equal(0.45f, ShadowCascades.DepthBias(0.2f, 0.8f, 0.05f, 0.5f, 0f), 3);
        // The slope is capped so a nearly back-facing surface cannot ask for an unbounded bias.
        Assert.Equal(6.925f, ShadowCascades.DepthBias(0.05f, 0.8f, 0.05f, 0.5f, 1f), 2);
    }
}
