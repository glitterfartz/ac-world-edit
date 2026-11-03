shader_type sky;

// The client's sky dome (DayNight::DNSky) and its pixel shader (retail "dnsky", ps_5_0).
//
// The client draws a 24-sided cap with six rings, each ring one of the light's six sky colors; the horizon
// ring and the funnel below it are the fog color. Instead of a mesh, each view ray is intersected with the
// strips between the rings, so the colors blend across the same triangles the client interpolates over.
// Colors are the raw band values, like the fog, so the horizon matches the fog that meets it.
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
    COLOR = mix(color, glow, closeness * closeness * closeness);
}
