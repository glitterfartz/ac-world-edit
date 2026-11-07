shader_type spatial;
render_mode unshaded, blend_mix, depth_draw_never, cull_disabled;
#include "res://Shaders/shared/client_lighting.gdshaderinc"

// leyline_0: P = a0*a1; rgb = 2*P*c0*c1 + (1-P)*base;
// alpha = (2*P+0.5)*vertexOpacity, followed by client fog.
// DB2 color/UV bindings are inferred. Rotated-U scroll, native-depth opacity
// and the shared 1250ms atlas period substitute for unidentified CPU inputs.
// procleylineabove_0 (the animated pass, same DB2 rows): two scrolling RG normal
// samples from the slot-3 bump map (near + coarse, distance blend .2+eye*.001,
// strength CB1[22].w) reconstruct a ripple normal that warps the base UVs by 0.03
// and shades the sun diffuse; the body is P*c0*c1*4 + base (P = a0*a1); a
// scene-depth comparison foams the slot-4 shore texture at the waterline; alpha is
// the raw vertex opacity. The eye's side picks the shipped half per fragment (like
// procwater): mediumleylinebelow_0 draws the underside with the same warped body
// blended toward the deep color (Color[1], standing in for CB1[21]) by view angle,
// and no sun, shore or reflection — but when a shore texture resolves, the below
// path follows procleylinebelow_0 instead (same body, mix(body, shore, 0.5), no angle
// term; the t6 depth-test UV pick is estimated as the above-water drifted UVs).
// The CB1[9].y per-light loop and the t16 pass remain unported. The ripple triple
// follows the layer convention (floats 6..8: same scale/angle/scroll shape on every
// shipped row); the coarse ratio, scroll directions, shore width, foam falloff and sun
// strength are estimates. Without the ripple map the shader keeps leyline_0.
uniform sampler2D albedo_tex : filter_linear, repeat_disable;
uniform sampler2D detail_tex : filter_linear, repeat_disable;
uniform sampler2D ripple_tex : filter_linear_mipmap, repeat_enable;
uniform sampler2D shore_tex : filter_linear_mipmap, repeat_enable;
uniform sampler2D scene_color : hint_screen_texture, filter_linear, repeat_disable;
uniform sampler2D scene_depth : hint_depth_texture, filter_nearest, repeat_disable;
// view_depth comes from liquid_reflection.gdshaderinc (its screen-trace reflection is
// deliberately unused: procleylineabove_0 has no reflection input, only scene depth).
#include "res://Shaders/liquid/liquid_reflection.gdshaderinc"
uniform float frames = 1.0;
uniform float detail_frames = 1.0;
uniform float period_ms = 1250.0;
uniform vec3 first_uv = vec3(1.0, 0.0, 0.0);
uniform vec3 second_uv = vec3(1.0, 0.0, 0.0);
uniform vec3 ripple_uv = vec3(1.0, 0.0, 0.02);
uniform vec3 base_color = vec3(0.0);
uniform vec3 deep_color = vec3(0.0);
uniform bool has_ripple = false;
uniform bool has_shore = false;
uniform float ripple_strength = 1.0;
uniform float shore_width = 2.0;

varying vec3 world_position;

