// Constants the client feeds every world shader, written each frame by SkyLighting through
// ClientShaderGlobals.

// Fog: x start, y end (yards), z density, w mode (0 off, 1 the 3.3.5a ramp, 2 retail exponential).
global uniform vec4 client_fog;
global uniform vec3 client_fog_color;
// The color the fog turns toward with distance (rgb) and the yards that takes (w, 0 for none); retail only.
global uniform vec4 client_fog_end;

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

// Fog as Godot's custom fog output: rgb color, a how much of it covers the surface. view_position is the
// fragment's view-space VERTEX.
vec4 client_fog_output(vec3 view_position) {
    float eye_distance = length(view_position);
    vec3 color = client_fog_color;
    if (client_fog.w > 1.5 && client_fog_end.w > 0.0) {
        color = mix(color, client_fog_end.rgb, clamp((eye_distance - client_fog.x) / client_fog_end.w, 0.0, 1.0));
    }
    return vec4(client_gamma_to_linear(color), 1.0 - client_fog_factor(-view_position.z, eye_distance));
}
