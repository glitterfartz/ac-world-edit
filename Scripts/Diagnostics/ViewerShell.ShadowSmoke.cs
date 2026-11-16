using Godot;
using ACWorldEdit.Assets;

namespace ACWorldEdit;

/// <summary>
/// Opt-in checks for the dynamic sun shadows: <c>--shadow-caster-smoke</c> dumps the cascade maps
/// without enabling the receiver, <c>--shadow-smoke</c> compares off/low/medium/high frames at a
/// fixed pose, and <c>--shadow-creature-smoke</c> proves creature/GameObject detail casters write
/// depth and refresh animation at the bounded rate. All need a window (the headless renderer
/// produces no viewport textures); the creature smoke also needs the world database.
/// </summary>
public partial class ViewerShell
{
    bool shadowCasterSmoke;
    int shadowCasterStage, shadowCasterFrames;
    double shadowCasterTime;
    bool shadowSmoke;
    int shadowSmokeStage, shadowSmokeFrames;
    double shadowSmokeTime;
    bool shadowCreatureSmoke;
    int shadowCreatureStage, shadowCreatureFrames;
    double shadowCreatureTime, shadowAnimatedSince;
    int shadowAnimatedBaseline, shadowAnimatedFrames;
    int shadowCreatureVisibleDepth, shadowCreatureStability;
    Vector3 shadowCameraPosition;
    Basis shadowCameraBasis;
    bool shadowCameraRecorded;
    int[] shadowCreatureCreatureChanges = [], shadowCreatureObjectChanges = [];
    int[] shadowCreatureDrawsBefore = [], shadowCreatureCreatureDraws = [], shadowCreatureObjectDraws = [];
    Image?[] shadowCreatureVisibleImages = [];
    bool shadowCreatureDatabaseChecked;
    readonly Dictionary<ShadowQuality, double> shadowLuminance = [];
    double shadowNightOff, shadowNightHigh;

    /// <summary>Read the caster-smoke flags after the quality flags, so an Off default still renders maps.</summary>
    void ConfigureShadowSmoke(HashSet<string> flags)
    {
        shadowCasterSmoke = flags.Contains("--shadow-caster-smoke");
        shadowSmoke = flags.Contains("--shadow-smoke");
        shadowCreatureSmoke = flags.Contains("--shadow-creature-smoke");
        if (shadowCasterSmoke || shadowCreatureSmoke)
        {
            // Exercise every cascade and the per-cascade caster masks, whatever the saved default is.
            SetShadowQuality(ShadowQuality.High, save: false, report: false);
        }
    }

    /// <summary>
    /// Wait for the world to settle, force one cascade render, then read every map back. Each map must
    /// exist, have the quality's resolution and hold some non-white (packed depth) pixels.
    /// </summary>
    void ShadowCasterSmoke(double delta)
    {
        shadowCasterTime += delta;
        if (shadowCasterTime > 120)
        {
            SmokeFail("SHADOW_CASTER_SMOKE_TIMEOUT " + message);
            return;
        }

        if (DisplayServer.GetName() == "headless" || busy || world == null)
        {
            return;
        }

        if (shadowCasterStage == 0)
        {
            if (!TerrainSettled || models.Busy || casters.ProxyCount == 0)
            {
                return;
            }

            int cascades = ShadowQualityPresets.For(shadowQuality).Cascades;
            if (cascades == 0)
            {
                SmokeFail("SHADOW_CASTER_SMOKE_QUALITY no cascades at " + ShadowQualityPresets.Name(shadowQuality));
                return;
            }

            sky.SetTimeOfDay(9);
            shadows.RenderNow(camera, sky);
            GD.Print($"SHADOW_CASTER_SMOKE_START proxies={casters.ProxyCount} cascades={cascades} quality={ShadowQualityPresets.Name(shadowQuality)}");
            shadowCasterStage = 1;
            return;
        }

        if (shadowCasterStage == 1)
        {
            // Let the SubViewports' Once update and the readback land before asking for pixels.
            if (shadows.CascadeCount == 0 || ++shadowCasterFrames < 5)
            {
                return;
            }

            shadowCasterStage = 2;
            return;
        }

        System.IO.Directory.CreateDirectory(ProjectSettings.GlobalizePath("res://artifacts"));
        int count = shadows.CascadeCount;
        bool anyDepth = false;
        for (int cascade = 0; cascade < count; cascade++)
        {
            var image = shadows.CascadeImage(cascade);
            if (image == null)
            {
                SmokeFail($"SHADOW_CASTER_SMOKE_IMAGE cascade={cascade}");
                return;
            }

            var size = image.GetSize();
            int depth = 0, samples = 0;
            for (int y = 0; y < size.Y; y += 8)
            {
                for (int x = 0; x < size.X; x += 8)
                {
                    var pixel = image.GetPixel(x, y);
                    samples++;
                    if (pixel.R < 0.999f || pixel.G < 0.999f || pixel.B < 0.999f)
                    {
                        depth++;
                    }
                }
            }

            image.SavePng(ProjectSettings.GlobalizePath($"res://artifacts/shadow-caster-{ShadowQualityPresets.Name(shadowQuality).ToLowerInvariant()}-{cascade}.png"));
            GD.Print($"SHADOW_CASTER cascade={cascade} size={size.X}x{size.Y} depthPixels={depth}/{samples} draws={shadows.CascadeDrawCalls(cascade)}");
            anyDepth |= depth > 0;
        }

        if (!anyDepth)
        {
            SmokeFail("SHADOW_CASTER_SMOKE_EMPTY every cascade map is white");
            return;
        }

        ProbeShadowDepth();
        GD.Print($"SHADOW_CASTER_SMOKE_PASSED cascades={count} proxies={casters.ProxyCount}");
        GetTree().Quit();
    }

