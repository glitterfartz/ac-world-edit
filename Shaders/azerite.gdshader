shader_type spatial;
render_mode unshaded, blend_mix, depth_draw_never, cull_disabled;
#include "res://Shaders/client_lighting.gdshaderinc"

// Retail azerite_0 (material 18): mix(deep, center, saturate(1 - depth*CB1[12].z)),
// opaque and unlit, then client fog.
// LiquidType slots 0 and 1 (azeritedeep, azeritecentercolor) are read as the two
// body layers; which shader unit each names is inferred from the textures, not
// recovered. Floats 0..2 and 3..5 are each layer's scale, angle in degrees and
// scroll speed, read like material 13's triples. The depth coefficient's source
// is not in the table, so it stays a fixed estimate.
// Unported: azerite_1's mask layers, procazerite's procedural body, the
// azeriteblend/transcolor/anim slots, and the per-light medium loop.
uniform sampler2D albedo_tex : filter_linear, repeat_disable;
uniform sampler2D center_tex : filter_linear_mipmap, repeat_enable;
uniform float frames = 1.0;
uniform float period_ms = 1250.0;
uniform vec3 deep_uv = vec3(1.0, 0.0, 0.0);
uniform vec3 center_uv = vec3(1.0, 0.0, 0.0);
uniform float depth_coefficient = 1.0;

vec2 layer_uv(vec2 uv, vec3 settings, float time) {
    float angle = radians(settings.y);
    float c = cos(angle);
    float s = sin(angle);
    vec2 p = uv * settings.x;
    // CPU texture-matrix scroll direction is estimated along rotated U.
    return vec2(c*p.x-s*p.y, s*p.x+c*p.y) + vec2(time*settings.z, 0.0);
}

void fragment() {
    float frame = mod(floor(TIME*1000.0/max(period_ms, 1.0)*frames), frames);
    vec2 size = vec2(textureSize(albedo_tex, 0));
    vec2 inset = vec2(0.5*frames/size.x, 0.5/size.y);
    vec2 uv = mix(inset, vec2(1.0)-inset, fract(layer_uv(UV, deep_uv, TIME)));
    vec3 deep = texture(albedo_tex, vec2((frame+uv.x)/frames, uv.y)).rgb;
    vec3 center = texture(center_tex, layer_uv(UV, center_uv, TIME)).rgb;
    // Native depth arrives as the vertex colour alpha; shallow (0) shows the centre colour.
    float blend = clamp(1.0-COLOR.a*depth_coefficient, 0.0, 1.0);
    ALBEDO = client_gamma_to_linear(mix(deep, center, blend));
    ALPHA = 1.0;
    FOG = client_fog_output(VERTEX, INV_VIEW_MATRIX);
}
