// Constants the client feeds every world shader, written each frame by SkyLighting through
// ClientShaderGlobals.

// Fog: x start, y end (yards), z density, w mode (0 off, 1 the 3.3.5a ramp, 2 retail exponential).
global uniform vec4 client_fog;
global uniform vec3 client_fog_color;
// The color the fog turns toward with distance (rgb) and the yards that takes (w, 0 for none); retail only.
global uniform vec4 client_fog_end;
// Retail height fog (see ClientShaderGlobals): plane height, scaler, density below the plane, w 1 when present;
// the transition cubic; the layer's color and its end color.
global uniform vec4 client_fog_height;
global uniform vec4 client_fog_poly;
global uniform vec4 client_fog_height_color;
global uniform vec4 client_fog_height_end;
// Retail sun scattering: color (band bytes) and strength; the direction toward the sun disc.
global uniform vec4 client_fog_sun;
global uniform vec3 client_sun_disc;

// The sun the client lights every surface with: the direction its light travels (world space) and the ambient,
// diffuse and specular band colors (8-bit values, gamma space).
global uniform vec3 client_sun_dir;
global uniform vec3 client_sun_ambient;
global uniform vec3 client_sun_diffuse;
global uniform vec3 client_sun_specular;

// Band colors are 8-bit values the client shows without a linear pipeline; Godot lights and tone maps in
// linear space, so fog (and the sky it meets at the horizon) are converted on the way out.
vec3 client_gamma_to_linear(vec3 c) {
    c = clamp(c, 0.0, 1.0);
    return mix(pow((c + 0.055) / 1.055, vec3(2.4)), c / 12.92, vec3(lessThan(c, vec3(0.04045))));
}

// How much of the surface survives the fog (1 = clear, 0 = fully fogged). The 3.3.5a client ramps
// pow(saturate((end - depth) / (end - start)), density) over view depth. Retail's volume fog integrates an
// extinction along the whole view ray, so it uses the distance to the eye: exp(-density * (distance - start) /
// (end - start)), which agrees with the old ramp near the start and never cuts off at the end.
float client_fog_factor(float view_depth, float eye_distance) {
    if (client_fog.w < 0.5) {
        return 1.0;
    }
    float span = max(client_fog.y - client_fog.x, 0.001);
    if (client_fog.w > 1.5) {
        return exp(-client_fog.z * max(eye_distance - client_fog.x, 0.0) / span);
    }
    return pow(clamp((client_fog.y - view_depth) / span, 0.0, 1.0), client_fog.z);
}

// How much of the main fog applies at a world height (1 above the fog plane, 0 in the layer below it), from the
// retail volume-fog compute shader: a cubic of the distance above the plane.
float client_fog_main_weight(float world_y) {
    float x = 1.0 - clamp((world_y - client_fog_height.x) * client_fog_height.y, 0.0, 1.0);
    float p = ((client_fog_poly.x * x + client_fog_poly.y) * x + client_fog_poly.z) * x + client_fog_poly.w;
    return 1.0 - clamp(p, 0.0, 1.0);
}

// Retail fog with a height layer: the extinction and color vary along the ray, so it is integrated in a few
// steps from the fog start to the fragment, the way the froxel volume accumulates its slices.
vec4 client_fog_layered(vec3 view_position, mat4 inv_view) {
    float eye_distance = length(view_position);
    float start = client_fog.x;
    if (eye_distance <= start) {
        return vec4(client_gamma_to_linear(client_fog_color), 0.0);
    }
    float span = max(client_fog.y - client_fog.x, 0.001);
    vec3 direction = normalize((inv_view * vec4(view_position, 0.0)).xyz);
    float camera_y = inv_view[3].y;
    const int STEPS = 6;
    float step_length = (eye_distance - start) / float(STEPS);
    float transmittance = 1.0;
    vec3 scattered = vec3(0.0);
    for (int i = 0; i < STEPS; i++) {
        float t = start + (float(i) + 0.5) * step_length;
        float main_weight = client_fog_main_weight(camera_y + direction.y * t);
        float density = mix(client_fog_height.z, client_fog.z, main_weight) / span;
        float fade = client_fog_end.w > 0.0 ? clamp((t - start) / client_fog_end.w, 0.0, 1.0) : 0.0;
        vec3 main_color = mix(client_fog_color, client_fog_end.rgb, fade);
        vec3 layer_color = mix(client_fog_height_color.rgb, client_fog_height_end.rgb, fade * fade * fade);
        float absorbed = 1.0 - exp(-density * step_length);
        scattered += transmittance * absorbed * mix(layer_color, main_color, main_weight);
        transmittance *= 1.0 - absorbed;
    }
    float alpha = 1.0 - transmittance;
    return vec4(client_gamma_to_linear(alpha > 0.0001 ? scattered / alpha : client_fog_color), alpha);
}