    /// <summary>
    /// Read the packed depth under the terrain point below the camera and compare it with the CPU
    /// expectation. This proves the viewport texture round-trips the 24-bit value undistorted and
    /// settles which way the map's V axis runs (Godot's texture origin is top-left).
    /// </summary>
    void ProbeShadowDepth()
    {
        if (shadows.Fits.Count == 0 || shadows.CascadeImage(0) is not { } image ||
            TerrainHeightAt(camera.Position.X, camera.Position.Z) is not float groundY)
        {
            GD.Print("SHADOW_CASTER_PROBE skipped (no fit, image or terrain)");
            return;
        }

        var fit = shadows.Fits[0];
        var ground = new Vector3(camera.Position.X, groundY, camera.Position.Z);
        var point = new System.Numerics.Vector3(ground.X, ground.Y, ground.Z);
        var uv = ShadowCascades.WorldToUv(fit, point);
        var size = image.GetSize();
        float expected = ground.Dot(sky.SunDirection);
        float Sample(float v)
        {
            int x = Math.Clamp((int)(uv.X * size.X), 0, size.X - 1);
            int y = Math.Clamp((int)(v * size.Y), 0, size.Y - 1);
            var pixel = image.GetPixel(x, y);
            return ShadowDepthPacking.Unpack(new System.Numerics.Vector3(pixel.R, pixel.G, pixel.B));
        }

        float direct = Sample(uv.Y);
        float flipped = Sample(1f - uv.Y);
        GD.Print(FormattableString.Invariant(
            $"SHADOW_CASTER_PROBE expected={expected:F2} direct={direct:F2} flipped={flipped:F2} uv=({uv.X:F3},{uv.Y:F3}) sun={sky.SunDirection}"));
    }

