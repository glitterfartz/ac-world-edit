shader_type sky;

// The client's sky dome (DayNight::DNSky) and its pixel shader (retail "dnsky", ps_5_0).
//
// The client draws a 24-sided cap with six rings, each ring one of the light's six sky colors; the horizon
// ring and the funnel below it are the fog color. Instead of a mesh, each view ray is intersected with the
// strips between the rings, so the colors blend across the same triangles the client interpolates over.
// Colors are the band values converted like the fog, so the horizon matches the fog that meets it.
uniform vec3 sky_zenith = vec3(0.25, 0.45, 0.8);
uniform vec3 sky_middle = vec3(0.4, 0.6, 0.85);
uniform vec3 sky_band1 = vec3(0.55, 0.72, 0.9);
uniform vec3 sky_band2 = vec3(0.65, 0.78, 0.92);
uniform vec3 sky_smog = vec3(0.75, 0.82, 0.9);
uniform vec3 sky_fog = vec3(0.78, 0.84, 0.9);
// Sunrise/sunset highlight strength (the highlight curve times the light's HighlightSky flag).
uniform float highlight = 0.0;
// The camera's heading in the client's convention; the highlight keeps its place relative to the view.
uniform float camera_yaw = 0.0;
// Direction toward the sun disc, in world space, and the color the sky takes on near it.
uniform vec3 sun_direction = vec3(0.0, 1.0, 0.0);
uniform vec3 sun_glow_color = vec3(1.0);
// The retail pixel shader's glow constants live in a stripped constant buffer; these are chosen to look
// like the stock sky: how close to the sun the glow starts (cosine), and how far the sky is pulled toward
// the glow color above the horizon and at it.
uniform float glow_threshold = 0.96;
uniform float glow_above = 0.2;
uniform float glow_horizon = 0.35;

const float TAU_F = 6.28318530718;

// Clouds (DayNight::DNClouds): the client fills a texture with four octaves of animated noise, keeps what is above
// the light's density through a sharpness curve, bakes the shading and the sun's bump lighting into it, and maps
// it onto a cap of the dome. This does the same per pixel. Hidden, like the planets, when a LightSkybox model
// takes over the sky.
uniform bool celestial_enabled = true;
uniform float cloud_density = 0.0;
uniform vec3 cloud_sun_color = vec3(1.0);
uniform vec3 cloud_shade_color = vec3(0.5);
uniform vec3 cloud_base_color = vec3(0.2);
// Direction toward the body that lights the clouds: the sun by day, the moon by night.
uniform vec3 cloud_light_direction = vec3(0.0, 1.0, 0.0);

const float CLOUD_TEXTURE_SIZE = 128.0;
const float CLOUD_SHARPNESS = 0.96;
// (255 - 101) / 256: the client computes it once at its default density, whose byte truncates to 101.
const float CLOUD_SHARPNESS_STEP = 0.6015625;
const float PI_F = 3.14159265359;

// Sun and moons: camera-facing quads PLANET_DISTANCE from the eye (DayNight::DNPlanet), drawn beneath the dome (so
// they add to the sky color) and modulated by the sun color; xyz is the direction toward the quad (world space) and w its width at that
// distance, 0 when the client has no texture for it. They fade over the 0.4 units above the horizon and are cut
// off below it. The client faces the quads along the camera axis; these face their own direction.
uniform sampler2D sun_tex : filter_linear_mipmap, repeat_disable;
uniform sampler2D moon_tex : filter_linear_mipmap, repeat_disable;
uniform sampler2D moon2_tex : filter_linear_mipmap, repeat_disable;
uniform vec4 sun_disc = vec4(0.0, 1.0, 0.0, 0.0);
uniform vec4 moon_disc = vec4(0.0, 1.0, 0.0, 0.0);
uniform vec4 moon2_disc = vec4(0.0, 1.0, 0.0, 0.0);
const float PLANET_DISTANCE = 12.0;

// The band colors are 8-bit values the client shows as they are; the horizon has to match the fog, which is
// converted the same way (Shaders/client_lighting.gdshaderinc).
vec3 band_to_linear(vec3 c) {
    c = clamp(c, 0.0, 1.0);
    return mix(pow((c + 0.055) / 1.055, vec3(2.4)), c / 12.92, vec3(lessThan(c, vec3(0.04045))));
}

