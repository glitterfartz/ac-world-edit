shader_type spatial;
render_mode unshaded, blend_mix, depth_draw_never, cull_disabled;
#include "res://Shaders/client_lighting.gdshaderinc"

// Liquids, in two water models and a magma/slime path, chosen per surface:
//
//   water_wrath   the 3.3.5a client's psLiquidWater (ps_3_0): animated frames added to the lit gradient.
//   water_retail  the retail client's "mediumwater" (ps_5_0): ripple normal map, fresnel, sun glint.
//   magma/slime   vertex color times the animated frames, unlit and opaque (both clients).
// Retail swamp material 14 uses swamp.gdshader (screen/depth inputs isolated from other liquids).
// Both retail models also have an underwater face ("mediumwaterbelow", "procwaterbelow"), chosen per
// fragment when the eye sits below the surface; the 3.3.5a client had no such variant.
//
// A surface uses the retail model when its LiquidType names a ripple normal map (retail data does, 3.3.5a
// data does not); see LiquidRendering.Configure. The depth gradient, lighting and fog are shared.
//
// Everything is computed on the 8-bit (gamma-space) band values, as both clients do, and only the finished
// color is converted to the linear space Godot expects.

// ---- Shared inputs ----------------------------------------------------------------------------------------

global uniform vec4 client_water_ocean_shallow;
global uniform vec4 client_water_ocean_deep;
global uniform vec4 client_water_river_shallow;
global uniform vec4 client_water_river_deep;

// LiquidType sound bank: 0 water, 1 ocean, 2 magma, 3 slime.
uniform int liquid_kind = 0;
// Which water model draws the surface (WATER_MODEL_*).
const int WATER_MODEL_WRATH = 0;
const int WATER_MODEL_RETAIL = 1;
uniform int water_model = 0;
// Which procedural depth gradient the type names: 0 river/lake, 1 ocean, 2 WMO water.
uniform int gradient = 0;
// Which depth table maps the stored depth byte to the gradient (LiquidType int 0): 0 river, 1 ocean.
uniform int depth_table = 0;
// Depth scale (LiquidType float 2).
uniform float depth_scale = 1.0;

// The animated native frames side by side. The 3.3.5a water adds their rgb to the lit gradient and masks its
// highlight with their alpha; magma and slime draw them directly. Sampled raw (no sRGB conversion).
uniform sampler2D albedo_tex : filter_linear, repeat_disable;
uniform float frames = 1.0;
// Milliseconds the frames take to loop (LiquidType int 1).
uniform float period_ms = 1250.0;
// Frame texture scale and rotation in radians (LiquidType floats 0 and 1).
uniform float uv_scale = 1.0;
uniform float uv_angle = 0.0;
// How fast magma and slime scroll their frames, in texture widths per second along U and V (LiquidType floats 0
// and 1; Liquid::GetTexScrollMtx). Water leaves it at zero.
uniform vec2 scroll_speed = vec2(0.0);

#include "res://Shaders/liquid_surface.gdshaderinc"

const int GRADIENT_ROWS = 64;

// ---- 3.3.5a water -----------------------------------------------------------------------------------------

const float WRATH_SPECULAR_EXPONENT = 6.0;

// ---- Retail water -----------------------------------------------------------------------------------------

// The ripple normal map (LiquidType texture 2), set only for surfaces that use the retail model.
uniform sampler2D retail_ripple_tex : filter_linear_mipmap, repeat_enable;
// The retail shader reads these from its constant buffer; the values are chosen to look like the stock
// water, because the buffer layout is not in the shipped bytecode.
const float RETAIL_RIPPLE_SPECULAR_SCALE = 1.0;
const float RETAIL_SPECULAR_EXPONENT = 16.0;

// ---- Shared helpers ---------------------------------------------------------------------------------------

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

// ---- 3.3.5a water -----------------------------------------------------------------------------------------

// psLiquidWater: (ambient + diffuse * N.L) times the depth gradient, plus the frame texture and its masked
// highlight (Blinn-Phong, exponent 6, the sun band as color). The surface normal is straight up, as the
// client builds it. Returns rgb in gamma space and the gradient alpha.
vec4 water_wrath(vec4 frame, float depth_v, vec3 view_position, mat4 view_matrix, vec3 normal) {
    vec3 sun = normalize((view_matrix * vec4(client_sun_dir, 0.0)).xyz);
    vec3 lit = client_sun_ambient + client_sun_diffuse * clamp(dot(-sun, normal), 0.0, 1.0);
    vec3 half_vector = normalize(normalize(view_position) + sun);
    float highlight = pow(max(dot(-half_vector, normal), 0.0), WRATH_SPECULAR_EXPONENT);
    vec4 depth_color = water_gradient(depth_v);
    return vec4(lit * depth_color.rgb + frame.rgb + (client_sun_specular * highlight + 0.25) * frame.a, depth_color.a);
}

// ---- Retail water -----------------------------------------------------------------------------------------

