shader_type spatial;
render_mode unshaded, blend_mix, depth_draw_never, cull_disabled;
#include "res://Shaders/shared/client_lighting.gdshaderinc"

// Retail azerite (material 18), recovered from the Midnight binary (base 0x7ff748f20000,
// build 70575): the render at 0x7ff74c6faf20 binds texture slots 0, 1 (body layers), 7, 8
// (shared defaults), 9, 2, 3, 4 (masks), then the slot-5 and slot-6 animated sequences.
// Slot descriptors are contiguous at settings+8+(slot+0x4e)*0x18. Per-slot constants land
// at CB +0x1f80/+0x1f84 (1.0/settings+0x728, guarded)/+0x1f88/+0x1f8c (from
// +0x71c/+0x728/+0x734/+0x738), +0x1f90 = 3, +0x1f94/+0x1f98 (from +0x73c/+0x718), anim
// phase at +0x1f9c over period settings+0x720 seconds, and the +0x6e4 UV-matrix block is
// copied to +0x1fc0. Which DB2 floats feed +0x718..+0x73c is still unknown, so K, fleck
// scale and the scroll split below stay tuned/estimated. The shipped pixel shaders split
// the surface into a three-stop depth ramp and warp every layer with an animated normal map:
//   deep      = azeritedeep        (slot 0, thick gold marble with cream ridges)
//   transcolor= azeritetranscolor  (slot 4, flat yellow transition band)
//   center    = azeritecentercolor (slot 1, blue speckle - the zero-depth fringe)
//   mask      = azeriteblend       (slot 2, cyan/white/pink detail flecks)
//   normals   = azeriteanim_%d.blp (slot 5, 30 animated normal frames)
// procazerite samples two consecutive normal frames and crossfades them (FUN_7ff74c6f9760
// returns the fractional phase; the count cycles over rate settings+0x72c seconds x 1000,
// the last frame wraps to 0; static slots bind with divisor 1 so they always use frame 0),
// then offsets every layer UV by CB1[12].x * normal.xy (refracted flow). azerite_1
// multiplies the blend by azeriteblend: blend = saturate(1 - mask * CB1[15].z * CB1[12].z).
//
// The shipped slot-3 azerite_basenormal is a flat placeholder (every DXT block is the
// same bytes), so it is not bound; the flow comes from the slot-5 sequence.
// Native depth arrives as the vertex-colour alpha, quantised per MH2O vertex. Reading
// it at 0..1 painted whole triangles blue wherever it dipped, which read as blotches,
// so it is scaled first (depth_coefficient, tuned, not recovered). The ramp is the
// client's linear form: w = saturate(1 - depth * K), body = lerp(deep, center, w),
// masked by the blend texture green channel in the azerite_1 shape when present.
// Per-layer UVs in the client are mesh UVs through stripped 2x2 matrices (vertex
// CB1[13..32]); identity-style defaults warped this pool in testing, so the layers
// keep the shared world-UV tiling until the matrices are bound from client data.
// The emissive boost approximates the client's self-lit pool; the per-light medium loop
// is not ported.
uniform sampler2D deep_tex : filter_linear_mipmap, repeat_enable;
uniform sampler2D center_tex : filter_linear_mipmap, repeat_enable;
uniform sampler2D transcolor_tex : filter_linear_mipmap, repeat_enable;
uniform sampler2D blend_tex : filter_linear_mipmap, repeat_enable;
uniform sampler2D normal_tex : filter_linear, repeat_enable;
uniform sampler2D scene_color : hint_screen_texture, filter_linear, repeat_disable;
uniform sampler2D scene_depth : hint_depth_texture, filter_nearest, repeat_disable;

uniform float deep_scroll = 0.36;
uniform float center_scroll = 0.36;
// Native depth is an 8-bit MH2O vertex value, so reading it at 0..1 painted whole
// triangles blue. The client ramps linearly (w = saturate(1 - depth * K)), but its
// depth input is mesh-UV2 units, not this 0..1 byte: K = 40 keeps the blue centre
// layer on the true zero-depth edge, matching the old smoothstep(0, 0.10) edge.
// Recovered as CB1[12].z once the settings fill is bound; until then this is tuned.
uniform float depth_coefficient = 40.0;
// Fleck-mask scale (client CB1[15].z in the azerite_1 permutation).
uniform float fleck_scale = 1.0;
uniform float normal_frames = 1.0;
uniform float normal_fps = 10.0;
uniform float normal_scale = 1.0;
uniform float warp_strength = 0.06;
uniform float flow_speed = 1.0;
uniform bool has_normals = false;
uniform bool has_masks = false;
uniform vec3 gold_color = vec3(1.0, 0.9, 0.68);
uniform vec3 center_color = vec3(0.30, 0.45, 0.55);
uniform float emission_strength = 0.08;
uniform float specular_strength = 1.0;
uniform float specular_power = 32.0;
uniform float reflection_strength = 0.2;

#include "res://Shaders/liquid/liquid_reflection.gdshaderinc"

varying vec3 world_position;