// The volume fog's sun scattering: a Henyey-Greenstein phase function (anisotropy 0.9) of the angle between the
// view ray and the sun, times the light's sun fog color and strength, added to the fog color.
vec3 client_fog_sun_scatter(vec3 view_position, mat4 inv_view) {
    if (client_fog_sun.w <= 0.0) {
        return vec3(0.0);
    }
    vec3 ray = normalize((inv_view * vec4(view_position, 0.0)).xyz);
    float phase = 0.19 / (12.5664 * pow(max(1.81 - 1.8 * dot(ray, client_sun_disc), 0.0001), 1.5));
    return client_gamma_to_linear(client_fog_sun.rgb) * client_fog_sun.w * phase;
}

// Placed retail fog volumes (the retail "volumefogmaterialinjection" pass): the few boxes nearest the camera, six
// texels each (see ClientShaderGlobals). Each ray from the eye to the fragment is clipped to the volume (a box or, for
// flag bit 2, the ellipsoid inscribed in it) and the density is sampled at three points along the part inside: the
// thickness, shaped by how far the point sits from the volume's faces (the client's signed-distance falloff, raised to
// the edge power), thinning from the bottom of the box to its top, and, for flag bit 0, broken up by two drifting
// layers of 3D noise through a contrast curve. The haze takes the volume's color, lit by the sun and ambient. The
// thickness scale, the box height, the falloff distance, the noise cell sizes and speeds and the contrast curve are
// estimates; the client's noise is a texture and its constants are not in the shipped bytecode.
global uniform sampler2D client_fog_volumes : filter_nearest, repeat_disable;
global uniform float client_fog_volume_count;
global uniform float client_fog_volume_time;
const int CLIENT_FOG_VOLUME_MAX = 6;
const float CLIENT_FOG_NOISE_CELL = 100.0;

float client_fog_hash(vec3 p) {
    p = fract(p * 0.1031);
    p += dot(p, p.zyx + 31.32);
    return fract((p.x + p.y) * p.z);
}

// 3D value noise in 0 to 1.
float client_fog_noise(vec3 p) {
    vec3 i = floor(p);
    vec3 f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    float a = mix(mix(client_fog_hash(i), client_fog_hash(i + vec3(1, 0, 0)), f.x), mix(client_fog_hash(i + vec3(0, 1, 0)), client_fog_hash(i + vec3(1, 1, 0)), f.x), f.y);
    float b = mix(mix(client_fog_hash(i + vec3(0, 0, 1)), client_fog_hash(i + vec3(1, 0, 1)), f.x), mix(client_fog_hash(i + vec3(0, 1, 1)), client_fog_hash(i + vec3(1, 1, 1)), f.x), f.y);
    return mix(a, b, f.z);
}

// Density factor at a world point: two noise layers drifting at their own speeds, averaged and contrasted so the
// mean stays near 1 and the haze breaks into clumps.
float client_fog_noise_factor(vec3 world, float speed_a, float speed_b) {
    vec3 a = (world - vec3(1.0, 0.0, 0.3) * speed_a * client_fog_volume_time) / CLIENT_FOG_NOISE_CELL;
    vec3 b = (world - vec3(-0.6, 0.0, 1.0) * speed_b * client_fog_volume_time) / (CLIENT_FOG_NOISE_CELL * 1.7) + 17.0;
    float n = 0.5 * (client_fog_noise(a) + client_fog_noise(b));
    return mix(0.3, 1.7, smoothstep(0.25, 0.75, n));
}

