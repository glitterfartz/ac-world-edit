using Godot;
using ACWorldEdit.Assets;

namespace ACWorldEdit;

/// <summary>
/// Drives the sun, ambient/fog color, the sky dome shader and LightSkybox model from client
/// Light.dbc data and a scrubbable time-of-day clock. Falls back to a fixed neutral look when no
/// client lighting table is available or the active map is an interior. Direction/shadow tuning is
/// an approximation of the client look, not a reconstruction of its exact sun trajectory or portal
/// -based interior light isolation.
/// </summary>
public sealed partial class SkyLighting : Node3D
{
    readonly DirectionalLight3D sun;
    readonly WorldEnvironment environment;
    readonly ShaderMaterial skyMaterial;
    Color skyTopColor = new("4a7fc4"), skyHorizonColor = new("c0d2e5");
    bool hasSunTexture, hasMoonTexture, hasMoon2Texture;
    readonly ModelLibrary models;
    readonly Node3D skyboxAnchor;
    ModelInstance? skyboxInstance;
    string? skyboxPath;
    ClientLighting? table;
    LightRegion[] mapRegions = [];
    uint? mapId;
    bool interior;

    public double TimeOfDayHours { get; private set; } = 9;
    public bool Playing { get; set; } = false;
    public float Speed { get; set; } = 1;

    public string? SkyboxPath => skyboxPath;
    public bool SkyboxReady => skyboxInstance != null && GodotObject.IsInstanceValid(skyboxInstance) && skyboxInstance.ModelReady;
    public Aabb? SkyboxBounds => SkyboxReady ? skyboxInstance!.Bounds : null;

    /// <summary>
    /// Hide or show the skybox model without changing the resolved path; used by the skybox smoke
    /// to compare frames with and without it.
    /// </summary>
    public bool SkyboxVisible
    {
        get => skyboxAnchor.Visible;
        set => skyboxAnchor.Visible = value;
    }

    /// <summary>Manually scrub the clock; wraps to the 0-24 range.</summary>
    public void SetTimeOfDay(double hours) => TimeOfDayHours = ((hours % 24) + 24) % 24;