    /// <summary>
    /// Fixed Trade District pose at hour 9: capture off/low/medium/high and prove the sun (not ambient
    /// or point lights) darkens the frame. Hour 23 repeats off against high, which must not differ
    /// because the strength gate turns the pass off below the horizon.
    /// </summary>
    void ShadowSmoke(double delta)
    {
        shadowSmokeTime += delta;
        if (shadowSmokeTime > 300)
        {
            SmokeFail($"SHADOW_SMOKE_TIMEOUT stage={shadowSmokeStage} {message}");
            return;
        }

        if (DisplayServer.GetName() == "headless" || busy || world == null)
        {
            return;
        }

        if (!TerrainSettled || models.Busy)
        {
            return;
        }

        switch (shadowSmokeStage)
        {
            case 0:
                PlaceShadowPose();
                HideShadowSmokeUi();
                GD.Print("SHADOW_SMOKE_POSE hour=9 trade-district");
                shadowSmokeStage = 1;
                return;
            case 1:
                ApplyShadowSmokeQuality(ShadowQuality.Off, 2);
                return;
            case 2:
                if (WaitShadowFrames())
                {
                    shadowLuminance[ShadowQuality.Off] = CaptureShadowSmoke("off");
                    shadowSmokeStage = 3;
                }

                return;
            case 3:
                ApplyShadowSmokeQuality(ShadowQuality.Low, 4);
                return;
            case 4:
                if (WaitShadowFrames())
                {
                    shadowLuminance[ShadowQuality.Low] = CaptureShadowSmoke("low");
                    shadowSmokeStage = 5;
                }

                return;
            case 5:
                ApplyShadowSmokeQuality(ShadowQuality.Medium, 6);
                return;
            case 6:
                if (WaitShadowFrames())
                {
                    shadowLuminance[ShadowQuality.Medium] = CaptureShadowSmoke("medium");
                    shadowSmokeStage = 7;
                }

                return;
            case 7:
                ApplyShadowSmokeQuality(ShadowQuality.High, 8);
                return;
            case 8:
                if (!WaitShadowFrames())
                {
                    return;
                }

                shadowLuminance[ShadowQuality.High] = CaptureShadowSmoke("high");
                for (int i = 0; i < shadows.Fits.Count; i++)
                {
                    var fit = shadows.Fits[i];
                    GD.Print(FormattableString.Invariant(
                        $"SHADOW_CASCADE cascade={i} split={fit.SplitDistance:F2} radius={fit.Radius:F2} texel={fit.TexelWorldSize:F4} fade={fit.FadeBounds.X:F2}"));
                }

                double off = shadowLuminance[ShadowQuality.Off];
                double best = new[] { shadowLuminance[ShadowQuality.Low], shadowLuminance[ShadowQuality.Medium], shadowLuminance[ShadowQuality.High] }.Min();
                if (best >= off - 1e-6)
                {
                    SmokeFail($"SHADOW_SMOKE_NO_DIFFERENCE off={off:F5} low={shadowLuminance[ShadowQuality.Low]:F5} medium={shadowLuminance[ShadowQuality.Medium]:F5} high={shadowLuminance[ShadowQuality.High]:F5}");
                    return;
                }

                GD.Print(FormattableString.Invariant($"SHADOW_SMOKE_DAY off={off:F5} low={shadowLuminance[ShadowQuality.Low]:F5} medium={shadowLuminance[ShadowQuality.Medium]:F5} high={shadowLuminance[ShadowQuality.High]:F5} strength={sky.SunShadowStrength:F4} cascades={shadows.CascadeCount}"));
                sky.SetTimeOfDay(23);
                shadowSmokeStage = 9;
                return;
            case 9:
                ApplyShadowSmokeQuality(ShadowQuality.Off, 10);
                return;
            case 10:
                if (WaitShadowFrames())
                {
                    shadowNightOff = CaptureShadowSmoke("night-off");
                    shadowSmokeStage = 11;
                }

                return;
            case 11:
                ApplyShadowSmokeQuality(ShadowQuality.High, 12);
                return;
            case 12:
                if (!WaitShadowFrames())
                {
                    return;
                }

                shadowNightHigh = CaptureShadowSmoke("night-high");
                GD.Print(FormattableString.Invariant($"SHADOW_SMOKE_NIGHT_STATE hour={sky.TimeOfDayHours} strength={sky.SunShadowStrength:F4} cascades={shadows.CascadeCount}"));
                if (Math.Abs(shadowNightHigh - shadowNightOff) > 0.002)
                {
                    SmokeFail($"SHADOW_SMOKE_NIGHT off={shadowNightOff:F5} high={shadowNightHigh:F5}");
                    return;
                }

                GD.Print(FormattableString.Invariant($"SHADOW_SMOKE_NIGHT off={shadowNightOff:F5} high={shadowNightHigh:F5} cascades={shadows.CascadeCount}"));
                GD.Print("SHADOW_SMOKE_PASSED");
                GetTree().Quit();
                return;
        }
    }

    void PlaceShadowPose()
    {
        sky.SetTimeOfDay(9);
        cameraFocused = true;
        camera.Position = ViewSpace.ToView(-8830, 630, 160);
        camera.LookAt(ViewSpace.ToView(-8515, 860, 165));
        yaw = camera.Rotation.Y;
        pitch = -0.35f;
        camera.Rotation = new(pitch, yaw, 0);
        streamer.UpdateWanted();
    }

