shader_type spatial;
render_mode unshaded, blend_mix, depth_draw_never, cull_disabled;
#include "res://Shaders/shared/client_lighting.gdshaderinc"

// Retail material 14. Combines procswamp's depth layer and water surface with
// procswampabovefog's shore overlay in one pass. All material math is gamma-space.
uniform sampler2D albedo_tex : filter_linear, repeat_disable;
uniform sampler2D swamp_middle_tex : filter_linear_mipmap, repeat_enable;
uniform sampler2D swamp_deep_tex : filter_linear_mipmap, repeat_enable;
uniform sampler2D swamp_shore_tex : filter_linear_mipmap, repeat_enable;
uniform sampler2D swamp_normal_tex : filter_linear, repeat_disable;
uniform sampler2D scene_color : hint_screen_texture, filter_linear, repeat_disable;
uniform sampler2D scene_depth : hint_depth_texture, filter_nearest, repeat_disable;
uniform float frames = 1.0;
uniform float period_ms = 1250.0;
uniform float normal_frames = 1.0;
uniform bool has_normals = false;
uniform bool has_shore = false;
uniform vec4 swamp_depths;
uniform vec3 swamp_scales;
uniform float swamp_opacity = 1.0;
// Stripped constant mapping inferred from LiquidType, not recovered CPU code.
uniform float normal_strength = 0.15;
uniform float specular_power = 300.0;
uniform float refraction_strength = 0.075;
uniform float wave_scale = 1.0;
uniform float reflection_strength = 0.6;
uniform float flow_speed = 0.3;
uniform float normal_fps = 60.0;
uniform float specular_strength = 0.1;
uniform float shore_width = 0.3;

#include "res://Shaders/liquid/liquid_reflection.gdshaderinc"

varying vec3 world_position;

