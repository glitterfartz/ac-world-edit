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

// Retail water (psLiquidWater "mediumwater", ps_5_0): the animated frames are gone and a ripple normal
// map (LiquidType texture 2) bends the surface instead. Set when the type names one.
uniform bool has_bump = false;
uniform sampler2D bump_tex : filter_linear_mipmap, repeat_enable;

const float WATER_SPECULAR_EXPONENT = 6.0;
const int GRADIENT_ROWS = 64;
// The retail shader reads these from its constant buffer; the values are chosen to look like the stock
// water, because the buffer layout is not in the shipped bytecode.
const float RIPPLE_STRENGTH = 1.0;
const float RIPPLE_SPECULAR_SCALE = 1.0;
const float RETAIL_SPECULAR_EXPONENT = 16.0;

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

varying vec3 world_position;

void vertex() {
    world_position = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

// One ripple layer: the normal map's red and green, 0.5 meaning flat.
vec2 ripple(vec2 uv) {
    return texture(bump_tex, uv).rg;
}

// The retail pixel shader's water, in the same gamma-space arithmetic as the 3.3.5a one. Two pairs of
// scrolling ripple samples (a near pair and a coarser far pair, blended by camera distance) bend the
// surface; fresnel mixes the lit gradient toward the sky color, and the sun adds a specular glint.
// Returns rgb in gamma space and the gradient alpha.
vec4 retail_water(float depth_v, vec3 view_position, mat4 view_matrix) {
    vec2 p = world_position.xz;
    float t = TIME;
    // The scroll rates and UV scales come from the vertex shader, which this client install does not
    // ship; these are chosen to read as the stock ripples.
    vec2 near_sum = ripple(p * 0.090 + vec2(0.021, 0.013) * t) + ripple(vec2(p.y, -p.x) * 0.170 + vec2(-0.015, 0.019) * t) - 1.0;
    vec2 far_sum = (ripple(p * 0.023 + vec2(0.006, -0.004) * t) + ripple(vec2(-p.y, p.x) * 0.047 + vec2(-0.005, -0.007) * t) - 1.0) * 1.4;
    float blend = min(0.2 + length(view_position) * 0.001, 1.0);
    vec2 n = ((near_sum * 2.0 - far_sum) * blend + far_sum) * RIPPLE_STRENGTH;
    vec2 n_unit = n * inversesqrt(dot(n, n) + 1.0);

    // The water is horizontal: its tangent axes are world X and Z, and the ripples tilt it from world up.
    vec3 up = normalize((view_matrix * vec4(0.0, 1.0, 0.0, 0.0)).xyz);
    vec3 axis_x = (view_matrix * vec4(1.0, 0.0, 0.0, 0.0)).xyz;
    vec3 axis_z = (view_matrix * vec4(0.0, 0.0, 1.0, 0.0)).xyz;
    vec3 surface = up + (axis_x * n_unit.x + axis_z * n_unit.y) * RIPPLE_SPECULAR_SCALE;

    vec3 sun = normalize((view_matrix * vec4(client_sun_dir, 0.0)).xyz);
    vec3 lit = client_sun_ambient + client_sun_diffuse * clamp(dot(-sun, up), 0.0, 1.0);
    vec4 gradient_color = water_gradient(clamp(depth_v + 0.1 * abs(n_unit.y), 0.0, 1.0));
    vec3 color = lit * gradient_color.rgb;

    vec3 to_eye = -normalize(view_position);
    float fresnel = 1.0 - clamp(dot(to_eye, surface), 0.0, 1.0);
    fresnel *= fresnel;
    color = mix(color, client_fog_color, 0.1 + 0.5 * fresnel);

    vec3 half_vector = normalize(-sun + to_eye);
    float glint = pow(clamp(dot(half_vector, surface), 0.0, 1.0), RETAIL_SPECULAR_EXPONENT) + 0.2;
    color += fresnel * glint * client_sun_specular;
    return vec4(color, min(gradient_color.a * 1.25, 1.0));
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
    } else if (has_bump) {
        vec4 water = retail_water(depth_coordinate(round(COLOR.a * 255.0)) * depth_scale, VERTEX, VIEW_MATRIX);
        ALBEDO = gamma_to_linear(water.rgb);
        ALPHA = water.a;
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
    FOG = client_fog_output(VERTEX);
}
