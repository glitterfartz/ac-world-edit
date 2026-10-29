using Godot;
using ACWorldEdit.Assets;

namespace ACWorldEdit;

/// <summary>
/// Drives the sun, ambient/fog color, procedural sky gradient and LightSkybox model from client
/// Light.dbc data and a scrubbable time-of-day clock. Falls back to a fixed neutral look when no
/// client lighting table is available or the active map is an interior. Direction/shadow tuning is
/// an approximation of the client look, not a reconstruction of its exact sun trajectory or portal
/// -based interior light isolation.
/// </summary>
public sealed partial class SkyLighting : Node3D
{
    readonly DirectionalLight3D sun;
    readonly WorldEnvironment environment;
    readonly ProceduralSkyMaterial skyMaterial;
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
        skyMaterial = new ProceduralSkyMaterial();
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
        table = lightTable;
        mapId = activeMapId;
        interior = isInterior;
        mapRegions = table != null && activeMapId != null ? table.ForMap(activeMapId.Value).ToArray() : [];
        if (interior || table == null)
        {
            environment.Environment.BackgroundMode = Godot.Environment.BGMode.Color;
            environment.Environment.BackgroundColor = interior ? new Color("080a0d") : new Color("91b5c9");
            environment.Environment.FogEnabled = false;
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
        var skyTop = Blend(p => p.SkyTop);
        var skyHorizon = Blend(p => p.SkyHorizon);
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
        skyMaterial.SkyTopColor = skyTop;
        skyMaterial.SkyHorizonColor = skyHorizon;
        skyMaterial.GroundHorizonColor = skyHorizon;
        skyMaterial.GroundBottomColor = skyHorizon.Darkened(0.3f);
        environment.Environment.FogEnabled = true;
        environment.Environment.FogLightColor = fog;
        // Retail fog is complete at the light's FogEnd; the exponential falloff reaches about 3 % transmittance there.
        float? backgroundEnd = backgroundProfile.FogEndAt(tick), foregroundEnd = foregroundProfile?.FogEndAt(tick);
        float? fogEnd = backgroundEnd is { } be && foregroundEnd is { } fe && bestWeight > 0 ? Mathf.Lerp(be, fe, bestWeight) : backgroundEnd ?? foregroundEnd;
        environment.Environment.FogDensity = fogEnd is { } end ? 3.5f / Math.Max(end, 10f) : 0.0035f;
        uint activeParamsId = best != null && bestWeight >= 0.999f ? best.ParamsId : background!.ParamsId;
        SetSkybox(table.SkyboxPath(table.Profile(activeParamsId)?.SkyboxId ?? 0));
        skyboxAnchor.Position = camera.Position;
        ApplyDiagnosticBackground();
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
