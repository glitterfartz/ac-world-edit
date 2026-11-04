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
    DirectionalLight3D sun = null!;
    WorldEnvironment environment = null!;
    ShaderMaterial skyMaterial = null!;
    Color skyTopColor = new("4a7fc4"), skyHorizonColor = new("c0d2e5");
    bool hasSunTexture, hasMoonTexture, hasMoon2Texture;
    Node3D starsAnchor = null!;
    MeshInstance3D cloudDome = null!;
    ShaderMaterial cloudMaterial = null!;
    ModelInstance? starsInstance;
    float appliedStarFade = -1;
    readonly (MeshInstance3D Quad, ShaderMaterial Material)[] glares = new (MeshInstance3D, ShaderMaterial)[2];
    bool hasSunGlare, hasMoonGlare;
    // The map's fog volumes as view-space boxes, and the texture the nearest few are handed to the shaders in.
    FogBox[] fogBoxes = [];
    readonly Image fogVolumeImage = Image.CreateEmpty(ClientShaderGlobals.MaxFogVolumes * ClientShaderGlobals.FogVolumeTexels, 1, false, Image.Format.Rgbaf);
    ImageTexture? fogVolumeTexture;
    MeshInstance3D shaftQuad = null!;
    ShaderMaterial shaftMaterial = null!;
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
        BuildSun();
        skyMaterial = new ShaderMaterial
        {
            Shader = GD.Load<Shader>("res://Shaders/sky.gdshader")
        };
        BuildEnvironment();
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
        BuildClouds();
        BuildGlares();
        BuildShafts();
    }

    void BuildSun()
    {
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
    }

    void BuildEnvironment()
    {
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
    }

    void BuildClouds()
    {
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
    }

    void BuildGlares()
    {
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
    }

    void BuildShafts()
    {
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
        fogBoxes = [];
        ClientShaderGlobals.Set(ClientShaderGlobals.FogVolumeCount, 0f);
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
            TimeOfDayHours = LightingSampler.Advance(TimeOfDayHours, delta, Speed);
        }

        if (interior || table == null || mapId == null)
        {
            return;
        }

        var sample = LightingSampler.Sample(new(TimeOfDayHours, camera.Position.X, camera.Position.Y, camera.Position.Z, mapRegions, table.Profile));
        if (sample is not { } light)
        {
            return;
        }

        ActiveLights = light.ActiveLights;
        var backgroundProfile = light.Background;
        var foregroundProfile = light.ForegroundProfile;
        float bestWeight = light.Weight;
        double tick = light.Tick;
        var diffuse = new Color(light.Diffuse.R, light.Diffuse.G, light.Diffuse.B);
        var ambient = new Color(light.Ambient.R, light.Ambient.G, light.Ambient.B);
        var fog = new Color(light.Fog.R, light.Fog.G, light.Fog.B);
        // The client lights everything along one direction (DayNight::SetDirection) with the diffuse and ambient bands at
        // full strength; it has no exposure curve, so the light's energy stays 1 (the tonemapper is linear).
        var sunDirection = new Vector3(light.SunDirection.X, light.SunDirection.Y, light.SunDirection.Z);
        sun.Basis = Basis.LookingAt(sunDirection, Vector3.Up);
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
        UpdateFogVolumes(camera);
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
        SetSkybox(table.SkyboxPath(table.Profile(light.ActiveParamsId)?.SkyboxId ?? 0));
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

}