    public SkyLighting(ModelLibrary models)
    {
        this.models = models;
        sun = new DirectionalLight3D
        {
            RotationDegrees = new Vector3(-50, -30, 0),
            LightEnergy = 1.25f,
            ShadowEnabled = true,
            DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel4Splits,
            DirectionalShadowMaxDistance = 400,
            DirectionalShadowBlendSplits = true
        };
        AddChild(sun);
        skyMaterial = new ShaderMaterial
        {
            Shader = GD.Load<Shader>("res://Shaders/sky.gdshader")
        };
        environment = new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new Color("91b5c9"),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color("c8d7e5"),
                AmbientLightEnergy = 0.65f,
                TonemapMode = Godot.Environment.ToneMapper.Filmic
            }
        };
        AddChild(environment);
        skyboxAnchor = new Node3D
        {
            Name = "Skybox"
        };
        AddChild(skyboxAnchor);
    }

    /// <summary>
    /// Switch the active map's lighting source. Null table or interior maps keep the fixed neutral
    /// look rather than guessing at portal-gated interior light.
    /// </summary>
    public void SetMap(ClientLighting? lightTable, uint? activeMapId, bool isInterior)
    {
        ViewDistance = Retail ? RetailViewDistance : ClassicViewDistance;
        table = lightTable;
        mapId = activeMapId;
        interior = isInterior;
        mapRegions = table != null && activeMapId != null ? table.ForMap(activeMapId.Value).ToArray() : [];
        if (interior || table == null)
        {
            environment.Environment.BackgroundMode = Godot.Environment.BGMode.Color;
            environment.Environment.BackgroundColor = interior ? new Color("080a0d") : new Color("91b5c9");
            environment.Environment.FogEnabled = false;
            ClientShaderGlobals.Set(ClientShaderGlobals.Fog, new Vector4(0, 1, 1, 0));
            SetSkybox(null);
        }
    }

    /// <summary>
    /// Advance the clock, resolve the applicable region(s) by falloff-weighted distance and apply
    /// interpolated bands to the sun, ambient/fog and sky gradient. No-op when interior or unlit.
    /// </summary>
    public void Update(Camera3D camera, double delta)
    {
        if (Playing)
        {
            TimeOfDayHours = (TimeOfDayHours + delta / 60.0 * Speed) % 24.0;
        }

        if (interior || table == null || mapId == null)
        {
            return;
        }

        LightRegion? background = null;
        LightRegion? nearestAny = null;
        float nearestDist = float.MaxValue;
        LightRegion? best = null;
        float bestWeight = 0;
        float bestFalloffEnd = float.MaxValue;
        foreach (var region in mapRegions)
        {
            if (region.IsDefault)
            {
                background ??= region;
                continue;
            }

            float dist = new Vector2(camera.Position.X - region.ViewPosition.X, camera.Position.Z - region.ViewPosition.Z).Length();
            if (dist < nearestDist)
            {
                nearestDist = dist;
                nearestAny = region;
            }

            float span = Math.Max(1f, region.FalloffEnd - region.FalloffStart);
            float weight = dist <= region.FalloffStart ? 1f : dist >= region.FalloffEnd ? 0f : (region.FalloffEnd - dist) / span;
            if (weight > 0 && region.FalloffEnd < bestFalloffEnd)
            {
                best = region;
                bestWeight = weight;
                bestFalloffEnd = region.FalloffEnd;
            }
        }

        background ??= nearestAny;
        var backgroundProfile = background != null ? table.Profile(background.ParamsId) : null;
        if (backgroundProfile == null)
        {
            return;
        }

        var foregroundProfile = best != null ? table.Profile(best.ParamsId) : null;
        double tick = TimeOfDayHours * 120.0;
        Color Blend(Func<LightProfile, LightBand> pick)
        {
            var bg = pick(backgroundProfile).At(tick);
            return foregroundProfile != null && bestWeight > 0 ? bg.Lerp(pick(foregroundProfile).At(tick), bestWeight) : bg;
        }

        var diffuse = Blend(p => p.Diffuse);
        var ambient = Blend(p => p.Ambient);
        var fog = Blend(p => p.Fog);
        double angle = TimeOfDayHours / 24.0 * Math.Tau;
        sun.RotationDegrees = new Vector3((float)(-55 + 35 * Math.Cos(angle)), -30, 0);
        sun.LightColor = diffuse;
        float luminance = 0.299f * diffuse.R + 0.587f * diffuse.G + 0.114f * diffuse.B;
        sun.LightEnergy = Mathf.Clamp(Mathf.Lerp(0.15f, 1.8f, luminance), 0.15f, 1.8f);
        environment.Environment.AmbientLightColor = ambient;
        environment.Environment.AmbientLightEnergy = 1.0f;
        environment.Environment.BackgroundMode = Godot.Environment.BGMode.Sky;
        environment.Environment.Sky ??= new Sky
        {
            SkyMaterial = skyMaterial
        };
        ApplySky(backgroundProfile, foregroundProfile, bestWeight, tick, camera);
        // The client's fog (DayNight::UpdateFog): it ramps from end * startMul to end, with the end capped at the
        // view distance. The shaders apply it (client_lighting.gdshaderinc), so Godot's own fog stays off.
        var backgroundFog = backgroundProfile.FogAt(tick);
        var foregroundFog = foregroundProfile?.FogAt(tick);
        (float End, float StartMul)? curve = backgroundFog is { } b && foregroundFog is { } f && bestWeight > 0
            ? (Mathf.Lerp(b.End, f.End, bestWeight), Mathf.Lerp(b.StartMul, f.StartMul, bestWeight))
            : backgroundFog ?? foregroundFog;
        float fogEnd = Math.Min(ViewDistance, curve?.End ?? ViewDistance);
        // A retail light with no fog distance (the newer lights) starts its fog at the eye.
        float fogStart = fogEnd * (curve?.StartMul ?? (Retail ? 0f : DefaultFogStartMultiplier));
        environment.Environment.FogEnabled = false;
        SetClientFog(fogStart, fogEnd, fog, RetailFogTerms(backgroundProfile, foregroundProfile, bestWeight, tick));
        ApplyWater(backgroundProfile, foregroundProfile, bestWeight, tick);
        ApplySunUniforms(diffuse, ambient, backgroundProfile, foregroundProfile, bestWeight, tick);
        uint activeParamsId = best != null && bestWeight >= 0.999f ? best.ParamsId : background!.ParamsId;
        SetSkybox(table.SkyboxPath(table.Profile(activeParamsId)?.SkyboxId ?? 0));
        skyboxAnchor.Position = camera.Position;
        ApplyFlat();
        ApplyDiagnosticBackground();
    }

    /// <summary>
    /// True for a retail client: its fog is exponential, with a wider view distance.
    /// </summary>
    public bool Retail { get; set; }
    /// <summary>
    /// The distance the fog is capped at, standing in for the client's view distance setting (the 3.3.5a
    /// "farclip" runs from 177 to 1277 yards; retail reaches several thousand). The camera here has no such
    /// limit, so this only shapes the fog: it ends at the smaller of this and the light's own fog end.
    /// </summary>
    public float ViewDistance { get; set; } = ClassicViewDistance;
    const float ClassicViewDistance = 777;
    const float RetailViewDistance = 2500;
    /// <summary>
    /// Fog start as a fraction of its end when a light has no fog data (DayNight DEFAULT_FOG_SCALER).
    /// </summary>
    const float DefaultFogStartMultiplier = 0.5f;
    Vector4 clientFog = new(0, 1, 1, 0);
    /// <summary>The fog values the shaders last received: start, end, density and mode.</summary>
    public Vector4 ClientFogValues => clientFog;
    /// <summary>The retail height fog values the shaders last received (plane, scaler, density, enabled).</summary>
    public Vector4 ClientFogHeightValues { get; private set; }

    /// <summary>
    /// Retail's fog terms beyond the 3.3.5a ones, at one tick: density, the end-fog color and distance, and the
    /// height layer.
    /// </summary>
    readonly record struct RetailFogValues(float Density, Color EndColor, float EndDistance, Vector4 Height, Vector4 Poly, Color HeightColor, Color HeightEndColor, Vector4 Sun)
    {
        public static RetailFogValues None { get; } = new(1, Colors.Black, 0, Vector4.Zero, Vector4.Zero, Colors.Black, Colors.Black, Vector4.Zero);
    }

    void SetClientFog(float start, float end, Color color, RetailFogValues terms)
    {
        clientFog = new(start, end, Retail ? terms.Density : 1, Retail ? 2 : 1);
        ClientShaderGlobals.Set(ClientShaderGlobals.Fog, clientFog);
        ClientShaderGlobals.Set(ClientShaderGlobals.FogColor, new Vector3(color.R, color.G, color.B));
        ClientShaderGlobals.Set(ClientShaderGlobals.FogEnd, new Vector4(terms.EndColor.R, terms.EndColor.G, terms.EndColor.B, terms.EndDistance));
        ClientShaderGlobals.Set(ClientShaderGlobals.FogHeight, terms.Height);
        ClientFogHeightValues = terms.Height;
        ClientShaderGlobals.Set(ClientShaderGlobals.FogPoly, terms.Poly);
        ClientShaderGlobals.Set(ClientShaderGlobals.FogHeightColor, new Vector4(terms.HeightColor.R, terms.HeightColor.G, terms.HeightColor.B, 0));
        ClientShaderGlobals.Set(ClientShaderGlobals.FogSun, terms.Sun);
        ClientShaderGlobals.Set(ClientShaderGlobals.FogHeightEndColor, new Vector4(terms.HeightEndColor.R, terms.HeightEndColor.G, terms.HeightEndColor.B, 0));
    }

    /// <summary>
    /// Retail's extra fog terms at a tick, blended like the other bands: the density (4.5 when a light has
    /// none), the color the fog turns toward with distance and the height layer. The layer counts only when its
    /// transition cubic is not all zero, as the client's shader treats the classic lights. Not used for 3.3.5a clients.
    /// </summary>
    RetailFogValues RetailFogTerms(LightProfile background, LightProfile? foreground, float weight, double tick)
    {
        if (!Retail || background.RetailFog is not { } bg)
        {
            return RetailFogValues.None;
        }

        var fg = foreground?.RetailFog is { } other && weight > 0 ? other : null;
        float Mix(Func<RetailFog, float> pick) => fg == null ? pick(bg) : Mathf.Lerp(pick(bg), pick(fg), weight);
        Color MixColor(Func<RetailFog, LightBand> pick)
        {
            var color = pick(bg).At(tick);
            return fg == null ? color : color.Lerp(pick(fg).At(tick), weight);
        }

        float density = Mix(r => r.Density.At(tick));
        var poly = new Vector4(Mix(r => r.Height.Coefficients[0].At(tick)), Mix(r => r.Height.Coefficients[1].At(tick)), Mix(r => r.Height.Coefficients[2].At(tick)), Mix(r => r.Height.Coefficients[3].At(tick)));
        bool layered = poly != Vector4.Zero;
        var height = new Vector4(Mix(r => r.Height.Plane.At(tick)), Mix(r => r.Height.Scaler.At(tick)), Mix(r => r.Height.Density.At(tick)), layered ? 1 : 0);
        var sunColor = MixColor(r => r.SunColor);
        float sunStrength = Mix(r => r.SunStrength.At(tick));
        // A light with no sun fog color has no scattering; the LightData default of white would otherwise light every fog.
        var sun = sunColor.R + sunColor.G + sunColor.B > 0.0001f ? new Vector4(sunColor.R, sunColor.G, sunColor.B, sunStrength) : Vector4.Zero;
        return new(density > 0 ? density : 4.5f, MixColor(r => r.EndColor), Mix(r => r.EndColorDistance.At(tick)), height, poly, MixColor(r => r.Height.Color), MixColor(r => r.Height.EndColor), sun);
    }

    /// <summary>
    /// Give the dome the client's sun and moon textures (sunCenter, moon, moon02); a null one leaves that body
    /// out.
    /// </summary>
    public void SetCelestialTextures(Texture2D? sunTexture, Texture2D? moonTexture, Texture2D? moon2Texture)
    {
        hasSunTexture = sunTexture != null;
        hasMoonTexture = moonTexture != null;
        hasMoon2Texture = moon2Texture != null;
        skyMaterial.SetShaderParameter("sun_tex", sunTexture);
        skyMaterial.SetShaderParameter("moon_tex", moonTexture);
        skyMaterial.SetShaderParameter("moon2_tex", moon2Texture);
    }

    /// <summary>
    /// Publish the six sky colors, the sunrise/sunset highlight, the camera heading and the sun's place for
    /// the dome shader (Shaders/sky.gdshader), blending the two profiles as the other bands are blended.
    /// </summary>
    void ApplySky(LightProfile background, LightProfile? foreground, float weight, double tick, Camera3D camera)
    {
        Vector3 Blend(Func<LightProfile, LightBand> pick)
        {
            var color = pick(background).At(tick);
            if (foreground != null && weight > 0)
            {
                color = color.Lerp(pick(foreground).At(tick), weight);
            }

            return new(color.R, color.G, color.B);
        }

        // Profiles without the in-between bands blend the zenith into the horizon.
        var top = Blend(p => p.SkyTop);
        var horizon = Blend(p => p.SkyHorizon);
        skyTopColor = new(top.X, top.Y, top.Z);
        skyHorizonColor = new(horizon.X, horizon.Y, horizon.Z);
        skyMaterial.SetShaderParameter("sky_zenith", top);
        skyMaterial.SetShaderParameter("sky_middle", Blend(p => p.Sky?.Middle ?? p.SkyTop));
        skyMaterial.SetShaderParameter("sky_band1", Blend(p => p.Sky?.Band1 ?? p.SkyHorizon));
        skyMaterial.SetShaderParameter("sky_band2", Blend(p => p.Sky?.Band2 ?? p.SkyHorizon));
        skyMaterial.SetShaderParameter("sky_smog", horizon);
        skyMaterial.SetShaderParameter("sky_fog", Blend(p => p.Fog));
        skyMaterial.SetShaderParameter("sun_glow_color", Blend(p => p.SunColor ?? p.Diffuse));

        float flag = (background.Sky?.Highlight ?? false ? 1f : 0f);
        if (foreground != null && weight > 0)
        {
            flag = Mathf.Lerp(flag, foreground.Sky?.Highlight ?? false ? 1f : 0f, weight);
        }

        float dayFraction = (float)(TimeOfDayHours / 24.0);
        skyMaterial.SetShaderParameter("highlight", SkyDome.Highlight(dayFraction) * flag);
        // The dome's axes are the client's: view X = -y, view Y = z, view Z = -x.
        var (sx, sy, sz) = SkyDome.SunDirection(dayFraction);
        skyMaterial.SetShaderParameter("sun_direction", new Vector3(-sy, sz, -sx));
        ClientShaderGlobals.Set(ClientShaderGlobals.SunDisc, new Vector3(-sy, sz, -sx).Normalized());
        // Quads whose texture the client does not have stay off (width 0).
        void Disc(string name, SkyDome.Planet planet, bool present)
        {
            var (px, py, pz) = planet.Direction;
            skyMaterial.SetShaderParameter(name, new Vector4(-py, pz, -px, present ? planet.Scale : 0f));
        }

        Disc("sun_disc", SkyDome.Sun(dayFraction), hasSunTexture);
        Disc("moon_disc", SkyDome.Moon(dayFraction), hasMoonTexture);
        Disc("moon2_disc", SkyDome.SecondMoon(dayFraction), hasMoon2Texture);
        var forward = -camera.GlobalTransform.Basis.Z;
        skyMaterial.SetShaderParameter("camera_yaw", Mathf.Atan2(-forward.X, -forward.Z));
    }

    /// <summary>
    /// Blend the profile's four water gradient ends the same way the other bands are blended and
    /// publish them for the liquid shader. Profiles without water bands keep the previous values.
    /// </summary>
    void ApplyWater(LightProfile background, LightProfile? foreground, float weight, double tick)
    {
        if (background.Water is not { } bw || background.WaterAlpha is not { } ba)
        {
            return;
        }

        Vector4 Gradient(Func<WaterColors, LightBand> band, Func<WaterAlphas, float> alpha)
        {
            var color = band(bw).At(tick);
            float a = alpha(ba);
            if (foreground is { Water: { } fw, WaterAlpha: { } fa } && weight > 0)
            {
                color = color.Lerp(band(fw).At(tick), weight);
                a = Mathf.Lerp(a, alpha(fa), weight);
            }

            return new(color.R, color.G, color.B, a);
        }

        ClientShaderGlobals.Set(ClientShaderGlobals.OceanShallow, Gradient(w => w.OceanShallow, a => a.OceanShallow));
        ClientShaderGlobals.Set(ClientShaderGlobals.OceanDeep, Gradient(w => w.OceanDeep, a => a.OceanDeep));
        ClientShaderGlobals.Set(ClientShaderGlobals.RiverShallow, Gradient(w => w.RiverShallow, a => a.RiverShallow));
        ClientShaderGlobals.Set(ClientShaderGlobals.RiverDeep, Gradient(w => w.RiverDeep, a => a.RiverDeep));
    }

    /// <summary>
    /// Publish the map sun for shaders that light themselves the client's way (water): the direction
    /// the light travels, its ambient and diffuse colors and the specular color (the sun band).
    /// </summary>
    void ApplySunUniforms(Color diffuse, Color ambient, LightProfile background, LightProfile? foreground, float weight, double tick)
    {
        var specular = background.SunColor?.At(tick) ?? Colors.White;
        if (foreground?.SunColor is { } sunBand && weight > 0)
        {
            specular = specular.Lerp(sunBand.At(tick), weight);
        }

        ClientShaderGlobals.Set(ClientShaderGlobals.SunDirection, (-sun.GlobalTransform.Basis.Z).Normalized());
        ClientShaderGlobals.Set(ClientShaderGlobals.SunAmbient, new Vector3(ambient.R, ambient.G, ambient.B));
        ClientShaderGlobals.Set(ClientShaderGlobals.SunDiffuse, new Vector3(diffuse.R, diffuse.G, diffuse.B));
        ClientShaderGlobals.Set(ClientShaderGlobals.SunSpecular, new Vector3(specular.R, specular.G, specular.B));
    }

    /// <summary>
    /// Diagnostic override: a solid background color replacing the procedural sky, so the skybox
    /// smoke can tell opaque world pixels (unchanged by the background) from sky and translucent
    /// surfaces. Null restores normal behavior on the next update.
    /// </summary>
    public Color? DiagnosticBackground { get; set; }

    void ApplyDiagnosticBackground()
    {
        if (DiagnosticBackground is Color color)
        {
            // Fog covers the background in the Compatibility renderer even with sky affect 0, so it
            // is off for diagnostic frames; the next normal update re-enables it.
            environment.Environment.FogEnabled = false;
            environment.Environment.BackgroundMode = Godot.Environment.BGMode.Color;
            environment.Environment.BackgroundColor = color;
        }
    }

    /// <summary>
    /// Eye distance the skybox shader projects sky vertices to. Must stay inside the camera far
    /// plane (TerrainViewer uses 8000) and beyond all streamed world geometry (about 2 km at the
    /// largest tile radius).
    /// </summary>
    public const float ShellDistance = 7500;

    /// <summary>
    /// Swap the attached LightSkybox model when the resolved path changes. Reuses ModelLibrary's
    /// shared decode/upload pipeline rather than a bespoke loader.
    /// </summary>
    void SetSkybox(string? path)
    {
        if (path == skyboxPath)
        {
            return;
        }

        skyboxPath = path;
        if (skyboxInstance != null && GodotObject.IsInstanceValid(skyboxInstance))
        {
            skyboxInstance.QueueFree();
        }

        skyboxInstance = path == null ? null : models.Attach(skyboxAnchor, new ModelKey(path, Skybox: true));
    }
}