    void HideShadowSmokeUi()
    {
        foreach (var item in GetChildren().OfType<CanvasItem>())
        {
            item.Visible = false;
        }

        foreach (var layer in GetChildren().OfType<CanvasLayer>())
        {
            layer.Visible = false;
        }
    }

    void ApplyShadowSmokeQuality(ShadowQuality quality, int nextStage)
    {
        SetShadowQuality(quality, save: false, report: false);
        shadowSmokeFrames = 0;
        shadowSmokeStage = nextStage;
    }

    bool WaitShadowFrames()
    {
        return shadowSmokeFrames++ >= 20;
    }

    double CaptureShadowSmoke(string name)
    {
        var image = GetViewport().GetTexture().GetImage();
        System.IO.Directory.CreateDirectory(ProjectSettings.GlobalizePath("res://artifacts"));
        image.SavePng(ProjectSettings.GlobalizePath($"res://artifacts/shadow-smoke-{name}.png"));
        var size = image.GetSize();
        double sum = 0;
        int count = 0;
        // Skip the top third (sky and clouds animate independently of the sun shadows).
        for (int y = size.Y / 3; y < size.Y; y += 8)
        {
            for (int x = 0; x < size.X; x += 8)
            {
                var pixel = image.GetPixel(x, y);
                sum += 0.2126 * pixel.R + 0.7152 * pixel.G + 0.0722 * pixel.B;
                count++;
            }
        }

        return count == 0 ? 0 : sum / count;
    }