// s_skyThetaTable (rings 0 to 5): distance from the dome axis and height over the horizon ring.
const float RING_RADIUS[6] = float[6](0.0, 0.50904142, 0.58778525, 0.66131187, 0.68454711, 0.70710678);
const float RING_HEIGHT[6] = float[6](0.29289322, 0.15362646, 0.10192523, 0.04299478, 0.02186553, 0.0);

// s_skyFaceCurve.
const float FACE_TIME[6] = float[6](0.125, 0.375, 0.5, 0.625, 0.75, 0.875);
const float FACE_VALUE[6] = float[6](1.0, 0.0, -0.5, -0.7, -0.5, 0.0);

// DayNight::InterpTable over the cyclic face curve.
float face_curve(float t) {
    int index = 0;
    for (int i = 0; i < 6; i++) {
        if (t > FACE_TIME[i]) {
            index = i + 1;
        }
    }
    int previous = 5;
    if (index == 6) {
        index = 0;
    } else if (index > 0) {
        previous = index - 1;
    }
    float range = FACE_TIME[index] - FACE_TIME[previous];
    if (abs(range) < 0.001) {
        return FACE_VALUE[previous];
    }
    if (range < 0.0) {
        range += 1.0;
    }
    float offset = t - FACE_TIME[previous];
    if (offset < 0.0) {
        offset += 1.0;
    }
    return mix(FACE_VALUE[previous], FACE_VALUE[index], offset / range);
}

// One of the four colored rings (rows 1 to 4) at a position around the dome (DNSky::SetColors).
vec3 ring_color(vec3 band, vec3 ramp, float u) {
    float face = face_curve(u);
    if (face >= 0.0) {
        return mix(band, ramp, (1.0 - face) * highlight);
    }
    vec3 dark = mix(ramp, sky_zenith, highlight * 0.7);
    return mix(ramp, dark, -face * highlight);
}

// Where a view ray lands on a planet quad: texture coordinates in xy and the horizon fade in z, with z < 0 when
// the ray misses the quad or hits it below the horizon.
vec3 planet_hit(vec3 dir, vec4 disc) {
    vec3 miss = vec3(0.0, 0.0, -1.0);
    if (disc.w <= 0.0) {
        return miss;
    }
    vec3 p = normalize(disc.xyz);
    float facing = dot(dir, p);
    if (facing <= 0.05) {
        return miss;
    }
    vec3 point = dir * (PLANET_DISTANCE / facing);
    if (point.y <= 0.0) {
        return miss;
    }
    vec3 across = cross(vec3(0.0, 1.0, 0.0), p);
    across = length(across) > 0.001 ? normalize(across) : vec3(1.0, 0.0, 0.0);
    vec3 up = cross(p, across);
    vec3 offset = point - PLANET_DISTANCE * p;
    vec2 uv = vec2(dot(offset, across) / disc.w + 0.5, 0.5 - dot(offset, up) / disc.w);
    if (uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0) {
        return miss;
    }
    return vec3(uv, clamp(point.y * 2.5, 0.0, 1.0));
}

// ---- Clouds -----------------------------------------------------------------------------------------------

// The lattice value at a cell, in [-1, 1] (the client fills a 256 entry table with random values).
float cloud_lattice(ivec3 cell) {
    uvec3 c = uvec3(cell & ivec3(255));
    uint h = (c.x * 1597334673u) ^ (c.y * 3812015801u) ^ (c.z * 2798796415u);
    h = (h ^ (h >> 15u)) * 2246822519u;
    h = (h ^ (h >> 13u)) * 3266489917u;
    h ^= h >> 16u;
    return float(h & 0xFFFFu) / 32767.5 - 1.0;
}

float cloud_fade(float f) {
    return (1.0 - cos(f * PI_F)) * 0.5;
}

