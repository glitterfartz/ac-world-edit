shader_type spatial;
render_mode cull_disabled;

// ModelLibrary chooses blend/cull/unlit variants from this shader. Keep the render-mode
// and roughness statements explicit because that material setup replaces those statements.
uniform sampler2D albedo_tex : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
// Four RGBA-float texels per bone matrix, one row per sampled native animation frame.
uniform sampler2D animation_palette : filter_nearest, repeat_disable;
uniform float animation_duration = 0.0;
uniform int animation_frames = 1;
uniform bool skinned = false;
uniform int blend_mode = 0;
uniform bool vertex_color = false;
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
    UV = vec2(dot(row0, vec3(UV, 1.0)), dot(row1, vec3(UV, 1.0)));
    // SKYBOX_VERTEX: ModelLibrary replaces this line for LightSkybox variants.
}

void fragment() {
    vec4 tex = texture(albedo_tex, UV);
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
    ALBEDO = tex.rgb * batch_color.rgb * (vertex_color ? COLOR.rgb : vec3(1.0));
    ROUGHNESS = 1.0;
    SPECULAR = 0.0;
}