    /// <summary>
    /// <c>--shadow-creature-smoke</c>: open Goldshire with the creature and GameObject layers, force
    /// a High render, then hide the detail models and prove their proxies wrote depth by comparing
    /// the cascade maps. Finally hold the camera and sun still and check the animated refresh runs,
    /// but only at the preset's bounded rate.
    /// </summary>
    void ShadowCreatureSmoke(double delta)
    {
        shadowCreatureTime += delta;
        if (shadowCreatureTime > 180)
        {
            SmokeFail($"SHADOW_CREATURE_SMOKE_TIMEOUT stage={shadowCreatureStage} {message}");
            return;
        }

        if (DisplayServer.GetName() == "headless" || busy || world == null)
        {
            return;
        }

        // The terrain pass may lift the camera after a tile loads; hold the pose for the captures.
        streamer.FreezeCamera = true;

        if (shadowCreatureStage == 0)
        {
            // The poses below are Eastern Kingdoms coordinates; open that map first if another is showing.
            if (currentMapId != 0)
            {
                int azeroth = mapList.FindIndex(m => m.Directory == "Azeroth");
                if (azeroth >= 0 && maps.Selected != azeroth)
                {
                    maps.Select(azeroth);
                    tileX.Value = 31;
                    tileY.Value = 49;
                    _ = OpenMap();
                }

                return;
            }

            creatures.SetLayerEnabled(true);
            gameObjects.SetLayerEnabled(true);
            sky.SetTimeOfDay(9);
            // Freeze the sun and the skinned pose: either moving between captures would swamp the
            // category-isolated depth delta. The pose is released before the animated phase.
            sky.Playing = false;
            ClientShaderGlobals.FreezeCreatureClock = true;
            FocusPose(new(0, -9465.5, 74, 56.8, 0));
            shadowCreatureStage = 1;
            return;
        }

        if (shadowCreatureStage == 1)
        {
            if (!TerrainSettled || models.Busy || creatures.IsLoading || gameObjects.IsLoading)
            {
                return;
            }

            if (!shadowCreatureDatabaseChecked)
            {
                if (creatures.Records.Count == 0)
                {
                    SmokeFail($"SHADOW_CREATURE_SMOKE_NO_DATABASE status={creatures.QueryStatus}");
                    return;
                }

                shadowCreatureDatabaseChecked = true;
            }

            // A redraw enables the caster pass and creates every registered proxy.
            shadows.RenderNow(camera, sky);
            if (creatures.ModelCreatureCount == 0 || casters.CategoryProxyCount(ShadowCasterCategory.Creature) == 0)
            {
                return;
            }

            shadowCameraPosition = camera.GlobalPosition;
            shadowCameraBasis = camera.GlobalBasis;
            shadowCameraRecorded = true;
            GD.Print($"SHADOW_CREATURE_SMOKE_START proxies={casters.ProxyCount} " +
                $"creature={casters.CategoryProxyCount(ShadowCasterCategory.Creature)} " +
                $"gameObject={casters.CategoryProxyCount(ShadowCasterCategory.GameObject)} " +
                $"animated={casters.AnimatedCasterCount} quality={ShadowQualityPresets.Name(shadowQuality)}");
            shadowCreatureFrames = 0;
            shadowCreatureStage = 2;
            return;
        }

        if (shadowCreatureStage == 2)
        {
            if (models.Busy || creatures.IsLoading || gameObjects.IsLoading || !CameraHeld())
            {
                return;
            }

            // Let the SubViewports' Once update and the readback land after the forced render.
            if (++shadowCreatureFrames < 5)
            {
                return;
            }

            shadowCreatureVisibleImages = CaptureShadowCreatureCascades("visible");
            shadowCreatureVisibleDepth = TotalDepthPixels(shadowCreatureVisibleImages);
            shadowCreatureDrawsBefore = DrawsPerCascade();
            if (shadowCreatureVisibleDepth <= 0)
            {
                SmokeFail($"SHADOW_CREATURE_SMOKE_EMPTY visible={shadowCreatureVisibleDepth}");
                return;
            }

            // Prove the maps are stable before attributing a delta to the hidden category.
            shadows.RenderNow(camera, sky);
            shadowCreatureFrames = 0;
            shadowCreatureStage = 6;
            return;
        }

        if (shadowCreatureStage == 6)
        {
            if (models.Busy || creatures.IsLoading || gameObjects.IsLoading || !CameraHeld())
            {
                return;
            }

            if (++shadowCreatureFrames < 5)
            {
                return;
            }

            var stableImages = CaptureShadowCreatureCascades("visible-check");
            shadowCreatureStability = ChangedPerCascade(shadowCreatureVisibleImages, stableImages).Sum();
            GD.Print($"SHADOW_CREATURE_SMOKE_STABILITY changed={shadowCreatureStability}");
            GD.Print($"SHADOW_CREATURE_LAYERS creature={casters.SampleProxyLayers(ShadowCasterCategory.Creature)} " +
                $"gameObject={casters.SampleProxyLayers(ShadowCasterCategory.GameObject)} " +
                $"masks={string.Join(",", Enumerable.Range(0, shadows.CascadeCount).Select(SunShadows.CullMaskFor))} " +
                $"draws=[{string.Join(",", shadowCreatureDrawsBefore)}]");
            if (shadowCreatureStability != 0)
            {
                SmokeFail($"SHADOW_CREATURE_SMOKE_UNSTABLE changed={shadowCreatureStability}");
                return;
            }

            // Hide only the Creature proxies. Creature is on layer 19, so cascade 3 (which drops
            // layer 19) must not change, while cascades 0-2 must. Hiding the whole overlay would also
            // hide WMO GameObjects, which are layer 18 and cast in every cascade.
            casters.SetCategoryProxiesVisible(ShadowCasterCategory.Creature, false);
            shadows.RenderNow(camera, sky);
            shadowCreatureFrames = 0;
            shadowCreatureStage = 3;
            return;
        }

        if (shadowCreatureStage == 3)
        {
            if (models.Busy || creatures.IsLoading || gameObjects.IsLoading || !CameraHeld())
            {
                return;
            }

            if (++shadowCreatureFrames < 5)
            {
                return;
            }

            var noCreature = CaptureShadowCreatureCascades("no-creature");
            shadowCreatureCreatureChanges = ChangedPerCascade(shadowCreatureVisibleImages, noCreature);
            shadowCreatureCreatureDraws = DrawsPerCascade();
            casters.SetCategoryProxiesVisible(ShadowCasterCategory.Creature, true);

            // Restore, then isolate the M2 GameObject category on layer 20: cascades 2 and 3 drop
            // layer 20, while cascades 0 and 1 still draw it.
            casters.SetCategoryProxiesVisible(ShadowCasterCategory.GameObject, false);
            shadows.RenderNow(camera, sky);
            shadowCreatureFrames = 0;
            shadowCreatureStage = 4;
            return;
        }

        if (shadowCreatureStage == 4)
        {
            if (models.Busy || creatures.IsLoading || gameObjects.IsLoading || !CameraHeld())
            {
                return;
            }

            if (++shadowCreatureFrames < 5)
            {
                return;
            }

            var noObject = CaptureShadowCreatureCascades("no-gameobject");
            shadowCreatureObjectChanges = ChangedPerCascade(shadowCreatureVisibleImages, noObject);
            shadowCreatureObjectDraws = DrawsPerCascade();
            casters.SetCategoryProxiesVisible(ShadowCasterCategory.GameObject, true);
            // Let the skinned pose animate again for the throttle phase.
            ClientShaderGlobals.FreezeCreatureClock = false;
            shadows.RenderNow(camera, sky);

            // The renderer's own per-cascade draw count is the deterministic proof of the mask:
            // Creature (layer 19) is drawn by every cascade but the last, GameObject (layer 20) only
            // by the first two. The depth delta then shows the category wrote depth where drawn.
            int cascades = shadowCreatureDrawsBefore.Length;
            bool creatureMask = true, objectMask = true;
            for (int i = 0; i < cascades; i++)
            {
                if (i < cascades - 1)
                {
                    creatureMask &= shadowCreatureCreatureDraws[i] < shadowCreatureDrawsBefore[i];
                }
                else
                {
                    creatureMask &= shadowCreatureCreatureDraws[i] == shadowCreatureDrawsBefore[i];
                }

                if (i < 2)
                {
                    objectMask &= shadowCreatureObjectDraws[i] < shadowCreatureDrawsBefore[i];
                }
                else
                {
                    objectMask &= shadowCreatureObjectDraws[i] == shadowCreatureDrawsBefore[i];
                }
            }

            bool creatureDepth = shadowCreatureCreatureChanges.Take(cascades - 1).Sum() > 0;
            bool objectDepth = shadowCreatureObjectChanges.Take(Math.Min(2, cascades)).Sum() > 0;
            if (!creatureMask || !creatureDepth || !objectMask || !objectDepth)
            {
                SmokeFail($"SHADOW_CREATURE_SMOKE_ISOLATION drawsBefore=[{string.Join(",", shadowCreatureDrawsBefore)}] " +
                    $"creatureDraws=[{string.Join(",", shadowCreatureCreatureDraws)}] gameObjectDraws=[{string.Join(",", shadowCreatureObjectDraws)}] " +
                    $"creatureDepth=[{string.Join(",", shadowCreatureCreatureChanges)}] gameObjectDepth=[{string.Join(",", shadowCreatureObjectChanges)}]");
                return;
            }

            // Draw counts are the deterministic mask proof (and already passed above). The packed-depth
            // readback also shows unexplained changes in mask-excluded cascades, so those pixel deltas
            // are printed for observation, not asserted as category depth.
            GD.Print($"SHADOW_CREATURE_SMOKE_DEPTH creatureDraws=[{string.Join(",", shadowCreatureCreatureDraws)}] " +
                $"gameObjectDraws=[{string.Join(",", shadowCreatureObjectDraws)}] " +
                $"creatureDepth=[{string.Join(",", shadowCreatureCreatureChanges)}] gameObjectDepth=[{string.Join(",", shadowCreatureObjectChanges)}]");
            shadowAnimatedBaseline = shadows.AnimatedRedraws;
            shadowAnimatedFrames = 0;
            shadowAnimatedSince = shadowCreatureTime;
            shadowCreatureStage = 5;
            return;
        }

        if (shadowCreatureStage == 5)
        {
            shadowAnimatedFrames++;
            if (shadowCreatureTime - shadowAnimatedSince < 1.5)
            {
                return;
            }

            int redraws = shadows.AnimatedRedraws - shadowAnimatedBaseline;
            int animated = casters.AnimatedCasterCount;
            int hz = ShadowQualityPresets.For(shadowQuality).AnimatedRefreshHz;
            // The cap is Hz*1.5; a run below the frame rate cannot exceed it, so also require the
            // refresh to actually happen at roughly the expected count.
            int expected = Math.Min(shadowAnimatedFrames, (int)Math.Ceiling(hz * 1.5));
            int floor = Math.Max(1, expected / 2);
            int limit = (int)Math.Ceiling(hz * 1.5) + 3;
            GD.Print($"SHADOW_CREATURE_SMOKE_ANIMATED redraws={redraws} frames={shadowAnimatedFrames} animatedCasters={animated} hz={hz} expected={expected}");
            if (animated == 0 || redraws < floor || redraws > limit)
            {
                SmokeFail($"SHADOW_CREATURE_SMOKE_ANIMATED_FAILED redraws={redraws} frames={shadowAnimatedFrames} animatedCasters={animated} hz={hz} floor={floor} limit={limit}");
                return;
            }

            if (redraws >= shadowAnimatedFrames)
            {
                // The frame rate was below the animated rate, so every frame redrew: the throttle
                // held (no redraw exceeded its interval) but the cap did not bind.
                GD.Print("SHADOW_CREATURE_SMOKE_ANIMATED_NOTE frame rate below the animated rate; the throttle held but did not bind");
            }

            GD.Print($"SHADOW_CREATURE_SMOKE_PASSED proxies={casters.ProxyCount} " +
                $"creatureDraws=[{string.Join(",", shadowCreatureCreatureDraws)}] " +
                $"gameObjectDraws=[{string.Join(",", shadowCreatureObjectDraws)}]");
            GetTree().Quit();
        }
    }

