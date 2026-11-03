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
    readonly Node3D starsAnchor;
    readonly MeshInstance3D cloudDome;
    readonly ShaderMaterial cloudMaterial;
    ModelInstance? starsInstance;
    float appliedStarFade = -1;
    readonly (MeshInstance3D Quad, ShaderMaterial Material)[] glares = new (MeshInstance3D, ShaderMaterial)[2];
    bool hasSunGlare, hasMoonGlare;
    readonly MeshInstance3D shaftQuad;
    readonly ShaderMaterial shaftMaterial;
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
                TonemapMode = Godot.Environment.ToneMapper.Linear
            }
        };
        AddChild(environment);
        skyboxAnchor = new Node3D
        {
            Name = "Skybox"
        };
        AddChild(skyboxAnchor);
        starsAnchor = new Node3D
        {
            Name = "Stars"
        };
        AddChild(starsAnchor);
        cloudMaterial = new ShaderMaterial
        {
            Shader = GD.Load<Shader>("res://Shaders/clouds.gdshader"),
            // After the star model (a skybox variant, priority below -1) and before the world's translucent surfaces.
            RenderPriority = -1
        };
        cloudMaterial.SetShaderParameter("shell_distance", ShellDistance);
        cloudDome = new MeshInstance3D
        {
            Name = "Clouds",
            Mesh = new SphereMesh { Radius = 1, Height = 2, RadialSegments = 64, Rings = 32 },
            MaterialOverride = cloudMaterial,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
            // The vertex shader moves the vertices, so the node's own bounds are meaningless to the culler.
            CustomAabb = new Aabb(new Vector3(-10000, -10000, -10000), new Vector3(20000, 20000, 20000))
        };
        AddChild(cloudDome);
        var glareShader = GD.Load<Shader>("res://Shaders/glare.gdshader");
        for (int i = 0; i < glares.Length; i++)
        {
            // The client draws glare last, over everything, so it takes the highest render priority.
            var material = new ShaderMaterial
            {
                Shader = glareShader,
                RenderPriority = (int)Material.RenderPriorityMax
            };
            material.SetShaderParameter("is_moon", i == 1);
            var quad = new MeshInstance3D
            {
                Name = i == 0 ? "SunGlare" : "MoonGlare",
                Mesh = new QuadMesh { Size = Vector2.One },
                MaterialOverride = material,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                Visible = false
            };
            AddChild(quad);
            glares[i] = (quad, material);
        }

        shaftMaterial = new ShaderMaterial
        {
            Shader = GD.Load<Shader>("res://Shaders/sunshafts.gdshader"),
            RenderPriority = (int)Material.RenderPriorityMax - 1
        };
        shaftQuad = new MeshInstance3D
        {
            Name = "SunShafts",
            Mesh = new QuadMesh { Size = new Vector2(2, 2) },
            MaterialOverride = shaftMaterial,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
            // The vertex shader covers the screen whatever the node's place, so the culler must never drop it.
            CustomAabb = new Aabb(new Vector3(-10000, -10000, -10000), new Vector3(20000, 20000, 20000))
        };
        AddChild(shaftQuad);
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
            foreach (var glare in glares)
            {
                glare.Quad.Visible = false;
            }

            starsAnchor.Visible = false;
            cloudDome.Visible = false;
            shaftQuad.Visible = false;
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

        // The base is the map's default light, the one at coordinates (0, 0, 0). A map without one uses Light 1, the
        // Eastern Kingdoms default, as the client does (DayNight::LightLoad), rather than whichever local light is nearest.
        var background = mapRegions.FirstOrDefault(r => r.IsDefault) ?? table.Regions.FirstOrDefault(r => r.Id == 1);
        var backgroundProfile = background != null ? table.Profile(background.ParamsId) : null;
        if (backgroundProfile == null)
        {
            return;
        }

        // Local lights blend over it by their distance to the camera in three dimensions, height included
        // (DayNight::BlendAreaLight). The client blends every light that covers it, the nearest last; this takes
        // the nearest.
        LightRegion? best = null;
        float bestWeight = 0;
        float bestDistance = float.MaxValue;
        foreach (var region in mapRegions)
        {
            if (region.IsDefault)
            {
                continue;
            }

            float dist = camera.Position.DistanceTo(region.ViewPosition);
            float span = Math.Max(1f, region.FalloffEnd - region.FalloffStart);
            float weight = dist <= region.FalloffStart ? 1f : dist >= region.FalloffEnd ? 0f : (region.FalloffEnd - dist) / span;
            if (weight > 0 && dist < bestDistance)
            {
                best = region;
                bestWeight = weight;
                bestDistance = dist;
            }
        }

        var foregroundProfile = best != null ? table.Profile(best.ParamsId) : null;
        ActiveLights = $"background={background?.Id}/params {background?.ParamsId} foreground={best?.Id}/params {best?.ParamsId} weight={bestWeight:0.00} regions={mapRegions.Length}";
        double tick = TimeOfDayHours * 120.0;
        Color Blend(Func<LightProfile, LightBand> pick)
        {
            var bg = pick(backgroundProfile).At(tick);
            return foregroundProfile != null && bestWeight > 0 ? bg.Lerp(pick(foregroundProfile).At(tick), bestWeight) : bg;
        }

        var diffuse = Blend(p => p.Diffuse);
        var ambient = Blend(p => p.Ambient);
        var fog = Blend(p => p.Fog);
        // The client lights everything along one direction (DayNight::SetDirection) with the diffuse and ambient bands at
        // full strength; it has no exposure curve, so the light's energy stays 1 (the tonemapper is linear).
        var (lx, ly, lz) = SkyDome.LightDirection((float)(TimeOfDayHours / 24.0));
        sun.Basis = Basis.LookingAt(new Vector3(-ly, lz, -lx).Normalized(), Vector3.Up);
        sun.LightColor = diffuse;
        sun.LightEnergy = 1f;
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
        // A retail light with no fog distance (the newer lights, FogEnd 0) scales its density over a fixed span:
        // their FogDensity runs 4 to 25, which over the view distance would leave a few hundred yards of sight, while
        // their EndFogColorDistance of 1000 to 2400 yards only means something if the fog is still thin out there.
        float fogEnd = curve == null && Retail ? ModernFogSpan : Math.Min(ViewDistance, curve?.End ?? ViewDistance);
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
    // The yards a newer retail light's FogDensity is spread over (an estimate; the client's own code is not readable here).
    const float ModernFogSpan = 10000;
    /// <summary>
    /// Fog start as a fraction of its end when a light has no fog data (DayNight DEFAULT_FOG_SCALER).
    /// </summary>
    const float DefaultFogStartMultiplier = 0.5f;
    Vector4 clientFog = new(0, 1, 1, 0);
    /// <summary>The fog values the shaders last received: start, end, density and mode.</summary>
    public Vector4 ClientFogValues => clientFog;
    /// <summary>The Light regions the sky last blended, for the diagnostics.</summary>
    public string ActiveLights { get; private set; } = "";
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
    /// Give the sky the client's sun and moon textures (sunCenter, moon, moon02) and their glare textures
    /// (sunGlare, moonGlare); a null one leaves that body or glare out.
    /// </summary>
    public void SetCelestialTextures(Texture2D? sunTexture, Texture2D? moonTexture, Texture2D? moon2Texture, Texture2D? sunGlareTexture = null, Texture2D? moonGlareTexture = null)
    {
        hasSunGlare = sunGlareTexture != null;
        hasMoonGlare = moonGlareTexture != null;
        glares[0].Material.SetShaderParameter("glare_tex", sunGlareTexture);
        glares[1].Material.SetShaderParameter("glare_tex", moonGlareTexture);
        hasSunTexture = sunTexture != null;
        hasMoonTexture = moonTexture != null;
        hasMoon2Texture = moon2Texture != null;
        skyMaterial.SetShaderParameter("sun_tex", sunTexture);
        skyMaterial.SetShaderParameter("moon_tex", moonTexture);
        skyMaterial.SetShaderParameter("moon2_tex", moon2Texture);
    }

    /// <summary>
    /// Place one glare quad over its body and set what the shader needs. The facing terms depend on where the
    /// camera looks, so the cosine to the body is taken here.
    /// </summary>
    void UpdateGlare(int index, Camera3D camera, Vector3 forward, Vector3 direction, float planetWidth, bool present, float cloudDensity, Vector3 color, float dayFraction)
    {
        var (quad, material) = glares[index];
        float cosine = forward.Dot(direction);
        var glare = index == 0 ? SkyDome.SunGlare(dayFraction, cosine) : SkyDome.MoonGlare(dayFraction, cosine, planetWidth);
        bool shown = present && skyboxPath == null && glare.Alpha > 0.001f;
        quad.Visible = shown;
        if (!shown)
        {
            return;
        }

        var position = camera.GlobalPosition + direction * SkyDome.PlanetDistance;
        quad.GlobalTransform = new Transform3D(Basis.Identity, position);
        quad.LookAt(camera.GlobalPosition, Vector3.Up);
        quad.Scale = Vector3.One * glare.Scale;
        material.SetShaderParameter("glare_color", color);
        material.SetShaderParameter("glare_alpha", glare.Alpha);
        material.SetShaderParameter("planet_direction", direction);
        material.SetShaderParameter("planet_width", planetWidth);
        material.SetShaderParameter("cloud_density", cloudDensity);
    }

    /// <summary>
    /// Sun shafts: the light's LightShaft color, intensity and radius blended like the other bands, scaled by how high
    /// the sun is. Lights without shafts (most of them) leave the quad off.
    /// </summary>
    void UpdateShafts(LightProfile background, LightProfile? foreground, float weight, double tick, float dayFraction)
    {
        float Intensity(LightProfile p) => p.Shafts?.Intensity.At(tick) ?? 0f;
        float intensity = Intensity(background);
        if (foreground != null && weight > 0)
        {
            intensity = Mathf.Lerp(intensity, Intensity(foreground), weight);
        }

        var (sx, sy, sz) = SkyDome.SunDirection(dayFraction);
        float amount = intensity * SkyDome.ShaftSunVisibility(sz);
        shaftQuad.Visible = amount > 0.001f;
        if (!shaftQuad.Visible)
        {
            return;
        }

        float Radius(LightProfile p) => p.Shafts?.Radius.At(tick) ?? 0f;
        float radius = Radius(background);
        var color = background.Shafts?.Color.At(tick) ?? Colors.White;
        if (foreground != null && weight > 0)
        {
            radius = Mathf.Lerp(radius, Radius(foreground), weight);
            color = color.Lerp(foreground.Shafts?.Color.At(tick) ?? color, weight);
        }

        shaftMaterial.SetShaderParameter("shaft_color", new Vector3(color.R, color.G, color.B));
        shaftMaterial.SetShaderParameter("shaft_amount", amount);
        shaftMaterial.SetShaderParameter("shaft_reach", SkyDome.ShaftReach(radius));
        shaftMaterial.SetShaderParameter("sun_direction", new Vector3(-sy, sz, -sx).Normalized());
    }

    /// <summary>
    /// Give the sky the client's star model (Environments/Stars/stars.m2); null removes it. It is drawn like a
    /// LightSkybox model and fades with the day (<see cref="SkyDome.StarFade"/>).
    /// </summary>
    public void SetStarsModel(string? path)
    {
        if (starsInstance != null && GodotObject.IsInstanceValid(starsInstance))
        {
            starsInstance.QueueFree();
        }

        starsInstance = path == null ? null : models.Attach(starsAnchor, new ModelKey(path, Skybox: true));
        appliedStarFade = -1;
    }

    // Follow the camera, and fade the stars by the day. The model's batches are plain geometry instances, so the
    // fade is the instance transparency, applied only when it changes.
    void ApplyStars(float dayFraction, Camera3D camera)
    {
        starsAnchor.Position = camera.Position;
        float fade = SkyDome.StarFade(dayFraction);
        bool show = starsInstance != null && GodotObject.IsInstanceValid(starsInstance) && skyboxPath == null && fade > 0.01f;
        starsAnchor.Visible = show;
        if (!show || starsInstance!.ModelReady == false || Math.Abs(fade - appliedStarFade) < 0.004f)
        {
            return;
        }

        appliedStarFade = fade;
        foreach (var child in starsInstance.FindChildren("*", "GeometryInstance3D", true, false))
        {
            ((GeometryInstance3D)child).Transparency = 1f - fade;
        }
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
        // A LightSkybox model brings its own sun, moons and clouds, and the client then draws none of ours.
        skyMaterial.SetShaderParameter("celestial_enabled", skyboxPath == null);
        float Density(LightProfile p) => p.Clouds?.Density.At(tick) ?? 0f;
        float density = Density(background);
        if (foreground != null && weight > 0)
        {
            density = Mathf.Lerp(density, Density(foreground), weight);
        }

        cloudMaterial.SetShaderParameter("cloud_density", density);
        cloudMaterial.SetShaderParameter("cloud_sun_color", Blend(p => p.Clouds?.Sun ?? p.Diffuse));
        cloudMaterial.SetShaderParameter("cloud_shade_color", Blend(p => p.Clouds?.Shade ?? p.Ambient));
        cloudMaterial.SetShaderParameter("cloud_base_color", Blend(p => p.Clouds?.Base ?? p.Ambient));
        cloudDome.Visible = skyboxPath == null && density > 0f;
        cloudDome.Position = camera.Position;

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

        // The clouds are lit by the moon at night (before about 04:50 and after about 22:10) and by the sun otherwise.
        var lightPlanet = dayFraction < 0.2013889f || dayFraction > 0.9236111f ? SkyDome.Moon(dayFraction) : SkyDome.Sun(dayFraction);
        var (lx, ly, lz) = lightPlanet.Direction;
        cloudMaterial.SetShaderParameter("cloud_light_direction", new Vector3(-ly, lz, -lx));
        Disc("sun_disc", SkyDome.Sun(dayFraction), hasSunTexture);
        Disc("moon_disc", SkyDome.Moon(dayFraction), hasMoonTexture);
        Disc("moon2_disc", SkyDome.SecondMoon(dayFraction), hasMoon2Texture);
        var forward = -camera.GlobalTransform.Basis.Z;
        var sunPlanet = SkyDome.Sun(dayFraction);
        var moonPlanet = SkyDome.Moon(dayFraction);
        UpdateGlare(0, camera, forward, new Vector3(-sunPlanet.Direction.Y, sunPlanet.Direction.Z, -sunPlanet.Direction.X), sunPlanet.Scale, hasSunGlare, density, Blend(p => p.SunColor ?? p.Diffuse), dayFraction);
        UpdateGlare(1, camera, forward, new Vector3(-moonPlanet.Direction.Y, moonPlanet.Direction.Z, -moonPlanet.Direction.X), moonPlanet.Scale, hasMoonGlare, density, Blend(p => p.SunColor ?? p.Diffuse), dayFraction);
        UpdateShafts(background, foreground, weight, tick, dayFraction);
        ApplyStars(dayFraction, camera);
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
