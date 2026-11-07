shader_type spatial;
render_mode unshaded, cull_disabled;
#include "res://Shaders/shared/client_lighting.gdshaderinc"
// Far-tile port of the retail terrainlow_0 pixel shader: one baked albedo plus one
// baked normal per tile instead of four splatted layers. The tile UV comes from the
// existing chunk UV sets (UV2 holds the chunk's cell, UV the chunk-local coordinate),
// so no extra vertex data or mesh is needed. Lighting matches the full path with the
// highlight masked out (the client bakes no per-layer alpha here): ambient plus wrapped
// diffuse N.L in gamma space, baked shadow taking 30 % off, vertex tint doubled at build.
// The hemisphere bands and the shadow distance fade stay estimates: all three bands read
// the ambient term so flat ground matches the full shader, and the shadow applies fully.
// The 3.3.5a terrain shader has no specular term either, so low tiles honor wrath_lighting
// the same way. An optional cell grid draws on top like the full shader.
uniform sampler2D baked_albedo : filter_linear_mipmap, repeat_disable;
uniform sampler2D baked_normal : filter_linear_mipmap, repeat_disable;
uniform sampler2D splat_map : filter_linear, repeat_disable;
uniform bool wrath_lighting = false;
uniform bool grid = false;

void fragment() {
    vec2 tile_uv = (UV2 + UV) / 16.0;
    vec3 albedo = texture(baked_albedo, tile_uv).rgb;
    vec3 normal_object = texture(baked_normal, tile_uv).rgb * 2.0 - 1.0;
    vec3 normal = normalize((VIEW_MATRIX * vec4(normal_object, 0.0)).xyz);
    float shadow = texture(splat_map, tile_uv).a;
    vec3 lit = albedo * COLOR.rgb;
    vec3 color = wrath_lighting
        ? client_wrath_surface_light(lit, 1.0 - shadow, normal, VIEW_MATRIX)
        : client_surface_light(lit, 0.0, 1.0 - shadow, VERTEX, normal, VIEW_MATRIX, client_world_pos(VERTEX, INV_VIEW_MATRIX), client_world_normal(normal, VIEW_MATRIX));
    if (grid) {
        vec2 edge = min(UV, 1.0 - UV);
        float line = 1.0 - smoothstep(0.0, 0.006, min(edge.x, edge.y));
        color = mix(color, vec3(0.12, 0.85, 0.4), line * 0.7);
    }
    ALBEDO = client_gamma_to_linear(color);
    FOG = client_fog_output(VERTEX, INV_VIEW_MATRIX);
}
