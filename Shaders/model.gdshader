shader_type spatial;
render_mode cull_disabled;
#include "res://Shaders/client_lighting.gdshaderinc"

// ModelLibrary chooses blend/cull/unlit variants from this shader. Keep the render-mode
// and roughness statements explicit because that material setup replaces those statements.
uniform sampler2D albedo_tex : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
// Four RGBA-float texels per bone matrix, one row per sampled native animation frame.
uniform sampler2D animation_palette : filter_nearest, repeat_disable;
uniform float animation_duration = 0.0;
uniform int animation_frames = 1;
uniform bool skinned = false;
uniform int blend_mode = 0;
// 0 none, 1 baked interior WMO lighting (replaces scene light, modulate-2x), 2 exterior WMO
// colors added as emission on top of sun lighting.
uniform int vertex_color = 0;
// Native batch color (alpha includes texture weight) and first-unit UV affine rows. Animated
// batches read them from material_palette at material_column (color), +1 and +2 (UV rows),
// each channel looping over its own period; static batches use the uniforms below.
uniform sampler2D material_palette : filter_nearest, repeat_disable;
uniform int material_column = -1;
uniform int color_frames = 1;
uniform float color_period = 0.0;
uniform int uv_frames = 1;
uniform float uv_period = 0.0;
uniform vec4 surface_color = vec4(1.0);
uniform vec3 uv_row0 = vec3(1.0, 0.0, 0.0);
uniform vec3 uv_row1 = vec3(0.0, 1.0, 0.0);
varying vec4 batch_color;
// Multi-texture batches: combiner is a LayerCombiner value (0 = plain single texture), layer_tex2..4
// are texture units two to four, and layer_coords gives each unit's coordinates (0 first UV set with
// the batch transform, 1 second UV set, 2 environment sphere map).
uniform int combiner = 0;
uniform bool edge_fade = false;
uniform ivec4 layer_coords = ivec4(0);
uniform sampler2D layer_tex2 : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D layer_tex3 : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D layer_tex4 : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
varying vec2 uv_b;
varying vec2 uv_c;
varying vec2 uv_d;
// A WMO's second vertex-color alpha (custom channel 2): the mask its composite shaders blend layers with.
varying float color2_alpha;

vec2 pick_coord(int source, vec2 first, vec2 second, vec2 env, vec2 wmo_env, vec2 planar) {
    return source == 1 ? second : source == 2 ? env : source == 3 ? wmo_env : source == 4 ? planar : first;
}

vec4 material_sample(int column, int frames, float period, float time) {
    float frame = mod(time, period) / period * float(frames - 1);
    int a = int(floor(frame));
    return mix(texelFetch(material_palette, ivec2(column, a), 0),
        texelFetch(material_palette, ivec2(column, min(a + 1, frames - 1)), 0), fract(frame));
}

mat4 bone_matrix(int bone, int frame) {
    int x = bone * 4;
    // CPU row-vector matrix rows become GLSL columns: this is the required transpose.
    return mat4(
        texelFetch(animation_palette, ivec2(x, frame), 0),
        texelFetch(animation_palette, ivec2(x + 1, frame), 0),
        texelFetch(animation_palette, ivec2(x + 2, frame), 0),
        texelFetch(animation_palette, ivec2(x + 3, frame), 0));
}

