shader_type spatial;
render_mode unshaded, blend_mix, depth_draw_never, cull_disabled;
#include "res://Shaders/client_lighting.gdshaderinc"

uniform sampler2D ripple_tex : filter_linear_mipmap, repeat_enable;
uniform sampler2D scene_color : hint_screen_texture, filter_linear, repeat_disable;
uniform sampler2D scene_depth : hint_depth_texture, filter_nearest, repeat_disable;
uniform bool has_normals = false;
// CPU ripple matrices and highlight exponent are unavailable: these are estimates.
uniform float normal_strength = 1.0;
uniform float specular_power = 16.0;
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

float view_depth(vec2 uv, mat4 inv_projection) {
    float depth = texture(scene_depth, uv).r;
    // Reverse-Z sky is 0. Do not divide its infinite-far homogeneous point by 0.
    if (depth <= 0.000001) return 1000000.0;
    vec3 ndc = vec3(uv * 2.0 - 1.0, depth);
    #if CURRENT_RENDERER == RENDERER_COMPATIBILITY
        ndc.z = depth * 2.0 - 1.0;
    #endif
    vec4 position = inv_projection * vec4(ndc, 1.0);
    return -position.z / position.w;
}

// Retail supplies a separate reflected scene/sky texture. This viewer substitutes
// bounded screen-space tracing, falling back to the region's sky/fog colour when
// the ray leaves the screen or sees geometry not present in the depth buffer.
vec3 reflection_color(vec3 position, vec3 direction, mat4 projection, mat4 inv_projection) {
    vec3 fallback = client_gamma_to_linear(client_fog_color);
    float previous = -1.0;
    for (int i = 0; i < 20; i++) {
        float distance = 0.5 + float(i * i) * 0.3;
        vec3 point = position + direction * distance;
        if (point.z >= -0.1) break;
        vec4 clip = projection * vec4(point, 1.0);
        vec2 uv = clip.xy / clip.w * 0.5 + 0.5;
        if (any(lessThan(uv, vec2(0.002))) || any(greaterThan(uv, vec2(0.998)))) break;
        float gap = -point.z - view_depth(uv, inv_projection);
        if (gap >= 0.0 && previous < 0.0 && gap < max(0.5, distance * 0.05)) {
            float edge = min(min(uv.x, uv.y), min(1.0 - uv.x, 1.0 - uv.y));
            return mix(fallback, texture(scene_color, uv).rgb, smoothstep(0.0, 0.05, edge));
        }
        previous = gap;
    }
    return fallback;
}


void fragment() {
    vec3 normal_world = surface_normal(world_position.xz, TIME, length(VERTEX));
    vec3 normal_view = normalize((VIEW_MATRIX*vec4(normal_world,0.0)).xyz);
    vec3 to_eye = normalize(-VERTEX);
    float fresnel2 = pow(1.0-clamp(dot(to_eye,normal_view),0.0,1.0),2.0);
    float fresnel4 = fresnel2*fresnel2;
    // Retail blends two separate reflection inputs. A single bounded screen trace
    // replaces both, with client fog/sky colour for offscreen or missing geometry.
    vec3 reflected = linear_to_gamma(reflection_color(VERTEX,reflect(-to_eye,normal_view),PROJECTION_MATRIX,INV_PROJECTION_MATRIX));
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
