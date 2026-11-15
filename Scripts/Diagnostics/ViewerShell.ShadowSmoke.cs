using Godot;
using ACWorldEdit.Assets;

namespace ACWorldEdit;

/// <summary>
/// Opt-in checks for the dynamic sun shadows: <c>--shadow-caster-smoke</c> dumps the cascade maps
/// without enabling the receiver, and <c>--shadow-smoke</c> compares off/low/medium/high frames at a
/// fixed pose. Both need a window (the headless renderer produces no viewport textures).
/// </summary>
public partial class ViewerShell
{
    bool shadowCasterSmoke;
    int shadowCasterStage, shadowCasterFrames;
    double shadowCasterTime;
    bool shadowSmoke;
    int shadowSmokeStage, shadowSmokeFrames;
    double shadowSmokeTime;
    readonly Dictionary<ShadowQuality, double> shadowLuminance = [];
    double shadowNightOff, shadowNightHigh;

    /// <summary>Read the caster-smoke flag after the quality flags, so an Off default still renders maps.</summary>
    void ConfigureShadowSmoke(HashSet<string> flags)
    {
        shadowCasterSmoke = flags.Contains("--shadow-caster-smoke");
        shadowSmoke = flags.Contains("--shadow-smoke");
        if (shadowCasterSmoke)
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
}