vec4 client_fog_volume_layer(vec3 origin, vec3 direction, float eye_distance) {
    float transmittance = 1.0;
    vec3 scattered = vec3(0.0);
    vec3 lit = clamp(client_sun_ambient + client_sun_diffuse * 0.5, 0.0, 1.0);
    int count = int(client_fog_volume_count + 0.5);
    for (int i = 0; i < CLIENT_FOG_VOLUME_MAX; i++) {
        if (i >= count) {
            break;
        }
        int at = i * 6;
        vec4 t0 = texelFetch(client_fog_volumes, ivec2(at, 0), 0);
        vec4 ax = texelFetch(client_fog_volumes, ivec2(at + 1, 0), 0);
        vec4 ay = texelFetch(client_fog_volumes, ivec2(at + 2, 0), 0);
        vec4 az = texelFetch(client_fog_volumes, ivec2(at + 3, 0), 0);
        vec4 t4 = texelFetch(client_fog_volumes, ivec2(at + 4, 0), 0);
        vec4 t5 = texelFetch(client_fog_volumes, ivec2(at + 5, 0), 0);
        bool ellipsoid = mod(t5.x, 2.0) > 0.5;
        bool noisy = t5.x > 1.5;
        vec3 half_size = vec3(ax.w, ay.w, az.w);
        vec3 o = origin - t0.xyz;
        vec3 local_origin = vec3(dot(o, ax.xyz), dot(o, ay.xyz), dot(o, az.xyz));
        vec3 local_direction = vec3(dot(direction, ax.xyz), dot(direction, ay.xyz), dot(direction, az.xyz));
        float t_in;
        float t_out;
        if (ellipsoid) {
            vec3 so = local_origin / half_size;
            vec3 sd = local_direction / half_size;
            float qa = dot(sd, sd);
            float qb = dot(so, sd);
            float disc = qb * qb - qa * (dot(so, so) - 1.0);
            if (disc <= 0.0) {
                continue;
            }
            float root = sqrt(disc);
            t_in = max((-qb - root) / qa, 0.0);
            t_out = min((-qb + root) / qa, eye_distance);
        } else {
            vec3 safe = mix(local_direction, vec3(1e-5), lessThan(abs(local_direction), vec3(1e-5)));
            vec3 ta = (-half_size - local_origin) / safe;
            vec3 tb = (half_size - local_origin) / safe;
            vec3 t_near = min(ta, tb);
            vec3 t_far = max(ta, tb);
            t_in = max(max(t_near.x, t_near.y), max(t_near.z, 0.0));
            t_out = min(min(t_far.x, t_far.y), min(t_far.z, eye_distance));
        }
        float length_inside = t_out - t_in;
        if (length_inside <= 0.0) {
            continue;
        }
        float falloff = max(min(min(half_size.x, half_size.y), half_size.z), 0.001);
        float density = 0.0;
        for (int tap = 0; tap < 3; tap++) {
            float t = t_in + length_inside * (0.1667 + 0.3333 * float(tap));
            vec3 local = local_origin + local_direction * t;
            float inside_distance = ellipsoid
                ? (1.0 - length(local / half_size)) * min(min(half_size.x, half_size.y), half_size.z)
                : min(min(half_size.x - abs(local.x), half_size.y - abs(local.y)), half_size.z - abs(local.z));
            float edge = pow(clamp(inside_distance / falloff, 0.0, 1.0), t4.w);
            float height = pow(clamp(0.5 - local.z / (2.0 * half_size.z), 0.0, 1.0), t5.w);
            float tap_density = edge * height;
            if (noisy) {
                tap_density *= client_fog_noise_factor(origin + direction * t, t5.y, t5.z);
            }
            density += tap_density / 3.0;
        }
        float opacity = 1.0 - exp(-t0.w * density * length_inside);
        scattered += transmittance * opacity * client_gamma_to_linear(t4.rgb) * lit;
        transmittance *= 1.0 - opacity;
    }
    float alpha = 1.0 - transmittance;
    return vec4(alpha > 0.0001 ? scattered / alpha : vec3(0.0), alpha);
}

