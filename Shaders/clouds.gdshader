shader_type spatial;
render_mode unshaded, blend_mix, depth_draw_never, cull_disabled, fog_disabled;
#include "res://Shaders/sky_clouds.gdshaderinc"

// The client's cloud layer (DayNight::DNClouds and the retail "dnclouds" pixel shader). The client fills a texture
// with four octaves of animated noise, keeps what is above the light's density through a sharpness curve, bakes
// the shading and the sun's bump lighting into it, and maps it onto a cap of the dome drawn after the stars, the
// planets and the dome. This does the same per pixel on a sphere around the camera that is projected out to just
// inside the far plane, like the LightSkybox models (ModelLibrary.SkyboxShader), so the world always draws over
// it and it draws after the star model. Hidden when a LightSkybox model takes over the sky.
uniform vec3 cloud_sun_color = vec3(1.0);
uniform vec3 cloud_shade_color = vec3(0.5);
uniform vec3 cloud_base_color = vec3(0.2);
// Direction toward the body that lights the clouds: the sun by day, the moon by night.
uniform vec3 cloud_light_direction = vec3(0.0, 1.0, 0.0);
// How far out the shell sits; inside the camera far plane (SkyLighting.ShellDistance).
uniform float shell_distance = 7500.0;

varying vec3 view_direction;

vec3 band_to_linear(vec3 c) {
    c = clamp(c, 0.0, 1.0);
    return mix(pow((c + 0.055) / 1.055, vec3(2.4)), c / 12.92, vec3(lessThan(c, vec3(0.04045))));
}

void vertex() {
    view_direction = VERTEX;
    vec3 eye = (MODELVIEW_MATRIX * vec4(VERTEX, 1.0)).xyz;
    POSITION = PROJECTION_MATRIX * vec4(normalize(eye) * shell_distance, 1.0);
}

// The cloud layer along a view ray: rgb (gamma space) and opacity. DNClouds::UpdateTextureAnimation bakes the
// color per texel and the retail "dnclouds" pixel shader adds a bright rim toward the light on thin cloud.
vec4 cloud_layer(vec3 dir) {
    float edge = cloud_edge_alpha(cloud_theta(dir));
    vec2 uv = cloud_uv(dir);
    float alpha = cloud_alpha_at(dir);
    if (edge <= 0.0 || alpha <= 0.0) {
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

void fragment() {
    vec3 dir = normalize(view_direction);
    if (dir.y <= 0.0) {
        discard;
    }
    vec4 clouds = cloud_layer(dir);
    ALBEDO = band_to_linear(clouds.rgb);
    ALPHA = clouds.a;
}
