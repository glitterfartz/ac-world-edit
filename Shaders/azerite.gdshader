shader_type spatial;
render_mode unshaded, blend_mix, depth_draw_never, cull_disabled;
#include "res://Shaders/client_lighting.gdshaderinc"

// Retail azerite_0 (material 18): mix(deep, center, saturate(1 - depth*CB1[12].z)),
// opaque and unlit, then client fog.
// LiquidType slots 0 and 1 (azeritedeep, azeritecentercolor) are the two body layers;
// which shader unit each names is inferred from the textures, not recovered.
// Both layers sample the native liquid UV at unit scale. The client's per-layer
// texture matrices are CPU constants that are not in the table; LiquidType floats 2..5
// are not applied because reading them as a scale/angle triple tiled this texture
// about 20 times per layer. Floats 0 and 1 are the layers' inferred scroll speeds.
// The depth coefficient's source is not in the table, so it stays a fixed estimate.
// Unported: azerite_1's mask layers, procazerite's procedural body, the
// azeriteblend/transcolor/anim slots, and the per-light medium loop.
uniform sampler2D albedo_tex : filter_linear, repeat_disable;
uniform sampler2D center_tex : filter_linear_mipmap, repeat_enable;
uniform float frames = 1.0;
uniform float period_ms = 1250.0;
uniform float uv_scale = 1.0;
uniform float deep_scroll = 0.0;
uniform float center_scroll = 0.0;
uniform float depth_coefficient = 1.0;

vec2 layer_uv(vec2 uv, float scroll, float time) {
    return uv * uv_scale + vec2(time * scroll, 0.0);
}

void fragment() {
    float frame = mod(floor(TIME*1000.0/max(period_ms, 1.0)*frames), frames);
    vec2 size = vec2(textureSize(albedo_tex, 0));
    vec2 inset = vec2(0.5*frames/size.x, 0.5/size.y);
    vec2 uv = mix(inset, vec2(1.0)-inset, fract(layer_uv(UV, deep_scroll, TIME)));
    vec3 deep = texture(albedo_tex, vec2((frame+uv.x)/frames, uv.y)).rgb;
    vec3 center = texture(center_tex, layer_uv(UV, center_scroll, TIME)).rgb;
    // Native depth arrives as the vertex colour alpha; shallow (0) shows the centre colour.
    float blend = clamp(1.0-COLOR.a*depth_coefficient, 0.0, 1.0);
    ALBEDO = client_gamma_to_linear(mix(deep, center, blend));
    ALPHA = 1.0;
    FOG = client_fog_output(VERTEX, INV_VIEW_MATRIX);
}
