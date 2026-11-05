shader_type spatial;
render_mode unshaded, blend_add, depth_draw_never, depth_test_disabled, cull_disabled, fog_disabled;
#include "res://Shaders/sky/sky_clouds.gdshaderinc"

// The client's sun and moon glare (DayNight::DNGlare and the retail "glare" pixel shader): a large additive quad
// drawn over the finished scene at the body's place in the sky, tinted by the sun color. The client dims it by
// the cloud in front of the body, by the body being under a skybox model or under water, and by how much of the
// body is hidden by the world (an occlusion query); the cloud and occlusion dimming are done here, the rest by
// SkyLighting. Not ported: the client's fade-in and fade-out rates (the glare changes at once).
uniform sampler2D glare_tex : filter_linear, repeat_disable;
uniform vec3 glare_color = vec3(1.0);
uniform float glare_alpha = 0.0;
// World direction toward the body and the width of its disc at the quad distance, which sizes the occlusion test.
uniform vec3 planet_direction = vec3(0.0, 1.0, 0.0);
uniform float planet_width = 1.0;
// The moon's cloud dimming peaks at half cloud cover; the sun's falls with cover.
uniform bool is_moon = false;
uniform sampler2D depth_texture : hint_depth_texture, filter_nearest, repeat_disable;

varying flat float cloud_dim;

vec3 band_to_linear(vec3 c) {
    c = clamp(c, 0.0, 1.0);
    return mix(pow((c + 0.055) / 1.055, vec3(2.4)), c / 12.92, vec3(lessThan(c, vec3(0.04045))));
}

void vertex() {
    float density = cloud_alpha_at(planet_direction);
    cloud_dim = is_moon ? 1.0 - abs((density - 0.5) * 2.0) : 1.0 - density;
}

// The share of the body's disc that shows sky, from depth samples across it: the world's depth is 0 only where
// nothing was drawn (reverse Z). Samples off screen count as clear.
float body_visibility(vec3 center, mat4 projection) {
    if (center.z > -0.1) {
        return 0.0;
    }
    float clear = 0.0;
    for (int i = -1; i <= 1; i++) {
        for (int j = -1; j <= 1; j++) {
            vec3 p = center + vec3(float(i), float(j), 0.0) * planet_width * 0.4;
            vec4 clip = projection * vec4(p, 1.0);
            vec2 uv = clip.xy / clip.w * 0.5 + 0.5;
            if (uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0) {
                clear += 1.0;
            } else {
                clear += textureLod(depth_texture, uv, 0.0).x < 0.00001 ? 1.0 : 0.0;
            }
        }
    }
    return clear / 9.0;
}

void fragment() {
    vec3 center = (VIEW_MATRIX * MODEL_MATRIX * vec4(0.0, 0.0, 0.0, 1.0)).xyz;
    vec4 glare = texture(glare_tex, UV);
    ALBEDO = band_to_linear(glare.rgb * glare_color);
    ALPHA = glare.a * glare_alpha * cloud_dim * body_visibility(center, PROJECTION_MATRIX);
}
