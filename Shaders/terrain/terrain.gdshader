shader_type spatial;
render_mode unshaded, cull_disabled;
#include "res://Shaders/shared/client_lighting.gdshaderinc"
// Up to four native layers share one chunk UV set. Everything is computed on the 8-bit (gamma-space) values, as the
// client does, so no texture is converted from source color space; only the finished color is converted to the
// linear space Godot expects. The layer textures' alpha is the specular mask.
uniform sampler2D layer0 : filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D layer1 : filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D layer2 : filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D layer3 : filter_linear_mipmap_anisotropic, repeat_enable;
// Per-tile atlas of 16x16 chunk cells (64x64 each): RGB layer opacity, A baked shadow. UV2 is
// the chunk's cell; clamping to texel centers inside the cell reproduces the per-chunk
// clamp-to-edge sampling, so neighbouring chunks never bleed in.
uniform sampler2D splat_map : filter_linear, repeat_disable;
// Retail height maps (the alpha of each layer's _h texture). With height_blend, each layer's weight is
// multiplied by height * scale + offset, and a layer that sits more than 1.0 below the tallest
// contribution is suppressed, so rock pokes through sand rather than fading into it.
uniform sampler2D height0 : filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D height1 : filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D height2 : filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D height3 : filter_linear_mipmap_anisotropic, repeat_enable;
uniform vec4 height_scales = vec4(0.0);
uniform vec4 height_offsets = vec4(1.0);
uniform bool height_blend = false;
// Per-layer texture scale (retail MTXP): a layer repeats 8 / scale times across a chunk.
uniform vec4 layer_scales = vec4(1.0);
uniform bool big_alpha = false;
uniform bool grid = false;

void fragment() {
    vec4 splat = texture(splat_map, (UV2 + clamp(UV, 0.5 / 64.0, 63.5 / 64.0)) / 16.0);
    vec3 a = splat.rgb;
    vec4 c0 = texture(layer0, UV * (8.0 / layer_scales[0]));
    vec4 c1 = texture(layer1, UV * (8.0 / layer_scales[1]));
    vec4 c2 = texture(layer2, UV * (8.0 / layer_scales[2]));
    vec4 c3 = texture(layer3, UV * (8.0 / layer_scales[3]));
    vec4 layered;
    // Large-alpha maps store independent layer weights; legacy maps apply layers
    // sequentially. Interchanging these modes changes the client terrain appearance.
    if (big_alpha && height_blend) {
        vec4 w = vec4(max(0.0, 1.0 - a.r - a.g - a.b), a.r, a.g, a.b);
        vec4 h = vec4(
            texture(height0, UV * (8.0 / layer_scales[0])).a,
            texture(height1, UV * (8.0 / layer_scales[1])).a,
            texture(height2, UV * (8.0 / layer_scales[2])).a,
            texture(height3, UV * (8.0 / layer_scales[3])).a);
        vec4 pct = w * (h * height_scales + height_offsets);
        float tallest = max(max(pct.x, pct.y), max(pct.z, pct.w));
        pct *= 1.0 - clamp(vec4(tallest) - pct, 0.0, 1.0);
        layered = (c0 * pct.x + c1 * pct.y + c2 * pct.z + c3 * pct.w) / max(pct.x + pct.y + pct.z + pct.w, 0.001);
    } else if (big_alpha) {
        float base = max(0.0, 1.0 - a.r - a.g - a.b);
        layered = (c0 * base + c1 * a.r + c2 * a.g + c3 * a.b) / max(base + a.r + a.g + a.b, 0.001);
    } else {
        layered = mix(mix(mix(c0, c1, a.r), c2, a.g), c3, a.b);
    }
    // The vertex tint (already doubled, as the client does) scales the layers; the baked shadow bits darken the lit result.
    vec3 color = client_surface_light(layered.rgb * COLOR.rgb, layered.a, 1.0 - splat.a, VERTEX, NORMAL, VIEW_MATRIX);
    if (grid) {
        vec2 edge = min(UV, 1.0 - UV);
        float line = 1.0 - smoothstep(0.0, 0.006, min(edge.x, edge.y));
        color = mix(color, vec3(0.12, 0.85, 0.8), line * 0.7);
    }
    ALBEDO = client_gamma_to_linear(color);
    FOG = client_fog_output(VERTEX, INV_VIEW_MATRIX);
}
