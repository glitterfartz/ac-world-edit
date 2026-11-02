shader_type spatial;
render_mode unshaded, blend_mix, depth_draw_never, cull_disabled;
#include "res://Shaders/client_lighting.gdshaderinc"

// The client's liquid shaders (vsLiquidWater/psLiquidWater, vsLiquidMagma/psLiquidMagma), ported from
// their ps_3_0/vs_3_0 bytecode with the constants Liquid::SetShaderLighting and the material classes
// feed them.
global uniform vec3 client_sun_dir;
global uniform vec3 client_sun_ambient;
global uniform vec3 client_sun_diffuse;
global uniform vec3 client_sun_specular;
global uniform vec4 client_water_ocean_shallow;
global uniform vec4 client_water_ocean_deep;
global uniform vec4 client_water_river_shallow;
global uniform vec4 client_water_river_deep;

// The animated native frames side by side (rgb is added to the lit gradient, alpha masks the highlight).
// Sampled raw: the client does this arithmetic on the 8-bit (gamma-space) values, so the shader does too
// and converts only the finished color to the linear space Godot expects.
uniform sampler2D albedo_tex : filter_linear, repeat_disable;
uniform float frames = 1.0;
// LiquidType sound bank: 0 water, 1 ocean, 2 magma, 3 slime.
uniform int liquid_kind = 0;
// Which procedural depth gradient the type names: 0 river/lake, 1 ocean, 2 WMO water.
uniform int gradient = 0;
// Which depth table maps the stored depth byte to the gradient (LiquidType int 0): 0 river, 1 ocean.
uniform int depth_table = 0;
// Milliseconds the frames take to loop (LiquidType int 1).
uniform float period_ms = 1250.0;
// Texture scale, rotation (radians) and depth scale (LiquidType floats 0, 1 and 2).
uniform float uv_scale = 1.0;
uniform float uv_angle = 0.0;
uniform float depth_scale = 1.0;

const float WATER_SPECULAR_EXPONENT = 6.0;
const int GRADIENT_ROWS = 64;

vec3 gamma_to_linear(vec3 c) {
    c = clamp(c, 0.0, 1.0);
    return mix(pow((c + 0.055) / 1.055, vec3(2.4)), c / 12.92, vec3(lessThan(c, vec3(0.04045))));
}

// The depth byte (stored in the vertex alpha) as the gradient's V (CMap::InitializeLiquidTables).
float depth_coordinate(float depth_byte) {
    if (depth_table == 1) {
        return depth_byte / 255.0;
    }
    return min(depth_byte * 0.11111111 * 0.21428572, 1.0);
}

// The procedural depth texture: GRADIENT_ROWS rows from the shallow to the deep color and alpha, the
// last ocean row darkened to 90 % (Liquid::ExtWaterDepthTexCallback and WmoWaterTexCallback).
vec4 water_gradient(float v) {
    vec4 shallow = client_water_river_shallow;
    vec4 deep = client_water_river_deep;
    if (gradient == 1) {
        shallow = client_water_ocean_shallow;
        deep = client_water_ocean_deep;
    } else if (gradient == 2) {
        // WMO water: the deep river color throughout, with the river alphas.
        shallow = vec4(client_water_river_deep.rgb, client_water_river_shallow.a);
    }
    float row = clamp(v * float(GRADIENT_ROWS) - 0.5, 0.0, float(GRADIENT_ROWS - 1));
    vec4 color = mix(shallow, deep, row / float(GRADIENT_ROWS));
    if (gradient == 1) {
        color.rgb *= 1.0 - 0.1 * clamp(row - float(GRADIENT_ROWS - 2), 0.0, 1.0);
    }
    return color;
}

void fragment() {
    float frame = mod(floor(mod(TIME * 1000.0, period_ms) / period_ms * frames), frames);
    vec2 size = vec2(textureSize(albedo_tex, 0));
    float s = sin(uv_angle);
    float c = cos(uv_angle);
    vec2 scaled = UV * uv_scale;
    vec2 tile = fract(vec2(c * scaled.x - s * scaled.y, s * scaled.x + c * scaled.y));
    // Clamp inside each frame to avoid sampling its neighbor in the atlas.
    vec2 inset = vec2(0.5 * frames / size.x, 0.5 / size.y);
    tile = mix(inset, vec2(1.0) - inset, tile);
    vec4 tex = texture(albedo_tex, vec2((frame + tile.x) / frames, tile.y));
    if (liquid_kind >= 2) {
        // Magma and slime: vertex color (white) times the texture, unlit and opaque.
        ALBEDO = gamma_to_linear(tex.rgb);
        ALPHA = 1.0;
    } else {
        // Water: (ambient + diffuse * N.L) times the depth gradient, plus the water texture and its
        // masked highlight. The surface normal is straight up, as the client builds it.
        vec3 sun = normalize((VIEW_MATRIX * vec4(client_sun_dir, 0.0)).xyz);
        float n_dot_l = clamp(dot(-sun, NORMAL), 0.0, 1.0);
        vec3 lit = client_sun_ambient + client_sun_diffuse * n_dot_l;
        vec3 half_vector = normalize(normalize(VERTEX) + sun);
        float highlight = pow(max(dot(-half_vector, NORMAL), 0.0), WATER_SPECULAR_EXPONENT);
        vec4 depth_color = water_gradient(depth_coordinate(round(COLOR.a * 255.0)) * depth_scale);
        ALBEDO = gamma_to_linear(lit * depth_color.rgb + tex.rgb + (client_sun_specular * highlight + 0.25) * tex.a);
        ALPHA = depth_color.a;
    }
    FOG = client_fog_output(-VERTEX.z);
}
