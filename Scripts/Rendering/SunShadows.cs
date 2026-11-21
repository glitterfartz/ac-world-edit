using Godot;
using ACWorldEdit.Assets;

namespace ACWorldEdit;

/// <summary>
/// Renders the sun's cascade maps and publishes them to the world shaders. One <see cref="SubViewport"/>
/// per cascade shares the main <see cref="World3D"/>, with an orthographic camera fitted by
/// <see cref="ShadowCascades"/>; only <see cref="ShadowCasterScene"/>'s proxy layers are visible to
/// those cameras, and the main camera clears those layers. Each map is redrawn only when the camera,
/// the sun or the caster set changes, so moving nothing costs nothing.
/// <para>
/// The passer owns the cascade data texture (four RGBA-float texels per cascade) and the
/// <c>client_shadow_*</c> globals declared in project.godot. The receiver is
/// <c>client_shadow_visibility</c> in <c>Shaders/shared/client_shadow.gdshaderinc</c>.
/// </para>
/// </summary>
public sealed partial class SunShadows
{
    /// <summary>The node that carries the cascade viewports; add it to the world root.</summary>
    public Node3D Root { get; }
    /// <summary>Extra yards the orthographic box reaches toward the sun, so tall buildings in front cast.</summary>
    public const float CasterPadding = 250f;

    /// <summary>Receiver depth bias in yards, and the normal-offset distance in yards.</summary>
    const float ReceiverBias = 0.05f, NormalOffset = 0.5f;

    /// <summary>
    /// Scale of the slope-scaled receiver bias (texel world size * tan(acos(N.L))). 1 is the standard
    /// term; raise it for more acne protection at grazing sun, lower it to keep contacts tight.
    /// </summary>
    const float SlopeBiasScale = 1f;

    /// <summary>Sun directions closer than this (about 0.05 degrees) do not need a redraw.</summary>
    const float SunAngleCosine = 0.9999996f;

    /// <summary>Camera movement smaller than this (yards squared) keeps the maps.</summary>
    const float CameraMoveEpsilon = 0.0025f;

    readonly ShadowCasterScene casters;
    readonly World3D world;
    readonly SubViewport?[] viewports = new SubViewport?[ClientShaderGlobals.MaxShadowCascades];
    readonly Camera3D?[] cameras = new Camera3D?[ClientShaderGlobals.MaxShadowCascades];
    readonly Image cascadeImage = Image.CreateEmpty(ClientShaderGlobals.MaxShadowCascades * ClientShaderGlobals.ShadowCascadeTexels, 1, false, Image.Format.Rgbaf);
    readonly ImageTexture cascadeTexture;
    readonly Godot.Environment casterEnvironment;

    ShadowQuality quality = ShadowQuality.Off;
    ShadowCascadeFit[] fits = [];
    int allocated;
    int resolution = 1024;
    int published;
    bool initialized;
    bool castersOn;
    float lastStrength = -1f;
    int lastVersion = -1;
    int lastTransformVersion = -1;
    double lastAnimatedRenderMs;
    Vector3 lastSun = new(float.NaN, float.NaN, float.NaN);
    Vector3 lastEye;
    Quaternion lastRotation;
    readonly bool supported = DisplayServer.GetName() != "headless";

    /// <summary>Cascades currently published to the shaders (0 while shadows are off or at night).</summary>
    public int CascadeCount => published;

    /// <summary>The rendered cascades' fits, for diagnostics and the perf breakdown.</summary>
    public IReadOnlyList<ShadowCascadeFit> Fits => fits;

    public ShadowQuality Quality => quality;

    /// <summary>False in headless runs, where no viewport produces a texture.</summary>
    public bool Supported => supported;

    /// <summary>Redraws since construction, for diagnostics and the perf breakdown.</summary>
    public int Redraws { get; private set; }

    /// <summary>Redraws forced by the throttled animated refresh, not by camera, sun or registration.</summary>
    public int AnimatedRedraws { get; private set; }

    public SunShadows(ShadowCasterScene casters, World3D world)
    {
        Root = new Node3D { Name = "SunShadows" };
        this.casters = casters;
        this.world = world;
        cascadeTexture = ImageTexture.CreateFromImage(cascadeImage);
        casterEnvironment = BuildEnvironment();
        ClientShaderGlobals.Set(ClientShaderGlobals.ShadowCascades, cascadeTexture);
        ClientShaderGlobals.Set(ClientShaderGlobals.ShadowDepthRange, new Vector4(ShadowDepthPacking.Min, ShadowDepthPacking.Max, 0, 0));
    }

