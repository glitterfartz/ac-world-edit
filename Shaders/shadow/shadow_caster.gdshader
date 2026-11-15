shader_type spatial;
render_mode unshaded, cull_disabled, depth_draw_opaque, fog_disabled;

// Renders shadow casters into one cascade map: the sun-space depth of each fragment, packed into
// RGB. A cascade map stores a global depth (dot(world, client_sun_dir)), so the same material serves
// every cascade. The map is cleared to white, which is the far end of the range and reads as "lit"
// in the receiver (client_shadow.gdshaderinc). Do not add blending, alpha test or a tonemap here:
// the receiver treats the three channels as exact 8-bit digits.

global uniform vec3 client_sun_dir;
// x the minimum and y the maximum of the global sun-space depth range (ShadowDepthPacking).
global uniform vec4 client_shadow_depth_range;
// Yards the fragment is pushed toward the light along its own normal, so a receiver biased the
// same way does not self-shadow (see client_shadow_visibility).
global uniform float client_shadow_normal_offset;

varying vec3 caster_world;

void vertex() {
    // MODEL_MATRIX carries the node transform and, for a MultiMesh, the instance transform.
    caster_world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
    // Offset away from the light along the normal. A sun-facing surface then stores a slightly
    // larger depth, so a receiver offset by the same distance compares in front of its own caster
    // instead of shadowing itself; a normal offset toward the light would do the opposite.
    caster_world -= normalize(MODEL_NORMAL_MATRIX * NORMAL) * client_shadow_normal_offset;
}

void fragment() {
    float range = max(client_shadow_depth_range.y - client_shadow_depth_range.x, 0.001);
    float t = clamp((dot(caster_world, client_sun_dir) - client_shadow_depth_range.x) / range, 0.0, 1.0);
    uint bits = uint(round(t * 16777215.0));
    ALBEDO = vec3(float(bits & 255u), float((bits >> 8u) & 255u), float((bits >> 16u) & 255u)) / 255.0;
    ALPHA = 1.0;
}