// "mediumwater": two pairs of scrolling ripple samples (a near pair and a coarser far pair, blended by
// camera distance) bend the surface; fresnel mixes the lit gradient toward the sky color, and the sun adds
// a specular glint. The animated frames are not used. Returns rgb in gamma space and the alpha the
// surface blends with.
//
// "mediumwaterbelow" is the same surface seen from under the water: the ripple normal turns to face the
// viewer, the lit gradient takes the surface in proportion to the gradient's own alpha (a tenth less at
// grazing angles) instead of the sky, and the glint loses its floor and 60% of its strength behind a
// fourth-power fresnel. The alpha is the gradient's own, which "mediumwater" stretches by 1.25.
vec4 water_retail(float depth_v, vec3 view_position, mat4 view_matrix, bool below) {
    // "mediumwaterbelow" keeps the above-water near/far ripple blend; only procwaterbelow folds the pairs.
    vec2 n_unit = liquid_ripple(retail_ripple_tex, world_position.xz, TIME, length(view_position), 1.0, false);

    // The client lights both faces with a stored scalar and a distance falloff; the sun's elevation is this
    // port's stand-in.
    vec3 lit = client_sun_ambient + client_sun_diffuse * clamp(-client_sun_dir.y, 0.0, 1.0);
    vec4 gradient_color = water_gradient(clamp(depth_v + 0.1 * abs(n_unit.y), 0.0, 1.0));

    // The water is horizontal: its tangent axes are world X and Z, and the ripples tilt it from world up.
    // From below the surface faces down at the viewer, the way the client builds its normal.
    vec3 up = normalize((view_matrix * vec4(0.0, 1.0, 0.0, 0.0)).xyz) * (below ? -1.0 : 1.0);
    vec3 axis_x = (view_matrix * vec4(1.0, 0.0, 0.0, 0.0)).xyz;
    vec3 axis_z = (view_matrix * vec4(0.0, 0.0, 1.0, 0.0)).xyz;
    vec3 surface = up + (axis_x * n_unit.x + axis_z * n_unit.y) * RETAIL_RIPPLE_SPECULAR_SCALE;

    vec3 sun = normalize((view_matrix * vec4(client_sun_dir, 0.0)).xyz);
    vec3 to_eye = -normalize(view_position);
    float fresnel = 1.0 - clamp(dot(to_eye, surface), 0.0, 1.0);
    fresnel *= fresnel;

    vec3 half_vector = normalize(-sun + to_eye);
    float glint = pow(clamp(dot(half_vector, surface), 0.0, 1.0), RETAIL_SPECULAR_EXPONENT);
    vec3 body = lit * gradient_color.rgb;

    if (below) {
        vec3 underwater = mix(client_fog_color, body, gradient_color.a * (1.0 - 0.1 * fresnel));
        underwater += 0.4 * fresnel * fresnel * gradient_color.a * glint * client_sun_specular;
        return vec4(underwater, gradient_color.a);
    }

    vec3 color = mix(body, client_fog_color, 0.1 + 0.5 * fresnel);
    color += fresnel * (glint + 0.2) * client_sun_specular;
    return vec4(color, min(gradient_color.a * 1.25, 1.0));
}

// ---- Fragment ---------------------------------------------------------------------------------------------

void fragment() {
    float frame = mod(floor(mod(TIME * 1000.0, period_ms) / period_ms * frames), frames);
    vec2 size = vec2(textureSize(albedo_tex, 0));
    float s = sin(uv_angle);
    float c = cos(uv_angle);
    vec2 scaled = UV * uv_scale + fract(TIME * scroll_speed);
    vec2 tile = fract(vec2(c * scaled.x - s * scaled.y, s * scaled.x + c * scaled.y));
    // Clamp inside each frame to avoid sampling its neighbor in the atlas.
    vec2 inset = vec2(0.5 * frames / size.x, 0.5 / size.y);
    tile = mix(inset, vec2(1.0) - inset, tile);
    vec4 frame_color = texture(albedo_tex, vec2((frame + tile.x) / frames, tile.y));

    if (liquid_kind >= 2) {
        // Magma and slime: vertex color (white) times the frame, unlit and opaque.
        ALBEDO = gamma_to_linear(frame_color.rgb);
        ALPHA = 1.0;
    } else {
        float depth_v = depth_coordinate(round(COLOR.a * 255.0)) * depth_scale;
        // The eye under the surface means the underside is what is drawn, for the retail models only.
        bool below = water_model == WATER_MODEL_RETAIL && INV_VIEW_MATRIX[3].y < world_position.y;
        vec4 water = water_model == WATER_MODEL_RETAIL
            ? water_retail(depth_v, VERTEX, VIEW_MATRIX, below)
            : water_wrath(frame_color, depth_v, VERTEX, VIEW_MATRIX, NORMAL);
        ALBEDO = gamma_to_linear(water.rgb);
        ALPHA = water.a;
    }
    FOG = client_fog_output(VERTEX, INV_VIEW_MATRIX);
}