    static Godot.Environment BuildEnvironment() => new()
    {
        BackgroundMode = Godot.Environment.BGMode.Color,
        BackgroundColor = Colors.White,
        // The maps are data, not a picture: no tone curve, glow, fog, AO or adjustment may touch them.
        TonemapMode = Godot.Environment.ToneMapper.Linear,
        TonemapExposure = 1f,
        TonemapWhite = 1f,
        GlowEnabled = false,
        FogEnabled = false,
        AdjustmentEnabled = false,
        SsaoEnabled = false,
        SsilEnabled = false,
        SdfgiEnabled = false,
        VolumetricFogEnabled = false,
        AmbientLightSource = Godot.Environment.AmbientSource.Disabled,
    };

    /// <summary>
    /// Change the cascade count, distance, resolution and tap count. Extra viewports are freed at
    /// once; the next <see cref="Update"/> redraws.
    /// </summary>
    public void SetQuality(ShadowQuality next)
    {
        if (quality == next)
        {
            return;
        }

        quality = next;
        resolution = ShadowQualityPresets.For(next).Resolution;
        EnsureViewports(ShadowQualityPresets.For(next).Cascades);
        initialized = false;
        Publish(0);
        if (next == ShadowQuality.Off)
        {
            casters.SetEnabled(false);
            castersOn = false;
        }
    }

    /// <summary>
    /// Redraw the cascades when the camera, sun or caster set moved, and publish the current strength
    /// every frame. Call after the sky and the model library have updated.
    /// </summary>
    public void Update(Camera3D camera, SkyLighting sky)
    {
        if (!supported || allocated == 0)
        {
            Publish(0);
            return;
        }

        float strength = sky.SunShadowStrength;
        if (strength <= 0.001f)
        {
            // Night, an interior or no light table: no caster pass and no receiver change.
            casters.SetEnabled(false);
            castersOn = false;
            Publish(0);
            if (lastStrength != 0f)
            {
                PublishStrength(0f);
            }

            return;
        }

        var sun = sky.SunDirection;
        var eye = camera.GlobalPosition;
        var rotation = camera.GlobalBasis.GetRotationQuaternion();
        bool cameraMoved = !initialized
            || eye.DistanceSquaredTo(lastEye) > CameraMoveEpsilon
            || 1f - Mathf.Abs(rotation.Dot(lastRotation)) > 1e-7f;
        bool sunMoved = !initialized || lastSun.Dot(sun) < SunAngleCosine;
        bool castersChanged = casters.Version != lastVersion || published != allocated;
        if (cameraMoved || sunMoved || castersChanged)
        {
            // Camera, sun and registration stay immediate: they change what the maps must contain.
            Render(camera, sky);
            return;
        }

        // Moving and animating casters only change the depth inside the same maps, so refresh them
        // at the preset's bounded rate. A transform-only change must keep retrying until the
        // deadline, so lastTransformVersion advances only when Render actually runs.
        bool transformMoved = casters.TransformVersion != lastTransformVersion;
        int refreshHz = ShadowQualityPresets.For(quality).AnimatedRefreshHz;
        if (refreshHz > 0 && (transformMoved || casters.AnimatedCasterCount > 0))
        {
            double now = Time.GetTicksMsec();
            bool due = now - lastAnimatedRenderMs >= 1000.0 / refreshHz;
            // The continuous animation case also stops while no animated caster is visible; a moved
            // caster still redraws because its own visibility is not the only thing that changed.
            if (due && (transformMoved || casters.HasVisibleAnimated()))
            {
                Render(camera, sky, animated: true);
                return;
            }
        }

        if (!Mathf.IsEqualApprox(strength, lastStrength))
        {
            ClientShaderGlobals.Set(ClientShaderGlobals.ShadowStrength, strength);
            lastStrength = strength;
        }
    }

    /// <summary>Force a redraw, for diagnostics (the next frame still needs a rendered viewport).</summary>
    public void RenderNow(Camera3D camera, SkyLighting sky)
    {
        initialized = false;
        lastVersion = -1;
        Update(camera, sky);
    }

    /// <summary>The image a cascade's viewport currently holds, for the caster smoke; null when off.</summary>
    public Image? CascadeImage(int index) =>
        supported && index >= 0 && index < allocated && viewports[index] is { } viewport
            ? viewport.GetTexture().GetImage()
            : null;

    /// <summary>The number of cascades the last render fitted.</summary>
    public int RenderedCascades => fits.Length;

    /// <summary>A cascade's viewport RID, for per-cascade render info in the perf breakdown.</summary>
    public Rid CascadeViewportRid(int index) =>
        index >= 0 && index < allocated && viewports[index] is { } viewport ? viewport.GetViewportRid() : default;

