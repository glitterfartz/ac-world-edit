// Sun-shadow receiver: the retail shadowmask math evaluated per fragment, with the same-frame
// cascade maps SunShadows renders (Shaders/shadow/shadow_caster.gdshader). A caster map stores the
// sun-space depth dot(world, client_sun_dir) packed into RGB over a fixed global range; the data
// texture holds four RGBA texels per cascade (world-to-UV rows, split/texel, fade bounds), and
// client_sun_dir is declared in client_lighting.gdshaderinc.
//
// Cascade count 0 or strength 0 returns 1 (fully lit). The result is 1 for lit and 0 for shadowed;
// callers multiply it by the sun's diffuse and specular only, never by ambient, hemisphere, point
// lights or the baked shadow.

global uniform sampler2D client_shadow_map0 : filter_nearest, repeat_disable;
global uniform sampler2D client_shadow_map1 : filter_nearest, repeat_disable;
global uniform sampler2D client_shadow_map2 : filter_nearest, repeat_disable;
global uniform sampler2D client_shadow_map3 : filter_nearest, repeat_disable;
global uniform sampler2D client_shadow_cascades : filter_nearest, repeat_disable;
global uniform float client_shadow_cascade_count;
global uniform float client_shadow_strength;
// x minimum and y maximum of the packed sun-space depth (ShadowDepthPacking.Min/Max).
global uniform vec4 client_shadow_depth_range;
global uniform float client_shadow_bias;
global uniform float client_shadow_normal_offset;
// Depth comparisons per fragment: 1, 5 or 9 (ShadowQualityPresets.PcfTaps).
global uniform float client_shadow_taps;

const int CLIENT_SHADOW_MAX = 4;
const float CLIENT_SHADOW_LEVELS = 16777215.0;

float client_shadow_unpack(vec3 rgb) {
    // The three channels are exact 8-bit digits, low to high (shadow_caster.gdshader packs them).
    vec3 digits = floor(clamp(rgb, 0.0, 1.0) * 255.0 + 0.5);
    float t = (digits.x + digits.y * 256.0 + digits.z * 65536.0) / CLIENT_SHADOW_LEVELS;
    return mix(client_shadow_depth_range.x, client_shadow_depth_range.y, t);
}

// One nearest comparison: 1 when the receiver is not behind the stored caster depth (plus bias).
float client_shadow_compare0(vec2 uv, float receiver) { return step(receiver, client_shadow_unpack(texture(client_shadow_map0, uv).rgb)); }
float client_shadow_compare1(vec2 uv, float receiver) { return step(receiver, client_shadow_unpack(texture(client_shadow_map1, uv).rgb)); }
float client_shadow_compare2(vec2 uv, float receiver) { return step(receiver, client_shadow_unpack(texture(client_shadow_map2, uv).rgb)); }
float client_shadow_compare3(vec2 uv, float receiver) { return step(receiver, client_shadow_unpack(texture(client_shadow_map3, uv).rgb)); }

// GLSL cannot index an array of samplers, so each cascade's map is a branch. The uniform branch is
// coherent per draw, so the cost is one comparison.
float client_shadow_compare(int cascade, vec2 uv, float receiver) {
    if (cascade == 0) { return client_shadow_compare0(uv, receiver); }
    if (cascade == 1) { return client_shadow_compare1(uv, receiver); }
    if (cascade == 2) { return client_shadow_compare2(uv, receiver); }
    return client_shadow_compare3(uv, receiver);
}

// Retail edge fade: the smallest saturate() distance to a faded UV edge, times 10. The middle of the
// map is 1; outside the square is 0, and callers treat that as lit rather than shadowed.
float client_shadow_edge(vec2 uv, vec4 bounds) {
    float x = min(clamp(uv.x - bounds.x, 0.0, 1.0), clamp(bounds.z - uv.x, 0.0, 1.0));
    float y = min(clamp(uv.y - bounds.y, 0.0, 1.0), clamp(bounds.w - uv.y, 0.0, 1.0));
    return min(min(x, y) * 10.0, 1.0);
}

