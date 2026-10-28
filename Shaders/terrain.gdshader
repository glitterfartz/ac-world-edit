shader_type spatial;
render_mode cull_disabled;
// Up to four native layers share one chunk UV set. Alpha textures use raw data
// sampling; only color textures are converted from source color space.
uniform sampler2D layer0 : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D layer1 : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D layer2 : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D layer3 : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
// Per-tile atlas of 16x16 chunk cells (64x64 each): RGB layer opacity, A baked shadow. UV2 is
// the chunk's cell; clamping to texel centers inside the cell reproduces the per-chunk
// clamp-to-edge sampling, so neighbouring chunks never bleed in.
uniform sampler2D splat_map : filter_linear, repeat_disable;
uniform bool big_alpha = false;
uniform bool grid = false;

void fragment() {
    vec4 splat = texture(splat_map, (UV2 + clamp(UV, 0.5 / 64.0, 63.5 / 64.0)) / 16.0);
    vec3 a = splat.rgb;
    vec3 c0 = texture(layer0, UV * 8.0).rgb;
    vec3 c1 = texture(layer1, UV * 8.0).rgb;
    vec3 c2 = texture(layer2, UV * 8.0).rgb;
    vec3 c3 = texture(layer3, UV * 8.0).rgb;
    vec3 color;
    // Large-alpha maps store independent layer weights; legacy maps apply layers
    // sequentially. Interchanging these modes changes the client terrain appearance.
    if (big_alpha) {
        float base = max(0.0, 1.0 - a.r - a.g - a.b);
        color = (c0 * base + c1 * a.r + c2 * a.g + c3 * a.b) / max(base + a.r + a.g + a.b, 0.001);
    } else {
        color = mix(mix(mix(c0, c1, a.r), c2, a.g), c3, a.b);
    }
    // Native vertex tint and baked shadow bits modulate the combined terrain color.
    color *= COLOR.rgb * (1.0 - splat.a * 0.3);
    if (grid) {
        vec2 edge = min(UV, 1.0 - UV);
        float line = 1.0 - smoothstep(0.0, 0.006, min(edge.x, edge.y));
        color = mix(color, vec3(0.12, 0.85, 0.8), line * 0.7);
    }
    ALBEDO = color;
    ROUGHNESS = 1.0;
}