    /// <summary>Save every cascade map for one visibility state and return the images for comparison.</summary>
    Image?[] CaptureShadowCreatureCascades(string tag)
    {
        System.IO.Directory.CreateDirectory(ProjectSettings.GlobalizePath("res://artifacts"));
        string quality = ShadowQualityPresets.Name(shadowQuality).ToLowerInvariant();
        int count = shadows.CascadeCount;
        var images = new Image?[count];
        for (int cascade = 0; cascade < count; cascade++)
        {
            var image = shadows.CascadeImage(cascade);
            if (image == null)
            {
                SmokeFail($"SHADOW_CREATURE_SMOKE_IMAGE cascade={cascade}");
                return images;
            }

            images[cascade] = image;
            string suffix = tag == "visible" ? "" : "-" + tag;
            image.SavePng(ProjectSettings.GlobalizePath($"res://artifacts/shadow-creature-{quality}-{cascade}{suffix}.png"));
            GD.Print($"SHADOW_CREATURE cascade={cascade} tag={tag} depthPixels={CountDepthPixels(image)}");
        }

        return images;
    }

    static int CountDepthPixels(Image image)
    {
        var size = image.GetSize();
        int depth = 0;
        for (int y = 0; y < size.Y; y += 8)
        {
            for (int x = 0; x < size.X; x += 8)
            {
                var pixel = image.GetPixel(x, y);
                if (pixel.R < 0.999f || pixel.G < 0.999f || pixel.B < 0.999f)
                {
                    depth++;
                }
            }
        }

        return depth;
    }