float client_shadow_filter(int cascade, vec2 uv, float receiver, float texel_uv) {
    float taps = client_shadow_taps;
    if (taps < 1.5) {
        return client_shadow_compare(cascade, uv, receiver);
    }
    vec2 o = vec2(texel_uv);
    if (taps < 4.5) {
        return (client_shadow_compare(cascade, uv, receiver)
            + client_shadow_compare(cascade, uv + vec2(o.x, o.y), receiver)
            + client_shadow_compare(cascade, uv + vec2(o.x, -o.y), receiver)
            + client_shadow_compare(cascade, uv + vec2(-o.x, o.y), receiver)
            + client_shadow_compare(cascade, uv + vec2(-o.x, -o.y), receiver)) * 0.2;
    }
    float sum = 0.0;
    for (int y = -1; y <= 1; y++) {
        for (int x = -1; x <= 1; x++) {
            sum += client_shadow_compare(cascade, uv + vec2(float(x), float(y)) * o, receiver);
        }
    }
    return sum / 9.0;
}

// Visibility of the sun at one world point through one cascade, before the strength blend.
float client_shadow_cascade(int cascade, vec3 world_pos, vec3 world_normal, float view_depth) {
    vec4 row_x = texelFetch(client_shadow_cascades, ivec2(cascade * 4, 0), 0);
    vec4 row_y = texelFetch(client_shadow_cascades, ivec2(cascade * 4 + 1, 0), 0);
    vec4 split_texel = texelFetch(client_shadow_cascades, ivec2(cascade * 4 + 2, 0), 0);
    vec4 fade = texelFetch(client_shadow_cascades, ivec2(cascade * 4 + 3, 0), 0);
    vec4 point = vec4(world_pos, 1.0);
    vec2 uv = vec2(dot(point, row_x), dot(point, row_y));
    if (uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0) {
        return 1.0;
    }
    float edge = client_shadow_edge(uv, fade);
    if (edge <= 0.0) {
        return 1.0;
    }
    // Push the receiver toward the sun by the bias: the caster offset the normal, and a surface
    // facing away from the sun needs more. Idle shadow-edge crawling is avoided by snapping in C#.
    float facing = clamp(dot(world_normal, -client_sun_dir), 0.0, 1.0);
    float receiver = dot(world_pos, client_sun_dir) - client_shadow_bias - client_shadow_normal_offset * (1.0 - facing);
    float texel_uv = 1.0 / max(split_texel.z, 1.0);
    return mix(1.0, client_shadow_filter(cascade, uv, receiver, texel_uv), edge);
}

// The sun's visibility at a fragment: 1 lit, 0 fully shadowed, blended across the last 5 % of each
// split and scaled by client_shadow_strength. view_depth is the view-space -z (-VERTEX.z).
float client_shadow_visibility(vec3 world_pos, vec3 world_normal, float view_depth) {
    if (client_shadow_cascade_count < 0.5 || client_shadow_strength <= 0.001) {
        return 1.0;
    }
    int count = int(client_shadow_cascade_count + 0.5);
    int cascade = 0;
    float split = 0.0;
    for (int i = 0; i < CLIENT_SHADOW_MAX; i++) {
        if (i >= count) {
            break;
        }
        split = texelFetch(client_shadow_cascades, ivec2(i * 4 + 2, 0), 0).x;
        cascade = i;
        if (view_depth <= split) {
            break;
        }
    }
    float lit = client_shadow_cascade(cascade, world_pos, world_normal, view_depth);
    if (cascade + 1 < count && view_depth > split * 0.95) {
        float weight = clamp((view_depth - split * 0.95) / max(split * 0.05, 0.0001), 0.0, 1.0);
        lit = mix(lit, client_shadow_cascade(cascade + 1, world_pos, world_normal, view_depth), weight);
    }
    return mix(1.0, lit, client_shadow_strength);
}
