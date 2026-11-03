using Godot;

namespace ACWorldEdit;

/// <summary>
/// Shader-wide parameters that mirror the constants the client feeds every world shader (fog, the
/// map sun and the time-of-day water colors). <c>SkyLighting</c> writes them each frame; the
/// terrain, model and liquid shaders read them through <c>Shaders/client_lighting.gdshaderinc</c>.
/// A shader that declares a global fails to compile if it does not exist yet, so shaders are loaded
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
    static bool registered;

    /// <summary>
    /// Register every global once, with values that draw like the client with no lights: no fog, a
    /// neutral sun and plain water.
    /// </summary>
    public static void Ensure()
    {
        if (registered)
        {
            return;
        }

        registered = true;
        // The parameters live only for this run (they are not in the project settings), so each is new;
        // listing the existing ones is an editor-only call.
        void Add(string name, RenderingServer.GlobalShaderParameterType type, Variant value) => RenderingServer.GlobalShaderParameterAdd(name, type, value);

        Add(Fog, RenderingServer.GlobalShaderParameterType.Vec4, new Vector4(0, 1, 1, 0));
        Add(FogColor, RenderingServer.GlobalShaderParameterType.Vec3, new Vector3(0.57f, 0.71f, 0.79f));
        Add(FogEnd, RenderingServer.GlobalShaderParameterType.Vec4, new Vector4(0, 0, 0, 0));
        Add(FogHeight, RenderingServer.GlobalShaderParameterType.Vec4, new Vector4(0, 0, 0, 0));
        Add(FogPoly, RenderingServer.GlobalShaderParameterType.Vec4, new Vector4(0, 0, 0, 0));
        Add(FogHeightColor, RenderingServer.GlobalShaderParameterType.Vec4, new Vector4(0, 0, 0, 0));
        Add(FogHeightEndColor, RenderingServer.GlobalShaderParameterType.Vec4, new Vector4(0, 0, 0, 0));
        Add(FogSun, RenderingServer.GlobalShaderParameterType.Vec4, new Vector4(0, 0, 0, 0));
        Add(FogVolumes, RenderingServer.GlobalShaderParameterType.Sampler2D, ImageTexture.CreateFromImage(Image.CreateEmpty(MaxFogVolumes * FogVolumeTexels, 1, false, Image.Format.Rgbaf)));
        Add(FogVolumeCount, RenderingServer.GlobalShaderParameterType.Float, 0f);
        Add(FogVolumeTime, RenderingServer.GlobalShaderParameterType.Float, 0f);
        Add(SunDisc, RenderingServer.GlobalShaderParameterType.Vec3, new Vector3(0, 1, 0));
        Add(SunDirection, RenderingServer.GlobalShaderParameterType.Vec3, new Vector3(0.4f, -0.8f, 0.3f).Normalized());
        Add(SunAmbient, RenderingServer.GlobalShaderParameterType.Vec3, new Vector3(0.25f, 0.25f, 0.25f));
        Add(SunDiffuse, RenderingServer.GlobalShaderParameterType.Vec3, new Vector3(1, 1, 1));
        Add(SunSpecular, RenderingServer.GlobalShaderParameterType.Vec3, new Vector3(0, 0, 0));
        Add(OceanShallow, RenderingServer.GlobalShaderParameterType.Vec4, new Vector4(0.1f, 0.25f, 0.35f, 0.75f));
        Add(OceanDeep, RenderingServer.GlobalShaderParameterType.Vec4, new Vector4(0.02f, 0.1f, 0.2f, 1));
        Add(RiverShallow, RenderingServer.GlobalShaderParameterType.Vec4, new Vector4(0.15f, 0.35f, 0.4f, 0.5f));
        Add(RiverDeep, RenderingServer.GlobalShaderParameterType.Vec4, new Vector4(0.05f, 0.2f, 0.3f, 1));
    }

    /// <summary>
    /// Load a shader after the globals it may declare exist.
    /// </summary>
    public static Shader Load(string path)
    {
        Ensure();
        return GD.Load<Shader>(path);
    }

    public static void Set(string name, Variant value) => RenderingServer.GlobalShaderParameterSet(name, value);
}