void vertex() {
    if (skinned) {
        // All instances of a variant share the native pose palette. Interpolate sampled
        // matrices between rows rather than rebuilding every creature's mesh on the CPU.
        float frame = animation_duration > 0.0
            ? mod(TIME, animation_duration) / animation_duration * float(animation_frames - 1)
            : 0.0;
        int a = int(floor(frame));
        int b = min(a + 1, animation_frames - 1);
        float f = fract(frame);
        vec4 p = vec4(0.0);
        vec3 n = vec3(0.0);
        float total = 0.0;
        for (int i = 0; i < 4; i++) {
            // Custom vertex channels hold the four resolved bone IDs and native weights.
            float w = CUSTOM1[i];
            int bone = int(CUSTOM0[i]);
            if (w > 0.0) {
                mat4 ma = bone_matrix(bone, a);
                mat4 mb = bone_matrix(bone, b);
                p += mix(ma * vec4(VERTEX, 1.0), mb * vec4(VERTEX, 1.0), f) * w;
                n += mix(mat3(ma) * NORMAL, mat3(mb) * NORMAL, f) * w;
                total += w;
            }
        }
        if (total > 0.0) {
            VERTEX = p.xyz / total;
            NORMAL = normalize(n);
        }
    }
    batch_color = surface_color;
    vec3 row0 = uv_row0;
    vec3 row1 = uv_row1;
    if (material_column >= 0 && color_frames > 1) {
        batch_color = material_sample(material_column, color_frames, color_period, TIME);
    }
    if (material_column >= 0 && uv_frames > 1) {
        row0 = material_sample(material_column + 1, uv_frames, uv_period, TIME).xyz;
        row1 = material_sample(material_column + 2, uv_frames, uv_period, TIME).xyz;
    }
    vec2 first_uv = vec2(dot(row0, vec3(UV, 1.0)), dot(row1, vec3(UV, 1.0)));
    UV = first_uv;
    if (combiner != 0 || edge_fade) {
        vec3 view_pos = (MODELVIEW_MATRIX * vec4(VERTEX, 1.0)).xyz;
        vec3 view_normal = normalize(MODELVIEW_NORMAL_MATRIX * NORMAL);
        // Environment map: the sphere-map coordinate of the view-space reflection vector.
        vec3 r = reflect(normalize(view_pos), view_normal);
        float m = 2.0 * sqrt(r.x * r.x + r.y * r.y + (r.z + 1.0) * (r.z + 1.0));
        vec2 env_uv = r.xy / m + 0.5;
        // WMO coordinates are defined in the client's axes: x = -z, y = -x, z = y of the viewer basis.
        vec2 wmo_env_uv = reflect(normalize(vec3(1.0)), vec3(-NORMAL.z, -NORMAL.x, NORMAL.y)).xy;
        vec2 planar_uv = vec2(VERTEX.z, VERTEX.x) * 0.24;
        UV = pick_coord(layer_coords.x, first_uv, UV2, env_uv, wmo_env_uv, planar_uv);
        uv_b = pick_coord(layer_coords.y, first_uv, UV2, env_uv, wmo_env_uv, planar_uv);
        uv_c = pick_coord(layer_coords.z, first_uv, UV2, env_uv, wmo_env_uv, planar_uv);
        uv_d = pick_coord(layer_coords.w, first_uv, UV2, env_uv, wmo_env_uv, planar_uv);
        color2_alpha = CUSTOM2.x;
        if (edge_fade) {
            // Edge-fade vertex shaders fade a surface out where it turns away from the viewer.
            float facing = abs(dot(view_normal, normalize(-view_pos)));
            batch_color.a *= clamp(facing * facing, 0.0, 1.0);
        }
    }
    // SKYBOX_VERTEX: ModelLibrary replaces this line for LightSkybox variants.
}

