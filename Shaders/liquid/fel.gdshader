shader_type spatial;
render_mode unshaded, blend_mix, depth_draw_never, cull_disabled;
#include "res://Shaders/shared/client_lighting.gdshaderinc"

// Retail fel_0: mix(deep, shallow, saturate(1-depth*CB1[12].z)), then fog.
// LiquidType float 14 -> depth coefficient is inferred, not recovered CPU code.
// procfel_0 (the animated pass, same DB2 rows): the slot-5 normal sequence is sampled
// twice and crossfaded (t3/t4, blend CB1[13].x), the warped normal offsets the base
// layer UVs (strength CB1[12].w) and shades the sun diffuse, and the slot-4 emissive
// overlay is sampled twice and added masked ((a+b)*a.a*b.a, added times its alpha).
// procfel_2/3 add the slot-2 specular and slot-3 shore on top of that body:
// specular is the same masked add sampled twice with animated UVs, scaled by native
// depth (v5.y * CB1[13].z); shore lerps toward the slot-3 texel by a depth factor
// (t6 resolve minus surface height, CB1[12].y) times the shore alpha. The t6 depth
// resolve is unrecoverable, so scene depth below the surface stands in (estimated
// sign and scale); specular strength and shore scale are estimates. The CB1[9].y
// per-light loop, the t16 3D-LUT pass and the fel_2/3 non-proc twins (same math
// behind their light loops) are not ported. Overlay slot (emissive), normal
// blend/strength/depth-fade (CB1[13]), warp strength (CB1[12].w) and animation rate
// are estimates; only the slot order, the two-frame crossfade shape and the masked
// add/shore-lerp formulas come from bytecode.
uniform sampler2D albedo_tex : filter_linear, repeat_disable;
uniform sampler2D shallow_tex : filter_linear_mipmap, repeat_enable;
uniform sampler2D overlay_tex : filter_linear_mipmap, repeat_enable;
uniform sampler2D normal_tex : filter_linear, repeat_enable;
uniform sampler2D specular_tex : filter_linear_mipmap, repeat_enable;
uniform sampler2D shore_tex : filter_linear_mipmap, repeat_enable;
uniform sampler2D scene_color : hint_screen_texture, filter_linear, repeat_disable;
uniform sampler2D scene_depth : hint_depth_texture, filter_nearest, repeat_disable;
// view_depth comes from liquid_reflection.gdshaderinc (its screen-trace reflection is
// deliberately unused: procfel has no reflection input, only the depth resolve).
#include "res://Shaders/liquid/liquid_reflection.gdshaderinc"
uniform float frames = 1.0;
uniform float period_ms = 1250.0;
uniform vec3 deep_uv = vec3(1.0, 0.0, 0.0);
uniform vec3 shallow_uv = vec3(1.0, 0.0, 0.0);
uniform float depth_coefficient = 1.0;
uniform bool has_overlay = false;
uniform bool has_normals = false;
uniform float normal_frames = 1.0;
uniform float normal_fps = 10.0;
uniform float normal_scale = 1.0;
uniform float warp_strength = 0.06;
uniform bool has_specular = false;
uniform bool has_shore = false;
uniform float specular_strength = 2.0;
uniform float shore_depth_scale = 0.1;

vec2 layer_uv(vec2 uv, vec3 settings, float time) {
    float angle = radians(settings.y);
    float c = cos(angle);
    float s = sin(angle);
    vec2 p = uv * settings.x;
    // CPU texture-matrix scroll direction is estimated along rotated U.
    return vec2(c*p.x-s*p.y, s*p.x+c*p.y) + vec2(time*settings.z, 0.0);
}

vec4 normal_frame(vec2 uv, float frame) {
    vec2 size = vec2(textureSize(normal_tex, 0));
    float count = max(normal_frames, 1.0);
    vec2 inset = vec2(0.5 / size.x, 0.5 / size.y);
    vec2 tile = mix(inset, vec2(1.0) - inset, fract(uv));
    return texture(normal_tex, vec2((mod(frame, count) + tile.x) / count, tile.y));
}

// procfel samples two consecutive normal frames and crossfades them:
// n = normalize(mix(n0, n1, fract) * 2 - 1), z = sqrt(1 - |xy|^2).
vec3 ripple(vec2 uv, float time) {
    if (!has_normals) {
        return vec3(0.0, 0.0, 1.0);
    }
    float phase = time * normal_fps;
    vec2 a = normal_frame(uv, floor(phase)).rg * 2.0 - 1.0;
    vec2 b = normal_frame(uv, floor(phase) + 1.0).rg * 2.0 - 1.0;
    vec2 slope = mix(a, b, fract(phase));
    float z2 = max(1.0 - dot(slope, slope), 0.0);
    return normalize(vec3(slope, sqrt(z2)));
}

