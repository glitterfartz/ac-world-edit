// Constants the client feeds every world shader, written each frame by SkyLighting through
// ClientShaderGlobals.

// Fog: x start, y end (view-space depth in yards), z density exponent, w 1 when fog is on.
global uniform vec4 client_fog;
global uniform vec3 client_fog_color;

// The client's fog factor (1 = clear, 0 = fully fogged): pow(saturate((end - depth) / (end - start)),
// density), with depth the view-space distance along the camera axis, not the radial distance.
float client_fog_factor(float view_depth) {
    if (client_fog.w < 0.5) {
        return 1.0;
    }
    float linear_factor = clamp((client_fog.y - view_depth) / max(client_fog.y - client_fog.x, 0.001), 0.0, 1.0);
    return pow(linear_factor, client_fog.z);
}

// Fog as Godot's custom fog output: rgb color, a how much of it covers the surface.
vec4 client_fog_output(float view_depth) {
    return vec4(client_fog_color, 1.0 - client_fog_factor(view_depth));
}
