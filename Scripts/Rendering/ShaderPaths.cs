namespace ACWorldEdit;

/// <summary>
/// Canonical resource paths of the client shaders. Shader <c>#include</c> directives are literal
/// <c>res://Shaders/...</c> paths, so a consuming project must expose the shader folder at
/// <c>res://Shaders</c>; the path is not configurable per consumer.
/// </summary>
public static class ShaderPaths
{
    public const string Root = "res://Shaders";

    public const string Terrain = Root + "/terrain/terrain.gdshader";
    public const string TerrainLow = Root + "/terrain/terrainlow.gdshader";
    public const string Model = Root + "/model/model.gdshader";
    public const string ShadowCaster = Root + "/shadow/shadow_caster.gdshader";
    public const string Sky = Root + "/sky/sky.gdshader";
    public const string Clouds = Root + "/sky/clouds.gdshader";
    public const string Glare = Root + "/sky/glare.gdshader";
    public const string SunShafts = Root + "/sky/sunshafts.gdshader";
    public const string Liquid = Root + "/liquid/liquid.gdshader";
    public const string LiquidFog = Root + "/liquid/liquidfog.gdshader";
    public const string Mercury = Root + "/liquid/mercury.gdshader";
    public const string Ley = Root + "/liquid/leyline.gdshader";
    public const string Fel = Root + "/liquid/fel.gdshader";
    public const string Swamp = Root + "/liquid/swamp.gdshader";
    public const string Azerite = Root + "/liquid/azerite.gdshader";
    public const string ProceduralWater = Root + "/liquid/procwater.gdshader";
    public const string ProceduralMagma = Root + "/liquid/procmagma.gdshader";
}
