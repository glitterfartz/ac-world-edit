// Shared GPU skinning for M2 model surfaces. The visible model shader and the shadow caster both
// sample the same native animation palette, so a creature's depth silhouette matches its lit pose.
// Include this file from a spatial shader and call client_skin(VERTEX, NORMAL) in vertex().

// Four RGBA-float texels per bone matrix, one row per sampled native animation frame.
uniform sampler2D animation_palette : filter_nearest, repeat_disable;
uniform float animation_duration = 0.0;
uniform int animation_frames = 1;
// Death (animation 1) plays once and holds its last frame like the client, instead of looping.
uniform bool animation_hold = false;
// Shared bone-animation clock (ClientShaderGlobals.CreatureClock, seconds since engine start,
// written every frame): shader TIME is renderer-private and offset from it, so the CPU seat
// follow and the skinned mounts must read the same value to stay in phase. Negative until the
// first write, when shaders fall back to TIME.
global uniform float creature_clock = -1.0;
uniform bool skinned = false;

mat4 bone_matrix(int bone, int frame) {
    int x = bone * 4;
    // CPU row-vector matrix rows become GLSL columns: this is the required transpose.
    return mat4(
        texelFetch(animation_palette, ivec2(x, frame), 0),
        texelFetch(animation_palette, ivec2(x + 1, frame), 0),
        texelFetch(animation_palette, ivec2(x + 2, frame), 0),
        texelFetch(animation_palette, ivec2(x + 3, frame), 0));
}

// Skins a vertex and normal in place from the shared palette, interpolating between the two
// sampled native frames. Does nothing when skinned is false.
void client_skin(inout vec3 vertex, inout vec3 normal) {
    if (!skinned) {
        return;
    }
    // All instances of a variant share the native pose palette. Interpolate sampled
    // matrices between rows rather than rebuilding every creature's mesh on the CPU.
    // The clock is shared with the CPU seat follow; TIME is only the pre-first-write fallback.
    float clock = creature_clock >= 0.0 ? creature_clock : TIME;
    float cycle = animation_duration > 0.0
        ? (animation_hold ? min(clock, animation_duration) : mod(clock, animation_duration))
        : 0.0;
    float frame = animation_duration > 0.0
        ? cycle / animation_duration * float(animation_frames - 1)
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
            p += mix(ma * vec4(vertex, 1.0), mb * vec4(vertex, 1.0), f) * w;
            n += mix(mat3(ma) * normal, mat3(mb) * normal, f) * w;
            total += w;
        }
    }
    if (total > 0.0) {
        vertex = p.xyz / total;
        normal = normalize(n);
    }
}