// Trilinear lattice noise with the client's cosine fade.
float cloud_noise(vec3 p) {
    vec3 base = floor(p);
    ivec3 cell = ivec3(base);
    vec3 f = vec3(cloud_fade(p.x - base.x), cloud_fade(p.y - base.y), cloud_fade(p.z - base.z));
    float x00 = mix(cloud_lattice(cell), cloud_lattice(cell + ivec3(1, 0, 0)), f.x);
    float x10 = mix(cloud_lattice(cell + ivec3(0, 1, 0)), cloud_lattice(cell + ivec3(1, 1, 0)), f.x);
    float x01 = mix(cloud_lattice(cell + ivec3(0, 0, 1)), cloud_lattice(cell + ivec3(1, 0, 1)), f.x);
    float x11 = mix(cloud_lattice(cell + ivec3(0, 1, 1)), cloud_lattice(cell + ivec3(1, 1, 1)), f.x);
    return mix(mix(x00, x10, f.y), mix(x01, x11, f.y), f.z);
}

// The first `octaves` octaves at a texture coordinate: 8, 16, 32 and 64 cells across, each half the amplitude of
// the last. The pattern drifts through the noise at the client's animation speed (2 steps of 1/256 per second).
float cloud_sum(vec2 uv, int octaves) {
    float drift = TIME / 128.0;
    float sum = 0.0;
    float amplitude = 1.0;
    float cells = 8.0;
    for (int i = 0; i < octaves; i++) {
        sum += cloud_noise(vec3(uv * cells + vec2(drift, 0.0), drift)) * amplitude;
        amplitude *= 0.5;
        cells *= 2.0;
    }
    return sum;
}

// Polar angle from the cloud sphere's axis for a view direction (WorldToTexture): the sphere is centered
// cos(45 degrees) below the eye, so the rim of the cloud cap is the horizon.
float cloud_theta(vec3 dir) {
    float along = -0.70710678 * dir.y;
    float t = along + sqrt(along * along + 0.5);
    return acos(clamp(t * dir.y + 0.70710678, -1.0, 1.0));
}

// A view direction's place in the cloud texture: an azimuthal map around the zenith out to 45 degrees.
vec2 cloud_uv(vec3 dir) {
    vec2 horizontal = vec2(-dir.z, -dir.x);
    float len = length(horizontal);
    vec2 around = len > 0.00001 ? horizontal / len : vec2(0.0);
    return around * min(cloud_theta(dir), 0.78539816) * 0.63661977 + 0.5;
}

float cloud_edge_alpha(float theta) {
    float turns = theta / PI_F;
    if (turns <= 0.205) {
        return 1.0;
    }
    if (turns <= 0.23) {
        return mix(1.0, 0.5, (turns - 0.205) / 0.025);
    }
    return mix(0.5, 0.0, clamp((turns - 0.23) / 0.015, 0.0, 1.0));
}

// The cloud layer along a view ray: rgb (gamma space) and opacity. DNClouds::UpdateTextureAnimation bakes the
// color per texel and the retail "dnclouds" pixel shader adds a bright rim toward the light on thin cloud.
vec4 cloud_layer(vec3 dir) {
    float edge = cloud_edge_alpha(cloud_theta(dir));
    vec2 uv = cloud_uv(dir);
    float level = floor(cloud_sum(uv, 4) * 64.0 + 128.5);
    float index = level - floor((1.0 - cloud_density) * 255.0);
    if (edge <= 0.0 || index < 0.0) {
        return vec4(0.0);
    }
    float alpha = 1.0 - pow(CLOUD_SHARPNESS, min(index, 255.0) * CLOUD_SHARPNESS_STEP);
    if (alpha <= 0.0) {
        return vec4(0.0);
    }

    // Shade by how thick the cloud is, then the sun's bump light from the slope of the first three octaves.
    float shade = (floor((1.0 - alpha) * 255.0 * 0.5) + 64.0) / 255.0;
    vec3 color = cloud_shade_color * shade + cloud_base_color;
    float here = cloud_sum(uv, 3);
    float slope_x = cloud_sum(uv - vec2(1.0 / CLOUD_TEXTURE_SIZE, 0.0), 3) - here;
    float slope_y = cloud_sum(uv - vec2(0.0, 1.0 / CLOUD_TEXTURE_SIZE), 3) - here;
    vec3 to_light = vec3((cloud_uv(cloud_light_direction) - uv) * CLOUD_TEXTURE_SIZE, 64.0);
    float lit = (to_light.z + slope_x * to_light.x + slope_y * to_light.y) / (length(vec3(slope_x, slope_y, 1.0)) * length(to_light));
    if (lit > 0.0) {
        color += cloud_sun_color * lit;
    }
    color = min(color, vec3(1.0));

    float rim = clamp(dot(dir, normalize(cloud_light_direction)), 0.0, 1.0);
    rim *= rim * clamp(1.0 - alpha * edge, 0.0, 1.0);
    color = min(color + color * cloud_sun_color * rim, vec3(1.0));
    return vec4(color, alpha * edge);
}

