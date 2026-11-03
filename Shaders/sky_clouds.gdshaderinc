// The client's cloud noise (DayNight::DNClouds), shared by the sky shader, which draws the clouds, and the glare
// shader, which dims the sun and moon glare by the cloud in front of them.
uniform float cloud_density = 0.0;

const float CLOUD_TEXTURE_SIZE = 128.0;
const float CLOUD_SHARPNESS = 0.96;
// (255 - 101) / 256: the client computes it once at its default density, whose byte truncates to 101.
const float CLOUD_SHARPNESS_STEP = 0.6015625;
const float PI_F = 3.14159265359;


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

// The cloud opacity along a view direction before the rim fade: the noise above the light's density threshold
// through the sharpness curve. This is the value the client keeps per texel as the cloud "density".
float cloud_alpha_at(vec3 dir) {
    float level = floor(cloud_sum(cloud_uv(dir), 4) * 64.0 + 128.5);
    float index = level - floor((1.0 - cloud_density) * 255.0);
    return index < 0.0 ? 0.0 : 1.0 - pow(CLOUD_SHARPNESS, min(index, 255.0) * CLOUD_SHARPNESS_STEP);
}