void vertex() {
    world_position = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

vec3 to_linear(vec3 c) {
    return client_gamma_to_linear(c);
}

vec4 atlas_sample(sampler2D tex, vec2 uv, float count, float frame) {
    vec2 size = vec2(textureSize(tex, 0));
    vec2 inset = vec2(0.5 * count / size.x, 0.5 / size.y);
    vec2 tile = mix(inset, vec2(1.0) - inset, fract(uv));
    return texture(tex, vec2((mod(frame, count) + tile.x) / count, tile.y));
}

// procswamp_6: two near and two coarse RG samples. Their sum is centered on 1,
// the coarse pair scales by 2.34, then the near/far difference blends by 0.15.
vec3 ripple_frame(vec2 p, float frame, float time) {
    // The vertex shader supplies texture matrices whose CPU values are stripped.
    // World-space UVs keep chunk/tile boundaries continuous. Coarse ratio and
    // scroll directions below are estimates; 1/32 and the -0.5 offset are in the VS.
    vec2 uv = p * (1.0 / 32.0) * wave_scale;
    vec2 shift = vec2(0.015, 0.011) * time * flow_speed;
    vec2 near_sum = atlas_sample(swamp_normal_tex, uv + shift, normal_frames, frame).rg
        + atlas_sample(swamp_normal_tex, -(uv.yx + 0.5 / 32.0) + shift.yx, normal_frames, frame).rg - 1.0;
    vec2 far_sum = atlas_sample(swamp_normal_tex, uv * 0.25 - shift, normal_frames, frame).rg
        + atlas_sample(swamp_normal_tex, -(uv.yx + 0.5 / 32.0) * 0.25 - shift.yx, normal_frames, frame).rg - 1.0;
    vec2 slope = mix(far_sum * 2.34, near_sum * 2.0, 0.15) * normal_strength;
    return normalize(vec3(slope, 1.0));
}

vec3 ripple(vec2 p, float time) {
    if (!has_normals) return vec3(0.0, 0.0, 1.0);
    float phase = time * normal_fps;
    // procswamp blends normals reconstructed from adjacent animation frames.
    return mix(ripple_frame(p, floor(phase), time), ripple_frame(p, floor(phase) + 1.0, time), fract(phase));
}

vec4 depth_layers(vec2 uv, float depth, vec2 bend, float time) {
    float frame = floor(mod(time * 1000.0 / period_ms, 1.0) * frames);
    vec4 a = atlas_sample(albedo_tex, uv * swamp_scales.x + bend, frames, frame);
    vec4 b = texture(swamp_middle_tex, uv * swamp_scales.y + bend);
    vec4 c = texture(swamp_deep_tex, uv * swamp_scales.z + bend);
    vec3 t = clamp((vec3(depth) - swamp_depths.xzy) / max(swamp_depths.ywz - swamp_depths.xzy, vec3(0.000001)), 0.0, 1.0);
    t = t * t * (3.0 - 2.0 * t);
    return mix(mix(a, b, t.x), mix(b, c, t.y), t.z);
}

void fragment() {
    vec3 wave = ripple(world_position.xz, TIME);
    vec3 normal_world = normalize(vec3(wave.x, wave.z, wave.y));
    vec3 normal_view = normalize((VIEW_MATRIX * vec4(normal_world, 0.0)).xyz);
    vec3 to_eye = normalize(-VERTEX);
    float thickness = max(view_depth(SCREEN_UV, INV_PROJECTION_MATRIX) + VERTEX.z, 0.0);
    // procswamp: saturate(depth * 25), then cubic smoothstep, at solid intersections.
    float edge_alpha = smoothstep(0.0, 0.04, thickness);
    float fresnel = pow(1.0 - clamp(dot(to_eye, normal_view), 0.0, 1.0), 2.0);

    // procswamp_20: depth-dependent refraction offset and foreground rejection.
    float wet_depth = thickness * edge_alpha;
    float offset = clamp(wet_depth * 0.01 * (1.2 - fresnel * reflection_strength), 0.0, 1.0) * 0.0625
        + clamp(wet_depth, 0.0, 1.0) * 0.05;
    vec2 refracted_uv = clamp(SCREEN_UV + normal_view.xy * offset * refraction_strength, vec2(0.001), vec2(0.999));
    if (view_depth(refracted_uv, INV_PROJECTION_MATRIX) < -VERTEX.z) refracted_uv = SCREEN_UV;
    vec3 background = texture(scene_color, refracted_uv).rgb;

    vec4 layer = depth_layers(UV, COLOR.a + 0.1 * abs(wave.y), wave.xy * 0.1, TIME);
    if (has_shore) {
        // procswampabovefog_6: view-angle/depth-weighted shore texture overlay.
        vec3 up_view = (VIEW_MATRIX * vec4(0.0, 1.0, 0.0, 0.0)).xyz;
        float shore = 1.0 - smoothstep(0.0, max(shore_width, 0.000001),
            (1.0 - clamp(dot(to_eye, up_view), 0.0, 1.0)) * thickness);
        vec4 coast = texture(swamp_shore_tex, UV * swamp_scales.x + wave.xy * 0.1);
        layer.rgb = mix(layer.rgb, coast.rgb, shore * coast.a);
        layer.a = clamp(layer.a + shore * coast.a, 0.0, 1.0);
    }
    vec3 lighting = client_sun_ambient + client_sun_diffuse * clamp(dot(-client_sun_dir, normal_world), 0.0, 1.0);
    vec4 fog = client_fog_output(VERTEX, INV_VIEW_MATRIX);
    vec3 local = mix(to_linear(layer.rgb * lighting), fog.rgb, fog.a);
    vec3 body = mix(background, local, max(layer.a, 0.35));

    vec3 view_dir = reflect(-to_eye, normal_view);
    vec3 world_dir = normalize((INV_VIEW_MATRIX * vec4(view_dir, 0.0)).xyz);
    vec3 reflection = liquid_reflection(VERTEX, view_dir, world_dir, to_linear(client_fog_color), SCREEN_UV, fwidth(SCREEN_UV), PROJECTION_MATRIX, INV_PROJECTION_MATRIX);
    vec3 half_vector = normalize(-client_sun_dir + normalize((INV_VIEW_MATRIX * vec4(to_eye, 0.0)).xyz));
    // procswamp_20: Blinn-Phong plus the small normal-up lobe above 0.8.
    float highlight = pow(clamp(dot(half_vector, normal_world), 0.0, 1.0), max(specular_power, 1.0))
        + max(normal_world.y - 0.8, 0.0);
    vec3 color = mix(body, reflection, clamp(fresnel * reflection_strength, 0.0, 1.0));
    color += to_linear(client_sun_specular * highlight * specular_strength) * (1.0 - fog.a);
    // Screen colour already contains scene fog. Composite explicitly so neither it
    // nor transparent edges receive the fog a second time through Godot's FOG output.
    ALBEDO = mix(texture(scene_color, SCREEN_UV).rgb, color, clamp(edge_alpha * swamp_opacity, 0.0, 1.0));
    ALPHA = 1.0;
}
