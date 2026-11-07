using Godot;

namespace ACWorldEdit;

/// <summary>
/// Shader-wide parameters that mirror the constants the client feeds every world shader (fog, the
/// map sun and the time-of-day water colors). <c>SkyLighting</c> writes them each frame; the
/// terrain, model and liquid shaders read them through <c>Shaders/shared/client_lighting.gdshaderinc</c>.
/// Globals and preview defaults live in project.godot so the editor can compile shaders too. Runtime textures are initialized
/// through <see cref="Load"/>.
/// </summary>
public static class ClientShaderGlobals
{
    /// <summary>
    /// Fog: x start, y end (yards), z density, w mode (0 off, 1 the 3.3.5a linear ramp over view depth, 2 retail
    /// exponential extinction over the distance to the eye).
    /// </summary>
    public const string Fog = "client_fog";
    public const string FogColor = "client_fog_color";
    /// <summary>
    /// The color the fog turns toward with distance (rgb) and the yards that takes (w, 0 for none); retail only.
    /// </summary>
    public const string FogEnd = "client_fog_end";
    /// <summary>
    /// Retail height fog: x fog plane height (world Y), y scaler, z density below the plane, w 1 when the light has height fog.
    /// </summary>
    public const string FogHeight = "client_fog_height";
    /// <summary>The cubic that shapes the transition, highest power first.</summary>
    public const string FogPoly = "client_fog_poly";
    public const string FogHeightColor = "client_fog_height_color";
    public const string FogHeightEndColor = "client_fog_height_end";
    /// <summary>
    /// Retail sun scattering: the color (rgb, band bytes) and strength (w) added around the sun's direction; zero when off.
    /// </summary>
    public const string FogSun = "client_fog_sun";
    /// <summary>
    /// The unit direction toward the sun disc in world space.
    /// </summary>
    public const string SunDisc = "client_sun_disc";
    /// <summary>
    /// Direction the sun light travels (world space, unit length).
    /// </summary>
    public const string SunDirection = "client_sun_dir";
    public const string SunAmbient = "client_sun_ambient";
    public const string SunDiffuse = "client_sun_diffuse";
    public const string SunSpecular = "client_sun_specular";
    /// <summary>
    /// Hemisphere bounce colors (zenith / horizon, gamma space), blended like the sky dome bands.
    /// </summary>
    public const string SkyTop = "client_sky_top";
    public const string SkyGround = "client_sky_ground";
    /// <summary>
    /// LightParams glow: the client adds some of the fog to everything in bright places.
    /// </summary>
    public const string Glow = "client_glow";
    /// <summary>
    /// Water gradient ends, rgb color plus alpha: ocean shallow/deep, river shallow/deep.
    /// </summary>
    public const string OceanShallow = "client_water_ocean_shallow";
    public const string OceanDeep = "client_water_ocean_deep";
    public const string RiverShallow = "client_water_river_shallow";
    public const string RiverDeep = "client_water_river_deep";
    /// <summary>
    /// The nearest fog volumes as a texture of six RGBA-float texels each: center and thickness; the box's three view-space
    /// axes with its half sizes; color and edge power; shape and noise flags, the two noise speeds and the height power. The count is how many are filled in.
    /// </summary>
    public const string FogVolumes = "client_fog_volumes";
    public const string FogVolumeCount = "client_fog_volume_count";
    /// <summary>Volumes the shaders integrate at once.</summary>
    public const int MaxFogVolumes = 6;
    public const int FogVolumeTexels = 6;
    /// <summary>Seconds the volumes' noise has drifted for.</summary>
    public const string FogVolumeTime = "client_fog_volume_time";
    /// <summary>
    /// The nearest placed WMO dynamic lights as a texture of four RGBA-float texels each:
    /// world position and attenuation end; gamma color times intensity and attenuation start;
    /// spot direction and type flag; cone cosines and falloff. The count is how many are filled.
    /// </summary>
    public const string PointLights = "client_point_lights";
    public const string PointLightCount = "client_point_light_count";
    /// <summary>Lights the shaders integrate at once.</summary>
    public const int MaxPointLights = 8;
    public const int PointLightTexels = 4;
    /// <summary>
    /// The shared bone-animation clock both the model shader and the CPU seat follow read: seconds
    /// since the engine started, written every frame by SkyLighting. Shader TIME is renderer-private
    /// and offset from it, so sharing one value keeps glued riders in phase with their mounts. A
    /// negative value means "not set yet": shaders fall back to TIME.
    /// </summary>
    public const string CreatureClock = "creature_clock";
    static bool registered;

    /// <summary>
    /// Initialize the runtime fog-volume texture once. Project settings supply the other defaults.
    /// </summary>
    public static void Ensure()
    {
        if (registered)
        {
            return;
        }

        registered = true;
        // project.godot declares every global, including its editor preview default.
        // Only the fog-volume data texture needs a runtime allocation.
        Set(FogVolumes, ImageTexture.CreateFromImage(Image.CreateEmpty(MaxFogVolumes * FogVolumeTexels, 1, false, Image.Format.Rgbaf)));
    }

    /// <summary>
    /// Load a shader after its runtime texture defaults exist.
    /// </summary>
    public static Shader Load(string path)
    {
        Ensure();
        return GD.Load<Shader>(path);
    }

    public static void Set(string name, Variant value) => RenderingServer.GlobalShaderParameterSet(name, value);
}
