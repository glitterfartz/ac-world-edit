shader_type spatial;
render_mode unshaded, blend_mix, depth_draw_never, cull_disabled;
#include "res://Shaders/client_lighting.gdshaderinc"

uniform sampler2D albedo_tex : filter_linear, repeat_disable;
uniform sampler2D middle_tex : filter_linear_mipmap, repeat_enable;
uniform sampler2D top_tex : filter_linear_mipmap, repeat_enable;
uniform float frames = 1.0;
uniform float period_ms = 1250.0;
uniform vec3 layer_scales = vec3(.55,.45,.35);
uniform vec3 base_color = vec3(1.0,.4,0.0);
uniform vec2 scroll_speed = vec2(.025,0.0);
uniform float noise_speed = .05;
varying float noise_blend;

// Substitute a bounded analytic value field for the unidentified client 3D noise.
// Hash, channel offsets, unit noise scale and UV directions are estimates.
float noise_hash(vec3 p) { return fract(sin(dot(p,vec3(127.1,311.7,74.7)))*43758.5453); }
float value_noise(vec3 p) {
    vec3 i = floor(p);
    vec3 f = fract(p);
    f = f*f*(3.0-2.0*f);
    return mix(mix(mix(noise_hash(i),noise_hash(i+vec3(1,0,0)),f.x),
        mix(noise_hash(i+vec3(0,1,0)),noise_hash(i+vec3(1,1,0)),f.x),f.y),
        mix(mix(noise_hash(i+vec3(0,0,1)),noise_hash(i+vec3(1,0,1)),f.x),
        mix(noise_hash(i+vec3(0,1,1)),noise_hash(i+vec3(1,1,1)),f.x),f.y),f.z);
}
vec3 noise_channels(vec3 p) {
    return vec3(value_noise(p),value_noise(p+vec3(17,31,13)),value_noise(p+vec3(41,7,23)));
}
void vertex() {
    // procmagma VS: three harmonic pairs, each half-weighted, then *.15 + .5.
    vec2 p = UV+scroll_speed*TIME;
    float wave = .5*(cos(p.x)+sin(p.y)+cos(2.0*p.x)+sin(2.0*p.y)+cos(3.0*p.x)+sin(3.0*p.y));
    noise_blend = wave*.15+.5;
}
void fragment() {
    vec2 shift = TIME*scroll_speed;
    vec2 p = UV+shift;
    vec3 noise = mix(noise_channels(vec3(p,TIME*noise_speed)),
        noise_channels(vec3(-p.yx+vec2(.5),TIME*noise_speed)),noise_blend);
    vec2 size = vec2(textureSize(albedo_tex,0));
    vec2 inset = vec2(.5*frames/size.x,.5/size.y);
    vec2 uv = mix(inset,vec2(1.0)-inset,fract(UV*layer_scales.x+shift));
    float frame = floor(fract(TIME*1000.0/max(period_ms,1.0))*frames);
    vec3 first = texture(albedo_tex,vec2((frame+uv.x)/frames,uv.y)).rgb;
    vec4 middle = texture(middle_tex,UV*layer_scales.y-shift.yx);
    vec4 top = texture(top_tex,UV*layer_scales.z+shift.yx);
    // procmagma_0: base -> first by noise.R, -> middle by noise.G*alpha,
    // then -> top by noise.B*alpha, opaque and unlit before client fog.
    vec3 color = mix(base_color,first,noise.r);
    color = mix(color,middle.rgb,noise.g*middle.a);
    color = mix(color,top.rgb,noise.b*top.a);
    ALBEDO = client_gamma_to_linear(color);
    ALPHA = 1.0;
    FOG = client_fog_output(VERTEX,INV_VIEW_MATRIX);
}
