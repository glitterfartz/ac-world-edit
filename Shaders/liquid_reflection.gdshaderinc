// Shared screen-space reflection for the liquids whose retail pixel shaders
// actually sample a reflection (procswamp, procmercury, procwaterabove). Do not
// include it in liquids whose decompiled shaders have no reflection input.
//
// The including shader must declare `scene_color` (hint_screen_texture) and
// `scene_depth` (hint_depth_texture), and include client_lighting before this
// file so the caller can pass the fog colour as the fallback.
#ifndef LIQUID_REFLECTION_INC
#define LIQUID_REFLECTION_INC

// View-space depth (positive in front of the camera) from the depth buffer.
float view_depth(vec2 uv, mat4 inv_projection) {
    float depth = texture(scene_depth, uv).r;
    // Reverse-Z sky is 0. Do not divide its infinite-far homogeneous point by 0.
    if (depth <= 0.000001) return 1000000.0;
    vec3 ndc = vec3(uv * 2.0 - 1.0, depth);
    #if CURRENT_RENDERER == RENDERER_COMPATIBILITY
        ndc.z = depth * 2.0 - 1.0;
    #endif
    vec4 position = inv_projection * vec4(ndc, 1.0);
    return -position.z / position.w;
}

// Retail supplies a separate reflected scene/sky image. This viewer substitutes a
// short screen trace: 32 samples over roughly 40 yards with the near end sampled
// finely. Steps jump several yards only far away where a miss is a sub-pixel
// error; taking the first depth crossing (no thickness test) keeps the hit bands
// from forming, and a binary search refines the crossing. The result fades to the
// fog colour with distance so nearby scenery reflects and the rest reads as water
// instead of a full-scene mirror (which is what a bird's-eye view used to show).
vec3 liquid_reflection(vec3 position, vec3 direction, vec3 fallback, vec2 screen_uv, vec2 pixel, mat4 projection, mat4 inv_projection) {
    const float max_distance = 40.0;
    // The per-pixel phase offset breaks up the sampling grid without moving hits.
    float jitter = fract(sin(dot(screen_uv, vec2(12.9898, 78.233))) * 43758.5453);
    float previous_t = 0.0;
    float hit_t = -1.0;
    for (int i = 1; i <= 32; i++) {
        float t = float(i) * 0.5 + float(i * i) * 0.0235 + jitter * 0.5;
        vec3 point = position + direction * t;
        if (point.z >= -0.05) break;
        vec4 clip = projection * vec4(point, 1.0);
        if (clip.w <= 0.0) break;
        vec2 uv = clip.xy / clip.w * 0.5 + 0.5;
        if (any(lessThan(uv, vec2(0.0))) || any(greaterThan(uv, vec2(1.0)))) break;
        if (-point.z > view_depth(uv, inv_projection)) { hit_t = t; break; }
        previous_t = t;
    }
    if (hit_t < 0.0) return fallback;
    float lo = previous_t;
    float hi = hit_t;
    for (int i = 0; i < 3; i++) {
        float mid = (lo + hi) * 0.5;
        vec3 point = position + direction * mid;
        vec4 clip = projection * vec4(point, 1.0);
        vec2 uv = clip.xy / clip.w * 0.5 + 0.5;
        if (-point.z > view_depth(uv, inv_projection)) hi = mid; else lo = mid;
    }
    vec3 point = position + direction * hi;
    vec4 clip = projection * vec4(point, 1.0);
    vec2 uv = clip.xy / clip.w * 0.5 + 0.5;
    float edge = min(min(uv.x, uv.y), min(1.0 - uv.x, 1.0 - uv.y));
    // Retail renders its reflection at a fraction of screen resolution. The blur
    // grows a little with distance, which keeps far hits soft rather than mirrored.
    vec2 blur = pixel * (1.5 + hi * 0.05);
    vec3 reflected = texture(scene_color, uv).rgb * 0.4;
    reflected += texture(scene_color, uv + vec2(blur.x, 0.0)).rgb * 0.15;
    reflected += texture(scene_color, uv - vec2(blur.x, 0.0)).rgb * 0.15;
    reflected += texture(scene_color, uv + vec2(0.0, blur.y)).rgb * 0.15;
    reflected += texture(scene_color, uv - vec2(0.0, blur.y)).rgb * 0.15;
    float fade = 1.0 - smoothstep(max_distance * 0.3, max_distance, hi);
    return mix(fallback, reflected, smoothstep(0.0, 0.08, edge) * fade);
}

#endif