void fragment() {
    vec4 tex = texture(albedo_tex, UV);
    vec3 layer_add = vec3(0.0);
    if (combiner != 0) {
        // The client's Combiners_* pixel shaders (https://wowdev.wiki/M2/.skin). The result replaces the
        // base texel: rgb is the combined color, a the alpha the blend mode uses, layer_add the part
        // the client adds after lighting (reflections and glows). Animated texture weights are not
        // sampled: weighted layers draw at full weight and the crossfades show their base texture.
        vec4 t1 = tex;
        vec4 t2 = texture(layer_tex2, uv_b);
        vec4 t3 = texture(layer_tex3, uv_c);
        vec3 rgb = t1.rgb;
        float a = t1.a;
        float c1a = COLOR.a;
        float c2a = color2_alpha;
        switch (combiner) {
            case 1: a = 1.0; break;
            case 2: break;
            case 3: rgb = t1.rgb * t2.rgb; a = t2.a; break;
            case 4: rgb = t1.rgb * t2.rgb * 2.0; a = t2.a * 2.0; break;
            case 5: rgb = t1.rgb * t2.rgb * 2.0; a = 1.0; break;
            case 6: rgb = t1.rgb * t2.rgb; a = 1.0; break;
            case 7: rgb = t1.rgb * t2.rgb; a = t1.a * t2.a; break;
            case 8: rgb = t1.rgb * t2.rgb * 2.0; a = t1.a * t2.a * 2.0; break;
            case 9: a = t1.a + t2.a; layer_add = t2.rgb; break;
            case 10: rgb = t1.rgb * t2.rgb * 2.0; break;
            case 11: layer_add = t2.rgb; break;
            case 12: rgb = t1.rgb * t2.rgb; break;
            case 13: rgb = mix(t1.rgb * t2.rgb * 2.0, t1.rgb, t1.a); a = 1.0; break;
            case 14: layer_add = t2.rgb * t2.a; a = 1.0; break;
            case 15: layer_add = t2.rgb * t2.a * (1.0 - t1.a); a = 1.0; break;
            case 16: rgb = mix(t1.rgb * t2.rgb * 2.0, t1.rgb, t1.a); layer_add = t3.rgb * t3.a; a = 1.0; break;
            case 17: layer_add = t2.rgb * t2.a; break;
            case 18: a = t1.a + t2.a * (0.3 * t2.r + 0.59 * t2.g + 0.11 * t2.b); layer_add = t2.rgb * t2.a * (1.0 - t1.a); break;
            case 19: rgb = mix(mix(t1.rgb, t2.rgb, t2.a), t1.rgb, t1.a); a = 1.0; break;
            case 20: rgb = mix(t1.rgb * t2.rgb * 2.0, t3.rgb, t3.a); a = 1.0; break;
            case 21: layer_add = t2.rgb * t2.a; a = 1.0; break;
            case 22: a = t1.a + t2.a; layer_add = t2.rgb * (1.0 - t1.a); break;
            case 23: rgb = mix(t1.rgb * t2.rgb, t1.rgb, t1.a); a = 1.0; break;
            case 24: layer_add = t2.rgb * t2.a; break;
            case 25: rgb = mix(t1.rgb, t2.rgb, t2.a); layer_add = t1.rgb * t1.a; a = 1.0; break;
            case 26: {
                float glow = clamp(t3.a, 0.0, 1.0);
                rgb = mix(t1.rgb * t2.rgb * 2.0, t1.rgb, t1.a) * (1.0 - glow);
                layer_add = t3.rgb * glow;
                a = 1.0;
                break;
            }
            case 27: break;
            case 28: rgb = mix(mix(t1.rgb * t2.rgb * 2.0, t3.rgb, t3.a), t1.rgb, t1.a); a = 1.0; break;
            case 29: a = t1.a * texture(layer_tex4, uv_b).a; break;
            case 30: rgb = mix(t1.rgb, t2.rgb, t2.a); a = 1.0; break;
            case 31: break;
            case 32: break;
            case 33: { vec4 all = t1 * t2 * t3; rgb = all.rgb; a = all.a; break; }
            case 34: rgb = t1.rgb * t2.rgb; a = t1.a * t2.a; break;
            // WMO MapObj pixel shaders: c1a and c2a are the two vertex-color alphas.
            case 40: layer_add = t2.rgb * t1.a; a = 1.0; break;
            case 41: layer_add = t1.rgb * t1.a * t2.rgb; a = 1.0; break;
            case 42: rgb = mix(mix(t1.rgb, t2.rgb, t2.a), t1.rgb, c2a); break;
            case 43: rgb = mix(t2.rgb, t1.rgb, c2a); break;
            case 44: layer_add = t2.rgb * t2.a * c2a; break;
            case 45: rgb = mix(mix(t1.rgb * t2.rgb * 2.0, t3.rgb, clamp(t3.a * c2a, 0.0, 1.0)), t1.rgb, t1.a); break;
            case 46: layer_add = t1.rgb * t1.a * t2.rgb + t3.rgb * t3.a * c2a; break;
            case 47: rgb = mix(t2.rgb, t1.rgb, c1a); a = 1.0; break;
            case 48: rgb = mix(t2.rgb * (1.0 - t2.a), t1.rgb, c2a); layer_add = t2.rgb * t2.a * (1.0 - c2a); break;
            case 49: rgb = mix(t1.rgb * t2.rgb * 2.0 + t3.rgb * clamp(t3.a * c2a, 0.0, 1.0), t1.rgb, t1.a); a = 1.0; break;
            case 50: rgb = mix(mix(t1.rgb, t2.rgb, t2.a), t1.rgb, c2a) * t3.rgb * 2.0; break;
            case 51: rgb = mix(t1.rgb, t1.rgb * t2.rgb * 2.0, c2a); break;
            case 53: layer_add = t1.rgb * t1.a * t3.rgb; break;
            case 52: rgb = mix(mix(t1.rgb, t2.rgb, t2.a), t1.rgb, t3.a) * t3.rgb * 2.0; break;
            default: break;
        }
        tex = vec4(rgb, a);
    }
    // Native alpha-test batches discard texels; other blend variants are configured
    // by ModelLibrary. Baked WMO color is separate from the texture's alpha channel.
    if (blend_mode == 1 && tex.a < 0.5) {
        discard;
    }
    // A fully faded batch is skipped like the client does. Opaque batches cannot blend in
    // this pass, so their partial alpha only matters at zero.
    if (batch_color.a <= 1.0 / 255.0) {
        discard;
    }
    vec3 base = tex.rgb * batch_color.rgb;
    // Mode 3 (retail interior): the alpha weights world lighting through the normal lit path, and the color
    // (already scaled by 1 - alpha, ambient included) is the group's own emissive term, doubled like mode 1.
    ALBEDO = vertex_color == 1 ? base * COLOR.rgb * 2.0 : vertex_color == 3 ? base * COLOR.a : base;
    EMISSION = (vertex_color == 2 ? base * COLOR.rgb : vertex_color == 3 ? base * COLOR.rgb * 2.0 : vec3(0.0)) + layer_add;
    ROUGHNESS = 1.0;
    SPECULAR = 0.0;
    FOG = client_fog_output(VERTEX);
}