    /// <summary>Draw calls the last rendered frame issued for one cascade.</summary>
    public long CascadeDrawCalls(int index) =>
        index >= 0 && index < allocated && viewports[index] is { } viewport
            ? RenderingServer.ViewportGetRenderInfo(viewport.GetViewportRid(), RenderingServer.ViewportRenderInfoType.Visible, RenderingServer.ViewportRenderInfo.DrawCallsInFrame)
            : 0;

    /// <summary>Release the globals and textures; the caller frees <see cref="Root"/>.</summary>
    public void Dispose()
    {
        casters.SetEnabled(false);
        castersOn = false;
        // Release the globals' references before the viewports and the data texture go away.
        for (int i = 0; i < ClientShaderGlobals.MaxShadowCascades; i++)
        {
            ClientShaderGlobals.Set(ClientShaderGlobals.ShadowMap(i), default);
        }

        ClientShaderGlobals.Set(ClientShaderGlobals.ShadowCascades, default);
        cascadeTexture.Dispose();
        // The viewports and cameras are children; the scene tree frees them with this node.
        Array.Clear(viewports);
        Array.Clear(cameras);
        allocated = 0;
    }

    void Render(Camera3D camera, SkyLighting sky, bool animated = false)
    {
        Redraws++;
        if (animated)
        {
            AnimatedRedraws++;
        }

        // A redraw of any kind refreshes the maps, so the animated throttle measures time since the
        // last real render rather than time since the last animated one.
        lastAnimatedRenderMs = Time.GetTicksMsec();
        if (!castersOn)
        {
            casters.SetEnabled(true);
            castersOn = true;
        }

        var preset = ShadowQualityPresets.For(quality);
        resolution = preset.Resolution;
        float[] splits = ShadowCascades.SplitDepths(camera.Near, preset.MaxDistance, allocated);
        if (splits.Length != allocated + 1)
        {
            Publish(0);
            return;
        }

        float aspect = camera.GetViewport().GetVisibleRect().Size.Y > 0f
            ? camera.GetViewport().GetVisibleRect().Size.X / camera.GetViewport().GetVisibleRect().Size.Y
            : 1f;
        float fov = Mathf.DegToRad(camera.Fov);
        var sun = sky.SunDirection;
        var rotation = camera.GlobalBasis.GetRotationQuaternion();
        var numericRotation = new System.Numerics.Quaternion(rotation.X, rotation.Y, rotation.Z, rotation.W);
        var numericSun = new System.Numerics.Vector3(sun.X, sun.Y, sun.Z);
        var numericEye = new System.Numerics.Vector3(camera.GlobalPosition.X, camera.GlobalPosition.Y, camera.GlobalPosition.Z);

        fits = new ShadowCascadeFit[allocated];
        for (int i = 0; i < allocated; i++)
        {
            var fit = ShadowCascades.SnapCenter(
                ShadowCascades.FitCascade(
                    numericSun,
                    numericEye,
                    numericRotation,
                    fov,
                    aspect,
                    camera.Near,
                    splits[i],
                    splits[i + 1],
                    resolution,
                    CasterPadding,
                    index: i),
                resolution);
            fits[i] = fit;
            var viewport = viewports[i]!;
            var caster = cameras[i]!;
            var size = new Vector2I(resolution, resolution);
            if (viewport.Size != size)
            {
                viewport.Size = size;
            }

            caster.SetOrthogonal(2f * fit.Radius, fit.Near, fit.Far);
            caster.Transform = new Transform3D(
                new Basis(
                    new Vector3(fit.LightRight.X, fit.LightRight.Y, fit.LightRight.Z),
                    new Vector3(fit.LightUp.X, fit.LightUp.Y, fit.LightUp.Z),
                    new Vector3(-fit.LightForward.X, -fit.LightForward.Y, -fit.LightForward.Z)),
                new Vector3(fit.Eye.X, fit.Eye.Y, fit.Eye.Z));
            caster.CullMask = MaskFor(i);
            viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
            WriteCascade(i, fit);
        }

        cascadeTexture.Update(cascadeImage);
        ClientShaderGlobals.Set(ClientShaderGlobals.ShadowTaps, (float)preset.PcfTaps);
        ClientShaderGlobals.Set(ClientShaderGlobals.ShadowBias, ReceiverBias);
        ClientShaderGlobals.Set(ClientShaderGlobals.ShadowNormalOffset, NormalOffset);
        ClientShaderGlobals.Set(ClientShaderGlobals.ShadowSlopeBias, SlopeBiasScale);
        Publish(allocated);
        PublishStrength(sky.SunShadowStrength);

        lastEye = camera.GlobalPosition;
        lastRotation = rotation;
        lastSun = sun;
        lastVersion = casters.Version;
        lastTransformVersion = casters.TransformVersion;
        initialized = true;
    }

