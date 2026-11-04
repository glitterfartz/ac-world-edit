using Godot;
using ACWorldEdit.Assets;

namespace ACWorldEdit;

/// <summary>Sky, celestial bodies, glare, shafts and stars.</summary>
public sealed partial class SkyLighting
{
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
        ApplySkyBands(background, foreground, weight, tick);
        float density = ApplyClouds(background, foreground, weight, tick, camera);
        ApplyCelestials(background, foreground, weight, tick, camera, density);
    }

    /// <summary>The six dome bands, blended as the other colors are.</summary>
    void ApplySkyBands(LightProfile background, LightProfile? foreground, float weight, double tick)
    {
        // Profiles without the in-between bands blend the zenith into the horizon.
        var top = BlendColor(background, foreground, weight, tick, p => p.SkyTop);
        var horizon = BlendColor(background, foreground, weight, tick, p => p.SkyHorizon);
        skyTopColor = new(top.X, top.Y, top.Z);
        skyHorizonColor = new(horizon.X, horizon.Y, horizon.Z);
        skyMaterial.SetShaderParameter("sky_zenith", top);
        skyMaterial.SetShaderParameter("sky_middle", BlendColor(background, foreground, weight, tick, p => p.Sky?.Middle ?? p.SkyTop));
        skyMaterial.SetShaderParameter("sky_band1", BlendColor(background, foreground, weight, tick, p => p.Sky?.Band1 ?? p.SkyHorizon));
        skyMaterial.SetShaderParameter("sky_band2", BlendColor(background, foreground, weight, tick, p => p.Sky?.Band2 ?? p.SkyHorizon));
        skyMaterial.SetShaderParameter("sky_smog", horizon);
        skyMaterial.SetShaderParameter("sky_fog", BlendColor(background, foreground, weight, tick, p => p.Fog));
        skyMaterial.SetShaderParameter("sun_glow_color", BlendColor(background, foreground, weight, tick, p => p.SunColor ?? p.Diffuse));
        // A LightSkybox model brings its own sun, moons and clouds, and the client then draws none of ours.
        skyMaterial.SetShaderParameter("celestial_enabled", skyboxPath == null);
    }

    /// <summary>The cloud dome's colors, density and follow position; returns the blended density.</summary>
    float ApplyClouds(LightProfile background, LightProfile? foreground, float weight, double tick, Camera3D camera)
    {
        float Density(LightProfile p) => p.Clouds?.Density.At(tick) ?? 0f;
        float density = Density(background);
        if (foreground != null && weight > 0)
        {
            density = Mathf.Lerp(density, Density(foreground), weight);
        }

        cloudMaterial.SetShaderParameter("cloud_density", density);
        cloudMaterial.SetShaderParameter("cloud_sun_color", BlendColor(background, foreground, weight, tick, p => p.Clouds?.Sun ?? p.Diffuse));
        cloudMaterial.SetShaderParameter("cloud_shade_color", BlendColor(background, foreground, weight, tick, p => p.Clouds?.Shade ?? p.Ambient));
        cloudMaterial.SetShaderParameter("cloud_base_color", BlendColor(background, foreground, weight, tick, p => p.Clouds?.Base ?? p.Ambient));
        cloudDome.Visible = skyboxPath == null && density > 0f;
        cloudDome.Position = camera.Position;
        return density;
    }

    /// <summary>Highlight, sun/moon discs, glare, shafts, stars and the camera yaw.</summary>
    void ApplyCelestials(LightProfile background, LightProfile? foreground, float weight, double tick, Camera3D camera, float density)
    {
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
        var glow = BlendColor(background, foreground, weight, tick, p => p.SunColor ?? p.Diffuse);
        UpdateGlare(0, camera, forward, new Vector3(-sunPlanet.Direction.Y, sunPlanet.Direction.Z, -sunPlanet.Direction.X), sunPlanet.Scale, hasSunGlare, density, glow, dayFraction);
        UpdateGlare(1, camera, forward, new Vector3(-moonPlanet.Direction.Y, moonPlanet.Direction.Z, -moonPlanet.Direction.X), moonPlanet.Scale, hasMoonGlare, density, glow, dayFraction);
        UpdateShafts(background, foreground, weight, tick, dayFraction);
        ApplyStars(dayFraction, camera);
        skyMaterial.SetShaderParameter("camera_yaw", Mathf.Atan2(-forward.X, -forward.Z));
    }

    /// <summary>Blend one band of two profiles as a Godot vector.</summary>
    static Vector3 BlendColor(LightProfile background, LightProfile? foreground, float weight, double tick, Func<LightProfile, LightBand> pick)
    {
        var (r, g, b) = pick(background).Rgb(tick);
        if (foreground != null && weight > 0)
        {
            var (fr, fg, fb) = pick(foreground).Rgb(tick);
            return new Vector3(r + (fr - r) * weight, g + (fg - g) * weight, b + (fb - b) * weight);
        }

        return new Vector3(r, g, b);
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
    /// plane (ViewerShell uses 8000) and beyond all streamed world geometry (about 2 km at the
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