void fragment() {
    float frame = mod(floor(TIME*1000.0/max(period_ms, 1.0)*frames), frames);
    vec2 size = vec2(textureSize(albedo_tex, 0));
    vec2 inset = vec2(0.5*frames/size.x, 0.5/size.y);
    // The warped normal offsets the base UVs (procfel r1.xy = CB1[12].w * n.xy + v2.xy).
    // Without normals the warp is zero and this is exactly fel_0.
    vec3 wave = ripple(layer_uv(UV, deep_uv, TIME) * normal_scale, TIME);
    vec2 warp = wave.xy * warp_strength;
    vec2 uv = mix(inset, vec2(1.0)-inset, fract(layer_uv(UV + warp, deep_uv, TIME)));
    vec3 deep = texture(albedo_tex, vec2((frame+uv.x)/frames, uv.y)).rgb;
    vec3 shallow = texture(shallow_tex, layer_uv(UV + warp, shallow_uv, TIME)).rgb;
    float blend = clamp(1.0-COLOR.a*depth_coefficient, 0.0, 1.0);
    vec3 body = mix(deep, shallow, blend);

    if (has_normals) {
        // The warped normal shades the body with the client's sun, normalised by the
        // flat-surface light so only the variation shows (same idiom as azerite).
        vec3 normal_world = normalize(vec3(wave.x, wave.z, wave.y));
        vec3 lit = client_sun_ambient + client_sun_diffuse * max(dot(-client_sun_dir, normal_world), 0.0);
        vec3 lit_flat = client_sun_ambient + client_sun_diffuse * max(dot(-client_sun_dir, vec3(0.0, 1.0, 0.0)), 0.0);
        body *= mix(vec3(1.0), lit / max(lit_flat, vec3(0.001)), 1.0);
    }

    if (has_overlay) {
        // procfel masked overlay add: (a+b)*a.a*b.a, added times its own alpha.
        // The client samples the overlay twice with two vertex UV sets (v4.xy/v4.zw);
        // the second set is a CPU matrix, estimated here as a coarser drifted tiling.
        vec2 ouv = UV + warp * 0.7;
        vec4 o1 = texture(overlay_tex, ouv + vec2(TIME * 0.004, -TIME * 0.003));
        vec4 o2 = texture(overlay_tex, ouv * 1.7 + vec2(-TIME * 0.007, TIME * 0.005));
        vec3 omask = (o1.rgb + o2.rgb) * o1.a * o2.a;
        float oalpha = (o1.a + o2.a) * o1.a * o2.a;
        body += omask * oalpha;
    }

    if (has_specular) {
        // procfel_2 specular (slot 2, t2): the same masked add, sampled twice with the
        // warped UVs at two tilings (the client's two CB1[25] UV sets are estimated);
        // the shipped strength scales with native depth.
        vec2 suv = UV + warp;
        vec4 s1 = texture(specular_tex, layer_uv(suv, shallow_uv, TIME) * 1.3 + vec2(TIME * 0.006, -TIME * 0.004));
        vec4 s2 = texture(specular_tex, layer_uv(suv, shallow_uv, TIME) * 2.1 + vec2(-TIME * 0.009, TIME * 0.007));
        vec3 smask = (s1.rgb + s2.rgb) * s1.a * s2.a;
        body += smask * clamp(COLOR.a * specular_strength, 0.0, 1.0);
    }

    if (has_shore) {
        // procfel_2/3 shore (slot 3, t5): lerp toward the shore texel by a depth factor
        // times the shore alpha. The client tests the t6 depth resolve against the surface
        // height; scene depth below the surface stands in here (estimated sign/scale).
        // The shore UV sweeps vertically with the factor, mirroring the bytecode.
        float depth_below = view_depth(SCREEN_UV, INV_PROJECTION_MATRIX) + VERTEX.z;
        float shore_factor = clamp(depth_below * shore_depth_scale, 0.0, 1.0);
        vec4 shore = texture(shore_tex, vec2(UV.x, 1.0 - shore_factor));
        body = mix(body, shore.rgb, shore_factor * shore.a);
    }

    ALBEDO = client_gamma_to_linear(body);
    ALPHA = 1.0;
    FOG = client_fog_output(VERTEX, INV_VIEW_MATRIX);
}
