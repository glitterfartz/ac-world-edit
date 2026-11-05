shader_type spatial;
render_mode unshaded, blend_mix, depth_draw_never, cull_disabled;
#include "res://Shaders/shared/client_lighting.gdshaderinc"

uniform sampler2D ripple_tex : filter_linear_mipmap, repeat_enable;
uniform sampler2D scene_color : hint_screen_texture, filter_linear, repeat_disable;
uniform sampler2D scene_depth : hint_depth_texture, filter_nearest, repeat_disable;
uniform bool has_normals = false;
// CPU ripple matrices and highlight exponent are unavailable: these are estimates.
uniform float normal_strength = 1.0;
uniform float specular_power = 16.0;

#include "res://Shaders/liquid/liquid_reflection.gdshaderinc"

varying vec3 world_position;

void vertex() {
    world_position = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

vec3 linear_to_gamma(vec3 c) {
    c = max(c, vec3(0.0));
    return mix(1.055*pow(c, vec3(1.0/2.4))-0.055, c*12.92, vec3(lessThanEqual(c, vec3(0.0031308))));
}

vec3 surface_normal(vec2 p, float time, float distance) {
    if (!has_normals) return vec3(0.0, 1.0, 0.0);
    // procmercury: four RG samples, far amplitude 1.4 and distance blend .2+d*.001.
    // UV scales and motion substitute for stripped CPU texture matrices.
    vec2 near_sum = texture(ripple_tex, p*.09+vec2(.021,.013)*time).rg
        + texture(ripple_tex, vec2(p.y,-p.x)*.17+vec2(-.015,.019)*time).rg - 1.0;
    vec2 far_sum = (texture(ripple_tex, p*.023+vec2(.006,-.004)*time).rg
        + texture(ripple_tex, vec2(-p.y,p.x)*.047+vec2(-.005,-.007)*time).rg - 1.0)*1.4;
    vec2 slope = mix(far_sum, near_sum*2.0, min(.2+distance*.001,1.0))*normal_strength;
    slope *= inversesqrt(dot(slope,slope)+1.0);
    return normalize(vec3(slope.x,1.0,slope.y));
}


void fragment() {
    vec3 normal_world = surface_normal(world_position.xz, TIME, length(VERTEX));
    vec3 normal_view = normalize((VIEW_MATRIX*vec4(normal_world,0.0)).xyz);
    vec3 to_eye = normalize(-VERTEX);
    float fresnel2 = pow(1.0-clamp(dot(to_eye,normal_view),0.0,1.0),2.0);
    float fresnel4 = fresnel2*fresnel2;
    // Retail blends two separate reflection inputs. A single bounded screen trace
    // replaces both, with client fog/sky colour for offscreen or missing geometry.
    vec3 reflected = linear_to_gamma(liquid_reflection(VERTEX,reflect(-to_eye,normal_view),client_gamma_to_linear(client_fog_color),SCREEN_UV,fwidth(SCREEN_UV),PROJECTION_MATRIX,INV_PROJECTION_MATRIX));
    float luminance = dot(reflected,vec3(.3,.6,.1));
    vec3 gray = vec3(luminance);
    // procmercury_0: two desaturation lobes and the fixed .2 material pedestal.
    vec3 response = mix(reflected,gray,.3)*(.5*min(fresnel4+.9,1.0))
        + mix(reflected,gray,.7)*(.5*fresnel4);
    vec3 half_vector = normalize(-client_sun_dir+normalize((INV_VIEW_MATRIX*vec4(to_eye,0.0)).xyz));
    float highlight = pow(clamp(dot(half_vector,normal_world),0.0,1.0),specular_power);
    vec3 local = vec3(.2)+.3*highlight*(reflected+1.0)*fresnel4+vec3(.3*luminance*fresnel4);
    // Preserve the client gamma-space composition before converting once.
    // Screen captures already contain fog: their reflected contribution can be
    // attenuated again here, unlike retail's dedicated reflection inputs.
    ALBEDO = client_gamma_to_linear(response+local);
    FOG = client_fog_output(VERTEX,INV_VIEW_MATRIX);
    ALPHA = 1.0;
}