    /// <summary>Diagnostics: the cull mask a cascade uses (the smoke prints the map).</summary>
    public static uint CullMaskFor(int cascade) => MaskFor(cascade);

    /// <summary>
    /// Which proxy layers a cascade draws. The nearest cascades include every caster; far ones drop
    /// small ADT doodads first, then WMO doodads, where the texel is already too coarse to show them.
    /// Cascade 2 drops layer 20 (ADT doodads and M2 GameObjects); cascade 3 and later also drop
    /// layer 19 (WMO doodads and creatures).
    /// </summary>
    static uint MaskFor(int cascade) => cascade switch
    {
        0 or 1 => ShadowCasterScene.ShadowLayerMask,
        2 => ShadowCasterScene.ShadowLayerMask & ~ShadowCasterScene.LayerFor(ShadowCasterCategory.AdtDoodad),
        _ => ShadowCasterScene.ShadowLayerMask & ~ShadowCasterScene.LayerFor(ShadowCasterCategory.AdtDoodad) & ~ShadowCasterScene.LayerFor(ShadowCasterCategory.WmoDoodad),
    };

    void WriteCascade(int index, in ShadowCascadeFit fit)
    {
        int at = index * ClientShaderGlobals.ShadowCascadeTexels;
        cascadeImage.SetPixel(at, 0, ToColor(fit.UvRowX));
        cascadeImage.SetPixel(at + 1, 0, ToColor(fit.UvRowY));
        cascadeImage.SetPixel(at + 2, 0, ToColor(fit.SplitAndTexel));
        cascadeImage.SetPixel(at + 3, 0, ToColor(fit.FadeBounds));
    }

    static Color ToColor(System.Numerics.Vector4 value) => new(value.X, value.Y, value.Z, value.W);

    void Publish(int count)
    {
        if (published == count && count == 0)
        {
            return;
        }

        published = count;
        ClientShaderGlobals.Set(ClientShaderGlobals.ShadowCascadeCount, (float)count);
    }

    void PublishStrength(float strength)
    {
        ClientShaderGlobals.Set(ClientShaderGlobals.ShadowStrength, strength);
        lastStrength = strength;
    }

    void EnsureViewports(int count)
    {
        for (int i = 0; i < viewports.Length; i++)
        {
            bool want = i < count;
            if (want && viewports[i] == null)
            {
                CreateViewport(i);
            }
            else if (!want && viewports[i] != null)
            {
                DestroyViewport(i);
            }
        }

        allocated = count;
        Publish(0);
    }

    void CreateViewport(int index)
    {
        var viewport = new SubViewport
        {
            Name = $"ShadowCascade{index}",
            Size = new Vector2I(resolution, resolution),
            World3D = world,
            OwnWorld3D = false,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled,
            RenderTargetClearMode = SubViewport.ClearMode.Always,
            UseHdr2D = true,
            Msaa3D = Viewport.Msaa.Disabled,
            ScreenSpaceAA = Viewport.ScreenSpaceAAEnum.Disabled,
            UseDebanding = false,
            TransparentBg = false,
        };
        var caster = new Camera3D
        {
            Name = "Caster",
            Projection = Camera3D.ProjectionType.Orthogonal,
            Environment = casterEnvironment,
            CullMask = ShadowCasterScene.ShadowLayerMask,
            Current = true,
        };
        viewport.AddChild(caster);
        Root.AddChild(viewport);
        RenderingServer.ViewportSetMeasureRenderTime(viewport.GetViewportRid(), true);
        viewports[index] = viewport;
        cameras[index] = caster;
        // The ViewportTexture follows this viewport for as long as it exists (a size change reuses
        // the same viewport), so the global is bound once instead of every redraw.
        ClientShaderGlobals.Set(ClientShaderGlobals.ShadowMap(index), viewport.GetTexture());
    }

    void DestroyViewport(int index)
    {
        if (viewports[index] is { } viewport)
        {
            viewports[index] = null;
            cameras[index] = null;
            // The receiver gates on the published count, but do not leave a freed viewport bound.
            ClientShaderGlobals.Set(ClientShaderGlobals.ShadowMap(index), default);
            // Immediate, so a quality change can rebuild the slot in the same frame.
            viewport.Free();
        }
    }
}