void vertex() {
    world_position = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

vec2 layer_uv(vec2 uv, vec3 settings, float time) {
    float a = radians(settings.y);
    vec2 p = uv*settings.x;
    return vec2(cos(a)*p.x-sin(a)*p.y, sin(a)*p.x+cos(a)*p.y) + vec2(time*settings.z, 0.0);
}

vec2 atlas_uv(vec2 uv, vec2 size, float count, float phase) {
    vec2 inset = vec2(0.5*count/size.x, 0.5/size.y);
    vec2 tile = mix(inset, vec2(1.0)-inset, fract(uv));
    return vec2((floor(phase*count)+tile.x)/count, tile.y);
}

void fragment() {
    float phase = fract(TIME*1000.0/max(period_ms, 1.0));
    vec4 first = texture(albedo_tex, atlas_uv(layer_uv(UV, first_uv, TIME), vec2(textureSize(albedo_tex,0)), frames, phase));
    vec4 second = texture(detail_tex, atlas_uv(layer_uv(UV, second_uv, TIME), vec2(textureSize(detail_tex,0)), detail_frames, phase));
    float product = first.a*second.a;
    vec3 color;
    float alpha;
    if (!has_ripple) {
        color = 2.0*product*first.rgb*second.rgb + (1.0-product)*base_color;
        alpha = clamp((2.0*product+0.5)*COLOR.a, 0.0, 1.0);
    } else {

    // Two scrolling RG samples (near + coarse, like the swamp ripple), blended by
    // eye distance (.2 + dist*.001, the client's own formula), reconstructed with
    // z = sqrt(1-|xy|^2) and normalized with the CB1[22].w strength (estimated 1.0).
    float dist = length(VERTEX);
    float near_mix = clamp(0.2 + dist * 0.001, 0.0, 1.0);
    vec2 near_uv = layer_uv(UV, ripple_uv, TIME);
    vec2 coarse_uv = layer_uv(UV * 0.15, vec3(1.0, 0.0, 0.0), TIME * 0.5);
    vec2 near_slope = texture(ripple_tex, near_uv).rg * 2.0 - 1.0;
    vec2 coarse_slope = texture(ripple_tex, coarse_uv).rg * 2.0 - 1.0;
    vec2 slope = mix(coarse_slope, near_slope, near_mix) * ripple_strength;
    float z2 = max(1.0 - dot(slope, slope), 0.0);
    vec3 ripple = normalize(vec3(slope, sqrt(z2)));

    // Warped base UVs (+n.xy*0.03, exact from the bytecode) sample the detail pair; the proc body
    // multiplies the texels and their alphas, quadruples, and adds the base color.
    vec2 warped = layer_uv(UV, first_uv, TIME) + ripple.xy * 0.03;
    vec2 warped2 = layer_uv(UV, second_uv, TIME) + ripple.xy * 0.03;
    vec4 t0 = texture(albedo_tex, atlas_uv(warped, vec2(textureSize(albedo_tex,0)), frames, phase));
    vec4 t1 = texture(detail_tex, atlas_uv(warped2, vec2(textureSize(detail_tex,0)), detail_frames, phase));
    float pproc = t0.a * t1.a;
    vec3 body = pproc * t0.rgb * t1.rgb * 4.0 + base_color;
    vec3 normal_world = normalize(vec3(ripple.x, ripple.z, ripple.y));
    vec3 to_eye = normalize(-VERTEX);
    vec3 to_eye_world = normalize((INV_VIEW_MATRIX * vec4(to_eye, 0.0)).xyz);

    // The eye's side of the surface picks the shipped half: mediumleylinebelow_0 draws
    // the underside when the eye is under the water, in the same fragment shader because
    // one material covers a whole type. The below path keeps the same ripple-warped body
    // but blends it toward the deep color by view angle (k = 1-0.1*(1-N.V)^2), with no
    // sun shading, no shore foam and no reflection input.
    bool below = INV_VIEW_MATRIX[3].y < world_position.y;
    if (!below) {
    // Sun diffuse through the warped normal, normalised by the flat-surface light
    // so only the variation shows (same idiom as azerite and fel; estimated gain).
    vec3 lit = client_sun_ambient + client_sun_diffuse * max(dot(-client_sun_dir, normal_world), 0.0);
    vec3 lit_flat = client_sun_ambient + client_sun_diffuse * max(dot(-client_sun_dir, vec3(0.0, 1.0, 0.0)), 0.0);
    body *= mix(vec3(1.0), lit / max(lit_flat, vec3(0.001)), 1.0);

    // Shore foam where the captured scene meets the surface, like the swamp sheet:
    // the t3 shore texture blends in over a short depth band (estimated width).
    if (has_shore) {
        float scene = view_depth(SCREEN_UV, INV_PROJECTION_MATRIX);
        float foam = 1.0 - smoothstep(0.0, shore_width, scene + VERTEX.z);
        vec4 shore = texture(shore_tex, UV * 1.7 + vec2(TIME * 0.008, -TIME * 0.005));
        body = mix(body, shore.rgb, clamp(foam, 0.0, 1.0) * shore.a);
    }
    } else {
        // The eye's side of the surface picks the shipped half: below variants draw
        // the underside when the eye is under the water, in the same fragment shader
        // because one material covers a whole type. procleylinebelow_0 keeps the same
        // ripple-warped body but blends it halfway toward the slot-4 shore texture
        // (mix(body, shore, 0.5), exact from the bytecode), with no sun shading and
        // no view-angle term. The client's depth-test against t6 that picks the shore
        // UVs is unrecoverable (stripped vertex matrices), so the shore reuses the
        // same drifted UVs as the above-water foam (estimate). Types without a shore
        // texture (void 924/927 name none) keep mediumleylinebelow_0's deep-color
        // view-angle blend instead.
        if (has_shore) {
            vec4 shore_below = texture(shore_tex, UV * 1.7 + vec2(TIME * 0.008, -TIME * 0.005));
            body = mix(body, shore_below.rgb, 0.5);
        } else {
            float ndv = clamp(dot(to_eye_world, normal_world), 0.0, 1.0);
            float k = clamp(1.0 - 0.1 * (1.0 - ndv) * (1.0 - ndv), 0.0, 1.0);
            body = mix(deep_color, body, k);
        }
    }

    color = body;
    alpha = clamp(COLOR.a, 0.0, 1.0);
    }

    ALBEDO = client_gamma_to_linear(color);
    ALPHA = alpha;
    FOG = client_fog_output(VERTEX, INV_VIEW_MATRIX);
}
