shader_type spatial;
render_mode unshaded, cull_disabled, depth_draw_opaque, fog_disabled;
#include "res://Shaders/shared/client_skinning.gdshaderinc"

// Renders shadow casters into one cascade map: the sun-space depth of each fragment, packed into
// RGB. A cascade map stores a global depth (dot(world, client_sun_dir)), so the same material serves
// every cascade. The map is cleared to white, which is the far end of the range and reads as "lit"
// in the receiver (client_shadow.gdshaderinc). Do not add blending or a tonemap here: the receiver
// treats the three channels as exact 8-bit digits. The one discard is the static-UV alpha test that
// gives hair and capes their edges. Material/UV animation is not sampled, so an animated UV
// silhouette lags the lit mesh (documented limitation).

global uniform vec3 client_sun_dir;
// x the minimum and y the maximum of the global sun-space depth range (ShadowDepthPacking).
global uniform vec4 client_shadow_depth_range;
// Yards the fragment is pushed toward the light along its own normal, so a receiver biased the
// same way does not self-shadow (see client_shadow_visibility).
global uniform float client_shadow_normal_offset;

// Alpha-tested caster surfaces (blend mode 1, e.g. hair and capes) need the albedo only for its
// alpha; the static affine UV rows match the model shader's first texture unit.
uniform sampler2D albedo_tex : filter_linear_mipmap_anisotropic, repeat_enable;
uniform int blend_mode = 0;
uniform vec3 uv_row0 = vec3(1.0, 0.0, 0.0);
uniform vec3 uv_row1 = vec3(0.0, 1.0, 0.0);

varying vec3 caster_world;
varying vec2 caster_uv;

void vertex() {
    // Match the visible GPU-skinned pose so the depth silhouette follows the animation.
    client_skin(VERTEX, NORMAL);
    caster_uv = vec2(dot(uv_row0, vec3(UV, 1.0)), dot(uv_row1, vec3(UV, 1.0)));
    // MODEL_MATRIX carries the node transform and, for a MultiMesh, the instance transform.
    caster_world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
    // Offset away from the light along the normal. A sun-facing surface then stores a slightly
    // larger depth, so a receiver offset by the same distance compares in front of its own caster
    // instead of shadowing itself; a normal offset toward the light would do the opposite.
    caster_world -= normalize(MODEL_NORMAL_MATRIX * NORMAL) * client_shadow_normal_offset;
}

void fragment() {
    if (blend_mode == 1 && texture(albedo_tex, caster_uv).a < 0.5) {
        discard;
    }
    float range = max(client_shadow_depth_range.y - client_shadow_depth_range.x, 0.001);
    float t = clamp((dot(caster_world, client_sun_dir) - client_shadow_depth_range.x) / range, 0.0, 1.0);
    uint bits = uint(round(t * 16777215.0));
    ALBEDO = vec3(float(bits & 255u), float((bits >> 8u) & 255u), float((bits >> 16u) & 255u)) / 255.0;
    ALPHA = 1.0;
}
