shader_type spatial;
render_mode unshaded, blend_mix, depth_draw_never, cull_disabled;
// Atlas texels retain native ripples; preview water tint/opacity are approximations.
uniform sampler2D albedo_tex : source_color, filter_linear, repeat_disable;
uniform float frames = 1.0;
uniform int liquid_kind = 0;

void fragment() {
    // Animate only the native texture sequence; keep geometry at its stored elevation.
    float frame = mod(floor(TIME * 30.0), frames);
    vec2 size = vec2(textureSize(albedo_tex, 0));
    vec2 tile = fract(UV);
    // Clamp inside each frame to avoid sampling its neighbor in the atlas.
    vec2 inset = vec2(0.5 * frames / size.x, 0.5 / size.y);
    tile = mix(inset, vec2(1.0) - inset, tile);
    vec4 tex = texture(albedo_tex, vec2((frame + tile.x) / frames, tile.y));
    ALBEDO = liquid_kind >= 2 ? tex.rgb : mix(vec3(0.035, 0.16, 0.23), vec3(0.16, 0.37, 0.43), clamp(tex.r * 8.0, 0.0, 1.0));
    // Lava/slime retain their native RGB. Water blends over the scene without writing
    // depth, so source terrain and WMO geometry still determine visible shorelines.
    if (liquid_kind >= 2) {
        ALPHA = 1.0;
        EMISSION = tex.rgb * (liquid_kind == 2 ? 0.35 : 0.08);
    } else {
        // Client vertex depth controls shoreline transparency. No invented height waves.
        float fresnel = pow(1.0 - abs(dot(normalize(NORMAL), normalize(VIEW))), 3.0);
        ALPHA = clamp((1.0 - exp(-COLOR.a * 6.0)) * 0.82 + fresnel * 0.4, 0.0, 0.9);
    }
}
