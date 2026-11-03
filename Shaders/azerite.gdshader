shader_type spatial;
render_mode unshaded, blend_mix, depth_draw_never, cull_disabled;
#include "res://Shaders/client_lighting.gdshaderinc"

// Retail azerite_0 (material 18): mix(deep, center, saturate(1 - depth*CB1[12].z)),
// then client fog. Both body layers are static textures sampled at the native liquid
// UV; only their scroll speeds animate.
// LiquidType slots 0 and 1 (azeritedeep, azeritecentercolor) are the two body layers;
// which shader unit each names is inferred from the textures, not recovered.
// Floats 2..5 are not applied: reading them as a scale/angle triple tiled the centre
// texture about 20 times per layer. Floats 0 and 1 are the layers' inferred scroll
// speeds; the shipped rows carry the same value in both, so which belongs to which
// layer cannot be confirmed from the data.
// The depth coefficient's source is not in the table, so it stays a fixed estimate.
// Unported: azerite_1's mask layers, procazerite's procedural body, the
// azeriteblend/transcolor/anim slots, and the per-light medium loop.
uniform sampler2D deep_tex : filter_linear_mipmap, repeat_enable;
uniform sampler2D center_tex : filter_linear_mipmap, repeat_enable;
uniform float deep_scroll = 0.0;
uniform float center_scroll = 0.0;
uniform float depth_coefficient = 1.0;

vec2 layer_uv(vec2 uv, float scroll, float time) {
    return uv + vec2(time * scroll, 0.0);
}

void fragment() {
    vec3 deep = texture(deep_tex, layer_uv(UV, deep_scroll, TIME)).rgb;
    vec3 center = texture(center_tex, layer_uv(UV, center_scroll, TIME)).rgb;
    // Native depth arrives as the vertex colour alpha; shallow (0) shows the centre colour.
    float blend = clamp(1.0-COLOR.a*depth_coefficient, 0.0, 1.0);
    ALBEDO = client_gamma_to_linear(mix(deep, center, blend));
    ALPHA = 1.0;
    FOG = client_fog_output(VERTEX, INV_VIEW_MATRIX);
}
