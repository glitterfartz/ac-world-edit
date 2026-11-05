shader_type spatial;
render_mode unshaded, blend_mix, depth_draw_never, cull_disabled;
#include "res://Shaders/shared/client_lighting.gdshaderinc"

// leyline_0: P = a0*a1; rgb = 2*P*c0*c1 + (1-P)*base;
// alpha = (2*P+0.5)*vertexOpacity, followed by client fog.
// DB2 color/UV bindings are inferred. Rotated-U scroll, native-depth opacity
// and the shared 1250ms atlas period substitute for unidentified CPU inputs.
// procleyline screen/reflection/normal/shore passes remain unported.
uniform sampler2D albedo_tex : filter_linear, repeat_disable;
uniform sampler2D detail_tex : filter_linear, repeat_disable;
uniform float frames = 1.0;
uniform float detail_frames = 1.0;
uniform float period_ms = 1250.0;
uniform vec3 first_uv = vec3(1.0, 0.0, 0.0);
uniform vec3 second_uv = vec3(1.0, 0.0, 0.0);
uniform vec3 base_color = vec3(0.0);

vec2 layer_uv(vec2 uv, vec3 settings, float time) {
    float a = radians(settings.y);
    vec2 p = uv*settings.x;
    return vec2(cos(a)*p.x-sin(a)*p.y, sin(a)*p.x+cos(a)*p.y) + vec2(time*settings.z, 0.0);
}

vec2 atlas_uv(vec2 uv, vec2 size, float count, float phase) {
    vec2 inset = vec2(0.5*count/size.x, 0.5/size.y);
    vec2 tile = mix(inset, vec2(1.0)-inset, fract(uv));
    return vec2((floor(phase*count)+tile.x)/count, tile.y);
}

void fragment() {
    float phase = fract(TIME*1000.0/max(period_ms, 1.0));
    vec4 first = texture(albedo_tex, atlas_uv(layer_uv(UV, first_uv, TIME), vec2(textureSize(albedo_tex,0)), frames, phase));
    vec4 second = texture(detail_tex, atlas_uv(layer_uv(UV, second_uv, TIME), vec2(textureSize(detail_tex,0)), detail_frames, phase));
    float product = first.a*second.a;
    vec3 color = 2.0*product*first.rgb*second.rgb + (1.0-product)*base_color;
    ALBEDO = client_gamma_to_linear(color);
    ALPHA = clamp((2.0*product+0.5)*COLOR.a, 0.0, 1.0);
    FOG = client_fog_output(VERTEX, INV_VIEW_MATRIX);
}