void sky() {
    vec3 dir = normalize(EYEDIR);
    // World up is +Y; the client's x and y are -Z and -X.
    float psi = atan(-dir.x, -dir.z);
    float u = fract(0.25 + (psi - camera_yaw) / TAU_F);

    vec3 bands[6] = vec3[6](sky_zenith, sky_middle, sky_band1, sky_band2, sky_smog, sky_fog);
    vec3 rings[6];
    rings[0] = sky_zenith;
    for (int i = 1; i < 5; i++) {
        rings[i] = ring_color(bands[i], mix(bands[i], sky_middle, highlight), u);
    }
    rings[5] = sky_fog;

    vec3 color = sky_fog;
    float height = 0.0;
    if (dir.y > 0.0) {
        float horizontal = sqrt(max(1.0 - dir.y * dir.y, 0.0));
        for (int k = 0; k < 5; k++) {
            float dr = RING_RADIUS[k + 1] - RING_RADIUS[k];
            float dz = RING_HEIGHT[k + 1] - RING_HEIGHT[k];
            float denominator = dz * horizontal - dr * dir.y;
            float along = (RING_RADIUS[k] * dir.y - RING_HEIGHT[k] * horizontal) / denominator;
            if (along >= -0.0001 && along <= 1.0001 && abs(denominator) > 0.00001) {
                color = mix(rings[k], rings[k + 1], clamp(along, 0.0, 1.0));
                height = (RING_RADIUS[k] + along * dr) / max(horizontal, 0.00001) * dir.y;
                break;
            }
        }
    }

    // The retail pixel shader pulls the color toward a glow around the sun, with a cubic falloff, more
    // strongly at the horizon than above it.
    vec3 above = mix(color, sun_glow_color, glow_above);
    vec3 at_horizon = mix(color, sun_glow_color, glow_horizon);
    vec3 glow = mix(at_horizon, above, clamp(height * 50.0, 0.0, 1.0));
    float closeness = clamp(dot(dir, normalize(sun_direction)) - glow_threshold, 0.0, 1.0) / (1.0 - glow_threshold);
    // The client draws the sun and moons first and the dome additively over them, then the clouds with alpha.
    vec3 sky_color = mix(color, glow, closeness * closeness * closeness);
    vec3 planets = vec3(0.0);
    if (celestial_enabled) {
        vec3 sun_hit = planet_hit(dir, sun_disc);
        if (sun_hit.z >= 0.0) {
            vec4 disc = texture(sun_tex, sun_hit.xy);
            planets += disc.rgb * sun_glow_color * disc.a * sun_hit.z;
        }
        vec3 moon_hit = planet_hit(dir, moon_disc);
        if (moon_hit.z >= 0.0) {
            vec4 disc = texture(moon_tex, moon_hit.xy);
            planets += disc.rgb * sun_glow_color * disc.a * moon_hit.z;
        }
        vec3 moon2_hit = planet_hit(dir, moon2_disc);
        if (moon2_hit.z >= 0.0) {
            vec4 disc = texture(moon2_tex, moon2_hit.xy);
            planets += disc.rgb * disc.a * moon2_hit.z;
        }
    }
    sky_color = min(sky_color + planets, vec3(1.0));
    if (celestial_enabled && cloud_density > 0.0 && dir.y > 0.0) {
        vec4 clouds = cloud_layer(dir);
        sky_color = mix(sky_color, clouds.rgb, clouds.a);
    }
    COLOR = band_to_linear(sky_color);
}