void vertex() {
    world_position = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

vec3 to_linear(vec3 c) {
    return client_gamma_to_linear(c);
}

// One texture per layer over a terrain chunk (~33 yards), so the pattern stays
// continuous across the pool's MH2O chunk boundaries. The gold reads best a little
// larger than the raw chunk scale, so the swirls span several yards.
const float LIQUID_TILE = 52.0;

vec4 normal_frame(vec2 uv, float frame) {
    vec2 size = vec2(textureSize(normal_tex, 0));
    float count = max(normal_frames, 1.0);
    vec2 inset = vec2(0.5 / size.x, 0.5 / size.y);
    vec2 tile = mix(inset, vec2(1.0) - inset, fract(uv));
    return texture(normal_tex, vec2((mod(frame, count) + tile.x) / count, tile.y));
}

// procazerite blends the normals reconstructed from two adjacent animation frames.
// procazerite blends the normals reconstructed from two adjacent animation frames:
// n = normalize(mix(n0, n1, fract)), z = sqrt(1 - |xy|^2).
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
    vec2 p = world_position.xz / LIQUID_TILE;
    float time = TIME;

    // Animated normal drives the flow; the normal itself drifts slowly so the
    // swirls travel instead of shimmering in place.
    vec3 wave = ripple(p * normal_scale + vec2(time * 0.004, -time * 0.003), time);
    vec2 warp = wave.xy * warp_strength;

    // Counter-scrolling layer UVs keep the gold and the transition band from sliding
    // as one sheet; the warp is what makes them read as flowing liquid.
    vec2 gold_uv = p + warp + vec2(time * deep_scroll, time * deep_scroll * 0.45) * flow_speed * 0.15;
    vec2 gold_uv_b = p - warp * 0.7 + vec2(-time * center_scroll * 0.55, time * center_scroll * 0.2) * flow_speed * 0.15;
    // azeritedeep is the thick gold marble (cream ridges, iridescent flecks): it is the
    // body. azeritetranscolor is the flat yellow transition band and azeritecentercolor
    // the blue speckle that shows where the pool shallows out.
    vec3 deep = texture(deep_tex, gold_uv).rgb;
    vec3 trans = texture(transcolor_tex, gold_uv_b).rgb;
    vec3 center = texture(center_tex, p + warp * 0.6 + vec2(-time * center_scroll, time * center_scroll * 0.4) * flow_speed * 0.1).rgb;

    float depth = clamp(COLOR.a * depth_coefficient, 0.0, 1.0);
    // azerite_0 depth ramp, exact: w = saturate(1 - depth * K), body = lerp(deep, center, w).
    float center_weight = clamp(1.0 - depth, 0.0, 1.0);
    float trans_weight = (1.0 - center_weight) * (1.0 - clamp(depth * 1.6, 0.0, 1.0));

    vec3 body = deep * gold_color;
    body = mix(body, trans * vec3(1.0, 0.92, 0.6), trans_weight * 0.5);
    body = mix(body, center * center_color, center_weight);

    // azeriteblend: the cyan/white/pink flecks that ride on the gold. Kept sparse so
    // they read as sparkle rather than mottling the body.
    if (has_masks) {
        vec4 mask = texture(blend_tex, p * 1.37 + warp * 0.5 + vec2(time * 0.006, -time * 0.008));
        float fleck = smoothstep(0.68, 1.0, dot(mask.rgb, vec3(0.25, 0.55, 0.2)) * 1.6);
        body = mix(body, mask.rgb * 1.1, fleck * 0.08);
    }

    // The warped normal is what gives the swirls their thickness: shade the body with
    // the client's sun, normalised by the flat-surface light so only the variation
    // shows and the pool keeps its own brightness.
    vec3 normal_world = normalize(vec3(wave.x, wave.z, wave.y));
    vec3 lit = client_sun_ambient + client_sun_diffuse * max(dot(-client_sun_dir, normal_world), 0.0);
    vec3 lit_flat = client_sun_ambient + client_sun_diffuse * max(dot(-client_sun_dir, vec3(0.0, 1.0, 0.0)), 0.0);
    body *= mix(vec3(1.0), lit / max(lit_flat, vec3(0.001)), 1.5);

    // Self-illumination: the client's pool glows. The body is already unlit, so this
    // only lifts it toward the bright end instead of painting it flat.
    body += body * emission_strength;

    // Glossy sheen from the warped normal, plus a short screen-space reflection.
    vec3 normal_view = normalize((VIEW_MATRIX * vec4(normal_world, 0.0)).xyz);
    vec3 to_eye = normalize(-VERTEX);
    float fresnel = pow(1.0 - clamp(dot(to_eye, normal_view), 0.0, 1.0), 3.0);
    vec3 half_vector = normalize(-client_sun_dir + normalize((INV_VIEW_MATRIX * vec4(to_eye, 0.0)).xyz));
    float highlight = pow(clamp(dot(half_vector, normal_world), 0.0, 1.0), specular_power);
    body += client_sun_specular * highlight * specular_strength;

    // The body terms are gamma-space like the client's; convert once, then blend the
    // (already linear) screen reflection.
    vec3 linear_body = client_gamma_to_linear(body);
    vec3 view_dir = reflect(-to_eye, normal_view);
    vec3 world_dir = normalize((INV_VIEW_MATRIX * vec4(view_dir, 0.0)).xyz);
    vec3 reflection = liquid_reflection(VERTEX, view_dir, world_dir, to_linear(client_fog_color), SCREEN_UV, fwidth(SCREEN_UV), PROJECTION_MATRIX, INV_PROJECTION_MATRIX);
    ALBEDO = mix(linear_body, reflection, clamp(fresnel * reflection_strength, 0.0, 1.0));
    ALPHA = 1.0;
    FOG = client_fog_output(VERTEX, INV_VIEW_MATRIX);
}