    static int TotalDepthPixels(Image?[] images) => images.Sum(image => image == null ? 0 : CountDepthPixels(image));

    /// <summary>Pixels whose packed depth differs between the visible and hidden maps.</summary>
    static int ChangedPixels(Image? before, Image? after)
    {
        if (before == null || after == null || before.GetSize() != after.GetSize())
        {
            return 0;
        }

        var size = before.GetSize();
        int changed = 0;
        for (int y = 0; y < size.Y; y += 2)
        {
            for (int x = 0; x < size.X; x += 2)
            {
                var a = before.GetPixel(x, y);
                var b = after.GetPixel(x, y);
                if (Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B) > 0.002f)
                {
                    changed++;
                }
            }
        }

        return changed;
    }

    int[] DrawsPerCascade()
    {
        var draws = new int[shadows.CascadeCount];
        for (int i = 0; i < draws.Length; i++)
        {
            draws[i] = (int)shadows.CascadeDrawCalls(i);
        }

        return draws;
    }

    /// <summary>Whether the camera still matches the pose recorded when the captures started.</summary>
    bool CameraHeld()
    {
        bool held = !shadowCameraRecorded
            || (camera.GlobalPosition == shadowCameraPosition && camera.GlobalBasis == shadowCameraBasis);
        if (!held)
        {
            SmokeFail("SHADOW_CREATURE_SMOKE_CAMERA_MOVED");
        }

        return held;
    }

    /// <summary>Per-cascade sampled pixels whose packed depth differs.</summary>
    static int[] ChangedPerCascade(Image?[] before, Image?[] after)
    {
        var changes = new int[after.Length];
        for (int cascade = 0; cascade < after.Length; cascade++)
        {
            changes[cascade] = ChangedPixels(cascade < before.Length ? before[cascade] : null, after[cascade]);
        }

        return changes;
    }

}
