shader_type spatial;
render_mode unshaded, blend_mix, depth_draw_never, cull_disabled;
#include "res://Shaders/shared/client_lighting.gdshaderinc"

uniform sampler2D albedo_tex : filter_linear, repeat_disable;
uniform sampler2D mask_tex : filter_linear_mipmap, repeat_enable;
uniform float frames = 1.0;
uniform float period_ms = 1250.0;
uniform vec3 fog_tint = vec3(1.0);
uniform float opacity_scale = 0.5;
uniform bool has_mask = false;

vec4 density_sample(vec2 uv, float phase) {
    vec2 size = vec2(textureSize(albedo_tex, 0));
    vec2 inset = vec2(0.5*frames/size.x, 0.5/size.y);
    vec2 tile = mix(inset, vec2(1.0)-inset, fract(uv));
    return texture(albedo_tex, vec2((floor(phase*frames)+tile.x)/frames,tile.y));
}

void fragment() {
    float phase = fract(TIME*1000.0/max(period_ms,1.0));
    // The VS offsets the second UV by .5 before CPU-supplied matrices.
    // Matrix scales/directions/rates are unavailable; these motions are estimates.
    vec4 first = density_sample(UV+vec2(.01,0.0)*TIME,phase);
    vec4 second = density_sample(UV+vec2(.5)+vec2(0.0,-.01)*TIME,phase);
    // liquidfog_0: swizzled MAD applies the same (a*b+b) to RGB and alpha,
    // then tint RGB and scale saturated alpha with the .3 pedestal and .5 factor.
    // liquidfog_1 (no CB1[9].y light loop: the shipped mask-only path) multiplies the
    // alpha by the red channel of the auxiliary mask texture (t15). The mask's own UV
    // motion is a CPU matrix and is estimated here as a slow drift like the density.
    vec3 color = (first.rgb*second.rgb+second.rgb)*fog_tint;
    float density = clamp(first.a*second.a+second.a,0.0,1.0);
    ALBEDO = client_gamma_to_linear(color);
    float alpha = clamp((density*opacity_scale+.3)*.5,0.0,1.0);
    if (has_mask) alpha *= texture(mask_tex, UV+vec2(-.01,.005)*TIME).r;
    ALPHA = alpha;
    FOG = client_fog_output(VERTEX,INV_VIEW_MATRIX);
}