// Fog as Godot's custom fog output: rgb color, a how much of it covers the surface. view_position is the
// fragment's view-space VERTEX and inv_view its INV_VIEW_MATRIX.
vec4 client_fog_base(vec3 view_position, mat4 inv_view) {
    if (client_fog.w > 1.5 && client_fog_height.w > 0.5) {
        vec4 layered = client_fog_layered(view_position, inv_view);
        return vec4(layered.rgb + client_fog_sun_scatter(view_position, inv_view), layered.a);
    }
    float eye_distance = length(view_position);
    vec3 color = client_fog_color;
    if (client_fog.w > 1.5 && client_fog_end.w > 0.0) {
        color = mix(color, client_fog_end.rgb, clamp((eye_distance - client_fog.x) / client_fog_end.w, 0.0, 1.0));
    }
    vec3 scattered = client_fog.w > 1.5 ? client_fog_sun_scatter(view_position, inv_view) : vec3(0.0);
    return vec4(client_gamma_to_linear(color) + scattered, 1.0 - client_fog_factor(-view_position.z, eye_distance));
}

// The client's light on a model or WMO batch (the retail combiners shaders), in gamma space: ambient, widened a little
// toward the sun side by a wrap term, plus diffuse N.L. It multiplies the surface color; there is no specular.
vec3 client_model_light(vec3 normal, mat4 view_matrix) {
    vec3 sun = normalize((view_matrix * vec4(client_sun_dir, 0.0)).xyz);
    float facing = dot(normal, -sun);
    return client_sun_ambient * (0.7 + 0.4 * (0.5 * facing + 0.5)) + client_sun_diffuse * max(facing, 0.0);
}

// The client's surface lighting (the retail terrain pixel shader), in gamma space like the client: ambient plus
// diffuse N.L scale the albedo, a Blinn-Phong highlight (exponent 20) masked by the texture's alpha adds on top, and
// the baked shadow only takes 30 % off. Lighting that brightens past white clamps, as the framebuffer does.
// shadow is 1 for lit and 0 for fully shadowed; view_position is the fragment's view-space VERTEX.
const float CLIENT_SPECULAR_EXPONENT = 20.0;
const float CLIENT_SPECULAR_STRENGTH = 1.0;
const float CLIENT_SHADOW_STRENGTH = 0.3;
vec3 client_surface_light(vec3 albedo, float specular_mask, float shadow, vec3 view_position, vec3 normal, mat4 view_matrix) {
    vec3 sun = normalize((view_matrix * vec4(client_sun_dir, 0.0)).xyz);
    vec3 lit = clamp(client_sun_ambient + client_sun_diffuse * max(dot(-sun, normal), 0.0), 0.0, 1.0);
    vec3 half_vector = normalize(normalize(view_position) + sun);
    float highlight = pow(max(dot(-half_vector, normal), 0.0), CLIENT_SPECULAR_EXPONENT);
    vec3 specular = clamp(client_sun_specular * highlight * shadow * specular_mask * CLIENT_SPECULAR_STRENGTH, 0.0, 1.0);
    vec3 color = albedo * lit + specular;
    return color * (1.0 - CLIENT_SHADOW_STRENGTH + CLIENT_SHADOW_STRENGTH * shadow);
}

// The 3.3.5a terrain pixel shader: the same ambient plus diffuse N.L scales the layers, and the baked
// shadow takes 30 % off, but that terrain has no specular term at all. Its layer textures' alpha is not
// a highlight mask (Wrath tiles are usually opaque DXT1, so alpha is 1 over the whole ground).
vec3 client_wrath_surface_light(vec3 albedo, float shadow, vec3 normal, mat4 view_matrix) {
    vec3 sun = normalize((view_matrix * vec4(client_sun_dir, 0.0)).xyz);
    vec3 lit = clamp(client_sun_ambient + client_sun_diffuse * max(dot(-sun, normal), 0.0), 0.0, 1.0);
    return albedo * lit * (1.0 - CLIENT_SHADOW_STRENGTH + CLIENT_SHADOW_STRENGTH * shadow);
}

// The light's fog with any placed volumes over it: the two coverages combine as independent layers.
vec4 client_fog_output(vec3 view_position, mat4 inv_view) {
    vec4 base = client_fog_base(view_position, inv_view);
    if (client_fog_volume_count < 0.5) {
        return base;
    }
    vec3 direction = normalize((inv_view * vec4(view_position, 0.0)).xyz);
    vec4 volumes = client_fog_volume_layer(inv_view[3].xyz, direction, length(view_position));
    float alpha = base.a + volumes.a * (1.0 - base.a);
    vec3 color = (base.rgb * base.a + volumes.rgb * volumes.a * (1.0 - base.a)) / max(alpha, 0.0001);
    return vec4(alpha > 0.0001 ? color : base.rgb, alpha);
}
