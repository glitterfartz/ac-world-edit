shader_type spatial;
render_mode unshaded, blend_add, depth_draw_never, depth_test_disabled, cull_disabled, fog_disabled;

// The client's sun shafts (retail "sunshaftsmask" and "sunshaftspropagate"): the sky is a mask (anything that wrote no
// depth), and each pixel gathers that mask along its line toward the sun's screen position, so light seems to stream
// past whatever covers the sun. The client blurs the mask in chained full-screen passes and adds the result in the
// post chain; this does the same gather in one pass over the finished scene, as an additive full-screen quad. The
// color, strength and reach come from the light's LightShaft rows (see ClientLighting). The tap count, the falloff
// with screen distance from the sun and the overall strength are estimates: the composite's constants are not in the
// shipped shader bytecode.
uniform sampler2D depth_texture : hint_depth_texture, filter_nearest, repeat_disable;
uniform vec3 shaft_color = vec3(1.0);
// Strength before the mask: the LightShaft intensity times how far above the horizon the sun is.
uniform float shaft_amount = 0.0;
// How much of the way to the sun each pixel gathers (0 to 1), from the LightShaft radius.
uniform float shaft_reach = 1.0;
// World direction toward the sun disc.
uniform vec3 sun_direction = vec3(0.0, 1.0, 0.0);

const int SHAFT_STEPS = 24;
const float SHAFT_STRENGTH = 0.5;
const float SHAFT_FALLOFF = 1.4;

vec3 band_to_linear(vec3 c) {
    c = clamp(c, 0.0, 1.0);
    return mix(pow((c + 0.055) / 1.055, vec3(2.4)), c / 12.92, vec3(lessThan(c, vec3(0.04045))));
}

void vertex() {
    // The quad is a 2 x 2 plane: its vertices are already clip-space corners.
    POSITION = vec4(VERTEX.xy, 1.0, 1.0);
}

// 1 where nothing was drawn (reverse Z: the sky's depth is 0), 0 where the world is, 0 off screen.
float sky_mask(vec2 uv) {
    if (uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0) {
        return 0.0;
    }
    return textureLod(depth_texture, uv, 0.0).x < 0.00001 ? 1.0 : 0.0;
}

void fragment() {
    vec3 toward = normalize((VIEW_MATRIX * vec4(sun_direction, 0.0)).xyz);
    if (toward.z > -0.05 || shaft_amount <= 0.0) {
        discard;
    }
    vec4 clip = PROJECTION_MATRIX * vec4(toward, 0.0);
    vec2 sun_uv = clip.xy / clip.w * 0.5 + 0.5;
    vec2 step_uv = (sun_uv - SCREEN_UV) * shaft_reach / float(SHAFT_STEPS);
    vec2 p = SCREEN_UV;
    float gathered = 0.0;
    float weight = 1.0;
    float total = 0.0;
    for (int i = 0; i < SHAFT_STEPS; i++) {
        p += step_uv;
        gathered += sky_mask(p) * weight;
        total += weight;
        weight *= 0.95;
    }
    float near_sun = clamp(1.0 - length(sun_uv - SCREEN_UV) / SHAFT_FALLOFF, 0.0, 1.0);
    ALBEDO = band_to_linear(shaft_color);
    ALPHA = clamp(gathered / total * near_sun * near_sun * shaft_amount * SHAFT_STRENGTH, 0.0, 1.0);
}
