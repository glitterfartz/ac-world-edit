using Godot;
using ACWorldEdit.Assets;

namespace ACWorldEdit;

/// <summary>
/// Publication half of the lighting rig: turn one pure <see cref="LightingDetail"/> into Godot Color
/// and Vector values and push them to the dome, cloud, water and celestial shaders. No blending here.
/// </summary>
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

    /// <summary>Follow the camera, and fade the stars by the day.</summary>
    void ApplyStars(float fade, Camera3D camera)
    {
        starsAnchor.Position = camera.Position;
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

    static Vector3 Vec(RgbBand band) => new(band.R, band.G, band.B);

    /// <summary>Publish the six dome bands, the sun glow, the highlight and the celestial switch.</summary>
    void PublishSky(SkyDetail sky)
    {
        var zenith = Vec(sky.Zenith);
        var horizon = Vec(sky.Smog);
        skyTopColor = new(zenith.X, zenith.Y, zenith.Z);
        skyHorizonColor = new(horizon.X, horizon.Y, horizon.Z);
        skyMaterial.SetShaderParameter("sky_zenith", zenith);
        skyMaterial.SetShaderParameter("sky_middle", Vec(sky.Middle));
        skyMaterial.SetShaderParameter("sky_band1", Vec(sky.Band1));
        skyMaterial.SetShaderParameter("sky_band2", Vec(sky.Band2));
        skyMaterial.SetShaderParameter("sky_smog", horizon);
        skyMaterial.SetShaderParameter("sky_fog", Vec(sky.Fog));
        skyMaterial.SetShaderParameter("sun_glow_color", Vec(sky.SunGlow));
        // A LightSkybox model brings its own sun, moons and clouds, and the client then draws none of ours.
        skyMaterial.SetShaderParameter("celestial_enabled", skyboxPath == null);
    }

    /// <summary>Publish the cloud dome's colors, density, follow position and the planet that lights it.</summary>
    void PublishClouds(CloudDetail clouds, Camera3D camera)
    {
        cloudMaterial.SetShaderParameter("cloud_density", clouds.Density);
        cloudMaterial.SetShaderParameter("cloud_sun_color", Vec(clouds.Sun));
        cloudMaterial.SetShaderParameter("cloud_shade_color", Vec(clouds.Shade));
        cloudMaterial.SetShaderParameter("cloud_base_color", Vec(clouds.Base));
        cloudMaterial.SetShaderParameter("cloud_light_direction", new Vector3(-clouds.LightY, clouds.LightZ, -clouds.LightX));
        cloudDome.Visible = skyboxPath == null && clouds.Density > 0f;
        cloudDome.Position = camera.Position;
    }

    /// <summary>Highlight, sun/moon discs, glare, shafts, stars and the camera yaw.</summary>
    void PublishCelestials(LightingDetail detail, Camera3D camera)
    {
        var celestial = detail.Celestials;
        skyMaterial.SetShaderParameter("highlight", detail.Sky.Highlight);
        // The dome's axes are the client's: view X = -y, view Y = z, view Z = -x.
        var sun = new Vector3(-celestial.SunY, celestial.SunZ, -celestial.SunX);
        skyMaterial.SetShaderParameter("sun_direction", sun);
        ClientShaderGlobals.Set(ClientShaderGlobals.SunDisc, sun.Normalized());
        // Quads whose texture the client does not have stay off (width 0).
        void Disc(string name, float x, float y, float z, float scale, bool present)
        {
            skyMaterial.SetShaderParameter(name, new Vector4(-y, z, -x, present ? scale : 0f));
        }

        Disc("sun_disc", celestial.SunX, celestial.SunY, celestial.SunZ, SkyDome.Sun(detail.DayFraction).Scale, hasSunTexture);
        Disc("moon_disc", celestial.MoonX, celestial.MoonY, celestial.MoonZ, SkyDome.Moon(detail.DayFraction).Scale, hasMoonTexture);
        Disc("moon2_disc", celestial.Moon2X, celestial.Moon2Y, celestial.Moon2Z, SkyDome.SecondMoon(detail.DayFraction).Scale, hasMoon2Texture);
        var forward = -camera.GlobalTransform.Basis.Z;
        var sunPlanet = SkyDome.Sun(detail.DayFraction);
        var moonPlanet = SkyDome.Moon(detail.DayFraction);
        var glow = Vec(detail.Sky.SunGlow);
        UpdateGlare(0, camera, forward, new Vector3(-sunPlanet.Direction.Y, sunPlanet.Direction.Z, -sunPlanet.Direction.X), sunPlanet.Scale, hasSunGlare, detail.Clouds.Density, glow, detail.DayFraction);
        UpdateGlare(1, camera, forward, new Vector3(-moonPlanet.Direction.Y, moonPlanet.Direction.Z, -moonPlanet.Direction.X), moonPlanet.Scale, hasMoonGlare, detail.Clouds.Density, glow, detail.DayFraction);
        PublishShafts(detail.Shafts, celestial);
        ApplyStars(detail.StarsFade, camera);
        skyMaterial.SetShaderParameter("camera_yaw", Mathf.Atan2(-forward.X, -forward.Z));
    }

    /// <summary>Shaft colour, amount and reach, with the sun direction the shader marches toward.</summary>
    void PublishShafts(ShaftDetail shafts, CelestialDetail celestial)
    {
        shaftQuad.Visible = shafts.Visible;
        if (!shafts.Visible)
        {
            return;
        }

        shaftMaterial.SetShaderParameter("shaft_color", Vec(shafts.Color));
        shaftMaterial.SetShaderParameter("shaft_amount", shafts.Amount);
        shaftMaterial.SetShaderParameter("shaft_reach", shafts.Reach);
        shaftMaterial.SetShaderParameter("sun_direction", new Vector3(-celestial.SunY, celestial.SunZ, -celestial.SunX).Normalized());
    }

    /// <summary>Publish the four water gradient ends.</summary>
    static void PublishWater(WaterDetail water)
    {
        Vector4 Band(WaterBand band) => new(band.Color.R, band.Color.G, band.Color.B, band.Alpha);
        ClientShaderGlobals.Set(ClientShaderGlobals.OceanShallow, Band(water.OceanShallow));
        ClientShaderGlobals.Set(ClientShaderGlobals.OceanDeep, Band(water.OceanDeep));
        ClientShaderGlobals.Set(ClientShaderGlobals.RiverShallow, Band(water.RiverShallow));
        ClientShaderGlobals.Set(ClientShaderGlobals.RiverDeep, Band(water.RiverDeep));
    }

    /// <summary>Publish the map sun for shaders that light themselves the client's way (water).</summary>
    void PublishSunUniforms(Color diffuse, Color ambient, RgbBand specular)
    {
        ClientShaderGlobals.Set(ClientShaderGlobals.SunDirection, (-sun.GlobalTransform.Basis.Z).Normalized());
        ClientShaderGlobals.Set(ClientShaderGlobals.SunAmbient, new Vector3(ambient.R, ambient.G, ambient.B));
        ClientShaderGlobals.Set(ClientShaderGlobals.SunDiffuse, new Vector3(diffuse.R, diffuse.G, diffuse.B));
        ClientShaderGlobals.Set(ClientShaderGlobals.SunSpecular, new Vector3(specular.R, specular.G, specular.B));
    }

    /// <summary>Swap the attached LightSkybox model when the resolved path changes.</summary>
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
